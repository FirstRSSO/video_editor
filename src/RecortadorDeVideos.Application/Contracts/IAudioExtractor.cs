using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato para extraer pistas de audio directamente desde un archivo de video.
/// </summary>
public interface IAudioExtractor
{
    Task<Result> ExtractAudioAsync(
        string sourceVideoPath,
        TimeRange timeRange,
        string destinationAudioPath,
        AudioExportFormat format,
        int bitrateKbps = 192,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
