using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato de puerto para la ejecución del recorte de video sin pérdida (FFmpeg).
/// </summary>
public interface IVideoTrimmer
{
    /// <summary>
    /// Ejecuta el recorte del video respetando la estrategia y configuraciones indicadas.
    /// </summary>
    Task<Result> TrimAsync(
        CutJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
