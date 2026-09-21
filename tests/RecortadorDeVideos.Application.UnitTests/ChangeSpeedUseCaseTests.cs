using Moq;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.ChangeSpeed;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using Xunit;

namespace RecortadorDeVideos.Application.UnitTests;

public class ChangeSpeedUseCaseTests
{
    private readonly Mock<IVideoSpeedChanger> _mockSpeedChanger;
    private readonly Mock<IFileSystemService> _mockFileSystemService;
    private readonly ChangeSpeedUseCase _useCase;

    public ChangeSpeedUseCaseTests()
    {
        _mockSpeedChanger = new Mock<IVideoSpeedChanger>();
        _mockFileSystemService = new Mock<IFileSystemService>();
        _useCase = new ChangeSpeedUseCase(_mockSpeedChanger.Object, _mockFileSystemService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

        var request = new ChangeSpeedRequestDto(
            SourceVideoPath: "inexistente.mp4",
            DestinationVideoPath: "salida.mp4",
            SpeedMultiplier: 2.0,
            OriginalDuration: TimeSpan.FromSeconds(30));

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("no existe", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.0)]
    public async Task ExecuteAsync_WhenMultiplierIsInvalid_ShouldReturnFailure(double invalidMultiplier)
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);

        var request = new ChangeSpeedRequestDto(
            SourceVideoPath: "video.mp4",
            DestinationVideoPath: "salida.mp4",
            SpeedMultiplier: invalidMultiplier,
            OriginalDuration: TimeSpan.FromSeconds(30));

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("mayor a cero", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ShouldCallSpeedChangerAndReturnSuccess()
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystemService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(2048 * 1024);

        _mockSpeedChanger
            .Setup(s => s.ChangeSpeedAsync(It.IsAny<SpeedJob>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var request = new ChangeSpeedRequestDto(
            SourceVideoPath: "video.mp4",
            DestinationVideoPath: "salida_2x.mp4",
            SpeedMultiplier: 2.0,
            OriginalDuration: TimeSpan.FromSeconds(60),
            MuteAudio: false);

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("salida_2x.mp4", result.Value.OutputFilePath);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Value.NewDuration);
        _mockSpeedChanger.Verify(s => s.ChangeSpeedAsync(
            It.Is<SpeedJob>(j => j.SpeedMultiplier == 2.0 && j.NewDuration == TimeSpan.FromSeconds(30)),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
