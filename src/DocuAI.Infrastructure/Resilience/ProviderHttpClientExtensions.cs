using System.Net;
using System.Net.Http.Headers;
using DocuAI.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DocuAI.Infrastructure.Resilience;

/// <summary>
/// Configuración de los <see cref="HttpClient"/> hacia los proveedores de IA:
/// (chat) y el proveedor de embeddings compatible con OpenAI (Ollama por defecto).
/// Cada uno lleva su propio pipeline de resiliencia Polly v8.
/// </summary>
internal static partial class ProviderHttpClientExtensions
{
    /// <summary>
    /// Cliente que el SDK de OpenAI usa como transporte para el chat del agente
    /// (la autenticación la añade el SDK).
    /// </summary>
    public const string ChatClientName = "Chat";

    /// <summary>Cliente del proveedor de embeddings.</summary>
    public const string EmbeddingsClientName = "Embeddings";

    /// <summary>Cliente sin reintentos para el health check del proveedor de embeddings.</summary>
    public const string EmbeddingsHealthClientName = "Embeddings.HealthCheck";

    extension(IHttpClientBuilder builder)
    {
        /// <summary>
        /// Configura la dirección base, el timeout y las cabeceras adicionales del proveedor de chat.
        /// La autenticación (<c>Authorization: Bearer</c>) la añade el SDK de OpenAI a partir de la API key.
        /// </summary>
        public IHttpClientBuilder ConfigureChatProviderClient() =>
            builder.ConfigureHttpClient((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ChatProviderOptions>>().Value;

                client.BaseAddress = ToBaseAddress(options.BaseUrl);

                // Los timeouts los gestiona el pipeline de resiliencia (por intento y total);
                // el streaming de respuestas puede superar el timeout por defecto de 100 s.
                client.Timeout = Timeout.InfiniteTimeSpan;
                
                // Se ignoran las vacías y Authorization, que es responsabilidad del SDK.
                foreach (var (name, value) in options.DefaultHeaders)
                {
                    if (string.IsNullOrWhiteSpace(value)
                        || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
                }
            });

        /// <summary>
        /// Configura el cliente del proveedor de embeddings. La cabecera Authorization solo se
        /// añade si hay API key (Ollama no la necesita).
        /// </summary>
        public IHttpClientBuilder ConfigureEmbeddingClient() =>
            builder.ConfigureHttpClient((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

                client.BaseAddress = ToBaseAddress(options.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }
            });

        /// <summary>
        /// Añade la estrategia de resiliencia (de fuera hacia dentro):
        /// timeout total → reintentos con backoff exponencial + jitter (respeta Retry-After) →
        /// circuit breaker → timeout por intento.
        /// </summary>
        /// <param name="pipelineName">Nombre del pipeline (aparece en logs y telemetría).</param>
        /// <param name="optionsSelector">Obtiene las opciones de resiliencia del proveedor.</param>
        public IHttpClientBuilder AddProviderResilience(
            string pipelineName,
            Func<IServiceProvider, HttpResilienceOptions> optionsSelector)
        {
            builder.AddResilienceHandler(pipelineName, (pipeline, context) =>
            {
                var options = optionsSelector(context.ServiceProvider);
                var logger = context.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger($"DocuAI.Resilience.{pipelineName}");

                pipeline.AddTimeout(options.TotalTimeout);

                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = options.BaseDelay,
                    MaxDelay = options.MaxDelay,

                    // Si el proveedor envía Retry-After en un 429 se respeta ese valor.
                    ShouldRetryAfterHeader = true,

                    // Reintenta explícitamente 429 (cuota) además de los fallos transitorios estándar.
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests
                        || HttpClientResiliencePredicates.IsTransient(args.Outcome)),

                    OnRetry = args =>
                    {
                        LogRetry(
                            logger,
                            pipelineName,
                            args.AttemptNumber + 1,
                            options.MaxRetryAttempts,
                            (int?)args.Outcome.Result?.StatusCode,
                            args.RetryDelay.TotalSeconds,
                            args.Outcome.Exception?.GetType().Name);

                        return ValueTask.CompletedTask;
                    }
                });

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    SamplingDuration = TimeSpan.FromMinutes(2),
                    MinimumThroughput = 8,
                    FailureRatio = 0.8,
                    BreakDuration = options.CircuitBreakDuration,
                    OnOpened = args =>
                    {
                        LogCircuitOpened(logger, pipelineName, args.BreakDuration.TotalSeconds);
                        return ValueTask.CompletedTask;
                    }
                });

                pipeline.AddTimeout(options.AttemptTimeout);
            });

            return builder;
        }
    }

    private static Uri ToBaseAddress(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Pipeline}: reintento {Attempt}/{MaxAttempts} (HTTP {StatusCode}) en {DelaySeconds:0.0} s. Excepción: {ExceptionType}")]
    private static partial void LogRetry(ILogger logger, string pipeline, int attempt, int maxAttempts, int? statusCode, double delaySeconds, string? exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Pipeline}: circuito abierto durante {BreakSeconds:0} s por fallos sostenidos")]
    private static partial void LogCircuitOpened(ILogger logger, string pipeline, double breakSeconds);
}
