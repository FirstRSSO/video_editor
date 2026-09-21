using RecortadorDeVideos.Domain.Entities;
using Xunit;

namespace RecortadorDeVideos.Domain.UnitTests;

public class SpeedJobTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldCalculateCorrectNewDuration()
    {
        // Arrange
        var originalDuration = TimeSpan.FromSeconds(60);
        double multiplier = 2.0;

        // Act
        var job = new SpeedJob(
            sourceVideoPath: @"C:\videos\input.mp4",
            destinationVideoPath: @"C:\videos\output.mp4",
            speedMultiplier: multiplier,
            originalDuration: originalDuration,
            muteAudio: false);

        // Assert
        Assert.Equal(@"C:\videos\input.mp4", job.SourceVideoPath);
        Assert.Equal(@"C:\videos\output.mp4", job.DestinationVideoPath);
        Assert.Equal(2.0, job.SpeedMultiplier);
        Assert.False(job.MuteAudio);
        Assert.Equal(TimeSpan.FromSeconds(60), job.OriginalDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), job.NewDuration);
    }

    [Theory]
    [InlineData(0.5, 120)]
    [InlineData(1.5, 40)]
    [InlineData(4.0, 15)]
    public void Create_WithDifferentMultipliers_ShouldScaleDurationCorrectly(double multiplier, double expectedSeconds)
    {
        // Arrange
        var original = TimeSpan.FromSeconds(60);

        // Act
        var job = new SpeedJob("C:/input.mp4", "C:/output.mp4", multiplier, original);

        // Assert
        Assert.Equal(expectedSeconds, job.NewDuration.TotalSeconds, precision: 3);
    }

    [Fact]
    public void Create_WithSameSourceAndDestination_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SpeedJob(
            @"C:\input.mp4",
            @"C:\input.mp4",
            2.0,
            TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void Create_WithInvalidMultiplier_ShouldThrowArgumentOutOfRangeException(double invalidMultiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeedJob(
            @"C:\input.mp4",
            @"C:\output.mp4",
            invalidMultiplier,
            TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Create_WithZeroOrNegativeDuration_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeedJob(
            @"C:\input.mp4",
            @"C:\output.mp4",
            2.0,
            TimeSpan.Zero));
    }
}
