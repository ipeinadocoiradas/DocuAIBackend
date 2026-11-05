using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using FluentValidation;

namespace DocuAI.Application.Features.Auth.IssueToken;

/// <summary>
/// Emite un JWT para un usuario local. Pensado para desarrollo y pruebas; en producción
/// los tokens los emite el proveedor de identidad corporativo (Entra ID, Keycloak…).
/// </summary>
public sealed record IssueTokenCommand(string UserName, string Password) : ICommand<TokenResponse>;

/// <summary>Token de acceso emitido.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, IReadOnlyCollection<string> Roles);

internal sealed class IssueTokenCommandValidator : AbstractValidator<IssueTokenCommand>
{
    public IssueTokenCommandValidator()
    {
        RuleFor(command => command.UserName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(200);
    }
}

internal sealed class IssueTokenCommandHandler(
    IIdentityService identityService,
    IJwtTokenProvider tokenProvider)
    : ICommandHandler<IssueTokenCommand, TokenResponse>
{
    public async Task<Result<TokenResponse>> Handle(IssueTokenCommand command, CancellationToken cancellationToken)
    {
        var userResult = await identityService.ValidateCredentialsAsync(command.UserName, command.Password, cancellationToken);

        return userResult.Map(user =>
        {
            var token = tokenProvider.CreateToken(user);
            return new TokenResponse(token.Token, "Bearer", token.ExpiresAtUtc, user.Roles);
        });
    }
}
