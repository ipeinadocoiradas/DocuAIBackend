using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;

namespace DocuAI.UnitTests.Domain;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData(@"C:\docs\Manual.PDF", "Manual.PDF")]
    public void FileName_DebeEliminarRutas(string raw, string expected)
    {
        var result = FileName.Create(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
        result.Value.Extension.ShouldBe(".pdf");
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("sin_extension")]
    public void FileName_ConExtensionNoSoportada_DebeFallar(string raw)
    {
        var result = FileName.Create(raw);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Document.UnsupportedFileType");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_DebeQuedarPendienteConHash()
    {
        var document = CreateDocument();

        document.Status.ShouldBe(DocumentStatus.Pending);
        document.ContentHash.Value.Length.ShouldBe(ContentHash.Length);
    }

    [Fact]
    public void Create_ConFicheroDemasiadoGrande_DebeFallar()
    {
        var result = Document.Create(
            FileName.Create("a.txt").Value, "text/plain", DocumentRules.MaxFileSizeBytes + 1, "texto", "admin", Now);

        result.Error.ShouldBe(DocumentErrors.FileTooLarge);
    }

    [Fact]
    public void CicloCompleto_DebeQuedarProcesado()
    {
        var document = CreateDocument();
        var chunk = DocumentChunk.Create(document.Id, 0, "contenido", 2, [0.1f, 0.2f]).Value;

        document.StartProcessing(Now).IsSuccess.ShouldBeTrue();
        document.CompleteProcessing([chunk], Now).IsSuccess.ShouldBeTrue();

        document.Status.ShouldBe(DocumentStatus.Processed);
        document.ChunkCount.ShouldBe(1);
        document.StartProcessing(Now).Error.ShouldBe(DocumentErrors.AlreadyProcessed);
    }

    [Fact]
    public void StartProcessing_AlSuperarIntentos_DebeMarcarComoFallido()
    {
        var document = CreateDocument();

        for (var i = 0; i < DocumentRules.MaxProcessingAttempts; i++)
        {
            document.StartProcessing(Now).IsSuccess.ShouldBeTrue();
        }

        var result = document.StartProcessing(Now);

        result.Error.ShouldBe(DocumentErrors.MaxAttemptsReached);
        document.Status.ShouldBe(DocumentStatus.Failed);
    }

    [Fact]
    public void ResetForReprocessing_SoloDesdeFallido()
    {
        var document = CreateDocument();

        document.ResetForReprocessing().Error.ShouldBe(DocumentErrors.NotFailed);

        document.StartProcessing(Now);
        document.MarkAsFailed("timeout");

        document.ResetForReprocessing().IsSuccess.ShouldBeTrue();
        document.Status.ShouldBe(DocumentStatus.Pending);
        document.ProcessingAttempts.ShouldBe(0);
    }

    private static Document CreateDocument() =>
        Document.Create(FileName.Create("manual.md").Value, "text/markdown", 1024, "# Manual\nTexto.", "admin", Now).Value;
}
