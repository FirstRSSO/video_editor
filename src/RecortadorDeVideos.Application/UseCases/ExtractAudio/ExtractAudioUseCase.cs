using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Application.UseCases.ExtractAudio;

public class ExtractAudioUseCase : IExtractAudioUseCase
{
    private readonly IAudioExtractor _audioExtractor;
    private readonly IFileSystemService _fileSystemService;

    public ExtractAudioUseCase(
        IAudioExtractor audioExtractor,
        IFileSystemService fileSystemService)
    {
        _audioExtractor = audioExtractor ?? throw new ArgumentNullException(nameof(audioExtractor));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<Result<ExtractAudioResponseDto>> ExecuteAsync(
        ExtractAudioRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Result<ExtractAudioResponseDto>.Failure("La solicitud no puede ser nula.");

        if (string.IsNullOrWhiteSpace(request.SourceVideoPath))
            return Result<ExtractAudioResponseDto>.Failure("La ruta del archivo de video origen no puede estar vacía.");

        if (string.IsNullOrWhiteSpace(request.DestinationAudioPath))
            return Result<ExtractAudioResponseDto>.Failure("La ruta del archivo de audio de destino no puede estar vacía.");

        if (!_fileSystemService.FileExists(request.SourceVideoPath))
            return Result<ExtractAudioResponseDto>.Failure($"El archivo de origen no existe: {request.SourceVideoPath}");

        if (!TimeRange.TryCreate(request.StartTime, request.EndTime, out var timeRange, out var timeError))
            return Result<ExtractAudioResponseDto>.Failure(timeError!);

        // Asegurar que el directorio de salida existe
        var destDir = Path.GetDirectoryName(request.DestinationAudioPath);
        if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var extractResult = await _audioExtractor.ExtractAudioAsync(
            request.SourceVideoPath,
            timeRange!,
            request.DestinationAudioPath,
            request.Format,
            request.BitrateKbps,
            progress,
            cancellationToken);

        if (extractResult.IsFailure)
        {
            return Result<ExtractAudioResponseDto>.Failure(extractResult.ErrorMessage!);
        }

        if (!_fileSystemService.FileExists(request.DestinationAudioPath))
        {
            return Result<ExtractAudioResponseDto>.Failure("El archivo de audio final no fue generado.");
        }

        var outputSize = _fileSystemService.GetFileSize(request.DestinationAudioPath);

        return Result<ExtractAudioResponseDto>.Success(new ExtractAudioResponseDto(
            Success: true,
            OutputFilePath: request.DestinationAudioPath,
            Duration: timeRange!.Duration,
            OutputSizeBytes: outputSize));
    }
}
