using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;
using Xunit;

namespace RecortadorDeVideos.Domain.UnitTests;

public class AudioOverlayClipTests
{
    [Fact]
    public void Create_ValidParameters_ShouldSucceed()
    {
        var clip = new AudioOverlayClip("voice.mp3", TimeSpan.FromSeconds(15), 0.8, "Intro");

        Assert.Equal("voice.mp3", clip.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(15), clip.StartTime);
        Assert.Equal(0.8, clip.Volume);
        Assert.Equal("Intro", clip.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_InvalidFilePath_ShouldThrowArgumentException(string? invalidPath)
    {
        Assert.Throws<ArgumentException>(() => new AudioOverlayClip(invalidPath!, TimeSpan.Zero));
    }

    [Fact]
    public void Create_NegativeStartTime_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioOverlayClip("voice.mp3", TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void Create_InvalidVolume_ShouldThrowArgumentOutOfRangeException(double invalidVol)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioOverlayClip("voice.mp3", TimeSpan.Zero, invalidVol));
    }

    [Fact]
    public void AudioTrackConfig_WithClips_ShouldConfigureProperly()
    {
        var clips = new List<AudioOverlayClip>
        {
            new("voice1.mp3", TimeSpan.FromSeconds(5), 1.0),
            new("voice2.mp3", TimeSpan.FromSeconds(20), 0.9)
        };

        var config = AudioTrackConfig.WithClips(clips, keepOriginalAudio: true, mainVolume: 0.5);

        Assert.Equal(AudioMode.OverlayClips, config.Mode);
        Assert.Equal(0.5, config.MainVolume);
        Assert.Equal(2, config.Clips.Count);
        Assert.Equal("voice1.mp3", config.Clips[0].FilePath);
        Assert.Equal(TimeSpan.FromSeconds(5), config.Clips[0].StartTime);
    }

    [Fact]
    public void Create_WithInPointAndDuration_ShouldSucceed()
    {
        var clip = new AudioOverlayClip(
            "voice.mp3", 
            TimeSpan.FromSeconds(10), 
            1.0, 
            "Trimmed Voice", 
            inPoint: TimeSpan.FromSeconds(3), 
            duration: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(3), clip.InPoint);
        Assert.Equal(TimeSpan.FromSeconds(5), clip.Duration);
    }

    [Fact]
    public void Create_NegativeInPoint_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new AudioOverlayClip("voice.mp3", TimeSpan.Zero, 1.0, null, inPoint: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Create_ZeroOrNegativeDuration_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new AudioOverlayClip("voice.mp3", TimeSpan.Zero, 1.0, null, duration: TimeSpan.Zero));
    }

    [Fact]
    public void Create_WithFades_ShouldSucceed()
    {
        var clip = new AudioOverlayClip(
            "voice.mp3",
            TimeSpan.FromSeconds(2),
            1.0,
            "Intro with fades",
            inPoint: TimeSpan.Zero,
            duration: TimeSpan.FromSeconds(10),
            fadeIn: TimeSpan.FromSeconds(1.5),
            fadeOut: TimeSpan.FromSeconds(2.0));

        Assert.Equal(TimeSpan.FromSeconds(1.5), clip.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(2.0), clip.FadeOut);
    }

    [Fact]
    public void Create_NegativeFadeIn_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AudioOverlayClip("voice.mp3", TimeSpan.Zero, 1.0, null, fadeIn: TimeSpan.FromSeconds(-0.5)));
    }

    [Fact]
    public void Create_NegativeFadeOut_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AudioOverlayClip("voice.mp3", TimeSpan.Zero, 1.0, null, fadeOut: TimeSpan.FromSeconds(-0.5)));
    }
}
