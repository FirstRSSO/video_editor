using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace RecortadorDeVideos.UI.ViewModels;

public class FileMergeItemViewModel : INotifyPropertyChanged
{
    private int _index;
    private string _filePath;
    private long _fileSizeBytes;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FileMergeItemViewModel(int index, string filePath, long fileSizeBytes = 0)
    {
        _index = index;
        _filePath = filePath;
        _fileSizeBytes = fileSizeBytes;
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileName => Path.GetFileName(FilePath);

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set { _fileSizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedSize)); }
    }

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes <= 0) return "0 MB";
            double mb = FileSizeBytes / (1024.0 * 1024.0);
            return $"{mb:F1} MB";
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
