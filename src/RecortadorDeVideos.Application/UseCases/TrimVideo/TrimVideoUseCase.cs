using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Application.UseCases.TrimVideo;

public interface ITrimVideoUseCase
{
    Task<Result<TrimVideoResponseDto>> ExecuteAsync(
        TrimVideoRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public class TrimVideoUseCase : ITrimVideoUseCase
{
    private readonly IVideoTrimmer _videoTrimmer;
    private readonly IAudioMuxer _audioMuxer;
    private readonly IFileSystemService _fileSystemService;

    public TrimVideoUseCase(
        IVideoTrimmer videoTrimmer,
        IAudioMuxer audioMuxer,
        IFileSystemService fileSystemService)
    {
        _videoTrimmer = videoTrimmer ?? throw new ArgumentNullException(nameof(videoTrimmer));
        _audioMuxer = audioMuxer ?? throw new ArgumentNullException(nameof(audioMuxer));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<Result<TrimVideoResponseDto>> ExecuteAsync(
        TrimVideoRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Result<TrimVideoResponseDto>.Failure("La solicitud no puede ser nula.");

        if (!_fileSystemService.FileExists(request.SourceVideoPath))
            return Result<TrimVideoResponseDto>.Failure($"El video de origen no existe: {request.SourceVideoPath}");

        if (!TimeRange.TryCreate(request.StartTime, request.EndTime, out var timeRange, out var timeError))
            return Result<TrimVideoResponseDto>.Failure(timeError!);

        // Construir configuración de audio
        AudioTrackConfig audioConfig = request.AudioMode switch
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

        var cutJob = new CutJob(
            request.SourceVideoPath,
            timeRange!,
            request.DestinationVideoPath,
            request.Strategy,
            audioConfig);

        Result executionResult;

        // Si se requiere reemplazo o mezcla de audio, se delega a IAudioMuxer; si es corte directo a IVideoTrimmer
        if (request.AudioMode is AudioMode.Replace or AudioMode.Mix)
        {
            executionResult = await _audioMuxer.ProcessAudioAsync(
                cutJob.SourceVideoPath,
                cutJob.TimeRange,
                cutJob.AudioConfig,
                cutJob.DestinationVideoPath,
                progress,
                cancellationToken);
        }
        else
        {
            executionResult = await _videoTrimmer.TrimAsync(
                cutJob,
                progress,
                cancellationToken);
        }

        if (executionResult.IsFailure)
        {
            return Result<TrimVideoResponseDto>.Failure(executionResult.ErrorMessage!);
        }

        long outputSize = _fileSystemService.FileExists(cutJob.DestinationVideoPath)
            ? _fileSystemService.GetFileSize(cutJob.DestinationVideoPath)
            : 0;

        var response = new TrimVideoResponseDto(
            Success: true,
            OutputFilePath: cutJob.DestinationVideoPath,
            Duration: cutJob.TimeRange.Duration,
            OutputSizeBytes: outputSize);

        return Result<TrimVideoResponseDto>.Success(response);
    }
}
