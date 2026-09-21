namespace RecortadorDeVideos.Domain.ValueObjects;

/// <summary>
/// Representa un clip de audio o locución posicionado en un punto temporal específico del video.
/// </summary>
public sealed record AudioOverlayClip
{
    public string FilePath { get; }
    public TimeSpan StartTime { get; }
    public double Volume { get; }
    public string? Label { get; }

    public AudioOverlayClip(string filePath, TimeSpan startTime, double volume = 1.0, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("La ruta del clip de audio no puede estar vacía.", nameof(filePath));

        if (startTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startTime), "El tiempo de inicio del clip no puede ser negativo.");

        if (volume < 0.0 || volume > 2.0)
            throw new ArgumentOutOfRangeException(nameof(volume), "El volumen del clip debe estar entre 0.0 y 2.0.");

        FilePath = filePath;
        StartTime = startTime;
        Volume = volume;
        Label = label;
    }
}
