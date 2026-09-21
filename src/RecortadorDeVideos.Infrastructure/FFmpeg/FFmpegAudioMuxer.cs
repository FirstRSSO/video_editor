using System.Globalization;
using System.Text;
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
        var duration = timeRange.DurationToFFmpeg();

        string arguments;

        // 1. Modo superposición de múltiples clips / locuciones por marcas de tiempo
        if (audioConfig.Mode == AudioMode.OverlayClips ||
            (audioConfig.Clips.Count > 1) ||
            (audioConfig.Clips.Count == 1 && audioConfig.Clips[0].StartTime > TimeSpan.Zero))
        {
            var activeClips = audioConfig.Clips
                .Where(c => c.StartTime < timeRange.EndTime)
                .ToList();

            if (activeClips.Count == 0)
            {
                // Si no hay clips en el rango de corte
                string fallbackAudio = audioConfig.MainVolume > 0.001 ? "-c:a copy" : "-an";
                arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -t {duration} -c:v copy {fallbackAudio} -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
            }
            else
            {
                var filterSteps = new List<string>();
                var mixInputs = new List<string>();

                // Audio base: Si se conserva el original del video, se usa [0:a]. Si se silencia, generamos silencio base.
                if (audioConfig.MainVolume > 0.001 && audioConfig.Mode != AudioMode.Replace)
                {
                    var mainVol = audioConfig.MainVolume.ToString("F2", CultureInfo.InvariantCulture);
                    filterSteps.Add($"[0:a]volume={mainVol}[a0]");
                    mixInputs.Add("[a0]");
                }
                else
                {
                    var durSec = timeRange.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
                    filterSteps.Add($"aevalsrc=0:d={durSec}:s=44100[base]");
                    mixInputs.Add("[base]");
                }

                var inputsBuilder = new StringBuilder();
                inputsBuilder.Append($"-y -ss {start} -i \"{sourceVideoPath}\" ");

                for (int i = 0; i < activeClips.Count; i++)
                {
                    var clip = activeClips[i];
                    int inputIndex = i + 1;
                    inputsBuilder.Append($"-i \"{clip.FilePath}\" ");

                    var vol = clip.Volume.ToString("F2", CultureInfo.InvariantCulture);
                    var delay = clip.StartTime >= timeRange.StartTime ? clip.StartTime - timeRange.StartTime : TimeSpan.Zero;
                    long delayMs = (long)Math.Round(delay.TotalMilliseconds);

                    if (delayMs > 0)
                    {
                        filterSteps.Add($"[{inputIndex}:a]volume={vol},adelay={delayMs}:all=1[a{inputIndex}]");
                    }
                    else
                    {
                        filterSteps.Add($"[{inputIndex}:a]volume={vol}[a{inputIndex}]");
                    }
                    mixInputs.Add($"[a{inputIndex}]");
                }

                filterSteps.Add($"{string.Concat(mixInputs)}amix=inputs={mixInputs.Count}:duration=first:dropout_transition=0[aout]");
                var filterComplex = string.Join(";", filterSteps);

                inputsBuilder.Append($"-t {duration} -filter_complex \"{filterComplex}\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -avoid_negative_ts make_zero \"{destinationVideoPath}\"");
                arguments = inputsBuilder.ToString();
            }
        }
        else if (audioConfig.Mode == AudioMode.Replace)
        {
            // Reemplazar audio: Copiar video idéntico, tomar audio de archivo externo codificando a AAC
            arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -i \"{audioConfig.ExternalAudioPath}\" -t {duration} " +
                        $"-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -shortest -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
        }
        else if (audioConfig.Mode == AudioMode.Mix)
        {
            var mainVol = audioConfig.MainVolume.ToString("F2", CultureInfo.InvariantCulture);
            var bgVol = audioConfig.BackgroundVolume.ToString("F2", CultureInfo.InvariantCulture);

            // Mezclar pistas: audio original ajustado + música/audio de fondo ajustado con filtro amix
            var filter = $"[0:a]volume={mainVol}[a0];[1:a]volume={bgVol}[a1];[a0][a1]amix=inputs=2:duration=first[aout]";

            arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -i \"{audioConfig.ExternalAudioPath}\" -t {duration} " +
                        $"-filter_complex \"{filter}\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
        }
        else
        {
            // Modo estándar o mudo
            string audioFlag = audioConfig.Mode == AudioMode.Mute ? "-an" : "-c:a copy";
            arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -t {duration} -c:v copy {audioFlag} -avoid_negative_ts make_zero \"{destinationVideoPath}\"";
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
