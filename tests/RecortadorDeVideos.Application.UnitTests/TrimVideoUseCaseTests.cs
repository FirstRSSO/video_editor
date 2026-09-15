using Moq;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.TrimVideo;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using Xunit;

namespace RecortadorDeVideos.Application.UnitTests;

public class TrimVideoUseCaseTests
{
    private readonly Mock<IVideoTrimmer> _mockVideoTrimmer;
    private readonly Mock<IAudioMuxer> _mockAudioMuxer;
    private readonly Mock<IFileSystemService> _mockFileSystemService;
    private readonly TrimVideoUseCase _useCase;

    public TrimVideoUseCaseTests()
    {
        _mockVideoTrimmer = new Mock<IVideoTrimmer>();
        _mockAudioMuxer = new Mock<IAudioMuxer>();
        _mockFileSystemService = new Mock<IFileSystemService>();

        _useCase = new TrimVideoUseCase(
            _mockVideoTrimmer.Object,
            _mockAudioMuxer.Object,
            _mockFileSystemService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

        var request = new TrimVideoRequestDto(
            SourceVideoPath: "inexistente.mp4",
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.FromSeconds(10),
            DestinationVideoPath: "salida.mp4");

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("no existe", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEndTimeBeforeStartTime_ShouldReturnFailure()
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);

        var request = new TrimVideoRequestDto(
            SourceVideoPath: "video.mp4",
            StartTime: TimeSpan.FromSeconds(20),
            EndTime: TimeSpan.FromSeconds(5),
            DestinationVideoPath: "salida.mp4");

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("estrictamente mayor", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ShouldCallTrimmerAndReturnSuccess()
    {
        // Arrange
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystemService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(1024 * 1024);

        _mockVideoTrimmer
            .Setup(t => t.TrimAsync(It.IsAny<CutJob>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var request = new TrimVideoRequestDto(
            SourceVideoPath: "video.mp4",
            StartTime: TimeSpan.FromSeconds(5),
            EndTime: TimeSpan.FromSeconds(15),
            DestinationVideoPath: "salida.mp4",
            AudioMode: AudioMode.KeepOriginal);

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("salida.mp4", result.Value.OutputFilePath);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Value.Duration);
        _mockVideoTrimmer.Verify(t => t.TrimAsync(It.IsAny<CutJob>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
