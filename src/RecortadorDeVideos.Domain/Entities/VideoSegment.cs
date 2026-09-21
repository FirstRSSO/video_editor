using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Domain.Entities;

/// <summary>
/// Representa un fragmento o segmento individual definido dentro de un video.
/// </summary>
public sealed record VideoSegment
{
    public Guid Id { get; }
    public TimeRange TimeRange { get; }
    public string Label { get; }
    public TimeSpan Duration => TimeRange.Duration;

    public VideoSegment(TimeRange timeRange, string? label = null)
    {
        Id = Guid.NewGuid();
        TimeRange = timeRange ?? throw new ArgumentNullException(nameof(timeRange));
        Label = label ?? $"Segmento {timeRange.StartToFFmpeg()} - {timeRange.EndToFFmpeg()}";
    }
}
