using System.Security.Cryptography;
using System.Text;
using DocuAI.Domain.Common;

namespace DocuAI.Domain.Documents;

/// <summary>
/// Nombre de fichero validado: sin rutas, con extensión admitida y longitud acotada.
/// </summary>
public sealed record FileName
{
    private FileName(string value)
    {
        Value = value;
        Extension = Path.GetExtension(value).ToLowerInvariant();
    }

    /// <summary>Nombre del fichero (sin directorios).</summary>
    public string Value { get; }

    /// <summary>Extensión en minúsculas incluyendo el punto (p. ej. <c>.pdf</c>).</summary>
    public string Extension { get; }

    /// <summary>Crea un nombre de fichero aplicando las reglas de negocio.</summary>
    public static Result<FileName> Create(string? rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return DocumentErrors.FileNameEmpty;
        }

        // Elimina cualquier componente de ruta para prevenir path traversal (p. ej. "..\..\a.pdf").
        var sanitized = Path.GetFileName(rawFileName.Replace('\\', '/').Trim());

        if (sanitized.Length == 0)
        {
            return DocumentErrors.FileNameEmpty;
        }

        if (sanitized.Length > DocumentRules.MaxFileNameLength)
        {
            return DocumentErrors.FileNameTooLong;
        }

        var extension = Path.GetExtension(sanitized);

        return DocumentRules.AllowedExtensions.Contains(extension)
            ? new FileName(sanitized)
            : DocumentErrors.UnsupportedFileType(string.IsNullOrEmpty(extension) ? "(sin extensión)" : extension);
    }

    /// <summary>
    /// Reconstruye el valor desde persistencia, donde ya fue validado. Uso exclusivo de infraestructura.
    /// </summary>
    public static FileName FromPersistence(string value) => new(value);

    public override string ToString() => Value;
}

/// <summary>
/// Huella SHA-256 (hexadecimal) del texto extraído. Permite detectar documentos duplicados.
/// </summary>
/// <param name="Value">Hash en hexadecimal en minúsculas (64 caracteres).</param>
public readonly record struct ContentHash(string Value)
{
    /// <summary>Longitud del hash SHA-256 en hexadecimal.</summary>
    public const int Length = 64;

    /// <summary>Calcula la huella del texto normalizado.</summary>
    public static ContentHash FromText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text.Trim());
        return new ContentHash(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public override string ToString() => Value;
}
