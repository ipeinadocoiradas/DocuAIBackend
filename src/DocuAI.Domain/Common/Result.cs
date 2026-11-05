using System.Diagnostics.CodeAnalysis;

namespace DocuAI.Domain.Common;

/// <summary>
/// Contrato estático (static abstract) que permite crear un resultado fallido de forma
/// genérica, sin reflexión. Lo utilizan los pipeline behaviors de la capa Application.
/// </summary>
/// <typeparam name="TSelf">Tipo concreto de resultado.</typeparam>
public interface IResultFactory<TSelf> where TSelf : IResultFactory<TSelf>
{
    /// <summary>Crea una instancia fallida con el error indicado.</summary>
    static abstract TSelf CreateFailure(Error error);
}

/// <summary>
/// Resultado de una operación sin valor de retorno. Sustituye al uso de excepciones
/// para el control del flujo de negocio.
/// </summary>
public class Result : IResultFactory<Result>
{
    protected Result(bool isSuccess, Error error)
    {
        // Invariante de construcción: un éxito no lleva error y un fallo siempre lo lleva.
        // Su incumplimiento es un error de programación, no de negocio.
        if (isSuccess == (error != Error.None))
        {
            throw new ArgumentException("Combinación inválida de estado y error en Result.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Indica si la operación se completó correctamente.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indica si la operación falló.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Error asociado; <see cref="Error.None"/> si la operación tuvo éxito.</summary>
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    static Result IResultFactory<Result>.CreateFailure(Error error) => Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Resultado de una operación que devuelve un valor de tipo <typeparamref name="TValue"/>.
/// </summary>
/// <remarks>
/// Las conversiones implícitas desde <typeparamref name="TValue"/> no aplican cuando el tipo es
/// una interfaz (restricción del lenguaje); en ese caso use <see cref="Result.Success{TValue}(TValue)"/>.
/// </remarks>
public sealed class Result<TValue> : Result, IResultFactory<Result<TValue>>
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>
    /// Valor del resultado. Acceder a él en un resultado fallido es un error de programación.
    /// </summary>
    [NotNull]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"No se puede acceder al valor de un resultado fallido ({Error.Code}).");

    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    static Result<TValue> IResultFactory<Result<TValue>>.CreateFailure(Error error) => Failure(error);

    public static implicit operator Result<TValue>(TValue value) => new(value, true, Error.None);

    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
