using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato de puerto para cambiar la velocidad de reproducción de un video (acelerar o desacelerar).
/// </summary>
public interface IVideoSpeedChanger
{
    /// <summary>
    /// Ejecuta el cambio de velocidad en el video ajustando video y audio según la configuración del SpeedJob.
    /// </summary>
    Task<Result> ChangeSpeedAsync(
        SpeedJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
