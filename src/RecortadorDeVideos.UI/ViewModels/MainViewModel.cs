using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Globalization;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.AnalyzeVideo;
using RecortadorDeVideos.Application.UseCases.ChangeSpeed;
using RecortadorDeVideos.Application.UseCases.ExtractAudio;
using RecortadorDeVideos.Application.UseCases.MergeVideos;
using RecortadorDeVideos.Application.UseCases.TrimVideo;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.UI.ViewModels;

public enum SidebarViewMode
{
    Segments,
    AudioClips
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IAnalyzeVideoUseCase _analyzeVideoUseCase;
    private readonly ITrimVideoUseCase _trimVideoUseCase;
    private readonly IMergeVideosUseCase _mergeVideosUseCase;
    private readonly IChangeSpeedUseCase _changeSpeedUseCase;
    private readonly IExtractAudioUseCase _extractAudioUseCase;

    private string? _videoFilePath;
    private string? _audioFilePath;
    private TimeSpan _videoDuration = TimeSpan.Zero;
    private TimeSpan _startTime = TimeSpan.Zero;
    private TimeSpan _endTime = TimeSpan.Zero;
    private TimeSpan _currentPlaybackPosition = TimeSpan.Zero;
    private double _progressPercentage = 0;
    private bool _isProcessing = false;
    private string _statusMessage = "Abre un video o suelta uno sobre la ventana.";

    private AudioMode _selectedAudioMode = AudioMode.KeepOriginal;
    private double _mainVolume = 1.0;
    private double _backgroundVolume = 0.3;
    private SidebarViewMode _currentSidebarView = SidebarViewMode.Segments;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = new();
    public ObservableCollection<FileMergeItemViewModel> FilesToMerge { get; } = new();
    public ObservableCollection<AudioClipItemViewModel> AudioClips { get; } = new();

    public MainViewModel(
        IAnalyzeVideoUseCase analyzeVideoUseCase,
        ITrimVideoUseCase trimVideoUseCase,
        IMergeVideosUseCase mergeVideosUseCase,
        IChangeSpeedUseCase changeSpeedUseCase,
        IExtractAudioUseCase extractAudioUseCase)
    {
        _analyzeVideoUseCase = analyzeVideoUseCase ?? throw new ArgumentNullException(nameof(analyzeVideoUseCase));
        _trimVideoUseCase = trimVideoUseCase ?? throw new ArgumentNullException(nameof(trimVideoUseCase));
        _mergeVideosUseCase = mergeVideosUseCase ?? throw new ArgumentNullException(nameof(mergeVideosUseCase));
        _changeSpeedUseCase = changeSpeedUseCase ?? throw new ArgumentNullException(nameof(changeSpeedUseCase));
        _extractAudioUseCase = extractAudioUseCase ?? throw new ArgumentNullException(nameof(extractAudioUseCase));

        // Comandos de corte individual y marcadores
        TrimCommand = new RelayCommand(async () => await ExecuteTrimAsync(), () => CanTrim());
        SetStartToCurrentCommand = new RelayCommand(() => StartTime = CurrentPlaybackPosition, () => HasVideo);
        SetEndToCurrentCommand = new RelayCommand(() => EndTime = CurrentPlaybackPosition, () => HasVideo);
        ExtractAudioMp3Command = new RelayCommand(async () => await ExecuteExtractAudioAsync(AudioExportFormat.Mp3), () => CanTrim());
        ExtractAudioWavCommand = new RelayCommand(async () => await ExecuteExtractAudioAsync(AudioExportFormat.Wav), () => CanTrim());

        // Comandos de segmentos múltiples del video
        AddCurrentRangeToSegmentsCommand = new RelayCommand(AddCurrentRangeToSegments, () => CanAddSegment());
        RemoveSegmentCommand = new RelayCommand(param => RemoveSegment(param as SegmentItemViewModel), _ => !IsProcessing);
        ClearSegmentsCommand = new RelayCommand(ClearSegments, () => Segments.Count > 0 && !IsProcessing);
        LoadSegmentRangeCommand = new RelayCommand(param => LoadSegmentRange(param as SegmentItemViewModel), _ => HasVideo);
        MergeSegmentsCommand = new RelayCommand(async () => await ExecuteMergeSegmentsAsync(), () => CanMergeSegments());

        // Comandos de unión de archivos externos
        RemoveFileToMergeCommand = new RelayCommand(param => RemoveFileToMerge(param as FileMergeItemViewModel), _ => !IsProcessing);
        ClearFilesToMergeCommand = new RelayCommand(ClearFilesToMerge, () => FilesToMerge.Count > 0 && !IsProcessing);
        MoveFileUpCommand = new RelayCommand(param => MoveFileOrder(param as FileMergeItemViewModel, -1), _ => !IsProcessing);
        MoveFileDownCommand = new RelayCommand(param => MoveFileOrder(param as FileMergeItemViewModel, 1), _ => !IsProcessing);
        MergeFilesCommand = new RelayCommand(async () => await ExecuteMergeFilesAsync(), () => CanMergeFiles());

        // Comandos de cambio de velocidad
        SetSpeedPresetCommand = new RelayCommand(param => SetSpeedPreset(param), _ => !IsProcessing);
        ChangeSpeedCommand = new RelayCommand(async () => await ExecuteChangeSpeedAsync(), () => CanChangeSpeed());

        // Comandos de audio
        ClearAudioCommand = new RelayCommand(() => AudioFilePath = null, () => !string.IsNullOrEmpty(AudioFilePath));
        RemoveAudioClipCommand = new RelayCommand(param => RemoveAudioClip(param as AudioClipItemViewModel), _ => !IsProcessing);
        ClearAudioClipsCommand = new RelayCommand(ClearAudioClips, () => AudioClips.Count > 0 && !IsProcessing);
        SeekToClipPositionCommand = new RelayCommand(param =>
        {
            if (param is AudioClipItemViewModel clip)
            {
                RequestSeekToPosition?.Invoke(clip.StartTime);
            }
        }, _ => HasVideo);
        SetClipToCurrentPositionCommand = new RelayCommand(param =>
        {
            if (param is AudioClipItemViewModel clip)
            {
                clip.StartTime = CurrentPlaybackPosition;
                StatusMessage = $"Audio alineado al cabezal ({clip.FormattedStartTime}).";
            }
        }, _ => HasVideo && !IsProcessing);
        ToggleClipPreviewCommand = new RelayCommand(param =>
        {
            if (param is AudioClipItemViewModel clip)
            {
                clip.TogglePreview();
            }
        });
        SwitchSidebarViewCommand = new RelayCommand(param =>
        {
            if (param is string str && Enum.TryParse<SidebarViewMode>(str, out var mode))
            {
                CurrentSidebarView = mode;
            }
            else if (param is SidebarViewMode vm)
            {
                CurrentSidebarView = vm;
            }
        });
        OpenAudioClipsSidebarCommand = new RelayCommand(() =>
        {
            CurrentSidebarView = SidebarViewMode.AudioClips;
            IsSidebarCollapsed = false;
            SelectedAudioMode = AudioMode.OverlayClips;
        });

        // Comandos de visualización y pantalla completa
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        ToggleFullscreenCommand = new RelayCommand(() => RequestToggleFullscreen?.Invoke());

        // Comandos de montaje y edición en línea de tiempo (estilo Camtasia)
        ExportTimelineVideoCommand = new RelayCommand(async () => await ExecuteExportTimelineAsync(), () => HasVideo && !IsProcessing);
        SelectAudioClipCommand = new RelayCommand(param => SelectedAudioClip = param as AudioClipItemViewModel);
        NudgeSelectedClipForwardCommand = new RelayCommand(() => NudgeSelectedClip(0.5), () => HasSelectedClip && !IsProcessing);
        NudgeSelectedClipBackwardCommand = new RelayCommand(() => NudgeSelectedClip(-0.5), () => HasSelectedClip && !IsProcessing);
        DeleteSelectedClipCommand = new RelayCommand(() =>
        {
            if (SelectedAudioClip != null)
            {
                var target = SelectedAudioClip;
                SelectedAudioClip = null;
                RemoveAudioClip(target);
            }
        }, () => HasSelectedClip && !IsProcessing);
        SetZoomCommand = new RelayCommand(param =>
        {
            if (param is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var z))
                TimelineZoom = z;
            else if (param is double d)
                TimelineZoom = d;
        });

