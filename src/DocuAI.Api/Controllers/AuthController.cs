using DocuAI.Api.Common;
using DocuAI.Api.Contracts;
using DocuAI.Application.Features.Auth.IssueToken;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuAI.Api.Controllers;

/// <summary>
/// Emisor de tokens local para desarrollo y pruebas. Se desactiva con
/// <c>Authentication:DevelopmentUsers:Enabled = false</c> (devuelve 403).
/// </summary>
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(ISender sender) : ApiController(sender)
{
    /// <summary>Obtiene un JWT a partir de credenciales locales.</summary>
    /// <response code="200">Token emitido.</response>
    /// <response code="401">Credenciales incorrectas.</response>
    /// <response code="403">Emisor local deshabilitado.</response>
    [HttpPost("token")]
    [EnableRateLimiting(RateLimitingPolicies.TokenIssuance)]
    [Consumes("application/json")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IssueToken([FromBody] IssueTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new IssueTokenCommand(request.UserName, request.Password), cancellationToken);
        return this.OkOrProblem(result);
    }
}
