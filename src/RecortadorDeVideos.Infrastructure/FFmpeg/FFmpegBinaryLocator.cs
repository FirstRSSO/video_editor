namespace RecortadorDeVideos.Infrastructure.FFmpeg;

public interface IFFmpegBinaryLocator
{
    string GetFFmpegPath();
    string GetFFprobePath();
    bool AreBinariesAvailable();
}

public class FFmpegBinaryLocator : IFFmpegBinaryLocator
{
    private string? _cachedFFmpegPath;
    private string? _cachedFFprobePath;

    public string GetFFmpegPath()
    {
        if (_cachedFFmpegPath != null) return _cachedFFmpegPath;
        _cachedFFmpegPath = ResolveBinary("ffmpeg.exe");
        return _cachedFFmpegPath;
    }

    public string GetFFprobePath()
    {
        if (_cachedFFprobePath != null) return _cachedFFprobePath;
        _cachedFFprobePath = ResolveBinary("ffprobe.exe");
        return _cachedFFprobePath;
    }

    public bool AreBinariesAvailable()
    {
        try
        {
            var ffmpeg = GetFFmpegPath();
            var ffprobe = GetFFprobePath();
            return File.Exists(ffmpeg) && File.Exists(ffprobe);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveBinary(string binaryName)
    {
        // 1. Buscar en la carpeta de la aplicación
        var appDir = AppContext.BaseDirectory;
        var localPath = Path.Combine(appDir, binaryName);
        if (File.Exists(localPath)) return localPath;

        var runtimesPath = Path.Combine(appDir, "runtimes", "win-x64", "native", binaryName);
        if (File.Exists(runtimesPath)) return runtimesPath;

        // 2. Buscar en WinGet / LocalAppData links
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetLinks = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", binaryName);
        if (File.Exists(wingetLinks)) return wingetLinks;

        var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            var matches = Directory.GetFiles(wingetPackages, binaryName, SearchOption.AllDirectories);
            if (matches.Length > 0) return matches[0];
        }

        // 3. Buscar en el PATH del sistema
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var full = Path.Combine(p.Trim(), binaryName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // Ignorar entradas inválidas en PATH
            }
        }

        // Si no se encuentra en ruta específica, retornar el nombre para que el SO intente resolverlo
        return binaryName;
    }
}
