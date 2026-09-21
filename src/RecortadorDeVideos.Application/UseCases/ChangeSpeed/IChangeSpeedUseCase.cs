using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;

namespace RecortadorDeVideos.Application.UseCases.ChangeSpeed;

public interface IChangeSpeedUseCase
{
    Task<Result<ChangeSpeedResponseDto>> ExecuteAsync(
        ChangeSpeedRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
