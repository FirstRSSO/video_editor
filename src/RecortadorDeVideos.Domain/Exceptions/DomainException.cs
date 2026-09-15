namespace RecortadorDeVideos.Domain.Exceptions;

/// <summary>
/// Excepción base para todas las infracciones de reglas de negocio en el dominio.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }

    protected DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Se lanza cuando un rango de tiempo es inválido (ej. tiempo de inicio mayor o igual que el final).
/// </summary>
public class InvalidTimeRangeException : DomainException
{
    public TimeSpan StartTime { get; }
    public TimeSpan EndTime { get; }

    public InvalidTimeRangeException(TimeSpan startTime, TimeSpan endTime, string message)
        : base(message)
    {
        StartTime = startTime;
        EndTime = endTime;
    }
}
