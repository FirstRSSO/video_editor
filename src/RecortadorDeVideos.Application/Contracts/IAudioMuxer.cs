using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato de puerto para operaciones de multiplexado, mezcla y reemplazo de pistas de audio en videos.
/// </summary>
public interface IAudioMuxer
{
    /// <summary>
    /// Reemplaza o mezcla el audio en un video preservando intacto el flujo de video original (-c:v copy).
    /// </summary>
    Task<Result> ProcessAudioAsync(
        string sourceVideoPath,
        TimeRange timeRange,
        AudioTrackConfig audioConfig,
        string destinationVideoPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
