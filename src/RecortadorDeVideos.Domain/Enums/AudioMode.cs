namespace RecortadorDeVideos.Domain.Enums;

/// <summary>
/// Define el modo de procesamiento de audio al recortar el video.
/// </summary>
public enum AudioMode
{
    /// <summary>
    /// Conserva el stream de audio original del video sin recodificar.
    /// </summary>
    KeepOriginal,

    /// <summary>
    /// Reemplaza el audio original por un archivo de audio externo.
    /// </summary>
    Replace,

    /// <summary>
    /// Mezcla el audio original del video con una pista de audio externa de fondo.
    /// </summary>
    Mix,

    /// <summary>
    /// Elimina por completo el audio, generando un video mudo.
    /// </summary>
    Mute
}
