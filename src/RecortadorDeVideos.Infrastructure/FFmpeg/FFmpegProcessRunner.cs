using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using RecortadorDeVideos.Domain.Common;

namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public interface IFFmpegProcessRunner
{
    Task<Result<string>> ExecuteAsync(
        string executablePath,
        string arguments,
        TimeSpan? expectedDuration = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public class FFmpegProcessRunner : IFFmpegProcessRunner
{
    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);

    public async Task<Result<string>> ExecuteAsync(
        string executablePath,
        string arguments,
        TimeSpan? expectedDuration = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stderrOutput = new System.Text.StringBuilder();
        var stdoutOutput = new System.Text.StringBuilder();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            stderrOutput.AppendLine(e.Data);

            if (progress != null && expectedDuration.HasValue && expectedDuration.Value.TotalSeconds > 0)
            {
                var match = TimeRegex.Match(e.Data);
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out var hours) &&
                        int.TryParse(match.Groups[2].Value, out var minutes) &&
                        int.TryParse(match.Groups[3].Value, out var seconds) &&
                        int.TryParse(match.Groups[4].Value, out var centis))
                    {
                        var currentTime = new TimeSpan(0, hours, minutes, seconds, centis * 10);
                        var percentage = (currentTime.TotalSeconds / expectedDuration.Value.TotalSeconds) * 100.0;
                        progress.Report(Math.Min(100.0, Math.Max(0.0, percentage)));
                    }
                }
            }
        };

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                stdoutOutput.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
            });

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var errorText = stderrOutput.ToString();
                return Result<string>.Failure($"El proceso terminó con código de error {process.ExitCode}: {errorText}");
            }

            progress?.Report(100.0);
            return Result<string>.Success(stdoutOutput.ToString());
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("La operación fue cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Error al ejecutar {Path.GetFileName(executablePath)}: {ex.Message}");
        }
    }
}
