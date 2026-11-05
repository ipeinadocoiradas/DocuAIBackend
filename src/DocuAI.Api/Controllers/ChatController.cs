using DocuAI.Api.Common;
using DocuAI.Api.Contracts;
using DocuAI.Application.Features.Chat.AskQuestion;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuAI.Api.Controllers;

/// <summary>Chat con el agente RAG. Acceso exclusivo para el rol <c>User</c>.</summary>
[Route("api/chat")]
[Authorize(Policy = AuthorizationPolicies.ChatUser)]
public sealed class ChatController(ISender sender, ILogger<ChatController> logger) : ApiController(sender)
{
    /// <summary>
    /// Formula una pregunta y recibe la respuesta en streaming (Server-Sent Events).
    /// </summary>
    /// <remarks>
    /// Eventos: <c>token</c> → <c>{"text":"…"}</c>, <c>error</c> → <c>{"code","message"}</c>,
    /// <c>done</c> → <c>{"tokenCount":n}</c>. Las validaciones y precondiciones se resuelven
    /// antes de abrir el stream, por lo que devuelven códigos HTTP convencionales.
    /// </remarks>
    /// <response code="200">Flujo <c>text/event-stream</c> con la respuesta.</response>
    /// <response code="400">Pregunta no válida.</response>
    /// <response code="422">La base de conocimiento aún no tiene documentos indexados.</response>
    /// <response code="429">Límite de consultas por minuto superado.</response>
    [HttpPost("query")]
    [EnableRateLimiting(RateLimitingPolicies.ChatPerUser)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Query([FromBody] AskQuestionRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new AskQuestionQuery(request.Question, request.TopK), cancellationToken);

        return result.IsSuccess
            ? new ServerSentEventsResult(result.Value, logger)
            : this.HandleFailure(result.Error);
    }
}
