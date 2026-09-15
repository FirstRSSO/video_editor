using System.Diagnostics;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;
using RecortadorDeVideos.Infrastructure.FFmpeg;

namespace RecortadorDeVideos.Infrastructure.IntegrationTests;

public class FFmpegIntegrationTests : IDisposable
{
    private readonly FFmpegBinaryLocator _locator;
    private readonly FFmpegProcessRunner _runner;
    private readonly FFmpegMediaAnalyzer _analyzer;
    private readonly FFmpegVideoTrimmer _trimmer;
    private readonly FFmpegAudioMuxer _muxer;
    private readonly string _tempDirectory;
    private readonly string _testVideoPath;
    private readonly string _testAudioPath;

    public FFmpegIntegrationTests()
    {
        _locator = new FFmpegBinaryLocator();
        _runner = new FFmpegProcessRunner();
        _analyzer = new FFmpegMediaAnalyzer(_locator, _runner);
        _trimmer = new FFmpegVideoTrimmer(_locator, _runner);
        _muxer = new FFmpegAudioMuxer(_locator, _runner);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "RecortadorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _testVideoPath = Path.Combine(_tempDirectory, "source_video.mp4");
        _testAudioPath = Path.Combine(_tempDirectory, "source_audio.mp3");

        GenerateSyntheticMedia();
    }

    private void GenerateSyntheticMedia()
    {
        var ffmpeg = _locator.GetFFmpegPath();

        // 1. Generar video MP4 de 5 segundos con video H.264 y audio AAC
        var videoArgs = $"-y -f lavfi -i testsrc=duration=5:size=640x360:rate=30 -f lavfi -i sine=frequency=1000:duration=5 -c:v libx264 -g 30 -c:a aac \"{_testVideoPath}\"";
        var psiVideo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = videoArgs,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var proc = Process.Start(psiVideo))
        {
            proc?.WaitForExit();
        }

        // 2. Generar audio MP3 de 5 segundos
        var audioArgs = $"-y -f lavfi -i sine=frequency=440:duration=5 -c:a libmp3lame \"{_testAudioPath}\"";
        var psiAudio = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = audioArgs,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var proc = Process.Start(psiAudio))
        {
            proc?.WaitForExit();
        }
    }

    [Fact]
    public void BinaryLocator_ShouldDetectFFmpegAndFFprobe()
    {
        Assert.True(_locator.AreBinariesAvailable(), "FFmpeg o FFprobe no fueron encontrados por el localizador.");
        Assert.True(File.Exists(_locator.GetFFmpegPath()));
        Assert.True(File.Exists(_locator.GetFFprobePath()));
    }

    [Fact]
    public async Task MediaAnalyzer_ShouldExtractCorrectMetadata()
    {
        var result = await _analyzer.AnalyzeAsync(_testVideoPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var metadata = result.Value!;
        Assert.Equal(640, metadata.Width);
        Assert.Equal(360, metadata.Height);
        Assert.Equal("h264", metadata.VideoCodec);
        Assert.Equal("aac", metadata.AudioCodec);
        Assert.True(metadata.Duration.TotalSeconds >= 4.9 && metadata.Duration.TotalSeconds <= 5.2);
    }

    [Fact]
    public async Task MediaAnalyzer_ShouldExtractKeyframes()
    {
        var result = await _analyzer.ExtractKeyframesAsync(_testVideoPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var keyframes = result.Value!;
        Assert.NotEmpty(keyframes);
        Assert.Equal(TimeSpan.Zero, keyframes[0]);
    }

    [Fact]
    public async Task VideoTrimmer_ShouldTrimWithoutLoss()
    {
        var outputPath = Path.Combine(_tempDirectory, "trimmed.mp4");
        var timeRange = TimeRange.Create(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var job = new CutJob(_testVideoPath, timeRange, outputPath, CutStrategy.LosslessStreamCopy, AudioTrackConfig.KeepOriginal());

        var result = await _trimmer.TrimAsync(job);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(outputPath));

        var metaResult = await _analyzer.AnalyzeAsync(outputPath);
        Assert.True(metaResult.IsSuccess);
        Assert.True(metaResult.Value!.Duration.TotalSeconds > 1.5 && metaResult.Value!.Duration.TotalSeconds <= 2.5);
    }

    [Fact]
    public async Task AudioMuxer_ShouldReplaceAudioSuccessfully()
    {
        var outputPath = Path.Combine(_tempDirectory, "replaced_audio.mp4");
        var timeRange = TimeRange.Create(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3));
        var audioConfig = AudioTrackConfig.ReplaceWith(_testAudioPath);

        var result = await _muxer.ProcessAudioAsync(_testVideoPath, timeRange, audioConfig, outputPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(outputPath));

        var metaResult = await _analyzer.AnalyzeAsync(outputPath);
        Assert.True(metaResult.IsSuccess);
        Assert.Equal("aac", metaResult.Value!.AudioCodec);
    }

    [Fact]
    public async Task AudioMuxer_ShouldMixAudioSuccessfully()
    {
        var outputPath = Path.Combine(_tempDirectory, "mixed_audio.mp4");
        var timeRange = TimeRange.Create(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3));
        var audioConfig = AudioTrackConfig.Mix(_testAudioPath, mainVolume: 1.0, backgroundVolume: 0.4);

        var result = await _muxer.ProcessAudioAsync(_testVideoPath, timeRange, audioConfig, outputPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(outputPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignorar errores de limpieza de temporales
        }
    }
}
