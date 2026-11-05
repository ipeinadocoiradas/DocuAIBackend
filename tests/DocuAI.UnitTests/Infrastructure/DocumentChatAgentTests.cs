using System.Runtime.CompilerServices;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using DocuAI.Infrastructure.AI.Agents;
using DocuAI.Infrastructure.AI.Tools;
using DocuAI.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocuAI.UnitTests.Infrastructure;

/// <summary>
/// Pruebas del agente RAG sobre Microsoft Agent Framework con un <see cref="IChatClient"/>
/// simulado: verifican qué recibe el modelo en cada modo de recuperación y que la
/// respuesta se transmite token a token.
/// </summary>
public sealed class DocumentChatAgentTests
{
    private const string Question = "¿Cómo se solicitan las vacaciones?";

    private readonly IKnowledgeRetriever _retriever = Substitute.For<IKnowledgeRetriever>();

    [Fact]
    public async Task PreRetrieval_DebeInyectarElContextoYNoExponerHerramientas()
    {
        _retriever.RetrieveAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Result.Success<IReadOnlyList<ChunkSearchResult>>(
                [new ChunkSearchResult(DocumentId.New(), "manual-rrhh.pdf", 3, "Las vacaciones se solicitan en el portal del empleado.", 0.82)]));

        var chatClient = new RecordingChatClient("Se solicitan ", "en el portal ", "[manual-rrhh.pdf, fragmento 3]");
        var agent = CreateAgent(chatClient, RetrievalMode.PreRetrieval);

        var tokens = await CollectAsync(agent.StreamAnswerAsync(Question, topK: 5));

        string.Concat(tokens).ShouldBe("Se solicitan en el portal [manual-rrhh.pdf, fragmento 3]");
        await _retriever.Received(1).RetrieveAsync(Question, 5, 0.25, Arg.Any<CancellationToken>());

        var userText = chatClient.LastUserMessageText();
        userText.ShouldContain("<contexto>");
        userText.ShouldContain("manual-rrhh.pdf");
        userText.ShouldContain("<pregunta>");
        userText.ShouldContain(Question);

        (chatClient.LastOptions?.Tools?.Count ?? 0).ShouldBe(0);
    }

    [Fact]
    public async Task FunctionCalling_DebeExponerLaHerramientaYEnviarLaPreguntaTalCual()
    {
        var chatClient = new RecordingChatClient("Respuesta");
        var agent = CreateAgent(chatClient, RetrievalMode.FunctionCalling);

        var tokens = await CollectAsync(agent.StreamAnswerAsync(Question, topK: 7));

        tokens.Count.ShouldBe(1);
        tokens[0].ShouldBe("Respuesta");
        chatClient.LastUserMessageText().ShouldBe(Question);

        // El modelo simulado no invoca la herramienta: no debe haberse hecho ninguna búsqueda.
        await _retriever.DidNotReceiveWithAnyArgs().RetrieveAsync(default!, default, default, default);

        var tool = chatClient.LastOptions?.Tools?.OfType<AIFunction>().SingleOrDefault();
        tool.ShouldNotBeNull();
        tool.Name.ShouldBe(DocumentSearchTool.FunctionName);

        // La herramienta expuesta está enlazada al Top K de la petición.
        _retriever.RetrieveAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Result.Success<IReadOnlyList<ChunkSearchResult>>([]));

        await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "vacaciones" });

        await _retriever.Received(1).RetrieveAsync("vacaciones", 7, 0.25, Arg.Any<CancellationToken>());
    }

    private DocumentChatAgent CreateAgent(IChatClient chatClient, RetrievalMode mode) => new(
        chatClient,
        _retriever,
        MsOptions.Create(new AgentOptions { RetrievalMode = mode, MinSimilarity = 0.25 }),
        NullLoggerFactory.Instance,
        NullLogger<DocumentChatAgent>.Instance);

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> stream)
    {
        List<string> items = [];

        await foreach (var item in stream)
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>Cliente de chat que registra la última petición y responde con fragmentos fijos.</summary>
    private sealed class RecordingChatClient(params string[] chunks) : IChatClient
    {
        public List<ChatMessage> LastMessages { get; } = [];

        public ChatOptions? LastOptions { get; private set; }

        public string LastUserMessageText() =>
            LastMessages.Last(message => message.Role == ChatRole.User).Text;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(messages, options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(messages, options);

            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private void Capture(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            LastMessages.Clear();
            LastMessages.AddRange(messages);
            LastOptions = options;
        }
    }
}
