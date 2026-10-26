using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;

namespace RecortadorDeVideos.Application.UseCases.ExtractAudio;

public interface IExtractAudioUseCase
{
    Task<Result<ExtractAudioResponseDto>> ExecuteAsync(
        ExtractAudioRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
