using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato de puerto para análisis e inspección de metadatos de medios audiovisuales (FFprobe).
/// </summary>
public interface IMediaAnalyzer
{
    /// <summary>
    /// Analiza un archivo de video y extrae metadatos técnicos (duración, códecs, resolución).
    /// </summary>
    Task<Result<VideoMetadata>> AnalyzeAsync(string videoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extrae la lista de posiciones temporales de todos los Keyframes (I-Frames).
    /// </summary>
    Task<Result<IReadOnlyList<TimeSpan>>> ExtractKeyframesAsync(string videoPath, CancellationToken cancellationToken = default);
}
