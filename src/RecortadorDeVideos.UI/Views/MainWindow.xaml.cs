using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;
using RecortadorDeVideos.UI.ViewModels;
using Path = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;
using WpfLine = System.Windows.Shapes.Line;

namespace RecortadorDeVideos.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider = false;
    private bool _isMouseDownOnSlider = false;
    private Point _sliderMouseDownPos;
    private bool _wasPlayingBeforeDrag = false;
    private bool _isPlaying = false;
    private WindowState _previousWindowState = WindowState.Maximized;
    private WindowStyle _previousWindowStyle = WindowStyle.SingleBorderWindow;
    private Thickness _previousRootMargin = new Thickness(22, 0, 22, 18);
    private GridLength _savedSidebarWidth = new GridLength(320);

    // Supresión del temporizador durante saltos para evitar rebotes asíncronos en DirectShow
    private DateTime _suppressTimerUntil = DateTime.MinValue;

    private readonly DispatcherTimer _speedTimer;
    private bool _isSpeedPlaying = false;
    private bool _isDraggingSpeedSlider = false;

    // Temporizador y estado de la pestaña Editar (Línea de tiempo multipista Camtasia)
    private readonly DispatcherTimer _editorTimer;
    private bool _isEditorPlaying = false;
    private bool _isDraggingRuler = false;
    private bool _isDraggingClip = false;
    private AudioClipItemViewModel? _draggedClip = null;
    private Point _clipDragStartMousePoint;
    private TimeSpan _clipDragStartTime;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.RequestToggleFullscreen += ToggleFullscreen;
        _viewModel.RequestSeekToPosition += PerformSeek;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible) || 
                e.PropertyName == nameof(MainViewModel.IsFullscreen))
            {
                UpdateSidebarVisibility();
            }
            else if (e.PropertyName == nameof(MainViewModel.VideoDuration) ||
                     e.PropertyName == nameof(MainViewModel.TimelineZoom) ||
                     e.PropertyName == nameof(MainViewModel.TimelineTotalWidth) ||
                     e.PropertyName == nameof(MainViewModel.SelectedAudioClip))
            {
                UpdateTimelineDisplay();
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentPlaybackPosition) && !_isEditorPlaying)
            {
                UpdatePlayheadPosition();
            }
            else if (e.PropertyName == nameof(MainViewModel.MainVolume))
            {
                if (EditorVideoPlayer != null && EditorVolumeSlider != null)
                {
                    EditorVideoPlayer.Volume = Math.Clamp(_viewModel.MainVolume * EditorVolumeSlider.Value, 0.0, 1.0);
                }
            }
        };

        _viewModel.AudioClips.CollectionChanged += (s, e) =>
        {
            UpdateAudioClipsOnTimeline();
        };

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += Timer_Tick;

        _speedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _speedTimer.Tick += SpeedTimer_Tick;

        _editorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _editorTimer.Tick += EditorTimer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isMouseDownOnSlider || _isDraggingSlider || DateTime.UtcNow < _suppressTimerUntil) return;
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var current = VideoPlayer.Position;

            // Evitar que lecturas transitorias anómalas (como 0 segundos justo tras un salto) sobreescriban la barra
            if (current == TimeSpan.Zero && _viewModel.CurrentPlaybackPosition.TotalSeconds > 1.0)
            {
                return;
            }

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

    private async void OpenSpeedVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Video (*.mp4;*.mkv;*.mov;*.avi)|*.mp4;*.mkv;*.mov;*.avi|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Video para Acelerar"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadSpeedVideoAsync(dialog.FileName);
        }
    }

    private void OpenFilesToMerge_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Archivos de Video (*.mp4;*.mkv;*.mov;*.avi)|*.mp4;*.mkv;*.mov;*.avi|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Múltiples Videos para Unir"
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            _viewModel.AddFilesToMergeList(dialog.FileNames);
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var videoFiles = files.Where(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".ts";
        }).ToList();

        if (videoFiles.Count == 0) return;

        if (MainTabControl.SelectedItem == TabItemSpeed)
        {
            // Pestaña de Acelerar Video
            await LoadSpeedVideoAsync(videoFiles[0]);
        }
        else if (MainTabControl.SelectedItem == TabItemMerge || videoFiles.Count > 1)
        {
            // Añadir a lista de unión y enfocar pestaña de unión
            _viewModel.AddFilesToMergeList(videoFiles);
            MainTabControl.SelectedItem = TabItemMerge;
        }
        else if (MainTabControl.SelectedItem == TabItemEditor)
        {
            await LoadVideoAsync(videoFiles[0]);
            SyncEditorVideoWithMain();
            _viewModel.UpdateTimelineMetrics(EditorTimelineScrollViewer.ActualWidth > 100 ? EditorTimelineScrollViewer.ActualWidth : 800);
            UpdateTimelineDisplay();
        }
        else
        {
            // Cargar en el reproductor para recortar
            await LoadVideoAsync(videoFiles[0]);
        }
    }

    private async Task LoadSpeedVideoAsync(string path)
    {
        try
        {
            SpeedVideoPlayer.Stop();
            _speedTimer.Stop();
            _isSpeedPlaying = false;
            SetTransportGlyph(BtnSpeedPlayPause, playing: false);
            SpeedTimelineSlider.Value = 0;
            TxtSpeedPlayerTime.Text = "00:00:00 / 00:00:00";

            SpeedVideoPlayer.Source = new Uri(path);
            SpeedVideoPlayer.Play();
            SpeedVideoPlayer.Pause();

            await _viewModel.LoadSpeedVideoAsync(path);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Error al cargar video para acelerar: {ex.Message}";
        }
    }

    private async Task LoadVideoAsync(string path)
    {
        try
        {
            VideoPlayer.Stop();
            _timer.Stop();
            _isPlaying = false;
            _suppressTimerUntil = DateTime.MinValue;
            _isDraggingSlider = false;
            _isMouseDownOnSlider = false;
            SetTransportGlyph(BtnPlayPause, playing: false);
            TimelineSlider.Value = 0;

            VideoPlayer.Source = new Uri(path);
            VideoPlayer.Play();
            VideoPlayer.Pause();

            await _viewModel.LoadVideoAsync(path);
            SyncEditorVideoWithMain();
            _viewModel.UpdateTimelineMetrics(EditorTimelineScrollViewer.ActualWidth > 100 ? EditorTimelineScrollViewer.ActualWidth : 800);
            UpdateTimelineDisplay();
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
                SetTransportGlyph(BtnPlayPause, playing: true);
            }
            else
            {
                VideoPlayer.Pause();
                _timer.Stop();
                _isPlaying = false;
                SetTransportGlyph(BtnPlayPause, playing: false);
            }
        }
        catch
        {
            // Proteger contra fallos en cambio de estado
        }
    }

    /// <summary>
    /// Pone en el disco de transporte el glifo que corresponde al estado actual.
    /// El triángulo de reproducir se desplaza un par de píxeles a la derecha para
    /// que quede centrado a la vista y no a la caja.
    /// </summary>
    private void SetTransportGlyph(Button button, bool playing)
    {
        var glyphSize = button.Width <= 36 ? 13.0 : 16.0;

        button.Content = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(playing ? "IconPauseGeometry" : "IconPlayGeometry"),
            Fill = (Brush)FindResource("Gate"),
            Width = glyphSize,
            Height = glyphSize,
            Stretch = Stretch.Uniform,
            Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0)
        };

        var label = playing ? "Pausar" : "Reproducir";
        button.ToolTip = $"{label} (Espacio)";
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VideoPlayer.Stop();
            _timer.Stop();
            _isPlaying = false;
            _suppressTimerUntil = DateTime.MinValue;
            _isDraggingSlider = false;
            _isMouseDownOnSlider = false;
            TimelineSlider.Value = 0;
            _viewModel.CurrentPlaybackPosition = TimeSpan.Zero;
            SetTransportGlyph(BtnPlayPause, playing: false);
        }
        catch
        {
        }
    }

    private void BtnBack5_Click(object sender, RoutedEventArgs e) => SeekRelative(-5.0);
    private void BtnBack1_Click(object sender, RoutedEventArgs e) => SeekRelative(-1.0);
    private void BtnFwd1_Click(object sender, RoutedEventArgs e) => SeekRelative(1.0);
    private void BtnFwd5_Click(object sender, RoutedEventArgs e) => SeekRelative(5.0);

    public void PerformSeek(TimeSpan targetTime)
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var clampedSeconds = Math.Clamp(targetTime.TotalSeconds, 0.0, Math.Max(0.0, total - 0.05));
            var finalTarget = TimeSpan.FromSeconds(clampedSeconds);

            _suppressTimerUntil = DateTime.UtcNow.AddMilliseconds(700);
            _viewModel.CurrentPlaybackPosition = finalTarget;

            if (total > 0)
            {
                TimelineSlider.Value = Math.Clamp((clampedSeconds / total) * 100.0, 0.0, 100.0);
            }

            if (_isPlaying)
            {
                VideoPlayer.Pause();
                VideoPlayer.Position = finalTarget;
                VideoPlayer.Play();
            }
            else
            {
                VideoPlayer.Position = finalTarget;
            }

            if (EditorVideoPlayer.NaturalDuration.HasTimeSpan)
            {
                EditorVideoPlayer.Position = finalTarget;
            }
            UpdatePlayheadPosition();
        }
        catch
        {
            // Evitar cualquier crash por llamadas rápidas durante transiciones de DirectShow
        }
    }

    public void SeekRelative(double deltaSeconds)
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var baseTime = DateTime.UtcNow < _suppressTimerUntil
                ? _viewModel.CurrentPlaybackPosition
                : VideoPlayer.Position;

            var targetSeconds = Math.Clamp(baseTime.TotalSeconds + deltaSeconds, 0.0, Math.Max(0.0, total - 0.05));
            PerformSeek(TimeSpan.FromSeconds(targetSeconds));
        }
        catch
        {
        }
    }

    private TimeSpan GetTimeFromMousePosition(Point mousePos)
    {
        if (TimelineSlider.ActualWidth <= 0 || !VideoPlayer.NaturalDuration.HasTimeSpan)
            return TimeSpan.Zero;

        const double thumbWidth = 16.0;
        const double halfThumb = thumbWidth / 2.0;
        double usableWidth = TimelineSlider.ActualWidth - thumbWidth;

        double ratio;
        if (usableWidth > 0)
        {
            ratio = Math.Clamp((mousePos.X - halfThumb) / usableWidth, 0.0, 1.0);
        }
        else
        {
            ratio = Math.Clamp(mousePos.X / TimelineSlider.ActualWidth, 0.0, 1.0);
        }

        var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        var targetSeconds = Math.Clamp(ratio * total, 0.0, Math.Max(0.0, total - 0.05));
        return TimeSpan.FromSeconds(targetSeconds);
    }

    private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        _isMouseDownOnSlider = true;
        _isDraggingSlider = false;
        _sliderMouseDownPos = e.GetPosition(TimelineSlider);
        _wasPlayingBeforeDrag = _isPlaying;

        // Comportamiento idéntico a YouTube: saltar inmediatamente al punto donde se hace clic
        var targetTime = GetTimeFromMousePosition(_sliderMouseDownPos);
        PerformSeek(targetTime);

        TimelineSlider.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isMouseDownOnSlider && e.LeftButton == MouseButtonState.Pressed)
        {
            var currentPos = e.GetPosition(TimelineSlider);
            if (!_isDraggingSlider && Math.Abs(currentPos.X - _sliderMouseDownPos.X) > 4)
            {
                _isDraggingSlider = true;
                if (_wasPlayingBeforeDrag)
                {
                    try { VideoPlayer.Pause(); } catch { }
                }
            }

            if (_isDraggingSlider)
            {
                var targetTime = GetTimeFromMousePosition(currentPos);
                _viewModel.CurrentPlaybackPosition = targetTime;

                var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (total > 0)
                {
                    TimelineSlider.Value = Math.Clamp((targetTime.TotalSeconds / total) * 100.0, 0.0, 100.0);
                }

                _suppressTimerUntil = DateTime.UtcNow.AddMilliseconds(700);

                // Scrubbing visual en tiempo real mientras se arrastra
                try
                {
                    VideoPlayer.Position = targetTime;
                }
                catch { }
            }

            e.Handled = true;
        }
    }

    private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMouseDownOnSlider)
        {
            bool wasDragging = _isDraggingSlider;

            _isMouseDownOnSlider = false;
            _isDraggingSlider = false;

            if (TimelineSlider.IsMouseCaptured)
            {
                TimelineSlider.ReleaseMouseCapture();
            }

            if (wasDragging)
            {
                // Si estaba arrastrando, consolidar el salto en la posición donde soltó
                var targetTime = GetTimeFromMousePosition(e.GetPosition(TimelineSlider));
                PerformSeek(targetTime);

                if (_wasPlayingBeforeDrag)
                {
                    try
                    {
                        VideoPlayer.Play();
                        _timer.Start();
                        _isPlaying = true;
                        SetTransportGlyph(BtnPlayPause, playing: true);
                    }
                    catch { }
                }
            }
            else
            {
                // Clic simple: el salto ya se realizó inmediatamente en MouseDown.
                // Asegurar que el temporizador permanezca suprimido para absorber la latencia de DirectShow
                _suppressTimerUntil = DateTime.UtcNow.AddMilliseconds(700);
            }

            e.Handled = true;
        }
    }

    private void TimelineSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isMouseDownOnSlider)
        {
            bool wasDragging = _isDraggingSlider;

            _isMouseDownOnSlider = false;
            _isDraggingSlider = false;

            if (wasDragging && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                var targetSeconds = Math.Clamp((TimelineSlider.Value / 100.0) * total, 0.0, Math.Max(0.0, total - 0.05));
                PerformSeek(TimeSpan.FromSeconds(targetSeconds));

                if (_wasPlayingBeforeDrag)
                {
                    try
                    {
                        VideoPlayer.Play();
                        _timer.Start();
                        _isPlaying = true;
                        SetTransportGlyph(BtnPlayPause, playing: true);
                    }
                    catch { }
                }
            }
        }
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var targetSeconds = Math.Clamp((TimelineSlider.Value / 100.0) * total, 0.0, Math.Max(0.0, total - 0.05));
            _viewModel.CurrentPlaybackPosition = TimeSpan.FromSeconds(targetSeconds);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;

        if (e.Key == Key.Escape && _viewModel.IsFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.F11 || (e.Key == Key.F && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Control)))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            if (MainTabControl.SelectedItem == TabItemEditor)
            {
                ToggleEditorPlayPause();
            }
            else if (MainTabControl.SelectedItem == TabItemSpeed)
            {
                ToggleSpeedPlayPause();
            }
            else
            {
                TogglePlayPause();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 5.0;
            if (MainTabControl.SelectedItem == TabItemEditor)
            {
                SeekEditorRelative(delta);
            }
            else
            {
                SeekRelative(delta);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1.0 : -5.0;
            if (MainTabControl.SelectedItem == TabItemEditor)
            {
                SeekEditorRelative(delta);
            }
            else
            {
                SeekRelative(delta);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.I || e.Key == Key.OemOpenBrackets) // 'I' o '['
        {
            if (_viewModel.HasVideo)
            {
                _viewModel.StartTime = _viewModel.CurrentPlaybackPosition;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.O || e.Key == Key.OemCloseBrackets) // 'O' o ']'
        {
            if (_viewModel.HasVideo)
            {
                _viewModel.EndTime = _viewModel.CurrentPlaybackPosition;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && _viewModel.AddCurrentRangeToSegmentsCommand.CanExecute(null))
        {
            _viewModel.AddCurrentRangeToSegmentsCommand.Execute(null);
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

    private void AddAudioClip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Audio (*.mp3;*.wav;*.aac;*.m4a;*.ogg;*.flac)|*.mp3;*.wav;*.aac;*.m4a;*.ogg;*.flac|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Audio o Locución para el Tutorial",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var fileName in dialog.FileNames)
            {
                _viewModel.AddAudioClip(fileName);
            }
            UpdateTimelineDisplay();
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

    private void UpdateSidebarVisibility()
    {
        if (_viewModel.IsSidebarVisible)
        {
            SegmentsColDef.Width = _savedSidebarWidth.Value > 50 ? _savedSidebarWidth : new GridLength(320);
            SplitterColDef.Width = GridLength.Auto;
            if (SegmentsPanelBorder != null) SegmentsPanelBorder.Visibility = Visibility.Visible;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (SegmentsColDef.Width.Value > 50)
                _savedSidebarWidth = SegmentsColDef.Width;
            SegmentsColDef.Width = new GridLength(0);
            SplitterColDef.Width = new GridLength(0);
            if (SegmentsPanelBorder != null) SegmentsPanelBorder.Visibility = Visibility.Collapsed;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
        }
    }

    public void ToggleFullscreen()
    {
        if (!_viewModel.IsFullscreen)
        {
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousRootMargin = RootWindowGrid.Margin;

            _viewModel.IsFullscreen = true;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            RootWindowGrid.Margin = new Thickness(0);
            MainTabControl.Margin = new Thickness(0);
        }
        else
        {
            _viewModel.IsFullscreen = false;
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
            RootWindowGrid.Margin = _previousRootMargin;
            MainTabControl.Margin = new Thickness(0);
        }
        UpdateSidebarVisibility();
    }

    #region Controles de Previsualización para Acelerar Video

    private void SpeedTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSpeedSlider || !SpeedVideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var current = SpeedVideoPlayer.Position;
            var total = SpeedVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (total > 0)
            {
                SpeedTimelineSlider.Value = Math.Clamp((current.TotalSeconds / total) * 100.0, 0.0, 100.0);
                TxtSpeedPlayerTime.Text = $"{TimeRange.FormatFFmpegTime(current)} / {TimeRange.FormatFFmpegTime(SpeedVideoPlayer.NaturalDuration.TimeSpan)}";
            }
        }
        catch { }
    }

    private void SpeedTimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSpeedSlider = true;
    }

    private void SpeedTimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSpeedSlider = false;
        ApplySpeedSliderPosition();
    }

    private void SpeedTimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSpeedSlider)
        {
            ApplySpeedSliderPosition();
        }
    }

    private void ApplySpeedSliderPosition()
    {
        if (!SpeedVideoPlayer.NaturalDuration.HasTimeSpan) return;
        try
        {
            var total = SpeedVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var targetSeconds = (SpeedTimelineSlider.Value / 100.0) * total;
            SpeedVideoPlayer.Position = TimeSpan.FromSeconds(targetSeconds);
            TxtSpeedPlayerTime.Text = $"{TimeRange.FormatFFmpegTime(TimeSpan.FromSeconds(targetSeconds))} / {TimeRange.FormatFFmpegTime(SpeedVideoPlayer.NaturalDuration.TimeSpan)}";
        }
        catch { }
    }

    private void SpeedVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (SpeedVideoPlayer.NaturalDuration.HasTimeSpan)
        {
            SpeedTimelineSlider.Maximum = 100;
            TxtSpeedPlayerTime.Text = $"00:00:00.000 / {TimeRange.FormatFFmpegTime(SpeedVideoPlayer.NaturalDuration.TimeSpan)}";
        }
    }

    private void SpeedVideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _viewModel.StatusMessage = $"Aviso de previsualización: {e.ErrorException?.Message ?? "No se pudo reproducir la previsualización directa."}";
    }

    private void BtnSpeedPlayPause_Click(object sender, RoutedEventArgs e)
    {
        ToggleSpeedPlayPause();
    }

    private void ToggleSpeedPlayPause()
    {
        if (!_viewModel.HasSpeedVideo) return;

        try
        {
            if (!_isSpeedPlaying)
            {
                SpeedVideoPlayer.Play();
                _speedTimer.Start();
                _isSpeedPlaying = true;
                SetTransportGlyph(BtnSpeedPlayPause, playing: true);
            }
            else
            {
                SpeedVideoPlayer.Pause();
                _speedTimer.Stop();
                _isSpeedPlaying = false;
                SetTransportGlyph(BtnSpeedPlayPause, playing: false);
            }
        }
        catch { }
    }

    #endregion

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void VideoMonitor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    #region Lógica de Mesa de Montaje y Línea de Tiempo (Editor estilo Camtasia)

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;

        if (MainTabControl.SelectedItem != TabItemCut && _isPlaying)
        {
            TogglePlayPause();
        }

        if (MainTabControl.SelectedItem != TabItemEditor && _isEditorPlaying)
        {
            ToggleEditorPlayPause();
        }
        else if (MainTabControl.SelectedItem != TabItemEditor)
        {
            StopAllAudioClipsPlayback();
        }

        if (MainTabControl.SelectedItem != TabItemSpeed && _isSpeedPlaying)
        {
            ToggleSpeedPlayPause();
        }

        if (MainTabControl.SelectedItem == TabItemEditor)
        {
            SyncEditorVideoWithMain();
            _viewModel.UpdateTimelineMetrics(EditorTimelineScrollViewer.ActualWidth > 100 ? EditorTimelineScrollViewer.ActualWidth : 800);
            UpdateTimelineDisplay();
        }
    }

    private void SyncEditorVideoWithMain()
    {
        if (!_viewModel.HasVideo) return;

        try
        {
            var targetUri = new Uri(_viewModel.VideoFilePath!);
            if (EditorVideoPlayer.Source == null || EditorVideoPlayer.Source != targetUri)
            {
                EditorVideoPlayer.Source = targetUri;
                EditorVideoPlayer.Play();
                EditorVideoPlayer.Pause();
            }

            if (EditorVolumeSlider != null)
            {
                EditorVideoPlayer.Volume = Math.Clamp(_viewModel.MainVolume * EditorVolumeSlider.Value, 0.0, 1.0);
            }
            EditorVideoPlayer.Position = _viewModel.CurrentPlaybackPosition;
            UpdatePlayheadPosition();
        }
        catch { }
    }

    private void EditorTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingRuler || _isDraggingClip) return;
        if (!EditorVideoPlayer.NaturalDuration.HasTimeSpan) return;

        try
        {
            var current = EditorVideoPlayer.Position;
            _viewModel.CurrentPlaybackPosition = current;
            UpdatePlayheadPosition();

            // Sincronizar reproducción en tiempo real de todas las locuciones
            var masterVol = EditorVolumeSlider != null ? EditorVolumeSlider.Value : 1.0;
            foreach (var clip in _viewModel.AudioClips)
            {
                clip.SyncPlayback(current, _isEditorPlaying, masterVol);
            }

            // Auto-scroll horizontal suave si el cabezal se aproxima al borde visible
            if (EditorTimelineScrollViewer != null && _viewModel.TimelinePixelsPerSecond > 0)
            {
                var x = current.TotalSeconds * _viewModel.TimelinePixelsPerSecond;
                var scrollLeft = EditorTimelineScrollViewer.HorizontalOffset;
                var viewWidth = EditorTimelineScrollViewer.ViewportWidth;

                if (viewWidth > 50 && x > scrollLeft + viewWidth - 40)
                {
                    EditorTimelineScrollViewer.ScrollToHorizontalOffset(x - viewWidth / 2.0);
                }
                else if (viewWidth > 50 && x < scrollLeft)
                {
                    EditorTimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, x - 20));
                }
            }
        }
        catch { }
    }

    private void EditorVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (EditorTimelineScrollViewer != null)
        {
            _viewModel.UpdateTimelineMetrics(EditorTimelineScrollViewer.ActualWidth > 100 ? EditorTimelineScrollViewer.ActualWidth : 800);
        }
        UpdateTimelineDisplay();
    }

    private void EditorVideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _viewModel.StatusMessage = $"Aviso de montaje: {e.ErrorException?.Message ?? "No se pudo renderizar la previsualización."}";
    }

    private void BtnEditorPlayPause_Click(object sender, RoutedEventArgs e)
    {
        ToggleEditorPlayPause();
    }

    private void ToggleEditorPlayPause()
    {
        if (!_viewModel.HasVideo) return;

        try
        {
            if (!_isEditorPlaying)
            {
                if (EditorVideoPlayer.NaturalDuration.HasTimeSpan && 
                    EditorVideoPlayer.Position >= EditorVideoPlayer.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(100))
                {
                    EditorVideoPlayer.Position = TimeSpan.Zero;
                    _viewModel.CurrentPlaybackPosition = TimeSpan.Zero;
                }

                if (EditorVolumeSlider != null)
                {
                    EditorVideoPlayer.Volume = Math.Clamp(_viewModel.MainVolume * EditorVolumeSlider.Value, 0.0, 1.0);
                }

                EditorVideoPlayer.Play();
                _editorTimer.Start();
                _isEditorPlaying = true;
                SetTransportGlyph(BtnEditorPlayPause, playing: true);

                // Iniciar audios activos en el fotograma actual
                var current = EditorVideoPlayer.Position;
                var masterVol = EditorVolumeSlider != null ? EditorVolumeSlider.Value : 1.0;
                foreach (var clip in _viewModel.AudioClips)
                {
                    clip.SyncPlayback(current, true, masterVol);
                }
            }
            else
            {
                EditorVideoPlayer.Pause();
                _editorTimer.Stop();
                _isEditorPlaying = false;
                SetTransportGlyph(BtnEditorPlayPause, playing: false);
                StopAllAudioClipsPlayback();
            }
        }
        catch { }
    }

    private void StopAllAudioClipsPlayback()
    {
        foreach (var clip in _viewModel.AudioClips)
        {
            clip.StopPlayback();
        }
    }

    private void BtnEditorJumpStart_Click(object sender, RoutedEventArgs e) => SeekEditorVideo(TimeSpan.Zero);
    private void BtnEditorStepBack_Click(object sender, RoutedEventArgs e) => SeekEditorRelative(-1.0);
    private void BtnEditorStepFwd_Click(object sender, RoutedEventArgs e) => SeekEditorRelative(1.0);

    private void SeekEditorRelative(double deltaSeconds)
    {
        if (!_viewModel.HasVideo) return;
        var target = _viewModel.CurrentPlaybackPosition + TimeSpan.FromSeconds(deltaSeconds);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (_viewModel.VideoDuration > TimeSpan.Zero && target > _viewModel.VideoDuration) target = _viewModel.VideoDuration;
        SeekEditorVideo(target);
    }

    private void SeekEditorVideo(TimeSpan target)
    {
        try
        {
            EditorVideoPlayer.Position = target;
            _viewModel.CurrentPlaybackPosition = target;
            UpdatePlayheadPosition();

            // Sincronizar audios al nuevo instante
            var masterVol = EditorVolumeSlider != null ? EditorVolumeSlider.Value : 1.0;
            foreach (var clip in _viewModel.AudioClips)
            {
                clip.SyncPlayback(target, _isEditorPlaying, masterVol);
            }
        }
        catch { }
    }

    private void EditorVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EditorVideoPlayer != null && EditorVolumeSlider != null)
        {
            EditorVideoPlayer.Volume = Math.Clamp(_viewModel.MainVolume * EditorVolumeSlider.Value, 0.0, 1.0);
        }

        var masterVol = EditorVolumeSlider != null ? EditorVolumeSlider.Value : 1.0;
        foreach (var clip in _viewModel.AudioClips)
        {
            if (clip.IsPlaying)
            {
                clip.SyncPlayback(_viewModel.CurrentPlaybackPosition, _isEditorPlaying, masterVol);
            }
        }
    }

    private void TimelineRuler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.HasVideo || _viewModel.TimelinePixelsPerSecond <= 0) return;

        var pos = e.GetPosition(TimelineRulerCanvas);
        var targetSec = pos.X / _viewModel.TimelinePixelsPerSecond;
        var targetTime = TimeSpan.FromSeconds(Math.Clamp(targetSec, 0, _viewModel.VideoDuration.TotalSeconds));

        SeekEditorVideo(targetTime);

        if (sender is UIElement el)
        {
            el.CaptureMouse();
            _isDraggingRuler = true;
        }
    }

    private void TimelineRuler_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingRuler || !_viewModel.HasVideo || _viewModel.TimelinePixelsPerSecond <= 0) return;

        var pos = e.GetPosition(TimelineRulerCanvas);
        var targetSec = pos.X / _viewModel.TimelinePixelsPerSecond;
        var targetTime = TimeSpan.FromSeconds(Math.Clamp(targetSec, 0, _viewModel.VideoDuration.TotalSeconds));

        SeekEditorVideo(targetTime);
    }

    private void TimelineRuler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingRuler)
        {
            _isDraggingRuler = false;
            if (sender is UIElement el)
            {
                el.ReleaseMouseCapture();
            }
        }
    }

    private void TimelineTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.HasVideo || _viewModel.TimelinePixelsPerSecond <= 0) return;

        var pos = e.GetPosition(TimelineContentGrid);
        var targetSec = pos.X / _viewModel.TimelinePixelsPerSecond;
        var targetTime = TimeSpan.FromSeconds(Math.Clamp(targetSec, 0, _viewModel.VideoDuration.TotalSeconds));

        SeekEditorVideo(targetTime);
    }

    private void EditorTimelineScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 100)
        {
            _viewModel.UpdateTimelineMetrics(e.NewSize.Width);
            UpdateTimelineDisplay();
        }
    }

    private void EditorTimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // El scrollviewer mantiene el viewport sincronizado
    }

    private void UpdateTimelineDisplay()
    {
        RenderTimelineRuler();
        UpdateAudioClipsOnTimeline();
        UpdatePlayheadPosition();
    }

    private void RenderTimelineRuler()
    {
        if (TimelineRulerCanvas == null) return;
        TimelineRulerCanvas.Children.Clear();
        if (_viewModel.VideoDuration.TotalSeconds <= 0 || _viewModel.TimelinePixelsPerSecond <= 0) return;

        var totalSeconds = _viewModel.VideoDuration.TotalSeconds;
        var pps = _viewModel.TimelinePixelsPerSecond;

        double majorStep;
        double minorStep;

        if (pps >= 100)      { majorStep = 1; minorStep = 0.2; }
        else if (pps >= 40)  { majorStep = 2; minorStep = 0.5; }
        else if (pps >= 15)  { majorStep = 5; minorStep = 1; }
        else if (pps >= 6)   { majorStep = 10; minorStep = 2; }
        else if (pps >= 2)   { majorStep = 30; minorStep = 5; }
        else                 { majorStep = 60; minorStep = 10; }

        var sageDimBrush = (Brush)FindResource("SageDim");
        var ruleBrightBrush = (Brush)FindResource("RuleBright");
        var monoFont = (FontFamily)FindResource("MonoFont");

        for (double sec = 0; sec <= totalSeconds + majorStep; sec += minorStep)
        {
            var x = sec * pps;
            bool isMajor = Math.Abs(sec % majorStep) < 0.001 || Math.Abs(sec % majorStep - majorStep) < 0.001;

            var line = new WpfLine
            {
                X1 = x,
                X2 = x,
                Y1 = isMajor ? 13 : 19,
                Y2 = 26,
                Stroke = isMajor ? sageDimBrush : ruleBrightBrush,
                StrokeThickness = 1,
                SnapsToDevicePixels = true
            };
            TimelineRulerCanvas.Children.Add(line);

            if (isMajor && sec <= totalSeconds)
            {
                var ts = TimeSpan.FromSeconds(sec);
                var labelText = ts.TotalHours >= 1 
                    ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{ts.Minutes:D2}:{ts.Seconds:D2}";

                var txt = new TextBlock
                {
                    Text = labelText,
                    FontFamily = monoFont,
                    FontSize = 9.5,
                    Foreground = sageDimBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(txt, x + 3);
                Canvas.SetTop(txt, 1);
                TimelineRulerCanvas.Children.Add(txt);
            }
        }
    }

    private void UpdateAudioClipsOnTimeline()
    {
        if (AudioClipsCanvas == null) return;
        AudioClipsCanvas.Children.Clear();
        if (_viewModel.TimelinePixelsPerSecond <= 0) return;

        var pps = _viewModel.TimelinePixelsPerSecond;
        var brassBrush = (Brush)FindResource("Brass");
        var brassLitBrush = (Brush)FindResource("BrassLit");
        var boneBrush = (Brush)FindResource("Bone");
        var sageBrush = (Brush)FindResource("Sage");
        var slateBrush = (Brush)FindResource("Slate");
        var monoFont = (FontFamily)FindResource("MonoFont");

        foreach (var clip in _viewModel.AudioClips)
        {
            clip.UpdateTimelineBounds(pps);

            var border = new Border
            {
                Tag = clip,
                Width = clip.TimelineWidth,
                Height = 52,
                CornerRadius = new CornerRadius(3),
                Background = clip.IsSelected 
                    ? new SolidColorBrush(Color.FromRgb(0x35, 0x2C, 0x18)) 
                    : new SolidColorBrush(Color.FromRgb(0x20, 0x2A, 0x23)),
                BorderBrush = clip.IsSelected ? brassBrush : new SolidColorBrush(Color.FromRgb(0x38, 0x4A, 0x3E)),
                BorderThickness = new Thickness(clip.IsSelected ? 2 : 1),
                Cursor = Cursors.Hand,
                ToolTip = $"{clip.FileName}\nInicio: {clip.FormattedStartTime}\nDuración: {clip.FormattedDuration}\nVolumen: {clip.VolumePercentString}\n(Arrastra horizontalmente para mover en la línea de tiempo)"
            };

            Canvas.SetLeft(border, clip.TimelineLeft);
            Canvas.SetTop(border, 6);

            var grid = new Grid
            {
                Margin = new Thickness(6, 4, 6, 4)
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topPanel = new DockPanel();

            var playBtn = new Button
            {
                Style = (Style)FindResource("TransportGlyph"),
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Escuchar fragmento"
            };
            var playPath = new WpfPath
            {
                Data = (Geometry)FindResource("IconPlayGeometry"),
                Fill = clip.IsSelected ? brassLitBrush : sageBrush,
                Width = 8,
                Height = 8,
                Stretch = Stretch.Uniform
            };
            playBtn.Content = playPath;
            var capturedClip = clip;
            playBtn.Click += (s, e) =>
            {
                e.Handled = true;
                capturedClip.TogglePreview();
            };
            DockPanel.SetDock(playBtn, Dock.Left);
            topPanel.Children.Add(playBtn);

            var titleText = new TextBlock
            {
                Text = clip.FileName,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = clip.IsSelected ? brassLitBrush : boneBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            topPanel.Children.Add(titleText);
            Grid.SetRow(topPanel, 0);
            grid.Children.Add(topPanel);

            var timeText = new TextBlock
            {
                Text = $"{clip.FormattedStartTime} ({clip.FormattedDuration})",
                FontFamily = monoFont,
                FontSize = 9,
                Foreground = slateBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(timeText, 1);
            grid.Children.Add(timeText);

            border.Child = grid;

            border.MouseLeftButtonDown += AudioClip_MouseLeftButtonDown;
            border.MouseMove += AudioClip_MouseMove;
            border.MouseLeftButtonUp += AudioClip_MouseLeftButtonUp;

            AudioClipsCanvas.Children.Add(border);
        }
    }

    private void AudioClip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not AudioClipItemViewModel clip) return;

        clip.StopPlayback();
        _viewModel.SelectedAudioClip = clip;
        _draggedClip = clip;
        _clipDragStartMousePoint = e.GetPosition(TimelineContentGrid);
        _clipDragStartTime = clip.StartTime;
        _isDraggingClip = true;

        border.CaptureMouse();
        e.Handled = true;
        UpdateTimelineDisplay();
    }

    private void AudioClip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingClip || _draggedClip == null || sender is not Border border) return;
        if (_viewModel.TimelinePixelsPerSecond <= 0) return;

        var currentMousePoint = e.GetPosition(TimelineContentGrid);
        var deltaX = currentMousePoint.X - _clipDragStartMousePoint.X;
        var deltaSeconds = deltaX / _viewModel.TimelinePixelsPerSecond;
        var newSeconds = _clipDragStartTime.TotalSeconds + deltaSeconds;

        var maxLimit = _viewModel.VideoDuration.TotalSeconds > 0 
            ? _viewModel.VideoDuration.TotalSeconds 
            : 3600.0;
        newSeconds = Math.Clamp(newSeconds, 0.0, maxLimit);

        _draggedClip.StartTime = TimeSpan.FromSeconds(newSeconds);
        _draggedClip.UpdateTimelineBounds(_viewModel.TimelinePixelsPerSecond);
        Canvas.SetLeft(border, _draggedClip.TimelineLeft);

        _viewModel.StatusMessage = $"Moviendo '{_draggedClip.FileName}' a {_draggedClip.FormattedStartTime}…";
    }

    private void AudioClip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingClip)
        {
            _isDraggingClip = false;
            if (sender is Border border)
            {
                border.ReleaseMouseCapture();
            }

            if (_draggedClip != null)
            {
                _viewModel.StatusMessage = $"Locución '{_draggedClip.FileName}' fijada en {_draggedClip.FormattedStartTime}.";
                if (_isEditorPlaying)
                {
                    var masterVol = EditorVolumeSlider != null ? EditorVolumeSlider.Value : 1.0;
                    _draggedClip.SyncPlayback(_viewModel.CurrentPlaybackPosition, true, masterVol);
                }
                _draggedClip = null;
            }

            UpdateTimelineDisplay();
            e.Handled = true;
        }
    }

    private void UpdatePlayheadPosition()
    {
        if (_viewModel.TimelinePixelsPerSecond <= 0 || PlayheadMarker == null || PlayheadLine == null) return;

        var x = _viewModel.CurrentPlaybackPosition.TotalSeconds * _viewModel.TimelinePixelsPerSecond;
        Canvas.SetLeft(PlayheadMarker, x);
        Canvas.SetLeft(PlayheadLine, x);
    }

    #endregion
}
