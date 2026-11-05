using DocuAI.Domain.Common;
using FluentValidation;
using MediatR;

namespace DocuAI.Application.Behaviors;

/// <summary>
/// Ejecuta todos los validadores FluentValidation de la petición antes del manejador.
/// Si hay errores devuelve un <see cref="Result"/> fallido de tipo validación
/// (nunca lanza excepciones), creado mediante <see cref="IResultFactory{TSelf}"/>.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    private readonly IValidator<TRequest>[] _validators = [.. validators];

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        if (errors.Count == 0)
        {
            return await next();
        }

        var error = Error.Validation(
            "Validation.Failed",
            "Uno o más campos no superan la validación.",
            errors);

        return TResponse.CreateFailure(error);
    }
}
