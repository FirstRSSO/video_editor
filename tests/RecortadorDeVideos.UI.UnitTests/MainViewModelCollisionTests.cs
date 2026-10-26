using Moq;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.UseCases.AnalyzeVideo;
using RecortadorDeVideos.Application.UseCases.ChangeSpeed;
using RecortadorDeVideos.Application.UseCases.ExtractAudio;
using RecortadorDeVideos.Application.UseCases.MergeVideos;
using RecortadorDeVideos.Application.UseCases.TrimVideo;
using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.UI.ViewModels;
using Xunit;

namespace RecortadorDeVideos.UI.UnitTests;

public class MainViewModelCollisionTests
{
    private readonly MainViewModel _vm;

    public MainViewModelCollisionTests()
    {
        var mockAnalyze = new Mock<IAnalyzeVideoUseCase>();
        var mockTrim = new Mock<ITrimVideoUseCase>();
        var mockMerge = new Mock<IMergeVideosUseCase>();
        var mockSpeed = new Mock<IChangeSpeedUseCase>();
        var mockExtract = new Mock<IExtractAudioUseCase>();

        _vm = new MainViewModel(
            mockAnalyze.Object,
            mockTrim.Object,
            mockMerge.Object,
            mockSpeed.Object,
            mockExtract.Object);
    }

    [Fact]
    public void CutStrategySelection_ShouldUpdateProperly()
    {
        Assert.Equal(CutStrategy.LosslessStreamCopy, _vm.SelectedCutStrategy);
        Assert.True(_vm.IsLosslessStrategy);
        Assert.False(_vm.IsSmartCutStrategy);

        _vm.IsSmartCutStrategy = true;
        Assert.Equal(CutStrategy.SmartCut, _vm.SelectedCutStrategy);
        Assert.True(_vm.IsSmartCutStrategy);
        Assert.Contains("Smart Cut", _vm.StrategyDescription);

        _vm.IsKeyframeSnapStrategy = true;
        Assert.Equal(CutStrategy.KeyframeSnap, _vm.SelectedCutStrategy);
        Assert.True(_vm.IsKeyframeSnapStrategy);
    }

    [Fact]
    public void GetNextAvailableTrackIndex_WhenNoCollision_ReturnsRequestedTrack()
    {
        var clip1 = new AudioClipItemViewModel(1, "voice1.mp3", TimeSpan.FromSeconds(0), duration: TimeSpan.FromSeconds(5), trackIndex: 0);
        _vm.AudioClips.Add(clip1);

        // Clip 2 en tiempo 10s (sin solapamiento con clip1 de 0s a 5s)
        var track = _vm.GetNextAvailableTrackIndex(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), preferredTrack: 0);

        Assert.Equal(0, track);
    }

    [Fact]
    public void GetNextAvailableTrackIndex_WhenCollisionOccurs_PushesToNextTrack()
    {
        var clip1 = new AudioClipItemViewModel(1, "voice1.mp3", TimeSpan.FromSeconds(2), duration: TimeSpan.FromSeconds(6), trackIndex: 0);
        _vm.AudioClips.Add(clip1);

        // Clip 2 intenta ubicarse en tiempo 4s (se solapa con clip1 de 2s a 8s)
        var track = _vm.GetNextAvailableTrackIndex(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), preferredTrack: 0);

        // Debe asignarle la pista 1 (la siguiente pista hacia abajo estilo Camtasia)
        Assert.Equal(1, track);
    }

    [Fact]
    public void CompactAudioTracks_ShouldReorderTracksWithoutEmptyGaps()
    {
        var clip1 = new AudioClipItemViewModel(1, "voice1.mp3", TimeSpan.FromSeconds(0), trackIndex: 5);
        _vm.AudioClips.Add(clip1);

        _vm.CompactAudioTracks();

        // Al haber solo 1 pista ocupada, se debe compactar a pista 0
        Assert.Equal(0, clip1.TrackIndex);
        Assert.Equal(1, _vm.TotalAudioTracksCount);
    }
}
