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
    public TimeSpan InPoint { get; }
    public TimeSpan? Duration { get; }

    public AudioOverlayClip(
        string filePath, 
        TimeSpan startTime, 
        double volume = 1.0, 
        string? label = null,
        TimeSpan? inPoint = null,
        TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("La ruta del clip de audio no puede estar vacía.", nameof(filePath));

        if (startTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startTime), "El tiempo de inicio del clip no puede ser negativo.");

        if (volume < 0.0 || volume > 2.0)
            throw new ArgumentOutOfRangeException(nameof(volume), "El volumen del clip debe estar entre 0.0 y 2.0.");

        if (inPoint.HasValue && inPoint.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inPoint), "El punto de inicio interno no puede ser negativo.");

        if (duration.HasValue && duration.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "La duración debe ser mayor que cero.");

        FilePath = filePath;
        StartTime = startTime;
        Volume = volume;
        Label = label;
        InPoint = inPoint ?? TimeSpan.Zero;
        Duration = duration;
    }
}
