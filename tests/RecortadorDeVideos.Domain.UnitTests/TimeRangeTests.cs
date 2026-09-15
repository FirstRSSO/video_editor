using RecortadorDeVideos.Domain.Exceptions;
using RecortadorDeVideos.Domain.ValueObjects;
using Xunit;

namespace RecortadorDeVideos.Domain.UnitTests;

public class TimeRangeTests
{
    [Fact]
    public void Create_WithValidTimes_ShouldCreateInstanceWithCorrectDuration()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(25);

        // Act
        var range = TimeRange.Create(start, end);

        // Assert
        Assert.Equal(start, range.StartTime);
        Assert.Equal(end, range.EndTime);
        Assert.Equal(TimeSpan.FromSeconds(15), range.Duration);
    }

    [Fact]
    public void Create_WithNegativeStartTime_ShouldThrowInvalidTimeRangeException()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(-5);
        var end = TimeSpan.FromSeconds(10);

        // Act & Assert
        Assert.Throws<InvalidTimeRangeException>(() => TimeRange.Create(start, end));
    }

    [Fact]
    public void Create_WithEndTimeLessOrEqualToStartTime_ShouldThrowInvalidTimeRangeException()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(20);
        var end = TimeSpan.FromSeconds(10);

        // Act & Assert
        Assert.Throws<InvalidTimeRangeException>(() => TimeRange.Create(start, end));
    }

    [Fact]
    public void FormatFFmpegTime_ShouldProduceCorrectStringFormat()
    {
        // Arrange
        var time = new TimeSpan(0, 1, 2, 3, 456); // 1 hora, 2 min, 3 seg, 456 ms

        // Act
        var formatted = TimeRange.FormatFFmpegTime(time);

        // Assert
        Assert.Equal("01:02:03.456", formatted);
    }
}
