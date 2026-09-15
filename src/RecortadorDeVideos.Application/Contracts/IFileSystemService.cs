namespace RecortadorDeVideos.Application.Contracts;

/// <summary>
/// Contrato para operaciones de verificación, validación y espacio de almacenamiento en el sistema de archivos.
/// </summary>
public interface IFileSystemService
{
    bool FileExists(string path);
    long GetFileSize(string path);
    bool HasSufficientDiskSpace(string destinationPath, long requiredBytes);
    string EnsureUniqueFilePath(string desiredPath);
    void DeleteFileIfExists(string path);
}
