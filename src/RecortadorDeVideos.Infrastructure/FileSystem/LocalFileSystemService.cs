using RecortadorDeVideos.Application.Contracts;

namespace RecortadorDeVideos.Infrastructure.FileSystem;

public class LocalFileSystemService : IFileSystemService
{
    public bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public long GetFileSize(string path)
    {
        if (!FileExists(path)) return 0;
        return new FileInfo(path).Length;
    }

    public bool HasSufficientDiskSpace(string destinationPath, long requiredBytes)
    {
        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return true;

            var driveInfo = new DriveInfo(root);
            return driveInfo.AvailableFreeSpace > requiredBytes;
        }
        catch
        {
            return true;
        }
    }

    public string EnsureUniqueFilePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileNameWithoutExt}_({counter}){extension}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

    public void DeleteFileIfExists(string path)
    {
        if (FileExists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignorar error al limpiar archivos temporales
            }
        }
    }
}
