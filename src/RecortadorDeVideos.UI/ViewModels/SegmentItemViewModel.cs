using System.ComponentModel;
using System.Runtime.CompilerServices;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.UI.ViewModels;

public class SegmentItemViewModel : INotifyPropertyChanged
{
    private int _index;
    private TimeSpan _start;
    private TimeSpan _end;
    private string? _name;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SegmentItemViewModel(int index, TimeSpan start, TimeSpan end, string? name = null)
    {
        _index = index;
        _start = start;
        _end = end;
        _name = name;
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public TimeSpan Start
    {
        get => _start;
        set
        {
            _start = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedRange));
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationString));
        }
    }

    public TimeSpan End
    {
        get => _end;
        set
        {
            _end = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedRange));
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationString));
        }
    }

    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    public string FormattedRange => $"{TimeRange.FormatFFmpegTime(Start)}  ➔  {TimeRange.FormatFFmpegTime(End)}";

    public string DurationString => TimeRange.FormatFFmpegTime(Duration);

    public string DisplayName => string.IsNullOrWhiteSpace(_name) ? $"Corte {Index}" : _name;

    public string? Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
