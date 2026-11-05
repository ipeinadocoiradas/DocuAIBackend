using System.Security.Claims;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Infrastructure.Identity;

namespace DocuAI.Api.Common;

/// <summary>Políticas de autorización basadas en roles estrictos.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Exclusiva de administradores: ingesta y gestión de la base de conocimiento.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Usuarios del chat RAG.</summary>
    public const string ChatUser = nameof(ChatUser);
}

/// <summary>Políticas de limitación de tasa propias de la API.</summary>
public static class RateLimitingPolicies
{
    /// <summary>Ventana fija por usuario para proteger la cuota del proveedor de IA.</summary>
    public const string ChatPerUser = nameof(ChatPerUser);

    /// <summary>Protección frente a fuerza bruta en la emisión de tokens.</summary>
    public const string TokenIssuance = nameof(TokenIssuance);
}

/// <summary>Implementación de <see cref="ICurrentUser"/> sobre el <see cref="HttpContext"/> actual.</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? UserId => Principal?.FindFirstValue(DocuAIClaimTypes.Subject);

    public string? UserName => Principal?.FindFirstValue(DocuAIClaimTypes.Name);
}
