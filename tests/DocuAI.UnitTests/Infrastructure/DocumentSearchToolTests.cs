using DocuAI.Application.Abstractions.Errors;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using DocuAI.Infrastructure.AI.Tools;

namespace DocuAI.UnitTests.Infrastructure;

/// <summary>Pruebas de la herramienta de búsqueda que el agente expone al modelo.</summary>
public sealed class DocumentSearchToolTests
{
    private readonly IKnowledgeRetriever _retriever = Substitute.For<IKnowledgeRetriever>();

    [Fact]
    public void AsAIFunction_DebeExponerNombreYEsquemaSinCancellationToken()
    {
        var function = new DocumentSearchTool(_retriever, topK: 5, minSimilarity: 0.25).AsAIFunction();

        function.Name.ShouldBe(DocumentSearchTool.FunctionName);
        function.Description.ShouldBe(DocumentSearchTool.FunctionDescription);

        var schema = function.JsonSchema.ToString();
        schema.ShouldContain("\"query\"");
        schema.ShouldNotContain("cancellationToken");
    }

    [Fact]
    public async Task ConResultados_DebeFormatearElContextoEscapandoLaFuente()
    {
        _retriever.RetrieveAsync("vacaciones", 5, 0.25, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ChunkSearchResult>>(
                [new ChunkSearchResult(DocumentId.New(), "a\"b<c>.md", 2, "  Texto del fragmento.  ", 0.9)]));

        var context = await new DocumentSearchTool(_retriever, 5, 0.25).SearchDocumentsAsync("vacaciones");

        context.ShouldStartWith("<contexto>");
        context.ShouldContain("fuente=\"a&quot;b&lt;c&gt;.md\" indice=\"2\" similitud=\"0.900\"");
        context.ShouldContain("Texto del fragmento.");
    }

    [Fact]
    public async Task SinResultados_DebeDevolverElMarcador()
    {
        _retriever.RetrieveAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Result.Success<IReadOnlyList<ChunkSearchResult>>([]));

        var context = await new DocumentSearchTool(_retriever, 5, 0.25).SearchDocumentsAsync("nada");

        context.ShouldStartWith(DocumentSearchTool.NoResultsMarker);
    }

    [Fact]
    public async Task FalloDelRetriever_DebeDevolverElMarcadorDeErrorSinLanzar()
    {
        _retriever.RetrieveAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Result.Failure<IReadOnlyList<ChunkSearchResult>>(AiErrors.Unavailable));

        var context = await new DocumentSearchTool(_retriever, 5, 0.25).SearchDocumentsAsync("vacaciones");

        context.ShouldStartWith(DocumentSearchTool.ErrorMarker);
        context.ShouldContain(AiErrors.Unavailable.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConsultaVacia_NoDebeConsultarElRetriever(string query)
    {
        var context = await new DocumentSearchTool(_retriever, 5, 0.25).SearchDocumentsAsync(query);

        context.ShouldStartWith(DocumentSearchTool.ErrorMarker);
        await _retriever.DidNotReceiveWithAnyArgs().RetrieveAsync(default!, default, default, default);
    }
}
