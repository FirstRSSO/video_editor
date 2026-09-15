namespace RecortadorDeVideos.Domain.Enums;

/// <summary>
/// Estrategia de recorte a utilizar para el procesamiento del video.
/// </summary>
public enum CutStrategy
{
    /// <summary>
    /// Recorte 100% sin pérdida directa con stream copy (-c copy).
    /// Ultra veloz, sin recodificación.
    /// </summary>
    LosslessStreamCopy,

    /// <summary>
    /// Ajusta automáticamente los puntos de corte al Keyframe (I-Frame) más cercano.
    /// Garantiza cero frames negros o congelados al inicio.
    /// </summary>
    KeyframeSnap,

    /// <summary>
    /// Smart Cut: Copia directa en el 99% del video, y recodificación quirúrgica
    /// solo de los fotogramas límite entre el punto exacto y el keyframe.
    /// </summary>
    SmartCut
}
