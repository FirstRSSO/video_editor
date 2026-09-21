using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato de infraestructura para la concatenación o unión sin pérdida de múltiples archivos de video.
/// </summary>
public interface IVideoConcatenator
{
    Task<Result> ConcatenateAsync(
        MergeJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
