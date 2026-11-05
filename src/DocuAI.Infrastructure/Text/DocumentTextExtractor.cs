using System.Text;
using System.Text.RegularExpressions;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocuAI.Infrastructure.Text;

/// <summary>
/// Extrae texto plano de PDF (PdfPig, respetando el orden de lectura), Markdown y texto.
/// Los ficheros corruptos se devuelven como <see cref="Result"/> fallido.
/// </summary>
internal sealed partial class DocumentTextExtractor(ILogger<DocumentTextExtractor> logger) : ITextExtractor
{
    public async Task<Result<string>> ExtractAsync(
        Stream content,
        FileName fileName,
        CancellationToken cancellationToken = default)
    {
        Result<string> textResult = fileName.Extension switch
        {
            ".pdf" => await ExtractPdfAsync(content, fileName, cancellationToken),
            ".txt" or ".md" => await ExtractPlainTextAsync(content, cancellationToken),
            _ => DocumentErrors.UnsupportedFileType(fileName.Extension)
        };

        if (textResult.IsFailure)
        {
            return textResult;
        }

        var normalized = Normalize(textResult.Value);

        return string.IsNullOrWhiteSpace(normalized)
            ? DocumentErrors.NoExtractableText
            : normalized;
    }

    private async Task<Result<string>> ExtractPdfAsync(Stream content, FileName fileName, CancellationToken cancellationToken)
    {
        // PdfPig necesita un flujo con posicionamiento: se copia a memoria (máx. 20 MB por regla de negocio).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        try
        {
            using var pdf = PdfDocument.Open(buffer);
            var builder = new StringBuilder();

            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine(ContentOrderTextExtractor.GetText(page)).AppendLine();
            }

            return builder.ToString();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Formato de PDF inválido o cifrado: es un problema del fichero, no de la infraestructura.
            LogCorruptPdf(logger, exception, fileName.Value);
            return DocumentErrors.CorruptFile(fileName.Value);
        }
    }

    private static async Task<Result<string>> ExtractPlainTextAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Unifica saltos de línea, elimina caracteres de control y colapsa espacios.</summary>
    private static string Normalize(string text)
    {
        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var withoutControl = ControlCharsRegex().Replace(unified, string.Empty);
        var collapsedSpaces = HorizontalWhitespaceRegex().Replace(withoutControl, " ");
        return ExcessiveNewLinesRegex().Replace(collapsedSpaces, "\n\n").Trim();
    }

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")]
    private static partial Regex ControlCharsRegex();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)+")]
    private static partial Regex ExcessiveNewLinesRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer el PDF '{FileName}'")]
    private static partial void LogCorruptPdf(ILogger logger, Exception exception, string fileName);
}
