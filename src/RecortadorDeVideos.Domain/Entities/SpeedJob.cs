namespace RecortadorDeVideos.Domain.Entities;

/// <summary>
/// Representa una solicitud u orden de cambio de velocidad de video.
/// </summary>
public sealed class SpeedJob
{
    public Guid Id { get; }
    public string SourceVideoPath { get; }
    public string DestinationVideoPath { get; }
    public double SpeedMultiplier { get; }
    public bool MuteAudio { get; }
    public TimeSpan OriginalDuration { get; }
    public TimeSpan NewDuration => TimeSpan.FromSeconds(OriginalDuration.TotalSeconds / SpeedMultiplier);
    public DateTime CreatedAtUtc { get; }

    public SpeedJob(
        string sourceVideoPath,
        string destinationVideoPath,
        double speedMultiplier,
        TimeSpan originalDuration,
        bool muteAudio = false)
    {
        if (string.IsNullOrWhiteSpace(sourceVideoPath))
            throw new ArgumentException("La ruta del video de origen es requerida.", nameof(sourceVideoPath));

        if (string.IsNullOrWhiteSpace(destinationVideoPath))
            throw new ArgumentException("La ruta del video de destino es requerida.", nameof(destinationVideoPath));

        if (string.Equals(sourceVideoPath, destinationVideoPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El archivo de destino no puede ser el mismo archivo de origen.");

        if (speedMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier), "El multiplicador de velocidad debe ser mayor a 0.");

        if (originalDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(originalDuration), "La duración original del video debe ser mayor a cero.");

        Id = Guid.NewGuid();
        SourceVideoPath = sourceVideoPath;
        DestinationVideoPath = destinationVideoPath;
        SpeedMultiplier = speedMultiplier;
        OriginalDuration = originalDuration;
        MuteAudio = muteAudio;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
