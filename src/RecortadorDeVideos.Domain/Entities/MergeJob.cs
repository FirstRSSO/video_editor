namespace RecortadorDeVideos.Domain.Entities;

/// <summary>
/// Representa una orden de unión (concatenación sin pérdida) de múltiples archivos de video.
/// </summary>
public sealed class MergeJob
{
    public Guid Id { get; }
    public IReadOnlyList<string> SourceVideoPaths { get; }
    public string DestinationVideoPath { get; }
    public DateTime CreatedAtUtc { get; }

    public MergeJob(IReadOnlyList<string> sourceVideoPaths, string destinationVideoPath)
    {
        if (sourceVideoPaths == null || sourceVideoPaths.Count < 2)
            throw new ArgumentException("Se requieren al menos dos videos para realizar una unión.", nameof(sourceVideoPaths));

        foreach (var path in sourceVideoPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ninguna ruta de video de origen puede estar vacía.", nameof(sourceVideoPaths));
        }

        if (string.IsNullOrWhiteSpace(destinationVideoPath))
            throw new ArgumentException("La ruta del video de destino no puede estar vacía.", nameof(destinationVideoPath));

        Id = Guid.NewGuid();
        SourceVideoPaths = sourceVideoPaths;
        DestinationVideoPath = destinationVideoPath;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
