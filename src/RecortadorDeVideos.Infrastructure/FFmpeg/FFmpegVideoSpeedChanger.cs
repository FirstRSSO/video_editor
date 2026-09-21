using System.Globalization;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegVideoSpeedChanger : IVideoSpeedChanger
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;
    private readonly IMediaAnalyzer _analyzer;

    public FFmpegVideoSpeedChanger(
        IFFmpegBinaryLocator locator,
        IFFmpegProcessRunner runner,
        IMediaAnalyzer analyzer)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public async Task<Result> ChangeSpeedAsync(
        SpeedJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = _locator.GetFFmpegPath();

        // 1. Analizar si el video tiene una pista de audio válida
        bool hasAudio = false;
        var analyzeResult = await _analyzer.AnalyzeAsync(job.SourceVideoPath, cancellationToken);
        if (analyzeResult.IsSuccess && !string.IsNullOrWhiteSpace(analyzeResult.Value?.AudioCodec))
        {
            hasAudio = true;
        }

        // 2. Construir filtro de video (ajuste de timestamps de presentación PTS)
        // Para acelerar N veces: setpts=(1/N)*PTS
        var ptsFactor = (1.0 / job.SpeedMultiplier).ToString("0.######", CultureInfo.InvariantCulture);
        var videoFilter = $"setpts={ptsFactor}*PTS";

        // 3. Construir configuración de audio
        string audioArgs;
        if (job.MuteAudio || !hasAudio)
        {
            audioArgs = "-an";
        }
        else
        {
            var atempoFilter = BuildAtempoFilter(job.SpeedMultiplier);
            audioArgs = $"-filter:a \"{atempoFilter}\" -c:a aac -b:a 192k";
        }

        // 4. Construir comando completo de FFmpeg
        // Recodificación de video de alta calidad usando x264 veryfast (CRF 19)
        var arguments = $"-y -i \"{job.SourceVideoPath}\" -filter:v \"{videoFilter}\" -c:v libx264 -preset veryfast -crf 19 {audioArgs} \"{job.DestinationVideoPath}\"";

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: job.NewDuration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
        {
            return Result.Failure($"Fallo al cambiar la velocidad del video: {runResult.ErrorMessage}");
        }

        return Result.Success();
    }

    public static string BuildAtempoFilter(double speed)
    {
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed), "El multiplicador de velocidad debe ser mayor a cero.");

        var filters = new List<string>();
        double remaining = speed;

        while (remaining > 2.0)
        {
            filters.Add("atempo=2.0");
            remaining /= 2.0;
        }

        while (remaining < 0.5)
        {
            filters.Add("atempo=0.5");
            remaining /= 0.5;
        }

        var formatted = string.Format(CultureInfo.InvariantCulture, "{0:0.0000}", remaining).TrimEnd('0').TrimEnd('.');
        filters.Add($"atempo={formatted}");

        return string.Join(",", filters);
    }
}
