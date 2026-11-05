using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DocuAI.Infrastructure.Identity;

/// <summary>Roles de la aplicación.</summary>
public static class Roles
{
    /// <summary>Alimenta la base de conocimiento (ingesta de documentos).</summary>
    public const string Admin = "Admin";

    /// <summary>Interactúa con el chat RAG.</summary>
    public const string User = "User";
}

/// <summary>Nombres de claims emitidos en el JWT (sin mapeo a URIs de WS-Federation).</summary>
public static class DocuAIClaimTypes
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Name = "name";
    public const string Role = "role";
}

/// <summary>Emite JWT firmados con HMAC-SHA256.</summary>
internal sealed class JwtTokenProvider(IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider) : IJwtTokenProvider
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateToken(AuthenticatedUser user)
    {
        var options = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(options.AccessTokenLifetime);

        List<Claim> claims =
        [
            new(DocuAIClaimTypes.Subject, user.UserId),
            new(DocuAIClaimTypes.Name, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            .. user.Roles.Select(role => new Claim(DocuAIClaimTypes.Role, role))
        ];

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}

/// <summary>
/// Almacén de usuarios basado en configuración, exclusivo para desarrollo y pruebas
/// (<c>Authentication:DevelopmentUsers:Enabled</c>). La comparación es en tiempo constante.
/// </summary>
internal sealed class ConfigurationIdentityService(IOptions<DevelopmentUsersOptions> usersOptions) : IIdentityService
{
    public Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var options = usersOptions.Value;

        if (!options.Enabled)
        {
            return Task.FromResult(Result.Failure<AuthenticatedUser>(AuthErrors.IssuerDisabled));
        }

        var user = options.Users.FirstOrDefault(candidate =>
            string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));

        // Se compara siempre (incluso sin usuario) para no revelar su existencia por tiempos de respuesta.
        var passwordMatches = FixedTimeEquals(user?.Password ?? string.Empty, password);

        Result<AuthenticatedUser> result = user is not null && passwordMatches && user.Password.Length > 0
            ? new AuthenticatedUser(user.UserName.ToLowerInvariant(), user.UserName, user.Roles)
            : AuthErrors.InvalidCredentials;

        return Task.FromResult(result);
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
}
