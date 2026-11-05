using System.Collections.Frozen;

namespace DocuAI.Domain.Documents;

/// <summary>
/// Reglas de negocio invariantes relativas a los documentos de la base de conocimiento.
/// </summary>
public static class DocumentRules
{
    /// <summary>Tamaño máximo admitido por fichero (20 MB).</summary>
    public const long MaxFileSizeBytes = 20L * 1024 * 1024;

    /// <summary>Longitud máxima del nombre de fichero.</summary>
    public const int MaxFileNameLength = 255;

    /// <summary>Longitud máxima del tipo MIME.</summary>
    public const int MaxContentTypeLength = 127;

    /// <summary>Número máximo de intentos de procesamiento antes de requerir intervención.</summary>
    public const int MaxProcessingAttempts = 5;

    /// <summary>Longitud máxima del motivo de fallo persistido.</summary>
    public const int MaxFailureReasonLength = 2000;

    /// <summary>Extensiones de fichero admitidas para la ingesta (en minúsculas, con punto).</summary>
    public static readonly FrozenSet<string> AllowedExtensions =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase, [".pdf", ".txt", ".md"]);
}
