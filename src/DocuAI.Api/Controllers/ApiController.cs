using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api.Controllers;

/// <summary>
/// Controlador base: expone <see cref="ISender"/> y fija las convenciones comunes.
/// La traducción Result → HTTP se realiza con las extensiones de <c>ResultHttpMapping</c>.
/// </summary>
/// <param name="sender">Despachador de comandos y consultas (MediatR).</param>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public abstract class ApiController(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;
}