        // Comandos de recorte y división de audios
        SplitSelectedClipCommand = new RelayCommand(SplitSelectedClipAtPlayhead, () => CanSplitSelectedClip && !IsProcessing);
        TrimSelectedClipStartToPlayheadCommand = new RelayCommand(TrimSelectedClipStartToPlayhead, () => HasSelectedClip && !IsProcessing);
        TrimSelectedClipEndToPlayheadCommand = new RelayCommand(TrimSelectedClipEndToPlayhead, () => HasSelectedClip && !IsProcessing);
        ResetSelectedClipTrimCommand = new RelayCommand(ResetSelectedClipTrim, () => HasSelectedClip && !IsProcessing);
        NudgeSelectedClipInPointForwardCommand = new RelayCommand(() => NudgeSelectedClipInPoint(0.5), () => HasSelectedClip && !IsProcessing);
        NudgeSelectedClipInPointBackwardCommand = new RelayCommand(() => NudgeSelectedClipInPoint(-0.5), () => HasSelectedClip && !IsProcessing);
        NudgeSelectedClipDurationForwardCommand = new RelayCommand(() => NudgeSelectedClipDuration(0.5), () => HasSelectedClip && !IsProcessing);
        NudgeSelectedClipDurationBackwardCommand = new RelayCommand(() => NudgeSelectedClipDuration(-0.5), () => HasSelectedClip && !IsProcessing);

