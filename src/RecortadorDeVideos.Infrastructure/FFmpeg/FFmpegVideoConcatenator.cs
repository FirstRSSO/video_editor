using System.Text;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public class FFmpegVideoConcatenator : IVideoConcatenator
{
    private readonly IFFmpegBinaryLocator _locator;
    private readonly IFFmpegProcessRunner _runner;

    public FFmpegVideoConcatenator(IFFmpegBinaryLocator locator, IFFmpegProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Result> ConcatenateAsync(
        MergeJob job,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = _locator.GetFFmpegPath();

        // 1. Crear el archivo temporal de lista para el demuxer concat de FFmpeg
        var tempDirectory = Path.GetDirectoryName(job.DestinationVideoPath) ?? Path.GetTempPath();
        var listFilePath = Path.Combine(tempDirectory, $"concat_list_{Guid.NewGuid():N}.txt");

        var sb = new StringBuilder();
        foreach (var path in job.SourceVideoPaths)
        {
            // En FFmpeg concat demuxer, las comillas simples se escapan como '\'' y las barras normales '/' son más seguras
            var normalizedPath = path.Replace("\\", "/").Replace("'", "'\\''");
            sb.AppendLine($"file '{normalizedPath}'");
        }

        await File.WriteAllTextAsync(listFilePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        try
        {
            // 2. Ejecutar FFmpeg con -f concat y -c copy para unión instantánea sin pérdida
            var arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy -avoid_negative_ts make_zero \"{job.DestinationVideoPath}\"";

            var runResult = await _runner.ExecuteAsync(
                ffmpeg,
                arguments,
                progress: progress,
                cancellationToken: cancellationToken);

            if (runResult.IsFailure)
                return Result.Failure($"Fallo en la unión de videos con Concat Demuxer: {runResult.ErrorMessage}");

            return Result.Success();
        }
        finally
        {
            // Limpieza del archivo de texto temporal
            try
            {
                if (File.Exists(listFilePath))
                {
                    File.Delete(listFilePath);
                }
            }
            catch
            {
                // Ignorar error de borrado del txt
            }
        }
    }
}
