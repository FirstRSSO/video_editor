using System.Windows;
using System.Windows.Controls;
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
            var current = VideoPlayer.Position;
            _viewModel.CurrentPlaybackPosition = current;

            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (total > 0)
            {
                TimelineSlider.Value = (current.TotalSeconds / total) * 100.0;
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
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".mp4" or ".mkv" or ".mov" or ".avi")
                {
                    await LoadVideoAsync(file);
                }
            }
        }
    }

    private async Task LoadVideoAsync(string path)
    {
        VideoPlayer.Stop();
        _timer.Stop();
        VideoPlayer.Source = new Uri(path);
        VideoPlayer.Play();
        VideoPlayer.Pause();

        await _viewModel.LoadVideoAsync(path);
        _timer.Start();
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            TimelineSlider.Maximum = 100;
        }
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (BtnPlayPause.Content.ToString()!.Contains("Reproducir"))
        {
            VideoPlayer.Play();
            _timer.Start();
            BtnPlayPause.Content = "⏸ Pausar";
        }
        else
        {
            VideoPlayer.Pause();
            BtnPlayPause.Content = "▶ Reproducir";
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Stop();
        _timer.Stop();
        TimelineSlider.Value = 0;
        _viewModel.CurrentPlaybackPosition = TimeSpan.Zero;
        BtnPlayPause.Content = "▶ Reproducir";
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TimelineSlider.IsMouseCaptureWithin && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var newSeconds = (TimelineSlider.Value / 100.0) * total;
            var targetTime = TimeSpan.FromSeconds(newSeconds);
            VideoPlayer.Position = targetTime;
            _viewModel.CurrentPlaybackPosition = targetTime;
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
