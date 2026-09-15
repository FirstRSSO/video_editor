using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;

namespace RecortadorDeVideos.Application.UseCases.AnalyzeVideo;

public interface IAnalyzeVideoUseCase
{
    Task<Result<VideoMetadataDto>> ExecuteAsync(string videoPath, CancellationToken cancellationToken = default);
}

public class AnalyzeVideoUseCase : IAnalyzeVideoUseCase
{
    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly IFileSystemService _fileSystemService;

    public AnalyzeVideoUseCase(IMediaAnalyzer mediaAnalyzer, IFileSystemService fileSystemService)
    {
        _mediaAnalyzer = mediaAnalyzer ?? throw new ArgumentNullException(nameof(mediaAnalyzer));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<Result<VideoMetadataDto>> ExecuteAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return Result<VideoMetadataDto>.Failure("La ruta del archivo de video no puede estar vacía.");

        if (!_fileSystemService.FileExists(videoPath))
            return Result<VideoMetadataDto>.Failure($"El archivo especificado no existe: {videoPath}");

        var analysisResult = await _mediaAnalyzer.AnalyzeAsync(videoPath, cancellationToken);
        if (analysisResult.IsFailure)
            return Result<VideoMetadataDto>.Failure(analysisResult.ErrorMessage!);

        var metadata = analysisResult.Value;
        var dto = new VideoMetadataDto(
            metadata.FilePath,
            metadata.Duration,
            metadata.Width,
            metadata.Height,
            metadata.FrameRate,
            metadata.VideoCodec,
            metadata.AudioCodec,
            metadata.FileSizeBytes,
            metadata.KeyframeTimestamps.Count);

        return Result<VideoMetadataDto>.Success(dto);
    }
}
