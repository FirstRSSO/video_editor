using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.UI.ViewModels;

namespace RecortadorDeVideos.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider = false;
    private bool _isPlaying = false;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            try
            {
                var current = VideoPlayer.Position;
                _viewModel.CurrentPlaybackPosition = current;

                var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (total > 0)
                {
                    TimelineSlider.Value = Math.Clamp((current.TotalSeconds / total) * 100.0, 0.0, 100.0);
                }
            }
            catch
            {
                // Prevenir excepciones durante transiciones de estado del reproductor
            }
        }
    }

    private async void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Video (*.mp4;*.mkv;*.mov)|*.mp4;*.mkv;*.mov|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Video para Recortar"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadVideoAsync(dialog.FileName);
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var file = files[0];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".mp4" or ".mkv" or ".mov" or ".avi")
                {
                    await LoadVideoAsync(file);
                }
            }
        }
    }

    private async Task LoadVideoAsync(string path)
    {
        try
        {
            VideoPlayer.Stop();
            _timer.Stop();
            _isPlaying = false;
            BtnPlayPause.Content = "▶ Reproducir";
            TimelineSlider.Value = 0;

            VideoPlayer.Source = new Uri(path);
            VideoPlayer.Play();
            VideoPlayer.Pause();

            await _viewModel.LoadVideoAsync(path);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Error al cargar video: {ex.Message}";
        }
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            TimelineSlider.Maximum = 100;
        }
    }

    private void VideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _viewModel.StatusMessage = $"Aviso del reproductor: {e.ErrorException?.Message ?? "No se pudo renderizar la previsualización directa."}";
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (!_viewModel.HasVideo) return;

        try
        {
            if (!_isPlaying)
            {
                VideoPlayer.Play();
                _timer.Start();
                _isPlaying = true;
                BtnPlayPause.Content = "⏸ Pausar";
            }
            else
            {
                VideoPlayer.Pause();
                _timer.Stop();
                _isPlaying = false;
                BtnPlayPause.Content = "▶ Reproducir";
            }
        }
        catch
        {
            // Proteger contra fallos en cambio de estado
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VideoPlayer.Stop();
            _timer.Stop();
            _isPlaying = false;
            TimelineSlider.Value = 0;
            _viewModel.CurrentPlaybackPosition = TimeSpan.Zero;
            BtnPlayPause.Content = "▶ Reproducir";
        }
        catch
        {
        }
    }

    private void BtnBack5_Click(object sender, RoutedEventArgs e) => SeekRelative(-5.0);
    private void BtnBack1_Click(object sender, RoutedEventArgs e) => SeekRelative(-1.0);
    private void BtnFwd1_Click(object sender, RoutedEventArgs e) => SeekRelative(1.0);
    private void BtnFwd5_Click(object sender, RoutedEventArgs e) => SeekRelative(5.0);

    public void SeekRelative(double deltaSeconds)
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var current = VideoPlayer.Position.TotalSeconds;
            var targetSeconds = Math.Clamp(current + deltaSeconds, 0.0, Math.Max(0.0, total - 0.1));
            var targetTime = TimeSpan.FromSeconds(targetSeconds);

            VideoPlayer.Position = targetTime;
            _viewModel.CurrentPlaybackPosition = targetTime;

            if (total > 0)
            {
                TimelineSlider.Value = (targetSeconds / total) * 100.0;
            }
        }
        catch
        {
            // Evitar cualquier crash por llamadas rápidas a DirectShow
        }
    }

    private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
        _timer.Stop();
    }

    private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        CommitSeek();

        if (_isPlaying)
        {
            try
            {
                VideoPlayer.Play();
                _timer.Start();
            }
            catch { }
        }
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            // Durante el arrastre, solo actualizamos el indicador visual de tiempo sin saturar DirectShow
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var targetSeconds = Math.Clamp((TimelineSlider.Value / 100.0) * total, 0.0, Math.Max(0.0, total - 0.1));
            _viewModel.CurrentPlaybackPosition = TimeSpan.FromSeconds(targetSeconds);
        }
    }

    private void CommitSeek()
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var targetSeconds = Math.Clamp((TimelineSlider.Value / 100.0) * total, 0.0, Math.Max(0.0, total - 0.1));
            var targetTime = TimeSpan.FromSeconds(targetSeconds);

            VideoPlayer.Position = targetTime;
            _viewModel.CurrentPlaybackPosition = targetTime;
        }
        catch
        {
            // Evitar crash si el reproductor está en transición
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            TogglePlayPause();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            SeekRelative(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 5.0);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            SeekRelative(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1.0 : -5.0);
            e.Handled = true;
        }
        else if (e.Key == Key.OemOpenBrackets) // '['
        {
            _viewModel.StartTime = _viewModel.CurrentPlaybackPosition;
            e.Handled = true;
        }
        else if (e.Key == Key.OemCloseBrackets) // ']'
        {
            _viewModel.EndTime = _viewModel.CurrentPlaybackPosition;
            e.Handled = true;
        }
    }

    private void OpenAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Audio (*.mp3;*.wav;*.aac;*.m4a)|*.mp3;*.wav;*.aac;*.m4a|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Audio Secundario"
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.AudioFilePath = dialog.FileName;
        }
    }

    private void AudioMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            if (Enum.TryParse<AudioMode>(tag, out var mode))
            {
                _viewModel.SelectedAudioMode = mode;
            }
        }
    }
}
