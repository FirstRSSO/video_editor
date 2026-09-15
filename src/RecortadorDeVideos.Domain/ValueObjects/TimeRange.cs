using RecortadorDeVideos.Domain.Exceptions;

namespace RecortadorDeVideos.Domain.ValueObjects;

/// <summary>
/// Objeto de Valor inmutable que representa un intervalo de tiempo válido para el recorte.
/// Garantiza la regla de negocio: Inicio >= 0 y Fin > Inicio.
/// </summary>
public sealed record TimeRange
{
    public TimeSpan StartTime { get; }
    public TimeSpan EndTime { get; }
    public TimeSpan Duration => EndTime - StartTime;

    private TimeRange(TimeSpan startTime, TimeSpan endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>
    /// Crea una nueva instancia de TimeRange validando sus invariantes.
    /// </summary>
    public static TimeRange Create(TimeSpan startTime, TimeSpan endTime)
    {
        if (startTime < TimeSpan.Zero)
            throw new InvalidTimeRangeException(startTime, endTime, "El tiempo de inicio no puede ser negativo.");

        if (endTime <= startTime)
            throw new InvalidTimeRangeException(startTime, endTime, "El tiempo de fin debe ser estrictamente mayor que el tiempo de inicio.");

        return new TimeRange(startTime, endTime);
    }

    /// <summary>
    /// Intenta crear un TimeRange sin lanzar excepciones.
    /// </summary>
    public static bool TryCreate(TimeSpan startTime, TimeSpan endTime, out TimeRange? timeRange, out string? errorMessage)
    {
        if (startTime < TimeSpan.Zero)
        {
            timeRange = null;
            errorMessage = "El tiempo de inicio no puede ser negativo.";
            return false;
        }

        if (endTime <= startTime)
        {
            timeRange = null;
            errorMessage = "El tiempo de fin debe ser estrictamente mayor que el tiempo de inicio.";
            return false;
        }

        timeRange = new TimeRange(startTime, endTime);
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Da formato al tiempo en la estructura estándar esperada por FFmpeg: HH:mm:ss.fff
    /// </summary>
    public string StartToFFmpeg() => FormatFFmpegTime(StartTime);

    /// <summary>
    /// Da formato al tiempo final en la estructura estándar esperada por FFmpeg: HH:mm:ss.fff
    /// </summary>
    public string EndToFFmpeg() => FormatFFmpegTime(EndTime);

    /// <summary>
    /// Da formato a la duración en la estructura estándar esperada por FFmpeg: HH:mm:ss.fff
    /// </summary>
    public string DurationToFFmpeg() => FormatFFmpegTime(Duration);

    public static string FormatFFmpegTime(TimeSpan time)
    {
        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }

    public override string ToString() => $"{StartToFFmpeg()} -> {EndToFFmpeg()} (Duración: {DurationToFFmpeg()})";
}
