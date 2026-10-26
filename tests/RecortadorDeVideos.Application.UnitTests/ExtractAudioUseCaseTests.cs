using Moq;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.ExtractAudio;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;
using Xunit;

namespace RecortadorDeVideos.Application.UnitTests;

public class ExtractAudioUseCaseTests
{
    private readonly Mock<IAudioExtractor> _mockAudioExtractor;
    private readonly Mock<IFileSystemService> _mockFileSystemService;
    private readonly ExtractAudioUseCase _useCase;

    public ExtractAudioUseCaseTests()
    {
        _mockAudioExtractor = new Mock<IAudioExtractor>();
        _mockFileSystemService = new Mock<IFileSystemService>();

        _useCase = new ExtractAudioUseCase(
            _mockAudioExtractor.Object,
            _mockFileSystemService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileDoesNotExist_ShouldReturnFailure()
    {
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

        var request = new ExtractAudioRequestDto(
            SourceVideoPath: "inexistente.mp4",
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.FromSeconds(10),
            DestinationAudioPath: "salida.mp3");

        var result = await _useCase.ExecuteAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains("no existe", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTimeRangeInvalid_ShouldReturnFailure()
    {
        _mockFileSystemService.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);

        var request = new ExtractAudioRequestDto(
            SourceVideoPath: "video.mp4",
            StartTime: TimeSpan.FromSeconds(10),
            EndTime: TimeSpan.FromSeconds(5),
            DestinationAudioPath: "salida.mp3");

        var result = await _useCase.ExecuteAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains("estrictamente mayor", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExtractionSucceeds_ShouldReturnSuccess()
    {
        _mockFileSystemService.Setup(f => f.FileExists("video.mp4")).Returns(true);
        _mockFileSystemService.Setup(f => f.FileExists("salida.mp3")).Returns(true);
        _mockFileSystemService.Setup(f => f.GetFileSize("salida.mp3")).Returns(500_000);

        _mockAudioExtractor
            .Setup(e => e.ExtractAudioAsync(
                It.IsAny<string>(),
                It.IsAny<TimeRange>(),
                It.IsAny<string>(),
                It.IsAny<AudioExportFormat>(),
                It.IsAny<int>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var request = new ExtractAudioRequestDto(
            SourceVideoPath: "video.mp4",
            StartTime: TimeSpan.FromSeconds(2),
            EndTime: TimeSpan.FromSeconds(12),
            DestinationAudioPath: "salida.mp3",
            Format: AudioExportFormat.Mp3);

        var result = await _useCase.ExecuteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("salida.mp3", result.Value.OutputFilePath);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Value.Duration);
        Assert.Equal(500_000, result.Value.OutputSizeBytes);

        _mockAudioExtractor.Verify(e => e.ExtractAudioAsync(
            "video.mp4",
            It.Is<TimeRange>(tr => tr.StartTime == TimeSpan.FromSeconds(2) && tr.EndTime == TimeSpan.FromSeconds(12)),
            "salida.mp3",
            AudioExportFormat.Mp3,
            It.IsAny<int>(),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
