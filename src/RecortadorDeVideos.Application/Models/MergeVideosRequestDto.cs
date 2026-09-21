using RecortadorDeVideos.Domain.Enums;

namespace RecortadorDeVideos.Application.Models;

public sealed record TimeSpanPairDto(TimeSpan Start, TimeSpan End, string? Label = null);

/// <summary>
/// Solicitud para recortar múltiples segmentos de un mismo video y unirlos en un único archivo sin pérdida.
/// </summary>
public sealed record MergeSegmentsRequestDto(
    string SourceVideoPath,
    IReadOnlyList<TimeSpanPairDto> Segments,
    string DestinationVideoPath,
    AudioMode AudioMode = AudioMode.KeepOriginal,
    string? ExternalAudioPath = null,
    double MainVolume = 1.0,
    double BackgroundVolume = 0.3,
    IReadOnlyList<AudioOverlayClipDto>? AudioClips = null);

/// <summary>
/// Solicitud para unir directamente una lista de archivos de video independientes en uno solo.
/// </summary>
public sealed record MergeFilesRequestDto(
    IReadOnlyList<string> SourceVideoPaths,
    string DestinationVideoPath);
