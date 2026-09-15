namespace RecortadorDeVideos.Application.Models;

public sealed record VideoMetadataDto(
    string FilePath,
    TimeSpan Duration,
    int Width,
    int Height,
    double FrameRate,
    string VideoCodec,
    string? AudioCodec,
    long FileSizeBytes,
    int KeyframeCount);
