using System.ClientModel;
using System.ClientModel.Primitives;
using DocuAI.Infrastructure.Options;
using DocuAI.Infrastructure.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DocuAI.Infrastructure.AI.Chat;

/// <summary>
/// Construye el <see cref="IChatClient"/> que consume el agente a partir de cualquier proveedor
/// con API compatible con OpenAI (Google Gemini, Groq, OpenRouter, Ollama…), usando el SDK
/// oficial de OpenAI apuntando al endpoint configurado.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>El transporte es el <see cref="HttpClient"/> nombrado de <see cref="IHttpClientFactory"/>,
/// que lleva el pipeline de resiliencia Polly v8 (backoff exponencial ante 429 respetando
/// Retry-After, circuit breaker, timeouts) y las cabeceras adicionales del proveedor.</item>
/// <item>Los reintentos propios del SDK se desactivan para que Polly sea la única capa de
/// reintentos; si no, ambos se multiplicarían y agotarían antes la cuota gratuita.</item>
/// <item>El bucle de invocación de funciones se limita a <see cref="AgentOptions.MaxToolIterations"/>:
/// cada iteración es una petición al modelo y cuenta contra la cuota del proveedor.</item>
/// </list>
/// </remarks>
internal static class ChatClientFactory
{
    /// <summary>Crea el cliente de chat. Se registra con ámbito por petición.</summary>
    public static IChatClient Create(IServiceProvider serviceProvider)
    {
        var provider = serviceProvider.GetRequiredService<IOptions<ChatProviderOptions>>().Value;
        var agent = serviceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ProviderHttpClientExtensions.ChatClientName);

        var clientOptions = new OpenAIClientOptions
        {
            // El SDK añade "/chat/completions"; se elimina la barra final para no duplicarla.
            Endpoint = new Uri(provider.BaseUrl.TrimEnd('/')),
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = provider.Resilience.TotalTimeout
        };

        var openAiChatClient = new ChatClient(
            provider.Model,
            new ApiKeyCredential(provider.ApiKey),
            clientOptions);

        return openAiChatClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, functionInvoker =>
            {
                functionInvoker.MaximumIterationsPerRequest = agent.MaxToolIterations;

                // Las búsquedas comparten DbContext (scoped): nunca en paralelo.
                functionInvoker.AllowConcurrentInvocation = false;
            })
            .Build(serviceProvider);
    }
}
