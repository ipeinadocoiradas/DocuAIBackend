using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DocuAI.Api.Common;

/// <summary>Nombres de las políticas CORS de la API.</summary>
public static class CorsPolicies
{
    /// <summary>Política aplicada a todas las peticiones procedentes del front (SPA).</summary>
    public const string Frontend = nameof(Frontend);
}

/// <summary>
/// Configuración CORS para el front (sección <c>Cors</c>).
/// </summary>
/// <remarks>
/// La autenticación viaja en la cabecera <c>Authorization</c> (Bearer), no en cookies, por lo
/// que la política no habilita credenciales. Sin orígenes configurados, el navegador bloquea
/// cualquier petición de otro origen (escenario de front y API tras el mismo dominio).
/// </remarks>
internal sealed class FrontendCorsOptions : IValidatableObject
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Orígenes exactos permitidos: esquema + host + puerto, sin barra final ni ruta
    /// (p. ej. <c>http://localhost:4200</c>). No se admite el comodín <c>*</c>.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>
    /// Cabeceras de petición que siempre se permiten: JSON, multipart, JWT y SSE.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowedHeaders =
    [
        HeaderNames.Authorization,
        HeaderNames.ContentType,
        HeaderNames.Accept,
        HeaderNames.AcceptLanguage,
        HeaderNames.CacheControl,
        "Last-Event-ID"
    ];

    /// <summary>
    /// Cabeceras adicionales a <see cref="DefaultAllowedHeaders"/> (p. ej. <c>traceparent</c> si el
    /// front propaga trazas). Se define como adicional porque el binder de configuración
    /// concatena los arrays en lugar de sustituirlos.
    /// </summary>
    public string[] AdditionalAllowedHeaders { get; init; } = [];

    /// <summary>Conjunto efectivo de cabeceras permitidas.</summary>
    public string[] GetAllowedHeaders() =>
        [.. DefaultAllowedHeaders.Concat(AdditionalAllowedHeaders).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Tiempo que el navegador puede cachear la respuesta preflight.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan PreflightMaxAge { get; init; } = TimeSpan.FromMinutes(10);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var origin in AllowedOrigins)
        {
            if (origin == "*")
            {
                yield return new ValidationResult(
                    "No se admite el origen comodín '*': indique los orígenes exactos del front.",
                    [nameof(AllowedOrigins)]);
                continue;
            }

            var isValidOrigin =
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);

            if (!isValidOrigin)
            {
                yield return new ValidationResult(
                    $"El origen CORS '{origin}' no es válido. Formato esperado: esquema://host[:puerto], sin ruta ni barra final.",
                    [nameof(AllowedOrigins)]);
            }
        }

        if (AdditionalAllowedHeaders.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult(
                "Las cabeceras CORS adicionales no pueden estar vacías.",
                [nameof(AdditionalAllowedHeaders)]);
        }
    }
}

/// <summary>Registro de CORS para el front.</summary>
internal static class FrontendCorsServiceCollectionExtensions
{
    /// <summary>
    /// Cabeceras de respuesta que el front necesita leer: <c>Location</c> (URL de polling tras
    /// la subida) y <c>Retry-After</c> (espera sugerida tras un 429).
    /// </summary>
    private static readonly string[] ExposedHeaders = [HeaderNames.Location, HeaderNames.RetryAfter];

    private static readonly string[] AllowedMethods = [HttpMethods.Get, HttpMethods.Post, HttpMethods.Delete];

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra la política <see cref="CorsPolicies.Frontend"/> a partir de la sección
        /// <c>Cors</c>, validada en el arranque.
        /// </summary>
        public IServiceCollection AddFrontendCors(IConfiguration configuration)
        {
            services.AddOptions<FrontendCorsOptions>()
                .Bind(configuration.GetSection(FrontendCorsOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddCors();

            // La política se construye a partir de las opciones ya validadas.
            services.AddOptions<CorsOptions>()
                .Configure<IOptions<FrontendCorsOptions>>((corsOptions, frontendOptions) =>
                {
                    var frontend = frontendOptions.Value;

                    corsOptions.AddPolicy(CorsPolicies.Frontend, policy => policy
                        .WithOrigins(frontend.AllowedOrigins)
                        .WithMethods(AllowedMethods)
                        .WithHeaders(frontend.GetAllowedHeaders())
                        .WithExposedHeaders(ExposedHeaders)
                        .SetPreflightMaxAge(frontend.PreflightMaxAge));
                });

            return services;
        }
    }
}
