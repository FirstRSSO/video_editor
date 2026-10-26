using RecortadorDeVideos.UI.ViewModels;
using Xunit;

namespace RecortadorDeVideos.UI.UnitTests;

public class AudioClipItemViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializePropertiesCorrectly()
    {
        var clip = new AudioClipItemViewModel(
            index: 1,
            filePath: "C:\\audios\\voice.mp3",
            startTime: TimeSpan.FromSeconds(5),
            volume: 1.2,
            label: "Voz en off",
            duration: TimeSpan.FromSeconds(10),
            inPoint: TimeSpan.FromSeconds(2),
            originalDuration: TimeSpan.FromSeconds(20),
            trackIndex: 1,
            fadeIn: TimeSpan.FromSeconds(1),
            fadeOut: TimeSpan.FromSeconds(1.5));

        Assert.Equal(1, clip.Index);
        Assert.Equal("C:\\audios\\voice.mp3", clip.FilePath);
        Assert.Equal("voice.mp3", clip.FileName);
        Assert.Equal(TimeSpan.FromSeconds(5), clip.StartTime);
        Assert.Equal(1.2, clip.Volume);
        Assert.Equal(TimeSpan.FromSeconds(2), clip.InPoint);
        Assert.Equal(TimeSpan.FromSeconds(10), clip.Duration);
        Assert.Equal(TimeSpan.FromSeconds(15), clip.EndTime);
        Assert.Equal(1, clip.TrackIndex);
        Assert.Equal("Pista 3", clip.TrackDisplayName);
        Assert.Equal(TimeSpan.FromSeconds(1), clip.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(1.5), clip.FadeOut);
        Assert.True(clip.HasFades);
    }

    [Fact]
    public void FadeProperties_ShouldClampAndNotify()
    {
        var clip = new AudioClipItemViewModel(1, "voice.mp3", TimeSpan.Zero);

        clip.FadeInSeconds = 2.5;
        clip.FadeOutSeconds = 3.0;

        Assert.Equal(2.5, clip.FadeInSeconds);
        Assert.Equal(3.0, clip.FadeOutSeconds);
        Assert.Equal(TimeSpan.FromSeconds(2.5), clip.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(3.0), clip.FadeOut);
        Assert.True(clip.HasFades);
        Assert.Contains("2.5s", clip.FormattedFadeSummary);
        Assert.Contains("3s", clip.FormattedFadeSummary);

        // Clamping negativo
        clip.FadeInSeconds = -1.0;
        Assert.Equal(0, clip.FadeInSeconds);
    }

    [Fact]
    public void UpdateTimelineBounds_ShouldCalculateCorrectPixels()
    {
        var clip = new AudioClipItemViewModel(
            index: 1,
            filePath: "voice.mp3",
            startTime: TimeSpan.FromSeconds(10),
            duration: TimeSpan.FromSeconds(5));

        double pixelsPerSecond = 20.0;
        clip.UpdateTimelineBounds(pixelsPerSecond);

        Assert.Equal(200.0, clip.TimelineLeft);
        Assert.Equal(100.0, clip.TimelineWidth);
    }

    [Fact]
    public void SetOriginalDuration_ShouldAdjustClampedDuration()
    {
        var clip = new AudioClipItemViewModel(
            index: 1,
            filePath: "voice.mp3",
            startTime: TimeSpan.Zero,
            duration: TimeSpan.FromSeconds(30));

        clip.SetOriginalDuration(TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(15), clip.OriginalDuration);
        Assert.True(clip.Duration <= TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void TrackIndex_CannotBeNegative()
    {
        var clip = new AudioClipItemViewModel(1, "voice.mp3", TimeSpan.Zero);

        clip.TrackIndex = -5;

        Assert.Equal(0, clip.TrackIndex);
        Assert.Equal("Pista 2", clip.TrackDisplayName);
    }
}
