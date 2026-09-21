using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.UI.ViewModels;

public class AudioClipItemViewModel : INotifyPropertyChanged, IDisposable
{
    private int _index;
    private string _filePath;
    private TimeSpan _startTime;
    private double _volume = 1.0;
    private string? _label;
    private bool _isPlaying;
    private MediaPlayer? _previewPlayer;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TimeSpan _duration = TimeSpan.FromSeconds(5);
    private bool _isSelected;
    private double _timelineLeft;
    private double _timelineWidth = 80;

    public AudioClipItemViewModel(int index, string filePath, TimeSpan startTime, double volume = 1.0, string? label = null, TimeSpan? duration = null)
    {
        _index = index;
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _startTime = startTime;
        _volume = Math.Clamp(volume, 0.0, 2.0);
        _label = label;
        if (duration.HasValue && duration.Value > TimeSpan.Zero)
        {
            _duration = duration.Value;
        }
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string FileName => !string.IsNullOrWhiteSpace(_filePath) ? Path.GetFileName(_filePath) : "Audio sin nombre";

    public string DisplayName => !string.IsNullOrWhiteSpace(_label) ? _label : $"Audio {Index}: {FileName}";

    public string? Label
    {
        get => _label;
        set
        {
            _label = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            _startTime = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedStartTime));
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(FormattedEndTime));
            OnPropertyChanged(nameof(FormattedRange));
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedDuration));
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(FormattedEndTime));
            OnPropertyChanged(nameof(FormattedRange));
        }
    }

    public TimeSpan EndTime => StartTime + Duration;

    public string FormattedStartTime => TimeRange.FormatFFmpegTime(StartTime);

    public string FormattedDuration => Duration > TimeSpan.Zero 
        ? $"{(int)Duration.TotalMinutes:D2}:{Duration.Seconds:D2}.{Duration.Milliseconds / 100}" 
        : "00:00.0";

    public string FormattedEndTime => TimeRange.FormatFFmpegTime(EndTime);

    public string FormattedRange => $"{FormattedStartTime} → {FormattedEndTime} ({FormattedDuration})";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public double TimelineLeft
    {
        get => _timelineLeft;
        set { _timelineLeft = value; OnPropertyChanged(); }
    }

    public double TimelineWidth
    {
        get => _timelineWidth;
        set { _timelineWidth = value; OnPropertyChanged(); }
    }

    public void UpdateTimelineBounds(double pixelsPerSecond, double minWidth = 70.0)
    {
        TimelineLeft = StartTime.TotalSeconds * pixelsPerSecond;
        TimelineWidth = Math.Max(minWidth, Duration.TotalSeconds * pixelsPerSecond);
    }

    public void NudgeStartTime(TimeSpan delta, TimeSpan maxLimit)
    {
        var newTime = StartTime + delta;
        if (newTime < TimeSpan.Zero) newTime = TimeSpan.Zero;
        if (maxLimit > TimeSpan.Zero && newTime > maxLimit) newTime = maxLimit;
        StartTime = newTime;
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 2.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumePercentString));
            if (_previewPlayer != null)
            {
                _previewPlayer.Volume = Math.Clamp(_volume, 0.0, 1.0);
            }
        }
    }

    public string VolumePercentString => $"{(int)Math.Round(Volume * 100)}%";

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { _isPlaying = value; OnPropertyChanged(); }
    }

    public void TogglePreview()
    {
        if (IsPlaying)
        {
            StopPreview();
        }
        else
        {
            PlayPreview();
        }
    }

    public void PlayPreview()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            _previewPlayer ??= new MediaPlayer();
            _previewPlayer.MediaEnded -= PreviewPlayer_MediaEnded;
            _previewPlayer.MediaEnded += PreviewPlayer_MediaEnded;
            _previewPlayer.Open(new Uri(FilePath));
            _previewPlayer.Volume = Math.Clamp(Volume, 0.0, 1.0);
            _previewPlayer.Play();
            IsPlaying = true;
        }
        catch
        {
            IsPlaying = false;
        }
    }

    public void StopPreview()
    {
        try
        {
            if (_previewPlayer != null)
            {
                _previewPlayer.Stop();
                _previewPlayer.Close();
            }
        }
        catch
        {
            // Ignorar errores al detener preview
        }
        finally
        {
            IsPlaying = false;
        }
    }

    public void SyncPlayback(TimeSpan currentVideoPosition, bool isVideoPlaying, double masterVolume = 1.0)
    {
        if (!File.Exists(FilePath)) return;

        bool inRange = currentVideoPosition >= StartTime && currentVideoPosition < EndTime;

        if (isVideoPlaying && inRange)
        {
            var offset = currentVideoPosition - StartTime;
            try
            {
                if (_previewPlayer == null)
                {
                    _previewPlayer = new MediaPlayer();
                    _previewPlayer.MediaEnded += PreviewPlayer_MediaEnded;
                    _previewPlayer.Open(new Uri(FilePath));
                }

                _previewPlayer.Volume = Math.Clamp(Volume * masterVolume, 0.0, 1.0);

                if (!IsPlaying)
                {
                    _previewPlayer.Position = offset;
                    _previewPlayer.Play();
                    IsPlaying = true;
                }
                else
                {
                    try
                    {
                        var playerPos = _previewPlayer.Position;
                        var drift = Math.Abs((playerPos - offset).TotalSeconds);
                        if (drift > 0.3)
                        {
                            _previewPlayer.Position = offset;
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                IsPlaying = false;
            }
        }
        else
        {
            if (IsPlaying)
            {
                try
                {
                    _previewPlayer?.Pause();
                }
                catch { }
                finally
                {
                    IsPlaying = false;
                }
            }
        }
    }

    public void StopPlayback()
    {
        try
        {
            if (_previewPlayer != null)
            {
                _previewPlayer.Pause();
            }
        }
        catch { }
        finally
        {
            IsPlaying = false;
        }
    }

    private void PreviewPlayer_MediaEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;
    }

    public void Dispose()
    {
        StopPreview();
        _previewPlayer = null;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
