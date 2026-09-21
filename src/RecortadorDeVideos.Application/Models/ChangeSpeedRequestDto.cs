namespace RecortadorDeVideos.Application.Models;

/// <summary>
/// DTO con los parámetros para solicitar el cambio de velocidad de un video.
/// </summary>
public record ChangeSpeedRequestDto(
    string SourceVideoPath,
    string DestinationVideoPath,
    double SpeedMultiplier,
    TimeSpan OriginalDuration,
    bool MuteAudio = false);

/// <summary>
/// DTO con el resultado de la operación de cambio de velocidad.
/// </summary>
public record ChangeSpeedResponseDto(
    bool Success,
    string OutputFilePath,
    TimeSpan NewDuration,
    long OutputSizeBytes);
