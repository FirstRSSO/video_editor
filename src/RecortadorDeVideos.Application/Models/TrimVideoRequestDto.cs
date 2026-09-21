using RecortadorDeVideos.Domain.Enums;

namespace RecortadorDeVideos.Application.Models;

/// <summary>
/// DTO para representar un clip de audio posicionado temporalmente.
/// </summary>
public sealed record AudioOverlayClipDto(
    string FilePath,
    TimeSpan StartTime,
    double Volume = 1.0,
    string? Label = null);

public sealed record TrimVideoRequestDto(
    string SourceVideoPath,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string DestinationVideoPath,
    CutStrategy Strategy = CutStrategy.LosslessStreamCopy,
    AudioMode AudioMode = AudioMode.KeepOriginal,
    string? ExternalAudioPath = null,
    double MainVolume = 1.0,
    double BackgroundVolume = 0.3,
    IReadOnlyList<AudioOverlayClipDto>? AudioClips = null);

public sealed record TrimVideoResponseDto(
    bool Success,
    string OutputFilePath,
    TimeSpan Duration,
    long OutputSizeBytes,
    string? ErrorMessage = null);
