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
    private DateTime _suppressSyncDriftUntil = DateTime.MinValue;
    private TimeSpan _lastSyncTargetAudioPosition = TimeSpan.MinValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TimeSpan _inPoint = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.FromSeconds(5);
    private TimeSpan _originalDuration = TimeSpan.Zero;
    private bool _hasExplicitDuration = false;

    private bool _isSelected;
    private double _timelineLeft;
    private double _timelineWidth = 80;
    private int _trackIndex = 0;

    public int TrackIndex
    {
        get => _trackIndex;
        set
        {
            var clamped = Math.Max(0, value);
            if (_trackIndex != clamped)
            {
                _trackIndex = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TrackDisplayName));
            }
        }
    }

    public string TrackDisplayName => $"Pista {_trackIndex + 2}";

    public AudioClipItemViewModel(
        int index,
        string filePath,
        TimeSpan startTime,
        double volume = 1.0,
        string? label = null,
        TimeSpan? duration = null,
        TimeSpan? inPoint = null,
        TimeSpan? originalDuration = null,
        int trackIndex = 0)
    {
        _index = index;
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _startTime = startTime < TimeSpan.Zero ? TimeSpan.Zero : startTime;
        _volume = Math.Clamp(volume, 0.0, 2.0);
        _label = label;
        _trackIndex = Math.Max(0, trackIndex);

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
            if (_filePath != value)
            {
                _filePath = value;
                if (_previewPlayer != null)
                {
                    try
                    {
                        _previewPlayer.Stop();
                        _previewPlayer.Close();
                    }
                    catch { }
                    _previewPlayer = null;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(DisplayName));
            }
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
        else if (_inPoint + _duration > _originalDuration)
        {
            _duration = _originalDuration - _inPoint;
            if (_duration < TimeSpan.FromMilliseconds(200))
                _duration = TimeSpan.FromMilliseconds(200);
        }

        NotifyAllTrimProperties();
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

    public void UpdateTimelineBounds(double pixelsPerSecond, double minWidth = 16.0)
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
    /// Ajusta StartTime, InPoint y reduce/aumenta Duration proporcionalmente.
    /// </summary>
    public void TrimStartByDelta(double deltaSeconds, TimeSpan maxLimit)
    {
        var minDuration = TimeSpan.FromMilliseconds(200);
        var delta = TimeSpan.FromSeconds(deltaSeconds);

        var proposedStartTime = StartTime + delta;
        var proposedInPoint = InPoint + delta;
        var proposedDuration = Duration - delta;

        if (proposedInPoint < TimeSpan.Zero)
        {
            var diff = TimeSpan.Zero - InPoint;
            proposedInPoint = TimeSpan.Zero;
            proposedStartTime = StartTime + diff;
            proposedDuration = Duration - diff;
        }

        if (proposedStartTime < TimeSpan.Zero)
        {
            var diff = TimeSpan.Zero - StartTime;
            proposedStartTime = TimeSpan.Zero;
            proposedInPoint = InPoint + diff;
            proposedDuration = Duration - diff;
        }

        if (proposedDuration < minDuration)
        {
            var diff = Duration - minDuration;
            proposedDuration = minDuration;
            proposedStartTime = StartTime + diff;
            proposedInPoint = InPoint + diff;
        }

        if (maxLimit > TimeSpan.Zero && proposedStartTime > maxLimit)
        {
            return;
        }

        _startTime = proposedStartTime;
        _inPoint = proposedInPoint;
        _duration = proposedDuration;
        _hasExplicitDuration = true;

        NotifyAllTrimProperties();
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

        NotifyAllTrimProperties();
    }

    /// <summary>
    /// Recorta o extiende el inicio del clip al cabezal de reproducción actual.
    /// </summary>
    public bool TrimStartAtPlayhead(TimeSpan playheadTime)
    {
        var minDuration = TimeSpan.FromMilliseconds(200);
        if (playheadTime >= EndTime - minDuration)
            return false;

        if (playheadTime >= StartTime)
        {
            var delta = playheadTime - StartTime;
            _startTime = playheadTime;
            _inPoint += delta;
            _duration -= delta;
        }
        else
        {
            var extension = StartTime - playheadTime;
            if (_inPoint < extension)
            {
                extension = _inPoint;
            }
            if (extension <= TimeSpan.Zero) return false;

            _startTime -= extension;
            _inPoint -= extension;
            _duration += extension;
        }

        _hasExplicitDuration = true;
        NotifyAllTrimProperties();
        return true;
    }

    /// <summary>
    /// Recorta o extiende el final del clip al cabezal de reproducción actual.
    /// </summary>
    public bool TrimEndAtPlayhead(TimeSpan playheadTime)
    {
        var minDuration = TimeSpan.FromMilliseconds(200);
        if (playheadTime <= StartTime + minDuration)
            return false;

        var proposedDuration = playheadTime - StartTime;
        if (_originalDuration > TimeSpan.Zero && _inPoint + proposedDuration > _originalDuration)
        {
            proposedDuration = _originalDuration - _inPoint;
            if (proposedDuration < minDuration) proposedDuration = minDuration;
        }

        _duration = proposedDuration;
        _hasExplicitDuration = true;

        NotifyAllTrimProperties();
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

        NotifyAllTrimProperties();

        // Crear la segunda mitad
        var secondClip = new AudioClipItemViewModel(
            index: newIndex,
            filePath: FilePath,
            startTime: splitVideoTime,
            volume: Volume,
            label: !string.IsNullOrWhiteSpace(Label) ? $"{Label} (Parte 2)" : null,
            duration: secondPieceDuration,
            inPoint: secondPieceInPoint,
            originalDuration: OriginalDuration,
            trackIndex: TrackIndex);

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

        NotifyAllTrimProperties();
    }

    private void NotifyAllTrimProperties()
    {
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

    private MediaPlayer GetOrCreatePlayer()
    {
        if (_previewPlayer == null)
        {
            _previewPlayer = new MediaPlayer();
            _previewPlayer.MediaOpened += PreviewPlayer_MediaOpened;
            _previewPlayer.MediaEnded += PreviewPlayer_MediaEnded;
            _previewPlayer.Open(new Uri(FilePath));
        }
        return _previewPlayer;
    }

    private void PreviewPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (_previewPlayer != null && _previewPlayer.NaturalDuration.HasTimeSpan)
        {
            var natDur = _previewPlayer.NaturalDuration.TimeSpan;
            if (natDur > TimeSpan.Zero && (_originalDuration <= TimeSpan.Zero || _originalDuration == TimeSpan.FromSeconds(5)))
            {
                SetOriginalDuration(natDur);
            }
        }
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
            var player = GetOrCreatePlayer();
            player.Position = InPoint;
            player.Volume = Math.Clamp(Volume, 0.0, 1.0);
            player.Play();
            IsPlaying = true;
            _suppressSyncDriftUntil = DateTime.UtcNow.AddMilliseconds(500);
            _lastSyncTargetAudioPosition = InPoint;

            // Timer para detener exactamente al terminar la porción recortada
            _previewTimer?.Stop();
            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
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
                _previewPlayer.Pause();
                _previewPlayer.Position = InPoint;
            }
        }
        catch { }
        finally
        {
            IsPlaying = false;
            _suppressSyncDriftUntil = DateTime.MinValue;
            _lastSyncTargetAudioPosition = TimeSpan.MinValue;
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
            if (targetAudioPosition < TimeSpan.Zero) targetAudioPosition = TimeSpan.Zero;

            var maxAllowed = _originalDuration > TimeSpan.Zero
                ? _originalDuration
                : (_previewPlayer?.NaturalDuration.HasTimeSpan == true ? _previewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero);

            if (maxAllowed > TimeSpan.Zero && targetAudioPosition >= maxAllowed - TimeSpan.FromMilliseconds(80))
            {
                StopPlayback();
                return;
            }

            try
            {
                // Si había un timer de preview individual corriendo, cancelarlo
                if (_previewTimer != null)
                {
                    _previewTimer.Stop();
                    _previewTimer = null;
                }

                var player = GetOrCreatePlayer();
                player.Volume = Math.Clamp(Volume * masterVolume, 0.0, 1.0);

                if (!IsPlaying)
                {
                    player.Position = targetAudioPosition;
                    player.Play();
                    IsPlaying = true;
                    _suppressSyncDriftUntil = DateTime.UtcNow.AddMilliseconds(500);
                    _lastSyncTargetAudioPosition = targetAudioPosition;
                }
                else
                {
                    // Detectar si el usuario retrocedió o saltó bruscamente en el tiempo (seek/scrub intencional)
                    var targetDelta = _lastSyncTargetAudioPosition == TimeSpan.MinValue
                        ? 0.0
                        : (targetAudioPosition - _lastSyncTargetAudioPosition).TotalSeconds;

                    bool isIntentionalJump = targetDelta < -0.15 || targetDelta > 0.40;

                    if (isIntentionalJump)
                    {
                        player.Position = targetAudioPosition;
                        _suppressSyncDriftUntil = DateTime.UtcNow.AddMilliseconds(500);
                        _lastSyncTargetAudioPosition = targetAudioPosition;
                    }
                    else
                    {
                        _lastSyncTargetAudioPosition = targetAudioPosition;

                        // Solo corregir desfases acumulados si estamos fuera de la ventana de amortiguación de salto
                        if (DateTime.UtcNow >= _suppressSyncDriftUntil)
                        {
                            try
                            {
                                var playerPos = player.Position;
                                var drift = Math.Abs((playerPos - targetAudioPosition).TotalSeconds);
                                if (drift > 0.45)
                                {
                                    player.Position = targetAudioPosition;
                                    _suppressSyncDriftUntil = DateTime.UtcNow.AddMilliseconds(500);
                                }
                            }
                            catch { }
                        }
                    }
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
                StopPlayback();
            }

            // Si el video está pausado pero el cursor está dentro del clip, pre-posicionar el audio sin saturar
            if (!isVideoPlaying && inRange && _previewPlayer != null)
            {
                try
                {
                    var offsetFromClipStart = currentVideoPosition - StartTime;
                    var targetAudioPosition = InPoint + offsetFromClipStart;
                    if (targetAudioPosition < TimeSpan.Zero) targetAudioPosition = TimeSpan.Zero;

                    var maxAllowed = _originalDuration > TimeSpan.Zero
                        ? _originalDuration
                        : (_previewPlayer.NaturalDuration.HasTimeSpan ? _previewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero);

                    if (maxAllowed <= TimeSpan.Zero || targetAudioPosition < maxAllowed)
                    {
                        if (Math.Abs((_previewPlayer.Position - targetAudioPosition).TotalSeconds) > 0.15)
                        {
                            _previewPlayer.Position = targetAudioPosition;
                        }
                    }
                }
                catch { }
            }
        }
    }

    public void StopPlayback()
    {
        try
        {
            _previewTimer?.Stop();
            _previewTimer = null;

            if (_previewPlayer != null)
            {
                _previewPlayer.Pause();
            }
        }
        catch { }
        finally
        {
            IsPlaying = false;
            _suppressSyncDriftUntil = DateTime.MinValue;
            _lastSyncTargetAudioPosition = TimeSpan.MinValue;
        }
    }

    private void PreviewPlayer_MediaEnded(object? sender, EventArgs e)
    {
        try
        {
            _previewTimer?.Stop();
            _previewTimer = null;
            if (_previewPlayer != null)
            {
                _previewPlayer.Pause();
                _previewPlayer.Position = InPoint;
            }
        }
        catch { }
        finally
        {
            IsPlaying = false;
            _suppressSyncDriftUntil = DateTime.MinValue;
            _lastSyncTargetAudioPosition = TimeSpan.MinValue;
        }
    }

    public void Dispose()
    {
        try
        {
            _previewTimer?.Stop();
            _previewTimer = null;

            if (_previewPlayer != null)
            {
                _previewPlayer.MediaOpened -= PreviewPlayer_MediaOpened;
                _previewPlayer.MediaEnded -= PreviewPlayer_MediaEnded;
                _previewPlayer.Stop();
                _previewPlayer.Close();
                _previewPlayer = null;
            }
        }
        catch { }
        finally
        {
            IsPlaying = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
