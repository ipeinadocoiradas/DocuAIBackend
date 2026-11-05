using DocuAI.Domain.Common;

namespace DocuAI.Domain.Documents;

/// <summary>Catálogo de errores de negocio del agregado <see cref="Document"/>.</summary>
public static class DocumentErrors
{
    public static readonly Error FileNameEmpty =
        Error.Validation("Document.FileNameEmpty", "El nombre del fichero es obligatorio.");

    public static readonly Error FileNameTooLong =
        Error.Validation("Document.FileNameTooLong", $"El nombre del fichero no puede superar {DocumentRules.MaxFileNameLength} caracteres.");

    public static readonly Error EmptyFile =
        Error.Validation("Document.EmptyFile", "El fichero está vacío.");

    public static readonly Error FileTooLarge =
        Error.Validation("Document.FileTooLarge", $"El fichero supera el tamaño máximo de {DocumentRules.MaxFileSizeBytes / (1024 * 1024)} MB.");

    public static readonly Error InvalidContentType =
        Error.Validation("Document.InvalidContentType", "El tipo de contenido del fichero no es válido.");

    public static readonly Error NoExtractableText =
        Error.Validation("Document.NoExtractableText", "No se ha podido extraer texto del documento (¿está escaneado o vacío?).");

    public static readonly Error UploaderRequired =
        Error.Validation("Document.UploaderRequired", "Es obligatorio identificar al usuario que sube el documento.");

    public static readonly Error AlreadyProcessed =
        Error.BusinessRule("Document.AlreadyProcessed", "El documento ya ha sido procesado e indexado.");

    public static readonly Error NotInProcessingState =
        Error.BusinessRule("Document.NotInProcessingState", "El documento no se encuentra en estado de procesamiento.");

    public static readonly Error NotFailed =
        Error.BusinessRule("Document.NotFailed", "Solo se pueden reprocesar documentos cuya ingesta haya fallado.");

    public static readonly Error MaxAttemptsReached =
        Error.BusinessRule("Document.MaxAttemptsReached", $"Se ha alcanzado el máximo de {DocumentRules.MaxProcessingAttempts} intentos de procesamiento.");

    public static readonly Error NoChunks =
        Error.BusinessRule("Document.NoChunks", "El procesamiento no ha generado ningún fragmento indexable.");

    public static readonly Error ChunkContentEmpty =
        Error.Validation("DocumentChunk.ContentEmpty", "El contenido del fragmento no puede estar vacío.");

    public static readonly Error ChunkEmbeddingEmpty =
        Error.Validation("DocumentChunk.EmbeddingEmpty", "El fragmento debe incluir su vector de embedding.");

    public static readonly Error ChunkInvalidIndex =
        Error.Validation("DocumentChunk.InvalidIndex", "El índice del fragmento no puede ser negativo.");

    public static Error UnsupportedFileType(string extension) =>
        Error.Validation(
            "Document.UnsupportedFileType",
            $"El tipo de fichero '{extension}' no está soportado. Formatos admitidos: {string.Join(", ", DocumentRules.AllowedExtensions.Order())}.");

    public static Error CorruptFile(string fileName) =>
        Error.Validation("Document.CorruptFile", $"El fichero '{fileName}' está dañado o no se puede leer.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("Document.NotFound", $"No existe ningún documento con el identificador '{id}'.");

    public static Error Duplicate(string fileName) =>
        Error.Conflict("Document.Duplicate", $"El contenido de '{fileName}' ya existe en la base de conocimiento.");

    
    /// <summary>No se puede eliminar un documento mientras el worker lo está procesando.</summary>
    public static Error CannotDeleteWhileProcessing(Guid id) => Error.Conflict(
        "Document.CannotDeleteWhileProcessing",
        $"El documento '{id}' se está procesando y no puede eliminarse hasta que termine.");
}
