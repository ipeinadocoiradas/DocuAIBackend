using DocuAI.Api.Common;
using DocuAI.Api.Contracts;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Features.Documents.DeleteDocument;
using DocuAI.Application.Features.Documents.GetDocumentById;
using DocuAI.Application.Features.Documents.ReprocessDocument;
using DocuAI.Application.Features.Documents.UploadDocument;
using DocuAI.Domain.Documents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api.Controllers;

/// <summary>
/// Gestión de la base de conocimiento del RAG. Acceso exclusivo para el rol <c>Admin</c>.
/// </summary>
[Route("api/documents")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class DocumentsController(ISender sender) : ApiController(sender)
{
    /// <summary>Margen para las cabeceras multipart sobre el tamaño máximo del fichero.</summary>
    private const long MultipartOverheadBytes = 1024 * 1024;

    /// <summary>
    /// Sube un documento (PDF, TXT o MD) para su ingesta. El texto se extrae al momento y la
    /// fragmentación + embeddings se procesan en segundo plano con throttling.
    /// </summary>
    /// <param name="file">Fichero a indexar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="202">Documento aceptado y encolado; consultar su estado en <c>Location</c>.</response>
    /// <response code="400">Fichero no válido o sin texto extraíble.</response>
    /// <response code="409">El contenido ya existe en la base de conocimiento.</response>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentRules.MaxFileSizeBytes + MultipartOverheadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentRules.MaxFileSizeBytes + MultipartOverheadBytes)]
    [ProducesResponseType<UploadDocumentResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();

        var command = new UploadDocumentCommand(file.FileName, file.ContentType, file.Length, stream);
        var result = await Sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { id = result.Value.DocumentId }, result.Value)
            : this.HandleFailure(result.Error);
    }

    /// <summary>
    /// Obtiene el detalle y el estado de ingesta de un documento. Pensado para el polling del
    /// front tras la subida (URL devuelta en <c>Location</c>): la respuesta nunca se cachea.
    /// </summary>
    /// <remarks>
    /// El polling puede detenerse cuando <c>status</c> sea <c>Processed</c> o <c>Failed</c>
    /// (en este último caso, <c>failureReason</c> indica el motivo).
    /// </remarks>
    /// <response code="200">Detalle del documento.</response>
    /// <response code="404">El documento no existe.</response>
    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<DocumentDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetDocumentByIdQuery(id), cancellationToken);
        return this.OkOrProblem(result);
    }

    /// <summary>
    /// Lista paginada de documentos con búsqueda por nombre, filtro por estado y rango de
    /// fechas de subida, y ordenación configurable (por defecto, más recientes primero).
    /// </summary>
    /// <param name="request">Filtros, ordenación y paginación (query string).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="200">Página de documentos con el total de coincidencias.</response>
    /// <response code="400">Parámetros no válidos.</response>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<PagedResponse<DocumentSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] ListDocumentsRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(request.ToQuery(), cancellationToken);
        return this.OkOrProblem(result);
    }

    /// <summary>
    /// Elimina un documento junto con todos sus fragmentos y embeddings. Deja de usarse
    /// como contexto del chat de forma inmediata.
    /// </summary>
    /// <response code="204">Documento eliminado.</response>
    /// <response code="404">El documento no existe.</response>
    /// <response code="409">El documento se está procesando; reintente cuando termine.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DeleteDocumentCommand(id), cancellationToken);
        return this.NoContentOrProblem(result);
    }

    /// <summary>Reencola un documento cuya ingesta falló.</summary>
    /// <response code="202">Documento reencolado.</response>
    /// <response code="404">El documento no existe.</response>
    /// <response code="422">El documento no está en estado fallido.</response>
    [HttpPost("{id:guid}/reprocess")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ReprocessDocumentCommand(id), cancellationToken);

        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { id }, null)
            : this.HandleFailure(result.Error);
    }
}
