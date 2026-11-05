using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Features.Chat.AskQuestion;
using DocuAI.Application.Features.Documents.ProcessDocument;
using DocuAI.Application.Features.Documents.UploadDocument;
using DocuAI.Application.Options;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocuAI.UnitTests.Application;

public sealed class UploadDocumentCommandValidatorTests
{
    private readonly UploadDocumentCommandValidator _validator = new();

    [Fact]
    public async Task ComandoValido_NoDebeTenerErrores()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await _validator.ValidateAsync(new UploadDocumentCommand("guia.pdf", "application/pdf", 3, stream));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ExtensionNoSoportada_DebeFallar()
    {
        await using var stream = new MemoryStream([1]);

        var result = await _validator.ValidateAsync(new UploadDocumentCommand("hoja.xlsx", "application/vnd.ms-excel", 1, stream));

        result.Errors.ShouldContain(error => error.PropertyName == nameof(UploadDocumentCommand.FileName));
    }
}

public sealed class AskQuestionQueryHandlerTests
{
    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IChatAgent _agent = Substitute.For<IChatAgent>();

    [Fact]
    public async Task SinDocumentosIndexados_DebeDevolverReglaDeNegocio()
    {
        _repository.AnyProcessedAsync(Arg.Any<CancellationToken>()).Returns(false);
        var handler = new AskQuestionQueryHandler(_repository, _agent, MsOptions.Create(new ChatOptions()));

        var result = await handler.Handle(new AskQuestionQuery("¿Qué es DocuAI?", null), CancellationToken.None);

        result.Error.ShouldBe(ChatErrors.KnowledgeBaseEmpty);
        _agent.DidNotReceiveWithAnyArgs().StreamAnswerAsync(default!, default);
    }

    [Fact]
    public async Task ConDocumentos_DebeUsarTopKPorDefecto()
    {
        _repository.AnyProcessedAsync(Arg.Any<CancellationToken>()).Returns(true);
        var options = new ChatOptions { DefaultTopK = 7 };
        var handler = new AskQuestionQueryHandler(_repository, _agent, MsOptions.Create(options));

        var result = await handler.Handle(new AskQuestionQuery("  pregunta  ", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _agent.Received(1).StreamAnswerAsync("pregunta", 7, Arg.Any<CancellationToken>());
    }
}

public sealed class ProcessDocumentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITextChunker _chunker = Substitute.For<ITextChunker>();
    private readonly IEmbeddingService _embeddings = Substitute.For<IEmbeddingService>();

    [Fact]
    public async Task DebeGenerarEmbeddingsEnLotes()
    {
        var document = CreateDocument();
        _repository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _chunker.Split(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns([.. Enumerable.Range(0, 5).Select(i => new TextChunk(i, $"fragmento {i}", 3))]);
        _embeddings.GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<IReadOnlyList<float[]>>(
                [.. call.Arg<IReadOnlyList<string>>().Select(_ => new[] { 0.5f, 0.5f })]));

        var handler = CreateHandler(batchSize: 2);

        var result = await handler.Handle(new ProcessDocumentCommand(document.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        document.Status.ShouldBe(DocumentStatus.Processed);
        document.ChunkCount.ShouldBe(5);
        await _embeddings.Received(3).GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalloDelProveedor_DebeMarcarElDocumentoComoFallido()
    {
        var document = CreateDocument();
        _repository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _chunker.Split(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns([new TextChunk(0, "texto", 1)]);
        _embeddings.GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<float[]>>(AiErrors.RateLimited));

        var result = await CreateHandler(batchSize: 16).Handle(new ProcessDocumentCommand(document.Id.Value), CancellationToken.None);

        result.Error.ShouldBe(AiErrors.RateLimited);
        document.Status.ShouldBe(DocumentStatus.Failed);
        document.FailureReason!.ShouldContain(AiErrors.RateLimited.Code);
    }

    private ProcessDocumentCommandHandler CreateHandler(int batchSize) => new(
        _repository,
        _unitOfWork,
        _chunker,
        _embeddings,
        MsOptions.Create(new IngestionOptions { EmbeddingBatchSize = batchSize, DelayBetweenBatches = TimeSpan.Zero }),
        TimeProvider.System,
        NullLogger<ProcessDocumentCommandHandler>.Instance);

    private static Document CreateDocument() =>
        Document.Create(FileName.Create("manual.txt").Value, "text/plain", 100, "Texto de prueba.", "admin", Now).Value;
}
