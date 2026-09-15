using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Domain.Entities;

/// <summary>
/// Representa la solicitud u orden de corte (Aggregate Root del proceso).
/// </summary>
public sealed class CutJob
{
    public Guid Id { get; }
    public string SourceVideoPath { get; }
    public TimeRange TimeRange { get; }
    public string DestinationVideoPath { get; }
    public CutStrategy Strategy { get; }
    public AudioTrackConfig AudioConfig { get; }
    public DateTime CreatedAtUtc { get; }

    public CutJob(
        string sourceVideoPath,
        TimeRange timeRange,
        string destinationVideoPath,
        CutStrategy strategy = CutStrategy.LosslessStreamCopy,
        AudioTrackConfig? audioConfig = null)
    {
        if (string.IsNullOrWhiteSpace(sourceVideoPath))
            throw new ArgumentException("La ruta del video de origen es requerida.", nameof(sourceVideoPath));

        if (string.IsNullOrWhiteSpace(destinationVideoPath))
            throw new ArgumentException("La ruta del video de destino es requerida.", nameof(destinationVideoPath));

        if (string.Equals(sourceVideoPath, destinationVideoPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El archivo de destino no puede ser el mismo archivo de origen.");

        Id = Guid.NewGuid();
        SourceVideoPath = sourceVideoPath;
        TimeRange = timeRange ?? throw new ArgumentNullException(nameof(timeRange));
        DestinationVideoPath = destinationVideoPath;
        Strategy = strategy;
        AudioConfig = audioConfig ?? AudioTrackConfig.KeepOriginal();
        CreatedAtUtc = DateTime.UtcNow;
    }
}
