using DocuAI.Domain.Common;

namespace DocuAI.Domain.Documents;

/// <summary>
/// Raíz de agregado que representa un documento de la base de conocimiento y su
/// ciclo de ingesta (fragmentación + embeddings).
/// </summary>
public sealed class Document : Entity<DocumentId>
{
    private readonly List<DocumentChunk> _chunks = [];

    private Document(
        DocumentId id,
        FileName fileName,
        string contentType,
        long sizeInBytes,
        string extractedText,
        ContentHash contentHash,
        string uploadedBy,
        DateTimeOffset uploadedAtUtc)
        : base(id)
    {
        FileName = fileName;
        ContentType = contentType;
        SizeInBytes = sizeInBytes;
        ExtractedText = extractedText;
        ContentHash = contentHash;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAtUtc;
        Status = DocumentStatus.Pending;
    }

    /// <summary>Constructor reservado para EF Core.</summary>
    private Document()
    {
    }

    public FileName FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeInBytes { get; private set; }

    /// <summary>Texto plano extraído del fichero original; fuente de la fragmentación.</summary>
    public string ExtractedText { get; private set; } = string.Empty;

    public ContentHash ContentHash { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string UploadedBy { get; private set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public DateTimeOffset? ProcessingStartedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public int ProcessingAttempts { get; private set; }

    public int ChunkCount { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Fragmentos indexados del documento.</summary>
    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks.AsReadOnly();

    /// <summary>
    /// Crea un nuevo documento pendiente de ingesta validando sus invariantes.
    /// </summary>
    public static Result<Document> Create(
        FileName fileName,
        string contentType,
        long sizeInBytes,
        string extractedText,
        string uploadedBy,
        DateTimeOffset nowUtc)
    {
        if (sizeInBytes <= 0)
        {
            return DocumentErrors.EmptyFile;
        }

        if (sizeInBytes > DocumentRules.MaxFileSizeBytes)
        {
            return DocumentErrors.FileTooLarge;
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > DocumentRules.MaxContentTypeLength)
        {
            return DocumentErrors.InvalidContentType;
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return DocumentErrors.NoExtractableText;
        }

        if (string.IsNullOrWhiteSpace(uploadedBy))
        {
            return DocumentErrors.UploaderRequired;
        }

        return new Document(
            DocumentId.New(),
            fileName,
            contentType.Trim(),
            sizeInBytes,
            extractedText,
            ContentHash.FromText(extractedText),
            uploadedBy.Trim(),
            nowUtc);
    }

    /// <summary>
    /// Marca el inicio de un intento de procesamiento. Admite reanudar documentos
    /// que quedaron en <see cref="DocumentStatus.Processing"/> tras un reinicio.
    /// </summary>
    public Result StartProcessing(DateTimeOffset nowUtc)
    {
        if (Status == DocumentStatus.Processed)
        {
            return DocumentErrors.AlreadyProcessed;
        }

        if (Status == DocumentStatus.Failed)
        {
            return DocumentErrors.NotInProcessingState;
        }

        if (ProcessingAttempts >= DocumentRules.MaxProcessingAttempts)
        {
            Fail(DocumentErrors.MaxAttemptsReached.Description);
            return DocumentErrors.MaxAttemptsReached;
        }

        ProcessingAttempts++;
        Status = DocumentStatus.Processing;
        ProcessingStartedAtUtc = nowUtc;
        FailureReason = null;

        return Result.Success();
    }

    /// <summary>
    /// Completa la ingesta asociando los fragmentos indexados.
    /// </summary>
    public Result CompleteProcessing(IReadOnlyCollection<DocumentChunk> chunks, DateTimeOffset nowUtc)
    {
        if (Status != DocumentStatus.Processing)
        {
            return DocumentErrors.NotInProcessingState;
        }

        if (chunks.Count == 0)
        {
            return DocumentErrors.NoChunks;
        }

        _chunks.Clear();
        _chunks.AddRange(chunks);

        ChunkCount = _chunks.Count;
        Status = DocumentStatus.Processed;
        ProcessedAtUtc = nowUtc;
        FailureReason = null;

        return Result.Success();
    }

    /// <summary>Registra el fallo de la ingesta con su motivo.</summary>
    public Result MarkAsFailed(string reason)
    {
        if (Status == DocumentStatus.Processed)
        {
            return DocumentErrors.AlreadyProcessed;
        }

        Fail(reason);
        return Result.Success();
    }

    /// <summary>
    /// Devuelve a la cola un documento fallido (acción administrativa explícita),
    /// reiniciando el contador de intentos.
    /// </summary>
    public Result ResetForReprocessing()
    {
        if (Status != DocumentStatus.Failed)
        {
            return DocumentErrors.NotFailed;
        }

        Status = DocumentStatus.Pending;
        ProcessingAttempts = 0;
        FailureReason = null;
        ProcessingStartedAtUtc = null;

        return Result.Success();
    }

    private void Fail(string reason)
    {
        Status = DocumentStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason)
            ? "Motivo desconocido."
            : reason.Length > DocumentRules.MaxFailureReasonLength
                ? reason[..DocumentRules.MaxFailureReasonLength]
                : reason;
    }

    /// <summary>
    /// Comprueba si el documento puede eliminarse. Un documento en <see cref="DocumentStatus.Processing"/>
    /// no se elimina para no interferir con la persistencia de fragmentos que está realizando el worker.
    /// </summary>
    public Result EnsureCanBeDeleted() =>
        Status == DocumentStatus.Processing
            ? Result.Failure(DocumentErrors.CannotDeleteWhileProcessing(Id.Value))
            : Result.Success();
}
