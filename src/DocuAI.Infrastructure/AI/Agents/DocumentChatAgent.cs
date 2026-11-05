using DocuAI.Application.Abstractions.Services;
using DocuAI.Infrastructure.AI.Tools;
using DocuAI.Infrastructure.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace DocuAI.Infrastructure.AI.Agents;

/// <summary>
/// Agente RAG basado en <see cref="ChatClientAgent"/> de Microsoft Agent Framework.
/// Usa la herramienta <see cref="DocumentSearchTool"/> para recuperar el contexto Top K y
/// genera una respuesta fundamentada que se transmite token a token.
/// </summary>
/// <remarks>
/// El agente se construye por petición (es un objeto ligero) porque la herramienta lleva el
/// Top K solicitado. El <see cref="IChatClient"/> inyectado ya incluye el bucle de invocación
/// de funciones con su límite de iteraciones (ver <c>OpenRouterChatClientFactory</c>), por lo
/// que el agente lo usa tal cual (<see cref="ChatClientAgentOptions.UseProvidedChatClientAsIs"/>).
/// </remarks>
internal sealed partial class DocumentChatAgent(
    IChatClient chatClient,
    IKnowledgeRetriever knowledgeRetriever,
    IOptions<AgentOptions> agentOptions,
    ILoggerFactory loggerFactory,
    ILogger<DocumentChatAgent> logger) : IChatAgent
{
    /// <summary>Nombre del agente (aparece en telemetría y en los mensajes del asistente).</summary>
    public const string AgentName = "DocuAI";

    private const string AgentDescription =
        "Responde preguntas sobre la documentación corporativa basándose exclusivamente en la base de conocimiento.";

    private readonly AgentOptions _options = agentOptions.Value;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string question,
        int topK,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchTool = new DocumentSearchTool(knowledgeRetriever, topK, _options.MinSimilarity);
        var useFunctionCalling = _options.RetrievalMode == RetrievalMode.FunctionCalling;

        // Pre-recuperación: la búsqueda se ejecuta de forma determinista antes de llamar al modelo
        // y el contexto viaja en el propio mensaje del usuario.
        var userMessage = useFunctionCalling
            ? question
            : AgentPrompts.BuildGroundedUserMessage(
                await searchTool.SearchDocumentsAsync(question, cancellationToken),
                question);

        // Tipado como AIAgent a propósito: se usa la API base del framework y se evitan
        // ambigüedades con las sobrecargas específicas de ChatClientAgent.
        AIAgent agent = CreateAgent(searchTool, useFunctionCalling);

        LogInvoking(logger, _options.RetrievalMode, topK);

        // Sin sesión: cada consulta es independiente (no hay historial conversacional).
        await foreach (var update in agent.RunStreamingAsync(userMessage, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } token)
            {
                yield return token;
            }
        }
    }

    private ChatClientAgent CreateAgent(DocumentSearchTool searchTool, bool useFunctionCalling)
    {
        List<AITool>? tools = useFunctionCalling ? [searchTool.AsAIFunction()] : null;

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = AgentName,
                Description = AgentDescription,
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = useFunctionCalling
                        ? AgentPrompts.FunctionCallingInstructions
                        : AgentPrompts.PreRetrievalInstructions,
                    Temperature = (float)_options.Temperature,
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Tools = tools,
                    ToolMode = useFunctionCalling ? ChatToolMode.Auto : null
                }
            },
            loggerFactory);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Invocando agente RAG (modo {RetrievalMode}, TopK {TopK})")]
    private static partial void LogInvoking(ILogger logger, RetrievalMode retrievalMode, int topK);
}
