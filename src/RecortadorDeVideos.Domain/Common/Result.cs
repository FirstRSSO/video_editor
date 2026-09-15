namespace RecortadorDeVideos.Domain.Common;

/// <summary>
/// Representa el resultado de una operación que puede ser exitosa o fallida.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorMessage { get; }

    protected Result(bool isSuccess, string? errorMessage)
    {
        if (isSuccess && !string.IsNullOrWhiteSpace(errorMessage))
            throw new InvalidOperationException("Una operación exitosa no puede tener mensaje de error.");

        if (!isSuccess && string.IsNullOrWhiteSpace(errorMessage))
            throw new InvalidOperationException("Una operación fallida debe tener un mensaje de error explicativo.");

        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// Representa el resultado de una operación que retorna un valor genérico en caso de éxito.
/// </summary>
/// <typeparam name="T">Tipo del valor resultante.</typeparam>
public class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"No se puede acceder al valor de un resultado fallido: {ErrorMessage}");

    public T? ValueOrDefault => _value;

    protected Result(bool isSuccess, T? value, string? errorMessage)
        : base(isSuccess, errorMessage)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public new static Result<T> Failure(string errorMessage) => new(false, default, errorMessage);

    public static implicit operator Result<T>(T value) => Success(value);
}
