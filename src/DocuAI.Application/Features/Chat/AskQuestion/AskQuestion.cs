using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Options;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace DocuAI.Application.Features.Chat.AskQuestion;

/// <summary>
/// Pregunta al agente RAG. Las precondiciones se evalúan de forma inmediata y devuelven
/// un <see cref="Result"/>; si son correctas se obtiene una secuencia perezosa de tokens
/// que la capa de presentación transmite por Server-Sent Events.
/// </summary>
/// <param name="Question">Pregunta en lenguaje natural.</param>
/// <param name="TopK">Número de fragmentos de contexto a recuperar (opcional).</param>
public sealed record AskQuestionQuery(string Question, int? TopK) : IQuery<IAsyncEnumerable<string>>;

internal sealed class AskQuestionQueryValidator : AbstractValidator<AskQuestionQuery>
{
    public AskQuestionQueryValidator(IOptions<ChatOptions> chatOptions)
    {
        var options = chatOptions.Value;

        RuleFor(query => query.Question)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("La pregunta es obligatoria.")
            .Must(question => !string.IsNullOrWhiteSpace(question)).WithMessage("La pregunta no puede estar en blanco.")
            .MaximumLength(options.MaxQuestionLength)
            .WithMessage($"La pregunta no puede superar {options.MaxQuestionLength} caracteres.");

        RuleFor(query => query.TopK)
            .InclusiveBetween(1, options.MaxTopK)
            .When(query => query.TopK.HasValue)
            .WithMessage($"TopK debe estar entre 1 y {options.MaxTopK}.");
    }
}

internal sealed class AskQuestionQueryHandler(
    IDocumentRepository documentRepository,
    IChatAgent chatAgent,
    IOptions<ChatOptions> chatOptions)
    : IQueryHandler<AskQuestionQuery, IAsyncEnumerable<string>>
{
    public async Task<Result<IAsyncEnumerable<string>>> Handle(
        AskQuestionQuery query,
        CancellationToken cancellationToken)
    {
        if (!await documentRepository.AnyProcessedAsync(cancellationToken))
        {
            return ChatErrors.KnowledgeBaseEmpty;
        }

        var topK = query.TopK ?? chatOptions.Value.DefaultTopK;
        var stream = chatAgent.StreamAnswerAsync(query.Question.Trim(), topK, cancellationToken);

        // IAsyncEnumerable es una interfaz: la conversión implícita no aplica.
        return Result.Success(stream);
    }
}
