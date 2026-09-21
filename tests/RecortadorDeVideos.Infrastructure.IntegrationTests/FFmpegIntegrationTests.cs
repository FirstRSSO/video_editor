using System.Diagnostics;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Application.UseCases.MergeVideos;
using RecortadorDeVideos.Domain.Entities;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.ValueObjects;
using RecortadorDeVideos.Infrastructure.FFmpeg;
using RecortadorDeVideos.Infrastructure.FileSystem;

namespace RecortadorDeVideos.Infrastructure.IntegrationTests;

public class FFmpegIntegrationTests : IDisposable
{
    private readonly FFmpegBinaryLocator _locator;
    private readonly FFmpegProcessRunner _runner;
    private readonly FFmpegMediaAnalyzer _analyzer;
    private readonly FFmpegVideoTrimmer _trimmer;
    private readonly FFmpegAudioMuxer _muxer;
    private readonly FFmpegVideoConcatenator _concatenator;
    private readonly FFmpegVideoSpeedChanger _speedChanger;
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
        _concatenator = new FFmpegVideoConcatenator(_locator, _runner);
        _speedChanger = new FFmpegVideoSpeedChanger(_locator, _runner, _analyzer);

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

    [Fact]
    public async Task AudioMuxer_ShouldOverlayPositionedClipsSuccessfully()
    {
        var outputPath = Path.Combine(_tempDirectory, "overlay_clips.mp4");
        var timeRange = TimeRange.Create(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4));
        var clips = new List<AudioOverlayClip>
        {
            new(_testAudioPath, TimeSpan.FromSeconds(1), 0.9, "Locución 1"),
            new(_testAudioPath, TimeSpan.FromSeconds(2.5), 1.0, "Locución 2")
        };
        var audioConfig = AudioTrackConfig.WithClips(clips, keepOriginalAudio: true, mainVolume: 0.3);

        var result = await _muxer.ProcessAudioAsync(_testVideoPath, timeRange, audioConfig, outputPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(outputPath));

        var meta = await _analyzer.AnalyzeAsync(outputPath);
        Assert.True(meta.IsSuccess);
        Assert.Equal("aac", meta.Value!.AudioCodec);
        Assert.True(meta.Value!.Duration.TotalSeconds >= 3.5 && meta.Value!.Duration.TotalSeconds <= 4.5);
    }

    [Fact]
    public async Task VideoConcatenator_ShouldConcatenateVideosLosslessly()
    {
        // 1. Recortar dos partes del video de prueba (0-2s y 2-4s)
        var part1 = Path.Combine(_tempDirectory, "concat_part1.mp4");
        var part2 = Path.Combine(_tempDirectory, "concat_part2.mp4");
        var mergedOutput = Path.Combine(_tempDirectory, "concat_merged.mp4");

        var job1 = new CutJob(_testVideoPath, TimeRange.Create(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)), part1);
        var job2 = new CutJob(_testVideoPath, TimeRange.Create(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)), part2);

        var cutRes1 = await _trimmer.TrimAsync(job1);
        var cutRes2 = await _trimmer.TrimAsync(job2);
        Assert.True(cutRes1.IsSuccess);
        Assert.True(cutRes2.IsSuccess);

        // 2. Concatenar ambas partes
        var mergeJob = new MergeJob(new[] { part1, part2 }, mergedOutput);
        var mergeResult = await _concatenator.ConcatenateAsync(mergeJob);

        Assert.True(mergeResult.IsSuccess, mergeResult.ErrorMessage);
        Assert.True(File.Exists(mergedOutput));

        var meta = await _analyzer.AnalyzeAsync(mergedOutput);
        Assert.True(meta.IsSuccess);
        Assert.True(meta.Value!.Duration.TotalSeconds >= 3.0 && meta.Value!.Duration.TotalSeconds <= 5.0, $"Actual duration was: {meta.Value!.Duration.TotalSeconds}");
    }

    [Fact]
    public async Task MergeVideosUseCase_ShouldCutAndMergeMultipleSegments()
    {
        var fileSystem = new LocalFileSystemService();
        var useCase = new MergeVideosUseCase(_trimmer, _muxer, _concatenator, fileSystem);

        var mergedOutput = Path.Combine(_tempDirectory, "usecase_multimerged.mp4");
        var segments = new List<TimeSpanPairDto>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.5)),
            new(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4.5))
        };

        var request = new MergeSegmentsRequestDto(
            SourceVideoPath: _testVideoPath,
            Segments: segments,
            DestinationVideoPath: mergedOutput,
            AudioMode: AudioMode.KeepOriginal);

        var result = await useCase.ExecuteMergeSegmentsAsync(request);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(mergedOutput));

        var meta = await _analyzer.AnalyzeAsync(mergedOutput);
        Assert.True(meta.IsSuccess);
        // Segmento 1: 1.5s + Segmento 2: 2.0s = ~3.5s
        Assert.True(meta.Value!.Duration.TotalSeconds >= 2.0 && meta.Value!.Duration.TotalSeconds <= 6.0, $"Actual duration was: {meta.Value!.Duration.TotalSeconds}");
    }

    [Fact]
    public async Task SpeedChanger_ShouldSpeedUpVideoAndAudio_2x()
    {
        var speedOutput = Path.Combine(_tempDirectory, "speed_2x.mp4");
        var job = new SpeedJob(
            sourceVideoPath: _testVideoPath,
            destinationVideoPath: speedOutput,
            speedMultiplier: 2.0,
            originalDuration: TimeSpan.FromSeconds(5),
            muteAudio: false);

        var result = await _speedChanger.ChangeSpeedAsync(job);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(speedOutput));

        var meta = await _analyzer.AnalyzeAsync(speedOutput);
        Assert.True(meta.IsSuccess);
        // Video de 5s a 2x debe durar ~2.5s
        Assert.True(meta.Value!.Duration.TotalSeconds >= 2.2 && meta.Value!.Duration.TotalSeconds <= 2.8,
            $"Expected ~2.5s, actual: {meta.Value!.Duration.TotalSeconds}");
        Assert.Equal("aac", meta.Value!.AudioCodec);
    }

    [Fact]
    public async Task SpeedChanger_ShouldMuteAudioWhenRequested()
    {
        var speedOutput = Path.Combine(_tempDirectory, "speed_muted.mp4");
        var job = new SpeedJob(
            sourceVideoPath: _testVideoPath,
            destinationVideoPath: speedOutput,
            speedMultiplier: 1.5,
            originalDuration: TimeSpan.FromSeconds(5),
            muteAudio: true);

        var result = await _speedChanger.ChangeSpeedAsync(job);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(speedOutput));

        var meta = await _analyzer.AnalyzeAsync(speedOutput);
        Assert.True(meta.IsSuccess);
        Assert.True(string.IsNullOrWhiteSpace(meta.Value!.AudioCodec), "Expected no audio track.");
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
