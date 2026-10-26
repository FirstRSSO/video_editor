using System.Text;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegVideoTrimmer : IVideoTrimmer
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;
    private readonly IMediaAnalyzer _analyzer;

    public FFmpegVideoTrimmer(
        IFFmpegBinaryLocator locator, 
        IFFmpegProcessRunner runner,
        IMediaAnalyzer analyzer)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public async Task<Result> TrimAsync(
        CutJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return job.Strategy switch
        {
            CutStrategy.KeyframeSnap => await TrimWithKeyframeSnapAsync(job, progress, cancellationToken),
            CutStrategy.SmartCut => await TrimWithSmartCutAsync(job, progress, cancellationToken),
            _ => await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken)
        };
    }

    private async Task<Result> TrimLosslessStreamCopyAsync(
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

        string audioFlag = audioConfig.Mode == AudioMode.Mute ? "-an" : "-c:a copy";

        // -ss antes de -i para fast seek, -t después de -i para recortar la duración exacta
        // -avoid_negative_ts make_zero para reiniciar las marcas temporales en 0
        var arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -t {duration} -c:v copy {audioFlag} -avoid_negative_ts make_zero \"{destinationVideoPath}\"";

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: timeRange.Duration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
            return Result.Failure($"Fallo en el recorte de video: {runResult.ErrorMessage}");

        return Result.Success();
    }

    private async Task<Result> TrimWithKeyframeSnapAsync(
        CutJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var keyframesResult = await _analyzer.ExtractKeyframesAsync(job.SourceVideoPath, cancellationToken);
        if (keyframesResult.IsFailure || keyframesResult.Value.Count == 0)
        {
            return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
        }

        var keyframes = keyframesResult.Value;
        var targetStart = job.TimeRange.StartTime;
        var targetEnd = job.TimeRange.EndTime;

        // Encontrar el keyframe más cercano al inicio deseado (priorizando el anterior para no perder nada)
        TimeSpan snappedStart = keyframes[0];
        var beforeKeyframes = keyframes.Where(k => k <= targetStart).ToList();
        if (beforeKeyframes.Count > 0)
        {
            snappedStart = beforeKeyframes.Last();
        }
        else
        {
            snappedStart = keyframes[0];
        }

        if (snappedStart >= targetEnd)
        {
            snappedStart = keyframes.LastOrDefault(k => k < targetEnd);
        }

        var snappedRange = TimeRange.Create(snappedStart, targetEnd);
        return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, snappedRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
    }

    private async Task<Result> TrimWithSmartCutAsync(
        CutJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var keyframesResult = await _analyzer.ExtractKeyframesAsync(job.SourceVideoPath, cancellationToken);
        if (keyframesResult.IsFailure || keyframesResult.Value.Count == 0)
        {
            return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
        }

        var keyframes = keyframesResult.Value;
        var start = job.TimeRange.StartTime;
        var end = job.TimeRange.EndTime;

        // Tolerancia de 60ms para considerar que el corte ya coincide con un keyframe
        var tolerance = TimeSpan.FromMilliseconds(60);
        bool startIsKeyframe = keyframes.Any(k => (k - start).Duration() <= tolerance);

        // Si el inicio ya es un keyframe, no requiere recodificación de pre-roll: corte directo stream copy
        if (startIsKeyframe)
        {
            return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
        }

        // Encontrar el siguiente keyframe después del punto de inicio
        var nextKfAfterStart = keyframes.FirstOrDefault(k => k > start + tolerance);

        // Si no hay keyframe posterior o el siguiente keyframe está más allá del final solicitado,
        // todo el tramo cabe dentro de un único GOP. Se recodifica solo este pequeño fragmento.
        if (nextKfAfterStart == default || nextKfAfterStart >= end)
        {
            return await ReencodeSegmentAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
        }

        // Smart Cut híbrido: Pre-roll recodificado + Segmento central copiado bit a bit + Post-roll si es necesario
        var tempDir = Path.Combine(Path.GetTempPath(), $"smartcut_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var tempFiles = new List<string>();
        try
        {
            // 1. Pre-roll: desde start hasta nextKfAfterStart (recodificado para sincronización exacta)
            var preRollPath = Path.Combine(tempDir, "preroll.mp4");
            var preRollRange = TimeRange.Create(start, nextKfAfterStart);
            var preRollResult = await ReencodeSegmentAsync(job.SourceVideoPath, preRollRange, job.AudioConfig, preRollPath, null, cancellationToken);
            if (preRollResult.IsFailure)
            {
                return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
            }
            tempFiles.Add(preRollPath);

            // 2. Comprobar si el final necesita post-roll
            var lastKfBeforeEnd = keyframes.LastOrDefault(k => k <= end - tolerance);

            if (lastKfBeforeEnd > nextKfAfterStart)
            {
                // Segmento principal sin pérdida (Stream Copy del 95%+ del video)
                var mainBodyPath = Path.Combine(tempDir, "body.mp4");
                var mainBodyRange = TimeRange.Create(nextKfAfterStart, lastKfBeforeEnd);
                var bodyResult = await TrimLosslessStreamCopyAsync(job.SourceVideoPath, mainBodyRange, job.AudioConfig, mainBodyPath, null, cancellationToken);
                if (bodyResult.IsFailure)
                {
                    return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
                }
                tempFiles.Add(mainBodyPath);

                // Segmento final (post-roll) desde lastKfBeforeEnd hasta end
                if (end > lastKfBeforeEnd + tolerance)
                {
                    var postRollPath = Path.Combine(tempDir, "postroll.mp4");
                    var postRollRange = TimeRange.Create(lastKfBeforeEnd, end);
                    var postResult = await ReencodeSegmentAsync(job.SourceVideoPath, postRollRange, job.AudioConfig, postRollPath, null, cancellationToken);
                    if (postResult.IsSuccess)
                    {
                        tempFiles.Add(postRollPath);
                    }
                }
            }
            else
            {
                // El resto desde nextKfAfterStart hasta end
                var remainderPath = Path.Combine(tempDir, "remainder.mp4");
                var remainderRange = TimeRange.Create(nextKfAfterStart, end);
                var remResult = await ReencodeSegmentAsync(job.SourceVideoPath, remainderRange, job.AudioConfig, remainderPath, null, cancellationToken);
                if (remResult.IsFailure)
                {
                    return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
                }
                tempFiles.Add(remainderPath);
            }

            // 3. Concatenar los tramos generados con Concat Demuxer sin recodificar
            var concatListPath = Path.Combine(tempDir, "concat.txt");
            var sb = new StringBuilder();
            foreach (var tf in tempFiles)
            {
                var norm = tf.Replace("\\", "/").Replace("'", "'\\''");
                sb.AppendLine($"file '{norm}'");
            }
            await File.WriteAllTextAsync(concatListPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);

            var ffmpeg = _locator.GetFFmpegPath();
            var concatArgs = $"-y -f concat -safe 0 -i \"{concatListPath}\" -c copy -avoid_negative_ts make_zero \"{job.DestinationVideoPath}\"";

            var concatResult = await _runner.ExecuteAsync(ffmpeg, concatArgs, expectedDuration: job.TimeRange.Duration, progress: progress, cancellationToken: cancellationToken);
            if (concatResult.IsFailure)
            {
                return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
            }

            return Result.Success();
        }
        catch
        {
            // Fallback ante cualquier eventualidad
            return await TrimLosslessStreamCopyAsync(job.SourceVideoPath, job.TimeRange, job.AudioConfig, job.DestinationVideoPath, progress, cancellationToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private async Task<Result> ReencodeSegmentAsync(
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

        string audioFlag = audioConfig.Mode == AudioMode.Mute ? "-an" : "-c:a aac -b:a 192k";

        // Re-codificar el fragmento quirúrgico con libx264 muy rápido manteniendo alta fidelidad visual (crf 18)
        var arguments = $"-y -ss {start} -i \"{sourceVideoPath}\" -t {duration} -c:v libx264 -preset veryfast -crf 18 {audioFlag} -avoid_negative_ts make_zero \"{destinationVideoPath}\"";

        var runResult = await _runner.ExecuteAsync(
            ffmpeg,
            arguments,
            expectedDuration: timeRange.Duration,
            progress: progress,
            cancellationToken: cancellationToken);

        if (runResult.IsFailure)
            return Result.Failure($"Fallo en la recodificación del fragmento: {runResult.ErrorMessage}");

        return Result.Success();
    }
}
