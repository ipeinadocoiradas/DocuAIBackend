namespace DocuAI.Domain.Common;

/// <summary>
/// Operaciones funcionales sobre <see cref="Result"/> implementadas con
/// los miembros de extensión de C# 14.
/// </summary>
public static class ResultExtensions
{
    extension(Result result)
    {
        /// <summary>Proyecta el resultado a un único valor según su estado.</summary>
        public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure) =>
            result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }

    extension<TIn>(Result<TIn> result)
    {
        /// <summary>Proyecta el resultado a un único valor según su estado.</summary>
        public TOut Match<TOut>(Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure) =>
            result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);

        /// <summary>Transforma el valor si el resultado es satisfactorio.</summary>
        public Result<TOut> Map<TOut>(Func<TIn, TOut> mapper) =>
            result.IsSuccess ? Result.Success(mapper(result.Value)) : Result.Failure<TOut>(result.Error);

        /// <summary>Encadena una operación que también puede fallar.</summary>
        public Result<TOut> Bind<TOut>(Func<TIn, Result<TOut>> binder) =>
            result.IsSuccess ? binder(result.Value) : Result.Failure<TOut>(result.Error);

        /// <summary>Encadena una operación asíncrona que también puede fallar.</summary>
        public async Task<Result<TOut>> BindAsync<TOut>(Func<TIn, Task<Result<TOut>>> binder) =>
            result.IsSuccess ? await binder(result.Value).ConfigureAwait(false) : Result.Failure<TOut>(result.Error);

        /// <summary>Verifica una condición adicional sobre el valor.</summary>
        public Result<TIn> Ensure(Func<TIn, bool> predicate, Error error) =>
            result.IsFailure ? result : predicate(result.Value) ? result : Result.Failure<TIn>(error);
    }
}
