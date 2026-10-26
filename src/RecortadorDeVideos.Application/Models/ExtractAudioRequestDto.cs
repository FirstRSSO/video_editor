using RecortadorDeVideos.Domain.Enums;

namespace RecortadorDeVideos.Application.Models;

/// <summary>
/// Solicitud para extraer la pista de audio de un fragmento de video sin recodificar video.
/// </summary>
public sealed record ExtractAudioRequestDto(
    string SourceVideoPath,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string DestinationAudioPath,
    AudioExportFormat Format = AudioExportFormat.Mp3,
    int BitrateKbps = 192);

/// <summary>
/// Respuesta tras la extracción de audio.
/// </summary>
public sealed record ExtractAudioResponseDto(
    bool Success,
    string OutputFilePath,
    TimeSpan Duration,
    long OutputSizeBytes,
    string? ErrorMessage = null);
