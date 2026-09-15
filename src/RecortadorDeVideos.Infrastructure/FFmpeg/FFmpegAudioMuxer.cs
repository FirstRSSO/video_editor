using System.Globalization;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegAudioMuxer : IAudioMuxer
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;

    public FFmpegAudioMuxer(IFFmpegBinaryLocator locator, IFFmpegProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Result> ProcessAudioAsync(
        string sourceVideoPath,
        TimeRange timeRange,
        AudioTrackConfig audioConfig,
        string destinationVideoPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = _locator.GetFFmpegPath();
        var start = timeRange.StartToFFmpeg();
        var end = timeRange.EndToFFmpeg();

        string arguments;

        if (audioConfig.Mode == AudioMode.Replace)
        {
            // Reemplazar audio: Copiar video idéntico, tomar audio de archivo externo codificando a AAC
            arguments = $"-y -ss {start} -to {end} -i \"{sourceVideoPath}\" -i \"{audioConfig.ExternalAudioPath}\" " +
                        $"-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -shortest -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
        }
        else if (audioConfig.Mode == AudioMode.Mix)
        {
            var mainVol = audioConfig.MainVolume.ToString("F2", CultureInfo.InvariantCulture);
            var bgVol = audioConfig.BackgroundVolume.ToString("F2", CultureInfo.InvariantCulture);

            // Mezclar pistas: audio original ajustado + música/audio de fondo ajustado con filtro amix
            var filter = $"[0:a]volume={mainVol}[a0];[1:a]volume={bgVol}[a1];[a0][a1]amix=inputs=2:duration=first[aout]";

            arguments = $"-y -ss {start} -to {end} -i \"{sourceVideoPath}\" -i \"{audioConfig.ExternalAudioPath}\" " +
                        $"-filter_complex \"{filter}\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
        }
        else
        {
            // Modo estándar o mudo
            string audioFlag = audioConfig.Mode == AudioMode.Mute ? "-an" : "-c:a copy";
            arguments = $"-y -ss {start} -to {end} -i \"{sourceVideoPath}\" -c:v copy {audioFlag} -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
        }

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: timeRange.Duration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
            return Result.Failure($"Fallo en el multiplexado/mezcla de audio: {runResult.ErrorMessage}");

        return Result.Success();
    }
}
