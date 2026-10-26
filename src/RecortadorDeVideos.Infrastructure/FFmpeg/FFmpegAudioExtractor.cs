using System.Globalization;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegAudioExtractor : IAudioExtractor
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;

    public FFmpegAudioExtractor(IFFmpegBinaryLocator locator, IFFmpegProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Result> ExtractAudioAsync(
        string sourceVideoPath,
        TimeRange timeRange,
        string destinationAudioPath,
        AudioExportFormat format,
        int bitrateKbps = 192,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = _locator.GetFFmpegPath();

        var start = timeRange.StartToFFmpeg();
        var duration = timeRange.DurationToFFmpeg();

        int validBitrate = Math.Clamp(bitrateKbps, 64, 320);

        string codecFlags = format switch
        {
            AudioExportFormat.Mp3 => $"-c:a libmp3lame -b:a {validBitrate}k",
            AudioExportFormat.Aac => $"-c:a aac -b:a {validBitrate}k",
            AudioExportFormat.M4a => $"-c:a aac -b:a {validBitrate}k",
            AudioExportFormat.Wav => "-c:a pcm_s16le",
            _ => $"-c:a libmp3lame -b:a {validBitrate}k"
        };

        var arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -t {duration} -vn {codecFlags} -avoid_negative_ts make_zero \"{destinationAudioPath}\"";

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: timeRange.Duration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
        {
            return Result.Failure($"Error al extraer el audio: {runResult.ErrorMessage}");
        }

        return Result.Success();
    }
}
