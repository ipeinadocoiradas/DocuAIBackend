using DocuAI.Infrastructure.Text;

namespace DocuAI.UnitTests.Infrastructure;

public sealed class SemanticTextChunkerTests
{
    private readonly SemanticTextChunker _chunker = new();

    [Fact]
    public void TextoVacio_NoDebeGenerarFragmentos() =>
        _chunker.Split("   ", 500, 100).ShouldBeEmpty();

    [Fact]
    public void NingunFragmento_DebeSuperarElMaximoDeTokens()
    {
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"Esta es la frase número {i} del documento de prueba."));

        var chunks = _chunker.Split(text, 100, 20);

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(chunk => chunk.TokenCount <= 100);
        chunks.Select(chunk => chunk.Index).ShouldBe(Enumerable.Range(0, chunks.Count));
    }

    [Fact]
    public void FragmentosConsecutivos_DebenSolaparse()
    {
        var text = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"Frase {i}."));

        var chunks = _chunker.Split(text, 50, 10);

        var lastSentenceOfFirst = chunks[0].Text.Split(". ").Last().TrimEnd('.');
        chunks[1].Text.ShouldContain(lastSentenceOfFirst);
    }

    [Fact]
    public void FraseSinPuntuacionMayorQueElMaximo_DebeTrocearse()
    {
        var text = string.Concat(Enumerable.Repeat("palabra ", 2000));

        var chunks = _chunker.Split(text, 100, 0);

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(chunk => chunk.TokenCount <= 100);
    }
}
