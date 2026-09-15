using RecortadorDeVideos.Domain.Enums;
using RecortadorDeVideos.Domain.Exceptions;

namespace RecortadorDeVideos.Domain.ValueObjects;

/// <summary>
/// Configuración inmutable para el manejo y mezcla de pistas de audio.
/// </summary>
public sealed record AudioTrackConfig
{
    public AudioMode Mode { get; }
    public string? ExternalAudioPath { get; }
    public double MainVolume { get; }
    public double BackgroundVolume { get; }
    public TimeSpan Offset { get; }

    private AudioTrackConfig(AudioMode mode, string? externalAudioPath, double mainVolume, double backgroundVolume, TimeSpan offset)
    {
        Mode = mode;
        ExternalAudioPath = externalAudioPath;
        MainVolume = mainVolume;
        BackgroundVolume = backgroundVolume;
        Offset = offset;
    }

    public static AudioTrackConfig KeepOriginal()
    {
        return new AudioTrackConfig(AudioMode.KeepOriginal, null, 1.0, 0.0, TimeSpan.Zero);
    }

    public static AudioTrackConfig Mute()
    {
        return new AudioTrackConfig(AudioMode.Mute, null, 0.0, 0.0, TimeSpan.Zero);
    }

    public static AudioTrackConfig ReplaceWith(string externalAudioPath)
    {
        if (string.IsNullOrWhiteSpace(externalAudioPath))
            throw new ArgumentException("La ruta del archivo de audio externo no puede estar vacía.", nameof(externalAudioPath));

        return new AudioTrackConfig(AudioMode.Replace, externalAudioPath, 0.0, 1.0, TimeSpan.Zero);
    }

    public static AudioTrackConfig Mix(string externalAudioPath, double mainVolume = 1.0, double backgroundVolume = 0.3, TimeSpan? offset = null)
    {
        if (string.IsNullOrWhiteSpace(externalAudioPath))
            throw new ArgumentException("La ruta del archivo de audio a mezclar no puede estar vacía.", nameof(externalAudioPath));

        if (mainVolume < 0.0 || mainVolume > 2.0)
            throw new ArgumentOutOfRangeException(nameof(mainVolume), "El volumen del audio principal debe estar entre 0.0 y 2.0.");

        if (backgroundVolume < 0.0 || backgroundVolume > 2.0)
            throw new ArgumentOutOfRangeException(nameof(backgroundVolume), "El volumen del audio de fondo debe estar entre 0.0 y 2.0.");

        return new AudioTrackConfig(AudioMode.Mix, externalAudioPath, mainVolume, backgroundVolume, offset ?? TimeSpan.Zero);
    }
}
