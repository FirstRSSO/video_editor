using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.AnalyzeVideo;
using RecortadorDeVideos.Application.UseCases.TrimVideo;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;

namespace RecortadorDeVideos.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IAnalyzeVideoUseCase _analyzeVideoUseCase;
    private readonly ITrimVideoUseCase _trimVideoUseCase;

    private string? _videoFilePath;
    private string? _audioFilePath;
    private TimeSpan _videoDuration = TimeSpan.Zero;
    private TimeSpan _startTime = TimeSpan.Zero;
    private TimeSpan _endTime = TimeSpan.Zero;
    private TimeSpan _currentPlaybackPosition = TimeSpan.Zero;
    private double _progressPercentage = 0;
    private bool _isProcessing = false;
    private string _statusMessage = "Listo. Selecciona o arrastra un video MP4 para comenzar.";

    private AudioMode _selectedAudioMode = AudioMode.KeepOriginal;
    private double _mainVolume = 1.0;
    private double _backgroundVolume = 0.3;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(IAnalyzeVideoUseCase analyzeVideoUseCase, ITrimVideoUseCase trimVideoUseCase)
    {
        _analyzeVideoUseCase = analyzeVideoUseCase ?? throw new ArgumentNullException(nameof(analyzeVideoUseCase));
        _trimVideoUseCase = trimVideoUseCase ?? throw new ArgumentNullException(nameof(trimVideoUseCase));

        TrimCommand = new RelayCommand(async () => await ExecuteTrimAsync(), () => CanTrim());
        SetStartToCurrentCommand = new RelayCommand(() => StartTime = CurrentPlaybackPosition, () => HasVideo);
        SetEndToCurrentCommand = new RelayCommand(() => EndTime = CurrentPlaybackPosition, () => HasVideo);
    }

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
        set { _startTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartTimeString)); OnPropertyChanged(nameof(TrimDurationString)); }
    }

    public string StartTimeString => TimeRange.FormatFFmpegTime(StartTime);

    public TimeSpan EndTime
    {
        get => _endTime;
        set { _endTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTimeString)); OnPropertyChanged(nameof(TrimDurationString)); }
    }

    public string EndTimeString => TimeRange.FormatFFmpegTime(EndTime);

    public TimeSpan CurrentPlaybackPosition
    {
        get => _currentPlaybackPosition;
        set { _currentPlaybackPosition = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionString)); }
    }

    public string CurrentPositionString => TimeRange.FormatFFmpegTime(CurrentPlaybackPosition);

    public string TrimDurationString => EndTime > StartTime ? TimeRange.FormatFFmpegTime(EndTime - StartTime) : "00:00:00.000";

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
        set { _selectedAudioMode = value; OnPropertyChanged(); }
    }

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

    public ICommand TrimCommand { get; }
    public ICommand SetStartToCurrentCommand { get; }
    public ICommand SetEndToCurrentCommand { get; }

    public async Task LoadVideoAsync(string path)
    {
        StatusMessage = "Analizando video...";
        var result = await _analyzeVideoUseCase.ExecuteAsync(path);
        if (result.IsSuccess)
        {
            var meta = result.Value;
            VideoFilePath = meta.FilePath;
            VideoDuration = meta.Duration;
            StartTime = TimeSpan.Zero;
            EndTime = meta.Duration;
            StatusMessage = $"Video cargado: {meta.Width}x{meta.Height} | Códec: {meta.VideoCodec} | Duración: {DurationString}";
        }
        else
        {
            StatusMessage = $"Error al cargar: {result.ErrorMessage}";
        }
    }

    private bool CanTrim()
    {
        return !IsProcessing && HasVideo && EndTime > StartTime;
    }

    private async Task ExecuteTrimAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath)) return;

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = "Iniciando recorte sin pérdida de calidad...";

        try
        {
            var dir = Path.GetDirectoryName(VideoFilePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(VideoFilePath);
            var ext = Path.GetExtension(VideoFilePath);
            var destPath = Path.Combine(dir, $"{fileName}_recorte{ext}");

            var progressReporter = new Progress<double>(p => ProgressPercentage = p);

            var request = new TrimVideoRequestDto(
                SourceVideoPath: VideoFilePath,
                StartTime: StartTime,
                EndTime: EndTime,
                DestinationVideoPath: destPath,
                Strategy: CutStrategy.LosslessStreamCopy,
                AudioMode: SelectedAudioMode,
                ExternalAudioPath: AudioFilePath,
                MainVolume: MainVolume,
                BackgroundVolume: BackgroundVolume);

            var trimResult = await _trimVideoUseCase.ExecuteAsync(request, progressReporter);

            if (trimResult.IsSuccess)
            {
                StatusMessage = $"¡Recorte completado con éxito! Guardado en: {Path.GetFileName(destPath)}";
                ProgressPercentage = 100;
            }
            else
            {
                StatusMessage = $"Fallo al recortar: {trimResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error inesperado: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
