using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Application.UseCases.MergeVideos;

public interface IMergeVideosUseCase
{
    Task<Result<TrimVideoResponseDto>> ExecuteMergeSegmentsAsync(
        MergeSegmentsRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<TrimVideoResponseDto>> ExecuteMergeFilesAsync(
        MergeFilesRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public class MergeVideosUseCase : IMergeVideosUseCase
{
    private readonly IVideoTrimmer _videoTrimmer;
    private readonly IAudioMuxer _audioMuxer;
    private readonly IVideoConcatenator _videoConcatenator;
    private readonly IFileSystemService _fileSystemService;

    public MergeVideosUseCase(
        IVideoTrimmer videoTrimmer,
        IAudioMuxer audioMuxer,
        IVideoConcatenator videoConcatenator,
        IFileSystemService fileSystemService)
    {
        _videoTrimmer = videoTrimmer ?? throw new ArgumentNullException(nameof(videoTrimmer));
        _audioMuxer = audioMuxer ?? throw new ArgumentNullException(nameof(audioMuxer));
        _videoConcatenator = videoConcatenator ?? throw new ArgumentNullException(nameof(videoConcatenator));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<Result<TrimVideoResponseDto>> ExecuteMergeSegmentsAsync(
        MergeSegmentsRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Result<TrimVideoResponseDto>.Failure("La solicitud de unión no puede ser nula.");

        if (!_fileSystemService.FileExists(request.SourceVideoPath))
            return Result<TrimVideoResponseDto>.Failure($"El video de origen no existe: {request.SourceVideoPath}");

        if (request.Segments == null || request.Segments.Count == 0)
            return Result<TrimVideoResponseDto>.Failure("Debes especificar al menos un segmento para recortar.");

        if (string.IsNullOrWhiteSpace(request.DestinationVideoPath))
            return Result<TrimVideoResponseDto>.Failure("La ruta de destino es obligatoria.");

        // Validar todos los rangos de tiempo
        var timeRanges = new List<TimeRange>();
        foreach (var seg in request.Segments)
        {
            if (!TimeRange.TryCreate(seg.Start, seg.End, out var range, out var timeError))
                return Result<TrimVideoResponseDto>.Failure($"Error en el segmento ({seg.Start} a {seg.End}): {timeError}");

            timeRanges.Add(range!);
        }

        // Construir configuración de audio
        AudioTrackConfig audioConfig;
        try
        {
            if (request.AudioMode == AudioMode.OverlayClips || (request.AudioClips != null && request.AudioClips.Count > 0))
            {
                var domainClips = (request.AudioClips ?? Array.Empty<AudioOverlayClipDto>())
                    .Select(c => new AudioOverlayClip(c.FilePath, c.StartTime, c.Volume, c.Label))
                    .ToList();

                if (domainClips.Count == 0)
                {
                    audioConfig = AudioTrackConfig.KeepOriginal();
                }
                else
                {
                    bool keepOriginal = request.MainVolume > 0.001;
                    audioConfig = AudioTrackConfig.WithClips(domainClips, keepOriginalAudio: keepOriginal, mainVolume: request.MainVolume);
                }
            }
            else
            {
                audioConfig = request.AudioMode switch
                {
                    AudioMode.KeepOriginal => AudioTrackConfig.KeepOriginal(),
                    AudioMode.Mute => AudioTrackConfig.Mute(),
                    AudioMode.Replace => string.IsNullOrWhiteSpace(request.ExternalAudioPath)
                        ? throw new InvalidOperationException("Se requiere la ruta del archivo de audio para reemplazar.")
                        : AudioTrackConfig.ReplaceWith(request.ExternalAudioPath),
                    AudioMode.Mix => string.IsNullOrWhiteSpace(request.ExternalAudioPath)
                        ? throw new InvalidOperationException("Se requiere la ruta del archivo de audio para mezclar.")
                        : AudioTrackConfig.Mix(request.ExternalAudioPath, request.MainVolume, request.BackgroundVolume),
                    _ => AudioTrackConfig.KeepOriginal()
                };
            }
        }
        catch (Exception ex)
        {
            return Result<TrimVideoResponseDto>.Failure(ex.Message);
        }

        // Si solo hay un segmento, podemos hacer un corte directo a destino
        if (timeRanges.Count == 1)
        {
            var singleJob = new CutJob(
                request.SourceVideoPath,
                timeRanges[0],
                request.DestinationVideoPath,
                CutStrategy.LosslessStreamCopy,
                audioConfig);

            Result cutResult = audioConfig.Mode is AudioMode.Replace or AudioMode.Mix or AudioMode.OverlayClips
                ? await _audioMuxer.ProcessAudioAsync(
                    request.SourceVideoPath,
                    timeRanges[0],
                    audioConfig,
                    request.DestinationVideoPath,
                    progress,
                    cancellationToken)
                : await _videoTrimmer.TrimAsync(singleJob, progress, cancellationToken);

            if (cutResult.IsFailure)
                return Result<TrimVideoResponseDto>.Failure(cutResult.ErrorMessage ?? "Fallo al recortar el video.");

            long size = _fileSystemService.GetFileSize(request.DestinationVideoPath);
            return Result<TrimVideoResponseDto>.Success(new TrimVideoResponseDto(
                Success: true,
                OutputFilePath: request.DestinationVideoPath,
                Duration: timeRanges[0].Duration,
                OutputSizeBytes: size));
        }

        // Múltiples segmentos: recortar a temporales y unir con Concat Demuxer
        var tempFolder = Path.Combine(Path.GetTempPath(), "RecortadorMulti_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        var tempSegmentPaths = new List<string>();
        TimeSpan totalDuration = TimeSpan.Zero;

        try
        {
            int totalSteps = timeRanges.Count + 1;

            for (int i = 0; i < timeRanges.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var range = timeRanges[i];
                totalDuration += range.Duration;

                var tempPath = Path.Combine(tempFolder, $"part_{i:D4}.mp4");
                tempSegmentPaths.Add(tempPath);

                var job = new CutJob(
                    request.SourceVideoPath,
                    range,
                    tempPath,
                    CutStrategy.LosslessStreamCopy,
                    audioConfig);

                var stepStartProgress = (double)i / totalSteps * 100.0;
                var stepFactor = 1.0 / totalSteps;

                var subProgress = new Progress<double>(p =>
                {
                    progress?.Report(stepStartProgress + (p * stepFactor));
                });

                Result segmentResult = audioConfig.Mode is AudioMode.Replace or AudioMode.Mix or AudioMode.OverlayClips
                    ? await _audioMuxer.ProcessAudioAsync(
                        request.SourceVideoPath,
                        range,
                        audioConfig,
                        tempPath,
                        subProgress,
                        cancellationToken)
                    : await _videoTrimmer.TrimAsync(job, subProgress, cancellationToken);

                if (segmentResult.IsFailure)
                    return Result<TrimVideoResponseDto>.Failure($"Fallo al recortar segmento #{i + 1}: {segmentResult.ErrorMessage}");
            }

            // Paso final: Unir con Concat Demuxer
            var mergeJob = new MergeJob(tempSegmentPaths, request.DestinationVideoPath);
            var concatStartProgress = (double)timeRanges.Count / totalSteps * 100.0;
            var concatFactor = 1.0 / totalSteps;

            var concatProgress = new Progress<double>(p =>
            {
                progress?.Report(concatStartProgress + (p * concatFactor));
            });

            var mergeResult = await _videoConcatenator.ConcatenateAsync(mergeJob, concatProgress, cancellationToken);
            if (mergeResult.IsFailure)
                return Result<TrimVideoResponseDto>.Failure($"Fallo al unir los segmentos: {mergeResult.ErrorMessage}");

            progress?.Report(100.0);

            long finalSize = _fileSystemService.FileExists(request.DestinationVideoPath)
                ? _fileSystemService.GetFileSize(request.DestinationVideoPath)
                : 0;

            return Result<TrimVideoResponseDto>.Success(new TrimVideoResponseDto(
                Success: true,
                OutputFilePath: request.DestinationVideoPath,
                Duration: totalDuration,
                OutputSizeBytes: finalSize));
        }
        finally
        {
            // Limpieza segura del directorio temporal
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, recursive: true);
                }
            }
            catch
            {
                // Ignorar errores menores al limpiar temporales
            }
        }
    }

    public async Task<Result<TrimVideoResponseDto>> ExecuteMergeFilesAsync(
        MergeFilesRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Result<TrimVideoResponseDto>.Failure("La solicitud de unión no puede ser nula.");

        if (request.SourceVideoPaths == null || request.SourceVideoPaths.Count < 2)
            return Result<TrimVideoResponseDto>.Failure("Se requieren al menos 2 archivos de video para unir.");

        if (string.IsNullOrWhiteSpace(request.DestinationVideoPath))
            return Result<TrimVideoResponseDto>.Failure("La ruta de destino es obligatoria.");

        foreach (var path in request.SourceVideoPaths)
        {
            if (!_fileSystemService.FileExists(path))
                return Result<TrimVideoResponseDto>.Failure($"El archivo de origen no existe: {path}");
        }

        var mergeJob = new MergeJob(request.SourceVideoPaths, request.DestinationVideoPath);
        var mergeResult = await _videoConcatenator.ConcatenateAsync(mergeJob, progress, cancellationToken);

        if (mergeResult.IsFailure)
            return Result<TrimVideoResponseDto>.Failure(mergeResult.ErrorMessage ?? "Fallo al unir los archivos de video.");

        long finalSize = _fileSystemService.FileExists(request.DestinationVideoPath)
            ? _fileSystemService.GetFileSize(request.DestinationVideoPath)
            : 0;

        return Result<TrimVideoResponseDto>.Success(new TrimVideoResponseDto(
            Success: true,
            OutputFilePath: request.DestinationVideoPath,
            Duration: TimeSpan.Zero,
            OutputSizeBytes: finalSize));
    }
}
