using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DocuAI.Api.Common;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api;

/// <summary>Opciones de limitación de tasa de la API.</summary>
internal sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Consultas de chat por usuario y minuto (la API gratuita admite ~20 RPM).</summary>
    public int ChatRequestsPerMinute { get; init; } = 10;

    /// <summary>Solicitudes de token por IP y minuto.</summary>
    public int TokenRequestsPerMinute { get; init; } = 5;
}

/// <summary>Registro de servicios de la capa de presentación.</summary>
internal static class PresentationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresentation(IConfiguration configuration)
        {
            services.AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            services.AddOpenApi();
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

            services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = context =>
                {
                    context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                    context.ProblemDetails.Extensions.TryAdd(
                        "traceId",
                        Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
                });

            services.AddExceptionHandler<GlobalExceptionHandler>();

            services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(Roles.Admin))
                .AddPolicy(AuthorizationPolicies.ChatUser, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(Roles.User));

            AddRateLimiting(services, configuration);

            services.AddFrontendCors(configuration);

            return services;
        }
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitingPolicies.ChatPerUser, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.FindFirstValue(DocuAIClaimTypes.Subject)
                                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                  ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.ChatRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(RateLimitingPolicies.TokenIssuance, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.TokenRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiadas peticiones",
                        Detail = "Ha superado el límite de peticiones permitido. Espere unos segundos e inténtelo de nuevo.",
                        Type = "https://tools.ietf.org/html/rfc6585#section-4"
                    }
                });
            };
        });
    }
}
