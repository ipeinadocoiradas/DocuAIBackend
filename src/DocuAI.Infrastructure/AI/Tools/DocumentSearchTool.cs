using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;

namespace DocuAI.Infrastructure.AI.Tools;

/// <summary>
/// Herramienta de búsqueda en la base vectorial para el agente. Es una clase C# normal, sin
/// atributos propios del framework: se expone al modelo como <see cref="AIFunction"/> mediante
/// <see cref="AIFunctionFactory"/>, que genera el esquema JSON a partir de la firma y de los
/// atributos <see cref="DescriptionAttribute"/>.
/// <para>
/// Se instancia por petición con el Top K solicitado. Nunca lanza excepciones: los fallos se
/// devuelven como texto con un marcador para que el agente informe al usuario.
/// </para>
/// </summary>
/// <param name="retriever">Servicio de recuperación semántica.</param>
/// <param name="topK">Número máximo de fragmentos a devolver.</param>
/// <param name="minSimilarity">Similitud coseno mínima.</param>
internal sealed class DocumentSearchTool(IKnowledgeRetriever retriever, int topK, double minSimilarity)
{
    /// <summary>Nombre de la función tal como la ve el modelo.</summary>
    public const string FunctionName = "search_documents";

    /// <summary>Descripción de la función tal como la ve el modelo.</summary>
    public const string FunctionDescription =
        "Busca en la base documental corporativa los fragmentos más relevantes para responder a una pregunta. " +
        "Devuelve fragmentos etiquetados con su fuente. Úsala SIEMPRE antes de responder.";

    public const string NoResultsMarker = "SIN_RESULTADOS";

    public const string ErrorMarker = "ERROR_BUSQUEDA";

    /// <summary>
    /// Expone la búsqueda como función invocable por el modelo. El parámetro
    /// <see cref="CancellationToken"/> se enlaza automáticamente y no aparece en el esquema.
    /// </summary>
    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(SearchDocumentsAsync, FunctionName, FunctionDescription);

    /// <summary>Ejecuta la búsqueda y devuelve el contexto formateado para el prompt.</summary>
    /// <param name="query">Consulta de búsqueda en lenguaje natural.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public async Task<string> SearchDocumentsAsync(
        [Description("Consulta de búsqueda autocontenida en lenguaje natural, reformulada para maximizar la recuperación.")]
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return $"{ErrorMarker}: la consulta de búsqueda está vacía.";
        }

        var result = await retriever.RetrieveAsync(query, topK, minSimilarity, cancellationToken);

        if (result.IsFailure)
        {
            return $"{ErrorMarker}: {result.Error.Description}";
        }

        if (result.Value.Count == 0)
        {
            return $"{NoResultsMarker}: no hay fragmentos suficientemente relevantes para la consulta.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("<contexto>");

        foreach (var match in result.Value)
        {
            builder.Append(CultureInfo.InvariantCulture,
                    $"<fragmento fuente=\"{WebUtility.HtmlEncode(match.FileName)}\" indice=\"{match.ChunkIndex}\" similitud=\"{match.Similarity:0.000}\">")
                .AppendLine()
                .AppendLine(match.Content.Trim())
                .AppendLine("</fragmento>");
        }

        builder.AppendLine("</contexto>");
        return builder.ToString();
    }
}
