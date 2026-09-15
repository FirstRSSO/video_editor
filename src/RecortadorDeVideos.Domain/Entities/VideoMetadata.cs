namespace RecortadorDeVideos.Domain.Entities;

/// <summary>
/// Metadatos técnicos extraídos de un archivo de video.
/// </summary>
public sealed class VideoMetadata
{
    public string FilePath { get; }
    public TimeSpan Duration { get; }
    public int Width { get; }
    public int Height { get; }
    public double FrameRate { get; }
    public string VideoCodec { get; }
    public string? AudioCodec { get; }
    public long FileSizeBytes { get; }
    public IReadOnlyList<TimeSpan> KeyframeTimestamps { get; }

    public VideoMetadata(
        string filePath,
        TimeSpan duration,
        int width,
        int height,
        double frameRate,
        string videoCodec,
        string? audioCodec,
        long fileSizeBytes,
        IEnumerable<TimeSpan>? keyframeTimestamps = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("La ruta del archivo de video no puede ser nula ni vacía.", nameof(filePath));

        FilePath = filePath;
        Duration = duration;
        Width = width;
        Height = height;
        FrameRate = frameRate;
        VideoCodec = videoCodec;
        AudioCodec = audioCodec;
        FileSizeBytes = fileSizeBytes;
        KeyframeTimestamps = keyframeTimestamps?.ToList() ?? new List<TimeSpan>();
    }

    /// <summary>
    /// Encuentra el fotograma clave (Keyframe) más cercano a una posición temporal dada.
    /// </summary>
    public TimeSpan? FindNearestKeyframe(TimeSpan targetTime)
    {
        if (KeyframeTimestamps.Count == 0) return null;

        return KeyframeTimestamps
            .OrderBy(kf => Math.Abs((kf - targetTime).TotalMilliseconds))
            .First();
    }
}
