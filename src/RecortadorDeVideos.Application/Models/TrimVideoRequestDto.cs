using RecortadorDeVideos.Domain.Enums;

namespace RecortadorDeVideos.Application.Models;

public sealed record TrimVideoRequestDto(
    string SourceVideoPath,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string DestinationVideoPath,
    CutStrategy Strategy = CutStrategy.LosslessStreamCopy,
    AudioMode AudioMode = AudioMode.KeepOriginal,
    string? ExternalAudioPath = null,
    double MainVolume = 1.0,
    double BackgroundVolume = 0.3);

public sealed record TrimVideoResponseDto(
    bool Success,
    string OutputFilePath,
    TimeSpan Duration,
    long OutputSizeBytes,
    string? ErrorMessage = null);
