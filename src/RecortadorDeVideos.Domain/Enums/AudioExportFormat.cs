namespace RecortadorDeVideos.Domain.Enums;

/// <summary>
/// Formatos de exportación admitidos para la extracción de pistas de audio independientes.
/// </summary>
public enum AudioExportFormat
{
    /// <summary>
    /// Audio comprimido MPEG Audio Layer III (.mp3), compatibilidad universal.
    /// </summary>
    Mp3,

    /// <summary>
    /// Advanced Audio Coding (.aac), alta fidelidad en tasa de bits eficiente.
    /// </summary>
    Aac,

    /// <summary>
    /// Audio sin comprimir PCM Lineal (.wav), máxima fidelidad y calidad de estudio.
    /// </summary>
    Wav,

    /// <summary>
    /// Contenedor de audio MPEG-4 (.m4a) con códec AAC.
    /// </summary>
    M4a
}
