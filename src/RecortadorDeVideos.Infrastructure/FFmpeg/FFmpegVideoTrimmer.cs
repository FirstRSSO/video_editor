using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegVideoTrimmer : IVideoTrimmer
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;

    public FFmpegVideoTrimmer(IFFmpegBinaryLocator locator, IFFmpegProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Result> TrimAsync(
        CutJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = _locator.GetFFmpegPath();

        // Construir argumentos para Stream Copy sin pérdida
        var start = job.TimeRange.StartToFFmpeg();
        var end = job.TimeRange.EndToFFmpeg();

        string audioFlag = job.AudioConfig.Mode == AudioMode.Mute ? "-an" : "-c:a copy";

        // -ss antes de -i para fast seek
        // -avoid_negative_ts make_zero para reiniciar las marcas temporales en 0
        var arguments = $"-y -ss {start} -to {end} -i \"{job.SourceVideoPath}\" -c:v copy {audioFlag} -avoid_negative_ts make_zero \"{job.DestinationVideoPath}\"";

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: job.TimeRange.Duration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
            return Result.Failure($"Fallo en el recorte de video: {runResult.ErrorMessage}");

        return Result.Success();
    }
}
