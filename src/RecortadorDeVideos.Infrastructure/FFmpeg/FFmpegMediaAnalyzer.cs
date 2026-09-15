using System.Globalization;
using System.Text.Json;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegMediaAnalyzer : IMediaAnalyzer
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;

    public FFmpegMediaAnalyzer(IFFmpegBinaryLocator locator, IFFmpegProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Result<VideoMetadata>> AnalyzeAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var ffprobe = _locator.GetFFprobePath();
        var arguments = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"";

        var runResult = await _runner.ExecuteAsync(ffprobe, arguments, cancellationToken: cancellationToken);
        if (runResult.IsFailure)
            return Result<VideoMetadata>.Failure($"Error al analizar video con ffprobe: {runResult.ErrorMessage}");

        try
        {
            using var doc = JsonDocument.Parse(runResult.Value);
            var root = doc.RootElement;

            TimeSpan duration = TimeSpan.Zero;
            long fileSize = 0;

            if (root.TryGetProperty("format", out var formatElement))
            {
                if (formatElement.TryGetProperty("duration", out var durProp) &&
                    double.TryParse(durProp.GetString(), CultureInfo.InvariantCulture, out var durSeconds))
                {
                    duration = TimeSpan.FromSeconds(durSeconds);
                }

                if (formatElement.TryGetProperty("size", out var sizeProp) &&
                    long.TryParse(sizeProp.GetString(), out var sizeVal))
                {
                    fileSize = sizeVal;
                }
            }

            int width = 0;
            int height = 0;
            double frameRate = 30.0;
            string videoCodec = "desconocido";
            string? audioCodec = null;

            if (root.TryGetProperty("streams", out var streamsElement))
            {
                foreach (var stream in streamsElement.EnumerateArray())
                {
                    var codecType = stream.GetProperty("codec_type").GetString();
                    if (codecType == "video" && width == 0)
                    {
                        if (stream.TryGetProperty("codec_name", out var vc)) videoCodec = vc.GetString() ?? "desconocido";
                        if (stream.TryGetProperty("width", out var w)) width = w.GetInt32();
                        if (stream.TryGetProperty("height", out var h)) height = h.GetInt32();
                        if (stream.TryGetProperty("r_frame_rate", out var rfr))
                        {
                            var parts = rfr.GetString()?.Split('/');
                            if (parts != null && parts.Length == 2 &&
                                double.TryParse(parts[0], CultureInfo.InvariantCulture, out var num) &&
                                double.TryParse(parts[1], CultureInfo.InvariantCulture, out var den) &&
                                den > 0)
                            {
                                frameRate = num / den;
                            }
                        }
                    }
                    else if (codecType == "audio" && audioCodec == null)
                    {
                        if (stream.TryGetProperty("codec_name", out var ac)) audioCodec = ac.GetString();
                    }
                }
            }

            var metadata = new VideoMetadata(
                filePath: videoPath,
                duration: duration,
                width: width,
                height: height,
                frameRate: frameRate,
                videoCodec: videoCodec,
                audioCodec: audioCodec,
                fileSizeBytes: fileSize);

            return Result<VideoMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            return Result<VideoMetadata>.Failure($"Error al parsear salida de ffprobe: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<TimeSpan>>> ExtractKeyframesAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var ffprobe = _locator.GetFFprobePath();
        // Extraer únicamente los frames de tipo I (Keyframes)
        var arguments = $"-select_streams v -skip_frame nokey -show_frames -show_entries frame=pkt_pts_time -of csv=p=0 -v quiet \"{videoPath}\"";

        var runResult = await _runner.ExecuteAsync(ffprobe, arguments, cancellationToken: cancellationToken);
        if (runResult.IsFailure)
            return Result<IReadOnlyList<TimeSpan>>.Failure($"Error al extraer keyframes: {runResult.ErrorMessage}");

        var keyframes = new List<TimeSpan>();
        using var reader = new StringReader(runResult.Value);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (double.TryParse(line.Trim(), CultureInfo.InvariantCulture, out var seconds))
            {
                keyframes.Add(TimeSpan.FromSeconds(seconds));
            }
        }

        return Result<IReadOnlyList<TimeSpan>>.Success(keyframes);
    }
}