        BrowseSaveAsCommand = new RelayCommand(BrowseSaveAs, () => HasVideo && !IsProcessing);
    }

    public delegate string? SaveFilePathRequestHandler(string title, string suggestedFileName, string defaultDirectory, string extension);
    public SaveFilePathRequestHandler? RequestSaveFilePath { get; set; }

    #region Propiedades de Video y Reproducción

    public string? VideoFilePath
    {
        get => _videoFilePath;
        set
        {
            if (_videoFilePath != value)
            {
                _videoFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasVideo));
                OnPropertyChanged(nameof(HasNoVideo));
                OnPropertyChanged(nameof(VideoFileName));
            }
        }
    }

    public string VideoFileName => !string.IsNullOrEmpty(VideoFilePath) ? Path.GetFileName(VideoFilePath) : "Ningún video seleccionado";
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoFilePath);
    public bool HasNoVideo => !HasVideo;

    public string? AudioFilePath
    {
        get => _audioFilePath;
        set { _audioFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(AudioFileName)); }
    }

    public string AudioFileName => !string.IsNullOrEmpty(AudioFilePath) ? Path.GetFileName(AudioFilePath) : "Ningún archivo de audio";

    public TimeSpan VideoDuration
    {
        get => _videoDuration;
        set { _videoDuration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationString)); }
    }

    public string DurationString => TimeRange.FormatFFmpegTime(VideoDuration);

    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            _startTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartTimeString));
            OnPropertyChanged(nameof(TrimDurationString));
            OnPropertyChanged(nameof(CanTrimVideo));
        }
    }

    public string StartTimeString => TimeRange.FormatFFmpegTime(StartTime);

    public TimeSpan EndTime
    {
        get => _endTime;
        set
        {
            _endTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EndTimeString));
            OnPropertyChanged(nameof(TrimDurationString));
            OnPropertyChanged(nameof(CanTrimVideo));
        }
    }

    public string EndTimeString => TimeRange.FormatFFmpegTime(EndTime);

    public TimeSpan CurrentPlaybackPosition
    {
        get => _currentPlaybackPosition;
        set
        {
            _currentPlaybackPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionString));
            OnPropertyChanged(nameof(CanSplitSelectedClip));
        }
    }

    public string CurrentPositionString => TimeRange.FormatFFmpegTime(CurrentPlaybackPosition);

    public string TrimDurationString => EndTime > StartTime ? TimeRange.FormatFFmpegTime(EndTime - StartTime) : "00:00:00.000";

    public bool CanTrimVideo => CanTrim();

    private string _outputFileName = string.Empty;
    public string OutputFileName
    {
        get => _outputFileName;
        set
        {
            if (_outputFileName != value)
            {
                _outputFileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedOutputFileNamePreview));
            }
        }
    }

    private string? _customOutputDirectory;
    public string? CustomOutputDirectory
    {
        get => _customOutputDirectory;
        set
        {
            if (_customOutputDirectory != value)
            {
                _customOutputDirectory = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveOutputDirectory));
                OnPropertyChanged(nameof(FormattedOutputLocationPreview));
            }
        }
    }

    public string EffectiveOutputDirectory =>
        !string.IsNullOrWhiteSpace(_customOutputDirectory) && Directory.Exists(_customOutputDirectory)
            ? _customOutputDirectory
            : (!string.IsNullOrWhiteSpace(VideoFilePath) ? Path.GetDirectoryName(VideoFilePath) ?? string.Empty : string.Empty);

    public string VideoExtension => !string.IsNullOrWhiteSpace(VideoFilePath) ? Path.GetExtension(VideoFilePath) : ".mp4";

    public string FormattedOutputFileNamePreview
    {
        get
        {
            var ext = VideoExtension;
            if (string.IsNullOrWhiteSpace(OutputFileName))
                return $"nuevo_corte{ext}";
            return OutputFileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? OutputFileName
                : $"{OutputFileName}{ext}";
        }
    }

    public string FormattedOutputLocationPreview =>
        !string.IsNullOrWhiteSpace(EffectiveOutputDirectory)
            ? Path.GetFileName(EffectiveOutputDirectory)
            : "Misma carpeta del video";

    private bool _askSaveLocationOnTrim = true;
    public bool AskSaveLocationOnTrim
    {
        get => _askSaveLocationOnTrim;
        set
        {
            if (_askSaveLocationOnTrim != value)
            {
                _askSaveLocationOnTrim = value;
                OnPropertyChanged();
            }
        }
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set { _progressPercentage = value; OnPropertyChanged(); }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set { _isProcessing = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public AudioMode SelectedAudioMode
    {
        get => _selectedAudioMode;
        set
        {
            if (_selectedAudioMode != value)
            {
                _selectedAudioMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAudioFileNeeded));
                OnPropertyChanged(nameof(IsMixModeActive));
                OnPropertyChanged(nameof(IsOverlayClipsModeActive));
                if (_selectedAudioMode == AudioMode.OverlayClips)
                {
                    CurrentSidebarView = SidebarViewMode.AudioClips;
                    IsSidebarCollapsed = false;
                }
            }
        }
    }

    public bool IsAudioFileNeeded => SelectedAudioMode is AudioMode.Replace or AudioMode.Mix;
    public bool IsMixModeActive => SelectedAudioMode == AudioMode.Mix;
    public bool IsOverlayClipsModeActive => SelectedAudioMode == AudioMode.OverlayClips;
    public bool HasAudioFile => !string.IsNullOrEmpty(AudioFilePath);

    public SidebarViewMode CurrentSidebarView
    {
        get => _currentSidebarView;
        set
        {
            if (_currentSidebarView != value)
            {
                _currentSidebarView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSegmentsSidebarVisible));
                OnPropertyChanged(nameof(IsAudioClipsSidebarVisible));
            }
        }
    }

    public bool IsSegmentsSidebarVisible => CurrentSidebarView == SidebarViewMode.Segments;
    public bool IsAudioClipsSidebarVisible => CurrentSidebarView == SidebarViewMode.AudioClips;

    public int TotalAudioClipsCount => AudioClips.Count;
    public int TotalAudioTracksCount => AudioClips.Count == 0 ? 1 : Math.Max(1, AudioClips.Max(c => c.TrackIndex) + 1);
    public double AudioTracksHeight => TotalAudioTracksCount * 56.0;
    public bool HasAudioClips => AudioClips.Count > 0;
    public bool HasNoAudioClips => AudioClips.Count == 0;

    public double MainVolume
    {
        get => _mainVolume;
        set { _mainVolume = value; OnPropertyChanged(); }
    }

    public double BackgroundVolume
    {
        get => _backgroundVolume;
        set { _backgroundVolume = value; OnPropertyChanged(); }
    }

    private VideoMetadataDto? _metadata;
    public VideoMetadataDto? Metadata
    {
        get => _metadata;
        private set
        {
            _metadata = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMetadata));
            OnPropertyChanged(nameof(VideoResolution));
            OnPropertyChanged(nameof(VideoCodec));
            OnPropertyChanged(nameof(AudioCodec));
            OnPropertyChanged(nameof(VideoFps));
            OnPropertyChanged(nameof(VideoFileSizeString));
        }
    }

    public bool HasMetadata => _metadata != null;
    public string VideoResolution => _metadata != null ? $"{_metadata.Width} × {_metadata.Height}" : string.Empty;
    public string VideoCodec => _metadata != null ? _metadata.VideoCodec.ToUpperInvariant() : string.Empty;
    public string AudioCodec => _metadata != null ? (!string.IsNullOrWhiteSpace(_metadata.AudioCodec) ? _metadata.AudioCodec.ToUpperInvariant() : "AAC") : string.Empty;
    public string VideoFps => _metadata != null && _metadata.FrameRate > 0 ? $"{_metadata.FrameRate:F0} FPS" : string.Empty;
    public string VideoFileSizeString
    {
        get
        {
            if (_metadata == null || _metadata.FileSizeBytes <= 0) return string.Empty;
            double mb = _metadata.FileSizeBytes / (1024.0 * 1024.0);
            return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
        }
    }

    #endregion

    #region Segmentos y Archivos Múltiples

    public bool HasSegments => Segments.Count > 0;
    public bool HasNoSegments => !HasSegments;
    public int TotalSegmentsCount => Segments.Count;

    public TimeSpan TotalSegmentsDuration => TimeSpan.FromTicks(Segments.Sum(s => s.Duration.Ticks));
    public string TotalSegmentsDurationString => TimeRange.FormatFFmpegTime(TotalSegmentsDuration);

    public bool HasFilesToMerge => FilesToMerge.Count >= 2;
    public bool HasNoFilesToMerge => FilesToMerge.Count == 0;
    public int TotalFilesToMergeCount => FilesToMerge.Count;

    #endregion

    #region Comandos

    public ICommand TrimCommand { get; }
    public ICommand ExtractAudioMp3Command { get; }
    public ICommand ExtractAudioWavCommand { get; }
    public ICommand BrowseSaveAsCommand { get; }
    public ICommand SetStartToCurrentCommand { get; }
    public ICommand SetEndToCurrentCommand { get; }

    public ICommand AddCurrentRangeToSegmentsCommand { get; }
    public ICommand RemoveSegmentCommand { get; }
    public ICommand ClearSegmentsCommand { get; }
    public ICommand LoadSegmentRangeCommand { get; }
    public ICommand MergeSegmentsCommand { get; }

    public ICommand RemoveFileToMergeCommand { get; }
    public ICommand ClearFilesToMergeCommand { get; }
    public ICommand MoveFileUpCommand { get; }
    public ICommand MoveFileDownCommand { get; }
    public ICommand MergeFilesCommand { get; }

    public ICommand SetSpeedPresetCommand { get; }
    public ICommand ChangeSpeedCommand { get; }

    public ICommand ClearAudioCommand { get; }
    public ICommand RemoveAudioClipCommand { get; }
    public ICommand ClearAudioClipsCommand { get; }
    public ICommand SeekToClipPositionCommand { get; }
    public ICommand SetClipToCurrentPositionCommand { get; }
    public ICommand ToggleClipPreviewCommand { get; }
    public ICommand SwitchSidebarViewCommand { get; }
    public ICommand OpenAudioClipsSidebarCommand { get; }
    public event Action<TimeSpan>? RequestSeekToPosition;
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public event Action? RequestToggleFullscreen;

    public ICommand ExportTimelineVideoCommand { get; }
    public ICommand SelectAudioClipCommand { get; }
    public ICommand NudgeSelectedClipForwardCommand { get; }
    public ICommand NudgeSelectedClipBackwardCommand { get; }
    public ICommand DeleteSelectedClipCommand { get; }
    public ICommand SetZoomCommand { get; }
    public ICommand SplitSelectedClipCommand { get; }
    public ICommand TrimSelectedClipStartToPlayheadCommand { get; }
    public ICommand TrimSelectedClipEndToPlayheadCommand { get; }
    public ICommand ResetSelectedClipTrimCommand { get; }
    public ICommand NudgeSelectedClipInPointForwardCommand { get; }
    public ICommand NudgeSelectedClipInPointBackwardCommand { get; }
    public ICommand NudgeSelectedClipDurationForwardCommand { get; }
    public ICommand NudgeSelectedClipDurationBackwardCommand { get; }

    private bool _isFullscreen;
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (_isFullscreen != value)
            {
                _isFullscreen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotFullscreen));
                OnPropertyChanged(nameof(IsSidebarVisible));
            }
        }
    }

    public bool IsNotFullscreen => !IsFullscreen;

    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (_isSidebarCollapsed != value)
            {
                _isSidebarCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSidebarVisible));
                OnPropertyChanged(nameof(SidebarToggleText));
                OnPropertyChanged(nameof(SidebarToggleTooltip));
            }
        }
    }

    public bool IsSidebarVisible => !IsSidebarCollapsed && !IsFullscreen;
    public string SidebarToggleText => IsSidebarCollapsed ? "Mostrar Cola" : "Ocultar Cola";
    public string SidebarToggleTooltip => IsSidebarCollapsed ? "Mostrar cola de segmentos (restaurar panel lateral)" : "Ocultar cola de segmentos (ampliar monitor de video)";

    #endregion

    #region Estado de Edición Multipista (Timeline Camtasia)

    private AudioClipItemViewModel? _selectedAudioClip;
    public AudioClipItemViewModel? SelectedAudioClip
    {
        get => _selectedAudioClip;
        set
        {
            if (_selectedAudioClip != value)
            {
                if (_selectedAudioClip != null) _selectedAudioClip.IsSelected = false;
                _selectedAudioClip = value;
                if (_selectedAudioClip != null) _selectedAudioClip.IsSelected = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedClip));
                OnPropertyChanged(nameof(SelectedClipFileName));
                OnPropertyChanged(nameof(SelectedClipTimeRangeString));
                OnPropertyChanged(nameof(CanSplitSelectedClip));
            }
        }
    }

    public bool HasSelectedClip => SelectedAudioClip != null;
    public string SelectedClipFileName => SelectedAudioClip?.FileName ?? "Ninguna locución seleccionada";
    public string SelectedClipTimeRangeString => SelectedAudioClip?.FormattedRange ?? string.Empty;

    private double _timelineZoom = 1.0;
    public double TimelineZoom
    {
        get => _timelineZoom;
        set
        {
            var clamped = Math.Clamp(value, 0.5, 6.0);
            if (Math.Abs(_timelineZoom - clamped) > 0.001)
            {
                _timelineZoom = clamped;
                OnPropertyChanged();
                UpdateTimelineMetrics();
            }
        }
    }

    private double _timelinePixelsPerSecond = 20.0;
    public double TimelinePixelsPerSecond
    {
        get => _timelinePixelsPerSecond;
        set
        {
            if (Math.Abs(_timelinePixelsPerSecond - value) > 0.001)
            {
                _timelinePixelsPerSecond = value;
                OnPropertyChanged();
            }
        }
    }

    private double _timelineTotalWidth = 800.0;
    public double TimelineTotalWidth
    {
        get => _timelineTotalWidth;
        set
        {
            if (Math.Abs(_timelineTotalWidth - value) > 0.5)
            {
                _timelineTotalWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public void UpdateTimelineMetrics(double availableWidth = 800.0)
    {
        if (VideoDuration.TotalSeconds > 0)
        {
            double basePps = availableWidth / VideoDuration.TotalSeconds;
            TimelinePixelsPerSecond = Math.Max(10.0, basePps * TimelineZoom);
            TimelineTotalWidth = Math.Max(availableWidth, VideoDuration.TotalSeconds * TimelinePixelsPerSecond);
        }
        else
        {
            TimelinePixelsPerSecond = 20.0 * TimelineZoom;
            TimelineTotalWidth = Math.Max(availableWidth, 800.0);
        }

        foreach (var clip in AudioClips)
        {
            clip.UpdateTimelineBounds(TimelinePixelsPerSecond);
        }
    }

    public void NudgeSelectedClip(double seconds)
    {
        if (SelectedAudioClip == null) return;
        SelectedAudioClip.NudgeStartTime(TimeSpan.FromSeconds(seconds), VideoDuration);
        SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
        StatusMessage = $"Locución '{SelectedAudioClip.FileName}' colocada en {SelectedAudioClip.FormattedStartTime}.";
    }

    public bool CanSplitSelectedClip =>
        HasSelectedClip &&
        CurrentPlaybackPosition > SelectedAudioClip!.StartTime + TimeSpan.FromMilliseconds(200) &&
        CurrentPlaybackPosition < SelectedAudioClip.EndTime - TimeSpan.FromMilliseconds(200);

    public void SplitSelectedClipAtPlayhead()
    {
        if (!CanSplitSelectedClip || SelectedAudioClip == null) return;

        int currentIndex = AudioClips.IndexOf(SelectedAudioClip);
        var secondClip = SelectedAudioClip.SplitAt(CurrentPlaybackPosition, AudioClips.Count + 1);
        if (secondClip != null)
        {
            secondClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
            SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);

            if (currentIndex >= 0 && currentIndex < AudioClips.Count)
            {
                AudioClips.Insert(currentIndex + 1, secondClip);
            }
            else
            {
                AudioClips.Add(secondClip);
            }

            ReindexAudioClips();
            ResolveAudioClipCollisions(secondClip);
            CompactAudioTracks();
            NotifyAudioClipsChanged();
            UpdateTimelineMetrics();
            SelectedAudioClip = secondClip;
            StatusMessage = $"Audio dividido en dos partes en {TimeRange.FormatFFmpegTime(CurrentPlaybackPosition)}.";
        }
    }

    public void TrimSelectedClipStartToPlayhead()
    {
        if (SelectedAudioClip == null) return;
        if (SelectedAudioClip.TrimStartAtPlayhead(CurrentPlaybackPosition))
        {
            SelectedAudioClip.TrackIndex = GetNextAvailableTrackIndex(SelectedAudioClip.StartTime, SelectedAudioClip.Duration, SelectedAudioClip, SelectedAudioClip.TrackIndex);
            ResolveAudioClipCollisions(SelectedAudioClip);
            CompactAudioTracks();
            SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
            UpdateTimelineMetrics();
            StatusMessage = $"Inicio de '{SelectedAudioClip.FileName}' recortado a {SelectedAudioClip.FormattedStartTime}.";
        }
    }

    public void TrimSelectedClipEndToPlayhead()
    {
        if (SelectedAudioClip == null) return;
        if (SelectedAudioClip.TrimEndAtPlayhead(CurrentPlaybackPosition))
        {
            SelectedAudioClip.TrackIndex = GetNextAvailableTrackIndex(SelectedAudioClip.StartTime, SelectedAudioClip.Duration, SelectedAudioClip, SelectedAudioClip.TrackIndex);
            ResolveAudioClipCollisions(SelectedAudioClip);
            CompactAudioTracks();
            SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
            UpdateTimelineMetrics();
            StatusMessage = $"Fin de '{SelectedAudioClip.FileName}' recortado a {SelectedAudioClip.FormattedEndTime}.";
        }
    }

    public void ResetSelectedClipTrim()
    {
        if (SelectedAudioClip == null) return;
        SelectedAudioClip.ResetTrim();
        SelectedAudioClip.TrackIndex = GetNextAvailableTrackIndex(SelectedAudioClip.StartTime, SelectedAudioClip.Duration, SelectedAudioClip, SelectedAudioClip.TrackIndex);
        ResolveAudioClipCollisions(SelectedAudioClip);
        CompactAudioTracks();
        SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
        UpdateTimelineMetrics();
        StatusMessage = $"Recorte restablecido para '{SelectedAudioClip.FileName}'.";
    }

    public void NudgeSelectedClipInPoint(double seconds)
    {
        if (SelectedAudioClip == null) return;
        SelectedAudioClip.NudgeInPoint(TimeSpan.FromSeconds(seconds));
        SelectedAudioClip.TrackIndex = GetNextAvailableTrackIndex(SelectedAudioClip.StartTime, SelectedAudioClip.Duration, SelectedAudioClip, SelectedAudioClip.TrackIndex);
        ResolveAudioClipCollisions(SelectedAudioClip);
        CompactAudioTracks();
        SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
        UpdateTimelineMetrics();
        StatusMessage = $"Punto de entrada: {SelectedAudioClip.FormattedInPoint}.";
    }

    public void NudgeSelectedClipDuration(double seconds)
    {
        if (SelectedAudioClip == null) return;
        SelectedAudioClip.NudgeDuration(TimeSpan.FromSeconds(seconds));
        SelectedAudioClip.TrackIndex = GetNextAvailableTrackIndex(SelectedAudioClip.StartTime, SelectedAudioClip.Duration, SelectedAudioClip, SelectedAudioClip.TrackIndex);
        ResolveAudioClipCollisions(SelectedAudioClip);
        CompactAudioTracks();
        SelectedAudioClip.UpdateTimelineBounds(TimelinePixelsPerSecond);
        UpdateTimelineMetrics();
        StatusMessage = $"Duración activa: {SelectedAudioClip.FormattedDuration}.";
    }

    #endregion

    public async Task LoadVideoAsync(string path)
    {
        StatusMessage = "Leyendo el video…";
        var result = await _analyzeVideoUseCase.ExecuteAsync(path);
        if (result.IsSuccess)
        {
            var meta = result.Value;
            Metadata = meta;
            VideoFilePath = meta.FilePath;
            VideoDuration = meta.Duration;
            StartTime = TimeSpan.Zero;
            EndTime = meta.Duration;

            var baseName = Path.GetFileNameWithoutExtension(meta.FilePath);
            OutputFileName = $"{baseName}_recorte";
            CustomOutputDirectory = null;
            OnPropertyChanged(nameof(VideoExtension));
            OnPropertyChanged(nameof(EffectiveOutputDirectory));
            OnPropertyChanged(nameof(FormattedOutputFileNamePreview));
            OnPropertyChanged(nameof(FormattedOutputLocationPreview));
            OnPropertyChanged(nameof(CanTrimVideo));

            UpdateTimelineMetrics();
            StatusMessage = "Video listo para cortar o montar en la línea de tiempo.";
        }
        else
        {
            Metadata = null;
            StatusMessage = $"No se pudo abrir el video: {result.ErrorMessage}";
        }
    }

    #region Lógica de Segmentos

    private bool CanTrim() => !IsProcessing && HasVideo && EndTime > StartTime;

    private bool CanAddSegment() => !IsProcessing && HasVideo && EndTime > StartTime;

    private bool CanMergeSegments() => !IsProcessing && HasVideo && Segments.Count > 0;

    private void AddCurrentRangeToSegments()
    {
        if (!CanAddSegment()) return;

        var newItem = new SegmentItemViewModel(
            index: Segments.Count + 1,
            start: StartTime,
            end: EndTime,
            name: $"Corte {Segments.Count + 1}");

        Segments.Add(newItem);
        NotifySegmentsChanged();
        StatusMessage = $"Corte {newItem.Index} guardado. Llevas {TotalSegmentsDurationString} en la lista.";
    }

    private void RemoveSegment(SegmentItemViewModel? item)
    {
        if (item == null || !Segments.Contains(item)) return;

        Segments.Remove(item);
        ReindexSegments();
        NotifySegmentsChanged();
        StatusMessage = $"Corte quitado. Quedan {Segments.Count}.";
    }

    private void ClearSegments()
    {
        Segments.Clear();
        NotifySegmentsChanged();
        StatusMessage = "Lista de cortes vacía.";
    }

    private void LoadSegmentRange(SegmentItemViewModel? item)
    {
        if (item == null) return;
        StartTime = item.Start;
        EndTime = item.End;
        StatusMessage = $"Reproductor colocado en el corte {item.Index}.";
    }

    private void ReindexSegments()
    {
        for (int i = 0; i < Segments.Count; i++)
        {
            Segments[i].Index = i + 1;
        }
    }

    private void NotifySegmentsChanged()
    {
        OnPropertyChanged(nameof(HasSegments));
        OnPropertyChanged(nameof(HasNoSegments));
        OnPropertyChanged(nameof(TotalSegmentsCount));
        OnPropertyChanged(nameof(TotalSegmentsDuration));
        OnPropertyChanged(nameof(TotalSegmentsDurationString));
    }

    #endregion

    #region Lógica de Clips de Audio

    public bool ClipsOverlap(AudioClipItemViewModel a, AudioClipItemViewModel b)
    {
        var eps = TimeSpan.FromMilliseconds(50);
        return a.StartTime + eps < b.EndTime && b.StartTime + eps < a.EndTime;
    }

    public int GetNextAvailableTrackIndex(TimeSpan start, TimeSpan duration, AudioClipItemViewModel? excludeClip = null, int preferredTrack = 0)
    {
        var end = start + duration;
        var eps = TimeSpan.FromMilliseconds(50);

        int track = Math.Max(0, preferredTrack);
        while (true)
        {
            bool hasCollision = AudioClips.Any(c => 
                c != excludeClip && 
                c.TrackIndex == track && 
                start + eps < c.EndTime && 
                c.StartTime + eps < end);

            if (!hasCollision)
            {
                return track;
            }
            track++;
        }
    }

    public void ResolveAudioClipCollisions(AudioClipItemViewModel? priorityClip = null)
    {
        if (AudioClips.Count <= 1)
        {
            OnPropertyChanged(nameof(TotalAudioTracksCount));
            OnPropertyChanged(nameof(AudioTracksHeight));
            return;
        }

        var eps = TimeSpan.FromMilliseconds(50);

        // Si hay un clip prioritario, los demás ceden ante él si colisionan en la misma pista
        var sorted = AudioClips
            .OrderBy(c => c == priorityClip ? 0 : 1)
            .ThenBy(c => c.TrackIndex)
            .ThenBy(c => c.StartTime)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];

            bool collides;
            do
            {
                collides = false;
                for (int j = 0; j < i; j++)
                {
                    var previous = sorted[j];
                    if (current.TrackIndex == previous.TrackIndex &&
                        current.StartTime + eps < previous.EndTime &&
                        previous.StartTime + eps < current.EndTime)
                    {
                        // Colisión detectada: desplazar el clip a la pista de abajo
                        current.TrackIndex = previous.TrackIndex + 1;
                        collides = true;
                        break;
                    }
                }
            } while (collides);
        }

        OnPropertyChanged(nameof(TotalAudioTracksCount));
        OnPropertyChanged(nameof(AudioTracksHeight));
    }

    public void CompactAudioTracks()
    {
        if (AudioClips.Count == 0)
        {
            OnPropertyChanged(nameof(TotalAudioTracksCount));
            OnPropertyChanged(nameof(AudioTracksHeight));
            return;
        }

        // Compactar pistas intermedias vacías para que los índices sean continuos (0, 1, 2...)
        var usedTracks = AudioClips.Select(c => c.TrackIndex).Distinct().OrderBy(t => t).ToList();
        for (int i = 0; i < usedTracks.Count; i++)
        {
            int oldTrack = usedTracks[i];
            if (oldTrack != i)
            {
                foreach (var clip in AudioClips.Where(c => c.TrackIndex == oldTrack))
                {
                    clip.TrackIndex = i;
                }
            }
        }

        OnPropertyChanged(nameof(TotalAudioTracksCount));
        OnPropertyChanged(nameof(AudioTracksHeight));
    }

    public void AddAudioClip(string filePath, TimeSpan? startTime = null, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var time = startTime ?? CurrentPlaybackPosition;
        var estimatedDuration = TimeSpan.FromSeconds(5);
        var targetTrack = GetNextAvailableTrackIndex(time, estimatedDuration);

        var newItem = new AudioClipItemViewModel(
            index: AudioClips.Count + 1,
            filePath: filePath,
            startTime: time,
            volume: 1.0,
            label: label,
            trackIndex: targetTrack);

        AudioClips.Add(newItem);
        SelectedAudioClip = newItem;
        ReindexAudioClips();
        ResolveAudioClipCollisions(newItem);
        CompactAudioTracks();
        NotifyAudioClipsChanged();

        SelectedAudioMode = AudioMode.OverlayClips;
        CurrentSidebarView = SidebarViewMode.AudioClips;
        IsSidebarCollapsed = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var analysis = await _analyzeVideoUseCase.ExecuteAsync(filePath);
                if (analysis.IsSuccess && analysis.Value.Duration > TimeSpan.Zero)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        newItem.SetOriginalDuration(analysis.Value.Duration);
                        newItem.TrackIndex = GetNextAvailableTrackIndex(newItem.StartTime, newItem.Duration, newItem, newItem.TrackIndex);
                        ResolveAudioClipCollisions(newItem);
                        CompactAudioTracks();
                        newItem.UpdateTimelineBounds(TimelinePixelsPerSecond);
                        UpdateTimelineMetrics();
                    });
                }
            }
            catch { }
        });

        UpdateTimelineMetrics();
        StatusMessage = $"Audio añadido en {newItem.FormattedStartTime} ({newItem.TrackDisplayName}).";
    }

    private void RemoveAudioClip(AudioClipItemViewModel? item)
    {
        if (item == null || !AudioClips.Contains(item)) return;

        item.Dispose();
        AudioClips.Remove(item);
        ReindexAudioClips();
        CompactAudioTracks();
        NotifyAudioClipsChanged();
        StatusMessage = $"Audio quitado. Quedan {AudioClips.Count}.";
    }

    private void ClearAudioClips()
    {
        foreach (var clip in AudioClips)
        {
            clip.Dispose();
        }
        AudioClips.Clear();
        CompactAudioTracks();
        NotifyAudioClipsChanged();
        StatusMessage = "Lista de audios vacía.";
    }

    private void ReindexAudioClips()
    {
        for (int i = 0; i < AudioClips.Count; i++)
        {
            AudioClips[i].Index = i + 1;
        }
    }

    private void NotifyAudioClipsChanged()
    {
        OnPropertyChanged(nameof(TotalAudioTracksCount));
        OnPropertyChanged(nameof(AudioTracksHeight));
        OnPropertyChanged(nameof(TotalAudioClipsCount));
        OnPropertyChanged(nameof(HasAudioClips));
        OnPropertyChanged(nameof(HasNoAudioClips));
    }

    #endregion

    #region Lógica de Archivos Externos para Unir

    public void AddFilesToMergeList(IEnumerable<string> filePaths)
    {
        int added = 0;
        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }

                FilesToMerge.Add(new FileMergeItemViewModel(FilesToMerge.Count + 1, path, size));
                added++;
            }
        }

        if (added > 0)
        {
            NotifyFilesToMergeChanged();
            StatusMessage = $"Ya hay {FilesToMerge.Count} videos en la lista. Ordénalos antes de unirlos.";
        }
    }

    private bool CanMergeFiles() => !IsProcessing && FilesToMerge.Count >= 2;

    private void RemoveFileToMerge(FileMergeItemViewModel? item)
    {
        if (item == null || !FilesToMerge.Contains(item)) return;

        FilesToMerge.Remove(item);
        ReindexFilesToMerge();
        NotifyFilesToMergeChanged();
        StatusMessage = $"Video quitado. Quedan {FilesToMerge.Count}.";
    }

    private void ClearFilesToMerge()
    {
        FilesToMerge.Clear();
        NotifyFilesToMergeChanged();
        StatusMessage = "Lista de videos vacía.";
    }

    private void MoveFileOrder(FileMergeItemViewModel? item, int direction)
    {
        if (item == null) return;
        int oldIndex = FilesToMerge.IndexOf(item);
        if (oldIndex < 0) return;

        int newIndex = oldIndex + direction;
        if (newIndex >= 0 && newIndex < FilesToMerge.Count)
        {
            FilesToMerge.Move(oldIndex, newIndex);
            ReindexFilesToMerge();
        }
    }

    private void ReindexFilesToMerge()
    {
        for (int i = 0; i < FilesToMerge.Count; i++)
        {
            FilesToMerge[i].Index = i + 1;
        }
    }

    private void NotifyFilesToMergeChanged()
    {
        OnPropertyChanged(nameof(HasFilesToMerge));
        OnPropertyChanged(nameof(HasNoFilesToMerge));
        OnPropertyChanged(nameof(TotalFilesToMergeCount));
    }

    #endregion

    #region Ejecución de Operaciones

    public void BrowseSaveAs()
    {
        if (RequestSaveFilePath == null || !HasVideo) return;
        var ext = VideoExtension;
        var suggested = FormattedOutputFileNamePreview;
        var chosen = RequestSaveFilePath("Elegir nombre y carpeta para guardar corte", suggested, EffectiveOutputDirectory, ext);
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            OutputFileName = Path.GetFileNameWithoutExtension(chosen);
            CustomOutputDirectory = Path.GetDirectoryName(chosen);
            StatusMessage = $"Se guardará como '{Path.GetFileName(chosen)}'.";
        }
    }

    public static string SuggestNextOutputFileName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return "video_recorte_1";

        // Caso 1: termina en dígitos (ej: "video_recorte_1" -> "video_recorte_2", "corte01" -> "corte02")
        var matchEndDigits = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*?)(\d+)$");
        if (matchEndDigits.Success)
        {
            var prefix = matchEndDigits.Groups[1].Value;
            var digits = matchEndDigits.Groups[2].Value;
            if (long.TryParse(digits, out long num))
            {
                var nextNum = (num + 1).ToString(new string('0', digits.Length));
                return $"{prefix}{nextNum}";
            }
        }

        // Caso 2: empieza por dígitos (ej: "01_intro" -> "02_intro", "1_modulo" -> "2_modulo")
        var matchStartDigits = System.Text.RegularExpressions.Regex.Match(baseName, @"^(\d+)(.*)$");
        if (matchStartDigits.Success)
        {
            var digits = matchStartDigits.Groups[1].Value;
            var suffix = matchStartDigits.Groups[2].Value;
            if (long.TryParse(digits, out long num))
            {
                var nextNum = (num + 1).ToString(new string('0', digits.Length));
                return $"{nextNum}{suffix}";
            }
        }

        // Caso 3: sin números (ej: "video_recorte" -> "video_recorte_2")
        return $"{baseName}_2";
    }

    private async Task ExecuteTrimAsync(string? explicitDestinationPath = null)
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !CanTrim()) return;

        string destPath;
        if (!string.IsNullOrWhiteSpace(explicitDestinationPath))
        {
            destPath = explicitDestinationPath;
            OutputFileName = Path.GetFileNameWithoutExtension(destPath);
            CustomOutputDirectory = Path.GetDirectoryName(destPath);
        }
        else if (AskSaveLocationOnTrim && RequestSaveFilePath != null)
        {
            var ext = VideoExtension;
            var suggested = FormattedOutputFileNamePreview;
            var chosen = RequestSaveFilePath("Guardar Video Recortado", suggested, EffectiveOutputDirectory, ext);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                StatusMessage = "Guardado cancelado.";
                return;
            }
            destPath = chosen;
            OutputFileName = Path.GetFileNameWithoutExtension(chosen);
            CustomOutputDirectory = Path.GetDirectoryName(chosen);
        }
        else
        {
            var dir = EffectiveOutputDirectory;
            var rawName = !string.IsNullOrWhiteSpace(OutputFileName)
                ? OutputFileName.Trim()
                : $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_recorte";

            var ext = VideoExtension;
            if (rawName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                rawName = rawName.Substring(0, rawName.Length - ext.Length);
            }

            destPath = GenerateSafeOutputPath(dir, rawName, ext);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = "Cortando…";

        try
        {
            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var clipDtos = AudioClips.Select(c => new AudioOverlayClipDto(c.FilePath, c.StartTime, c.Volume, c.DisplayName, c.InPoint, c.Duration, c.FadeIn, c.FadeOut)).ToList();

            var request = new TrimVideoRequestDto(
                SourceVideoPath: VideoFilePath,
                StartTime: StartTime,
                EndTime: EndTime,
                DestinationVideoPath: destPath,
                Strategy: CutStrategy.LosslessStreamCopy,
                AudioMode: SelectedAudioMode,
                ExternalAudioPath: AudioFilePath,
                MainVolume: MainVolume,
                BackgroundVolume: BackgroundVolume,
                AudioClips: clipDtos);

            var trimResult = await _trimVideoUseCase.ExecuteAsync(request, progressReporter);

            if (trimResult.IsSuccess)
            {
                var savedFileName = Path.GetFileName(destPath);
                StatusMessage = $"Corte guardado en {savedFileName}.";
                ProgressPercentage = 100;

                // Sugerir el siguiente nombre organizado para el próximo corte
                var baseName = Path.GetFileNameWithoutExtension(destPath);
                OutputFileName = SuggestNextOutputFileName(baseName);
            }
            else
            {
                StatusMessage = $"No se pudo cortar: {trimResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo cortar: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ExecuteExtractAudioAsync(AudioExportFormat format)
    {
        if (!CanTrim() || string.IsNullOrWhiteSpace(VideoFilePath)) return;

        var ext = format switch
        {
            AudioExportFormat.Mp3 => ".mp3",
            AudioExportFormat.Wav => ".wav",
            AudioExportFormat.Aac => ".aac",
            AudioExportFormat.M4a => ".m4a",
            _ => ".mp3"
        };

        var baseName = !string.IsNullOrWhiteSpace(OutputFileName)
            ? OutputFileName.Trim()
            : $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_audio";

        string destPath;
        if (RequestSaveFilePath != null)
        {
            var suggested = $"{baseName}{ext}";
            var chosen = RequestSaveFilePath($"Extraer Audio ({format})", suggested, EffectiveOutputDirectory, ext);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                StatusMessage = "Extracción de audio cancelada.";
                return;
            }
            destPath = chosen;
        }
        else
        {
            destPath = GenerateSafeOutputPath(EffectiveOutputDirectory, baseName, ext);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = $"Extrayendo pista de audio a formato {format}…";

        try
        {
            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var request = new ExtractAudioRequestDto(
                SourceVideoPath: VideoFilePath,
                StartTime: StartTime,
                EndTime: EndTime,
                DestinationAudioPath: destPath,
                Format: format);

            var result = await _extractAudioUseCase.ExecuteAsync(request, progressReporter);
            if (result.IsSuccess)
            {
                var mb = result.Value.OutputSizeBytes / (1024.0 * 1024.0);
                StatusMessage = $"Audio extraído con éxito ({format.ToString().ToUpper()}): {Path.GetFileName(destPath)} ({mb:F2} MB)";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"Error al extraer audio: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Excepción al extraer audio: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ExecuteExportTimelineAsync(string? explicitDestinationPath = null)
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath)) return;

        string destPath;
        if (!string.IsNullOrWhiteSpace(explicitDestinationPath))
        {
            destPath = explicitDestinationPath;
        }
        else if (RequestSaveFilePath != null)
        {
            var fileName = Path.GetFileNameWithoutExtension(VideoFilePath);
            var ext = VideoExtension;
            var chosen = RequestSaveFilePath("Exportar Video Editado con Locuciones", $"{fileName}_montaje{ext}", EffectiveOutputDirectory, ext);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                StatusMessage = "Exportación cancelada.";
                return;
            }
            destPath = chosen;
        }
        else
        {
            var dir = EffectiveOutputDirectory;
            var fileName = Path.GetFileNameWithoutExtension(VideoFilePath);
            var ext = VideoExtension;
            destPath = GenerateSafeOutputPath(dir, $"{fileName}_montaje_editado", ext);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = "Exportando montaje de video y locuciones…";

        try
        {
            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var clipDtos = AudioClips.Select(c => new AudioOverlayClipDto(c.FilePath, c.StartTime, c.Volume, c.DisplayName, c.InPoint, c.Duration, c.FadeIn, c.FadeOut)).ToList();

            var request = new TrimVideoRequestDto(
                SourceVideoPath: VideoFilePath,
                StartTime: TimeSpan.Zero,
                EndTime: VideoDuration,
                DestinationVideoPath: destPath,
                Strategy: CutStrategy.LosslessStreamCopy,
                AudioMode: AudioMode.OverlayClips,
                ExternalAudioPath: null,
                MainVolume: MainVolume,
                BackgroundVolume: BackgroundVolume,
                AudioClips: clipDtos);

            var trimResult = await _trimVideoUseCase.ExecuteAsync(request, progressReporter);

            if (trimResult.IsSuccess)
            {
                StatusMessage = $"Montaje exportado con éxito en {Path.GetFileName(destPath)}.";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"No se pudo exportar el montaje: {trimResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo exportar el montaje: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ExecuteMergeSegmentsAsync(string? explicitDestinationPath = null)
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || Segments.Count == 0) return;

        string destPath;
        if (!string.IsNullOrWhiteSpace(explicitDestinationPath))
        {
            destPath = explicitDestinationPath;
        }
        else if (RequestSaveFilePath != null)
        {
            var fileName = Path.GetFileNameWithoutExtension(VideoFilePath);
            var ext = VideoExtension;
            var chosen = RequestSaveFilePath("Exportar Video Final (Unión de Cortes)", $"{fileName}_cortes_unidos{ext}", EffectiveOutputDirectory, ext);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                StatusMessage = "Exportación cancelada.";
                return;
            }
            destPath = chosen;
        }
        else
        {
            var dir = EffectiveOutputDirectory;
            var fileName = Path.GetFileNameWithoutExtension(VideoFilePath);
            var ext = VideoExtension;
            destPath = GenerateSafeOutputPath(dir, $"{fileName}_segmentos_unidos", ext);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = $"Cortando y pegando {Segments.Count} tramos…";

        try
        {
            var segmentPairs = Segments.Select(s => new TimeSpanPairDto(s.Start, s.End, s.DisplayName)).ToList();
            var clipDtos = AudioClips.Select(c => new AudioOverlayClipDto(c.FilePath, c.StartTime, c.Volume, c.DisplayName, c.InPoint, c.Duration, c.FadeIn, c.FadeOut)).ToList();

            var request = new MergeSegmentsRequestDto(
                SourceVideoPath: VideoFilePath,
                Segments: segmentPairs,
                DestinationVideoPath: destPath,
                AudioMode: SelectedAudioMode,
                ExternalAudioPath: AudioFilePath,
                MainVolume: MainVolume,
                BackgroundVolume: BackgroundVolume,
                AudioClips: clipDtos);

            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var mergeResult = await _mergeVideosUseCase.ExecuteMergeSegmentsAsync(request, progressReporter);

            if (mergeResult.IsSuccess)
            {
                StatusMessage = $"Video final guardado en {Path.GetFileName(destPath)}.";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"No se pudieron unir los cortes: {mergeResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron unir los cortes: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ExecuteMergeFilesAsync()
    {
        if (FilesToMerge.Count < 2) return;

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = $"Uniendo {FilesToMerge.Count} videos…";

        try
        {
            var firstFile = FilesToMerge[0].FilePath;
            var dir = Path.GetDirectoryName(firstFile) ?? string.Empty;
            var ext = Path.GetExtension(firstFile);
            var destPath = GenerateSafeOutputPath(dir, "videos_unidos", ext);

            var filePaths = FilesToMerge.Select(f => f.FilePath).ToList();
            var request = new MergeFilesRequestDto(filePaths, destPath);

            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var mergeResult = await _mergeVideosUseCase.ExecuteMergeFilesAsync(request, progressReporter);

            if (mergeResult.IsSuccess)
            {
                StatusMessage = $"Videos unidos y guardados en {Path.GetFileName(destPath)}.";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"No se pudieron unir los videos: {mergeResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron unir los videos: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private static string GenerateSafeOutputPath(string directory, string baseName, string extension)
    {
        if (string.IsNullOrWhiteSpace(directory))
            directory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        string candidate = Path.Combine(directory, $"{baseName}{extension}");
        int counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_{counter}{extension}");
            counter++;
        }
        return candidate;
    }

    #endregion

    #region Sección: Cambio de Velocidad (Acelerar Video)

    private string? _speedVideoFilePath;
    public string? SpeedVideoFilePath
    {
        get => _speedVideoFilePath;
        set
        {
            if (_speedVideoFilePath != value)
            {
                _speedVideoFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSpeedVideo));
                OnPropertyChanged(nameof(HasNoSpeedVideo));
                OnPropertyChanged(nameof(SpeedVideoFileName));
            }
        }
    }

    public string SpeedVideoFileName => !string.IsNullOrEmpty(SpeedVideoFilePath) ? Path.GetFileName(SpeedVideoFilePath) : "Ningún video seleccionado";
    public bool HasSpeedVideo => !string.IsNullOrWhiteSpace(SpeedVideoFilePath);
    public bool HasNoSpeedVideo => !HasSpeedVideo;

    private VideoMetadataDto? _speedMetadata;
    public VideoMetadataDto? SpeedMetadata
    {
        get => _speedMetadata;
        private set
        {
            _speedMetadata = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSpeedMetadata));
            OnPropertyChanged(nameof(SpeedResolution));
            OnPropertyChanged(nameof(SpeedVideoCodec));
            OnPropertyChanged(nameof(SpeedAudioCodec));
            OnPropertyChanged(nameof(SpeedFps));
            OnPropertyChanged(nameof(SpeedFileSizeString));
        }
    }

    public bool HasSpeedMetadata => _speedMetadata != null;
    public string SpeedResolution => _speedMetadata != null ? $"{_speedMetadata.Width} × {_speedMetadata.Height}" : string.Empty;
    public string SpeedVideoCodec => _speedMetadata != null ? _speedMetadata.VideoCodec.ToUpperInvariant() : string.Empty;
    public string SpeedAudioCodec => _speedMetadata != null ? (!string.IsNullOrWhiteSpace(_speedMetadata.AudioCodec) ? _speedMetadata.AudioCodec.ToUpperInvariant() : "Sin audio") : string.Empty;
    public string SpeedFps => _speedMetadata != null && _speedMetadata.FrameRate > 0 ? $"{_speedMetadata.FrameRate:F0} FPS" : string.Empty;
    public string SpeedFileSizeString
    {
        get
        {
            if (_speedMetadata == null || _speedMetadata.FileSizeBytes <= 0) return string.Empty;
            double mb = _speedMetadata.FileSizeBytes / (1024.0 * 1024.0);
            return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
        }
    }

    private TimeSpan _speedOriginalDuration = TimeSpan.Zero;
    public TimeSpan SpeedOriginalDuration
    {
        get => _speedOriginalDuration;
        set
        {
            _speedOriginalDuration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeedOriginalDurationString));
            OnPropertyChanged(nameof(SpeedEstimatedDuration));
            OnPropertyChanged(nameof(SpeedEstimatedDurationString));
            OnPropertyChanged(nameof(SpeedTimeDifferenceString));
            OnPropertyChanged(nameof(SpeedTimeSavingsString));
        }
    }

    public string SpeedOriginalDurationString => TimeRange.FormatFFmpegTime(SpeedOriginalDuration);

    private double _speedMultiplier = 2.0;
    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            var rounded = Math.Round(value, 2);
            if (Math.Abs(_speedMultiplier - rounded) > 0.001)
            {
                _speedMultiplier = Math.Clamp(rounded, 0.25, 8.0);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedMultiplierString));
                OnPropertyChanged(nameof(SpeedEstimatedDuration));
                OnPropertyChanged(nameof(SpeedEstimatedDurationString));
                OnPropertyChanged(nameof(SpeedTimeDifferenceString));
                OnPropertyChanged(nameof(SpeedTimeSavingsString));
            }
        }
    }

    public string SpeedMultiplierString => $"{SpeedMultiplier:0.00}×";

    private bool _speedMuteAudio = false;
    public bool SpeedMuteAudio
    {
        get => _speedMuteAudio;
        set
        {
            if (_speedMuteAudio != value)
            {
                _speedMuteAudio = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedKeepAudio));
            }
        }
    }

    public bool SpeedKeepAudio
    {
        get => !_speedMuteAudio;
        set
        {
            if (value != !_speedMuteAudio)
            {
                _speedMuteAudio = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedMuteAudio));
            }
        }
    }

    public TimeSpan SpeedEstimatedDuration => SpeedMultiplier > 0 && SpeedOriginalDuration > TimeSpan.Zero
        ? TimeSpan.FromSeconds(SpeedOriginalDuration.TotalSeconds / SpeedMultiplier)
        : TimeSpan.Zero;

    public string SpeedEstimatedDurationString => TimeRange.FormatFFmpegTime(SpeedEstimatedDuration);

    public string SpeedTimeSavingsString
    {
        get
        {
            if (SpeedMultiplier > 1.0)
            {
                double savingPercent = (1.0 - 1.0 / SpeedMultiplier) * 100.0;
                return $"{savingPercent:0.#}% más rápido";
            }
            if (SpeedMultiplier < 1.0)
            {
                double slowerPercent = (1.0 / SpeedMultiplier - 1.0) * 100.0;
                return $"{slowerPercent:0.#}% más lento (cámara lenta)";
            }
            return "Velocidad original (1.0×)";
        }
    }

    public string SpeedTimeDifferenceString
    {
        get
        {
            if (SpeedOriginalDuration <= TimeSpan.Zero) return "—";

            // Se devuelve la magnitud con la palabra que la explica: el signo
            // suelto se leia al reves dentro de la frase ("te ahorras -38 s").
            if (SpeedMultiplier > 1.0)
            {
                var diff = SpeedOriginalDuration - SpeedEstimatedDuration;
                return $"{TimeRange.FormatFFmpegTime(diff)} menos";
            }
            if (SpeedMultiplier < 1.0)
            {
                var diff = SpeedEstimatedDuration - SpeedOriginalDuration;
                return $"{TimeRange.FormatFFmpegTime(diff)} más";
            }
            return "lo mismo";
        }
    }

    public void SetSpeedPreset(object? param)
    {
        if (param == null) return;
        if (double.TryParse(param.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
        {
            SpeedMultiplier = m;
        }
    }

    public bool CanChangeSpeed() => !IsProcessing && HasSpeedVideo && SpeedMultiplier > 0;

    public async Task LoadSpeedVideoAsync(string path)
    {
        StatusMessage = "Leyendo el video…";
        var result = await _analyzeVideoUseCase.ExecuteAsync(path);
        if (result.IsSuccess)
        {
            var meta = result.Value;
            SpeedMetadata = meta;
            SpeedVideoFilePath = meta.FilePath;
            SpeedOriginalDuration = meta.Duration;
            StatusMessage = "Video listo. Elige a qué velocidad quieres exportarlo.";
        }
        else
        {
            SpeedMetadata = null;
            SpeedVideoFilePath = null;
            StatusMessage = $"No se pudo abrir el video: {result.ErrorMessage}";
        }
    }

    private async Task ExecuteChangeSpeedAsync(string? explicitDestinationPath = null)
    {
        if (!CanChangeSpeed() || string.IsNullOrWhiteSpace(SpeedVideoFilePath)) return;

        var multSuffix = SpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_');
        var fileName = Path.GetFileNameWithoutExtension(SpeedVideoFilePath);
        var ext = Path.GetExtension(SpeedVideoFilePath);
        var dir = Path.GetDirectoryName(SpeedVideoFilePath) ?? string.Empty;

        string destPath;
        if (!string.IsNullOrWhiteSpace(explicitDestinationPath))
        {
            destPath = explicitDestinationPath;
        }
        else if (RequestSaveFilePath != null)
        {
            var chosen = RequestSaveFilePath("Guardar Video Acelerado", $"{fileName}_{multSuffix}x{ext}", dir, ext);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                StatusMessage = "Aceleración cancelada.";
                return;
            }
            destPath = chosen;
        }
        else
        {
            destPath = GenerateSafeOutputPath(dir, $"{fileName}_{multSuffix}x", ext);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = $"Acelerando a {SpeedMultiplierString}…";

        try
        {
            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var request = new ChangeSpeedRequestDto(
                SourceVideoPath: SpeedVideoFilePath,
                DestinationVideoPath: destPath,
                SpeedMultiplier: SpeedMultiplier,
                OriginalDuration: SpeedOriginalDuration,
                MuteAudio: SpeedMuteAudio);

            var speedResult = await _changeSpeedUseCase.ExecuteAsync(request, progressReporter);

            if (speedResult.IsSuccess)
            {
                StatusMessage = $"Video acelerado y guardado en {Path.GetFileName(destPath)}. Dura {TimeRange.FormatFFmpegTime(speedResult.Value.NewDuration)}.";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"No se pudo acelerar: {speedResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo acelerar: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    #endregion

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
