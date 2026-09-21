using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
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
    private DispatcherTimer? _previewTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TimeSpan _inPoint = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.FromSeconds(5);
    private TimeSpan _originalDuration = TimeSpan.FromSeconds(5);
    private bool _hasExplicitDuration = false;

    private bool _isSelected;
    private double _timelineLeft;
    private double _timelineWidth = 80;

    public AudioClipItemViewModel(
        int index,
        string filePath,
        TimeSpan startTime,
        double volume = 1.0,
        string? label = null,
        TimeSpan? duration = null,
        TimeSpan? inPoint = null,
        TimeSpan? originalDuration = null)
    {
        _index = index;
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _startTime = startTime < TimeSpan.Zero ? TimeSpan.Zero : startTime;
        _volume = Math.Clamp(volume, 0.0, 2.0);
        _label = label;

        if (inPoint.HasValue && inPoint.Value >= TimeSpan.Zero)
        {
            _inPoint = inPoint.Value;
        }

        if (duration.HasValue && duration.Value > TimeSpan.Zero)
        {
            _duration = duration.Value;
            _hasExplicitDuration = true;
        }

        if (originalDuration.HasValue && originalDuration.Value > TimeSpan.Zero)
        {
            _originalDuration = originalDuration.Value;
        }
        else if (duration.HasValue && duration.Value > TimeSpan.Zero)
        {
            _originalDuration = duration.Value;
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

    public TimeSpan InPoint
    {
        get => _inPoint;
        set
        {
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            if (_originalDuration > TimeSpan.Zero && clamped >= _originalDuration)
            {
                clamped = TimeSpan.FromSeconds(Math.Max(0, _originalDuration.TotalSeconds - 0.2));
            }
            _inPoint = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedInPoint));
            OnPropertyChanged(nameof(IsTrimmed));
            OnPropertyChanged(nameof(FormattedTrimSummary));
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            var minDur = TimeSpan.FromMilliseconds(200);
            var dur = value < minDur ? minDur : value;

            if (_originalDuration > TimeSpan.Zero && _inPoint + dur > _originalDuration)
            {
                var maxAvailable = _originalDuration - _inPoint;
                dur = maxAvailable >= minDur ? maxAvailable : minDur;
            }

            _duration = dur;
            _hasExplicitDuration = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedDuration));
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(FormattedEndTime));
            OnPropertyChanged(nameof(FormattedRange));
            OnPropertyChanged(nameof(IsTrimmed));
            OnPropertyChanged(nameof(FormattedTrimSummary));
        }
    }

    public TimeSpan OriginalDuration
    {
        get => _originalDuration;
        set
        {
            if (value > TimeSpan.Zero)
            {
                _originalDuration = value;
                if (!_hasExplicitDuration || _duration > _originalDuration)
                {
                    _duration = _originalDuration;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedOriginalDuration));
                OnPropertyChanged(nameof(FormattedDuration));
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(FormattedEndTime));
                OnPropertyChanged(nameof(FormattedRange));
                OnPropertyChanged(nameof(IsTrimmed));
                OnPropertyChanged(nameof(FormattedTrimSummary));
            }
        }
    }

    public void SetOriginalDuration(TimeSpan totalDuration)
    {
        if (totalDuration <= TimeSpan.Zero) return;
        _originalDuration = totalDuration;

        if (!_hasExplicitDuration)
        {
            _duration = totalDuration - _inPoint;
            if (_duration < TimeSpan.FromMilliseconds(200))
                _duration = TimeSpan.FromMilliseconds(200);
        }

        OnPropertyChanged(nameof(OriginalDuration));
        OnPropertyChanged(nameof(FormattedOriginalDuration));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
    }

    public TimeSpan EndTime => StartTime + Duration;

    public bool IsTrimmed =>
        InPoint > TimeSpan.FromMilliseconds(100) ||
        (OriginalDuration > TimeSpan.Zero && Duration < OriginalDuration - TimeSpan.FromMilliseconds(200));

    public string FormattedStartTime => TimeRange.FormatFFmpegTime(StartTime);

    public string FormattedInPoint => InPoint > TimeSpan.Zero
        ? $"{(int)InPoint.TotalMinutes:D2}:{InPoint.Seconds:D2}.{InPoint.Milliseconds / 100}"
        : "00:00.0";

    public string FormattedDuration => Duration > TimeSpan.Zero 
        ? $"{(int)Duration.TotalMinutes:D2}:{Duration.Seconds:D2}.{Duration.Milliseconds / 100}" 
        : "00:00.0";

    public string FormattedOriginalDuration => OriginalDuration > TimeSpan.Zero
        ? $"{(int)OriginalDuration.TotalMinutes:D2}:{OriginalDuration.Seconds:D2}.{OriginalDuration.Milliseconds / 100}"
        : "00:00.0";

    public string FormattedEndTime => TimeRange.FormatFFmpegTime(EndTime);

    public string FormattedRange => $"{FormattedStartTime} → {FormattedEndTime} ({FormattedDuration})";

    public string FormattedTrimSummary => IsTrimmed
        ? $"Recorte: {FormattedInPoint} → {TimeRange.FormatFFmpegTime(InPoint + Duration)} (Total archivo: {FormattedOriginalDuration})"
        : $"Completo: {FormattedOriginalDuration}";

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

    public void NudgeInPoint(TimeSpan delta)
    {
        var newIn = InPoint + delta;
        if (newIn < TimeSpan.Zero) newIn = TimeSpan.Zero;
        if (_originalDuration > TimeSpan.Zero && newIn + Duration > _originalDuration)
        {
            newIn = _originalDuration - Duration;
            if (newIn < TimeSpan.Zero) newIn = TimeSpan.Zero;
        }
        InPoint = newIn;
    }

    public void NudgeDuration(TimeSpan delta)
    {
        Duration += delta;
    }

    #region Métodos de Recorte

    /// <summary>
    /// Recorta el inicio del clip arrastrando la manija izquierda.
    /// Ajusta StartTime, InPoint y reduce Duration proporcionalmente.
    /// </summary>
    public void TrimStartByDelta(double deltaSeconds, TimeSpan maxLimit)
    {
        var minDuration = TimeSpan.FromMilliseconds(200);
        var delta = TimeSpan.FromSeconds(deltaSeconds);

        var proposedStartTime = StartTime + delta;
        var proposedInPoint = InPoint + delta;
        var proposedDuration = Duration - delta;

        if (proposedStartTime < TimeSpan.Zero)
        {
            var diff = TimeSpan.Zero - StartTime;
            proposedStartTime = TimeSpan.Zero;
            proposedInPoint = InPoint + diff;
            proposedDuration = Duration - diff;
        }

        if (proposedInPoint < TimeSpan.Zero)
        {
            var diff = TimeSpan.Zero - InPoint;
            proposedInPoint = TimeSpan.Zero;
            proposedStartTime = StartTime + diff;
            proposedDuration = Duration - diff;
        }

        if (proposedDuration < minDuration)
        {
            return;
        }

        if (maxLimit > TimeSpan.Zero && proposedStartTime > maxLimit)
        {
            return;
        }

        _startTime = proposedStartTime;
        _inPoint = proposedInPoint;
        _duration = proposedDuration;
        _hasExplicitDuration = true;

        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(FormattedStartTime));
        OnPropertyChanged(nameof(InPoint));
        OnPropertyChanged(nameof(FormattedInPoint));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
    }

    /// <summary>
    /// Recorta el final del clip arrastrando la manija derecha.
    /// Ajusta Duration respetando los límites de duración original del archivo.
    /// </summary>
    public void TrimEndByDelta(double deltaSeconds)
    {
        var minDuration = TimeSpan.FromMilliseconds(200);
        var proposedDuration = Duration + TimeSpan.FromSeconds(deltaSeconds);

        if (proposedDuration < minDuration)
        {
            proposedDuration = minDuration;
        }

        if (_originalDuration > TimeSpan.Zero && _inPoint + proposedDuration > _originalDuration)
        {
            proposedDuration = _originalDuration - _inPoint;
            if (proposedDuration < minDuration) proposedDuration = minDuration;
        }

        _duration = proposedDuration;
        _hasExplicitDuration = true;

        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
    }

    /// <summary>
    /// Recorta el inicio del clip al cabezal de reproducción actual.
    /// </summary>
    public bool TrimStartAtPlayhead(TimeSpan playheadTime)
    {
        if (playheadTime <= StartTime || playheadTime >= EndTime - TimeSpan.FromMilliseconds(200))
            return false;

        var delta = playheadTime - StartTime;
        _startTime = playheadTime;
        _inPoint += delta;
        _duration -= delta;
        _hasExplicitDuration = true;

        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(FormattedStartTime));
        OnPropertyChanged(nameof(InPoint));
        OnPropertyChanged(nameof(FormattedInPoint));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
        return true;
    }

    /// <summary>
    /// Recorta el final del clip al cabezal de reproducción actual.
    /// </summary>
    public bool TrimEndAtPlayhead(TimeSpan playheadTime)
    {
        if (playheadTime <= StartTime + TimeSpan.FromMilliseconds(200) || playheadTime >= EndTime)
            return false;

        _duration = playheadTime - StartTime;
        _hasExplicitDuration = true;

        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
        return true;
    }

    /// <summary>
    /// Divide este clip en dos piezas exactamente en la posición del cabezal.
    /// Devuelve la segunda pieza generada.
    /// </summary>
    public AudioClipItemViewModel? SplitAt(TimeSpan splitVideoTime, int newIndex)
    {
        if (splitVideoTime <= StartTime + TimeSpan.FromMilliseconds(200) || 
            splitVideoTime >= EndTime - TimeSpan.FromMilliseconds(200))
        {
            return null;
        }

        var firstPieceDuration = splitVideoTime - StartTime;
        var secondPieceInPoint = InPoint + firstPieceDuration;
        var secondPieceDuration = Duration - firstPieceDuration;

        // Ajustar este clip para ser la primera mitad
        _duration = firstPieceDuration;
        _hasExplicitDuration = true;

        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));

        // Crear la segunda mitad
        var secondClip = new AudioClipItemViewModel(
            index: newIndex,
            filePath: FilePath,
            startTime: splitVideoTime,
            volume: Volume,
            label: !string.IsNullOrWhiteSpace(Label) ? $"{Label} (Parte 2)" : null,
            duration: secondPieceDuration,
            inPoint: secondPieceInPoint,
            originalDuration: OriginalDuration);

        return secondClip;
    }

    /// <summary>
    /// Restablece el clip a su duración completa original sin recortes.
    /// </summary>
    public void ResetTrim()
    {
        _inPoint = TimeSpan.Zero;
        if (_originalDuration > TimeSpan.Zero)
        {
            _duration = _originalDuration;
        }
        _hasExplicitDuration = false;

        OnPropertyChanged(nameof(InPoint));
        OnPropertyChanged(nameof(FormattedInPoint));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(FormattedEndTime));
        OnPropertyChanged(nameof(FormattedRange));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(FormattedTrimSummary));
    }

    #endregion

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
            _previewPlayer.Position = InPoint;
            _previewPlayer.Volume = Math.Clamp(Volume, 0.0, 1.0);
            _previewPlayer.Play();
            IsPlaying = true;

            // Timer para detener exactamente al terminar la porción recortada
            _previewTimer?.Stop();
            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            var previewEndTime = InPoint + Duration;
            _previewTimer.Tick += (s, e) =>
            {
                if (!IsPlaying || _previewPlayer == null)
                {
                    _previewTimer?.Stop();
                    return;
                }

                if (_previewPlayer.Position >= previewEndTime)
                {
                    StopPreview();
                }
            };
            _previewTimer.Start();
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
            _previewTimer?.Stop();
            _previewTimer = null;

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
            var offsetFromClipStart = currentVideoPosition - StartTime;
            var targetAudioPosition = InPoint + offsetFromClipStart;

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
                    _previewPlayer.Position = targetAudioPosition;
                    _previewPlayer.Play();
                    IsPlaying = true;
                }
                else
                {
                    try
                    {
                        var playerPos = _previewPlayer.Position;
                        var drift = Math.Abs((playerPos - targetAudioPosition).TotalSeconds);
                        if (drift > 0.3)
                        {
                            _previewPlayer.Position = targetAudioPosition;
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
        StopPreview();
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
