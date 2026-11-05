using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;

namespace DocuAI.Application.Abstractions.Services;

/// <summary>Extrae texto plano de un fichero subido.</summary>
public interface ITextExtractor
{
    /// <param name="content">Flujo con el contenido del fichero.</param>
    /// <param name="fileName">Nombre validado del fichero (determina el formato).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<Result<string>> ExtractAsync(Stream content, FileName fileName, CancellationToken cancellationToken = default);
}

/// <summary>Fragmento de texto resultante de la fragmentación semántica.</summary>
/// <param name="Index">Posición (base 0).</param>
/// <param name="Text">Contenido del fragmento.</param>
/// <param name="TokenCount">Número de tokens del fragmento.</param>
public sealed record TextChunk(int Index, string Text, int TokenCount);

/// <summary>Divide un texto en fragmentos semánticos con solapamiento, medidos en tokens.</summary>
public interface ITextChunker
{
    IReadOnlyList<TextChunk> Split(string text, int maxTokensPerChunk, int overlapTokens);
}

/// <summary>Genera embeddings vectoriales para textos.</summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Genera un embedding por cada entrada, respetando el orden.
    /// Los fallos del proveedor se devuelven como <see cref="Result"/> fallido.
    /// </summary>
    Task<Result<IReadOnlyList<float[]>>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}

/// <summary>Agente conversacional RAG fundamentado en la base documental.</summary>
public interface IChatAgent
{
    /// <summary>
    /// Genera la respuesta token a token. La secuencia es perezosa: la llamada al
    /// modelo se produce al enumerarla.
    /// </summary>
    IAsyncEnumerable<string> StreamAnswerAsync(string question, int topK, CancellationToken cancellationToken = default);
}

/// <summary>Cola de documentos pendientes de ingesta en segundo plano.</summary>
public interface IDocumentIngestionQueue
{
    ValueTask EnqueueAsync(DocumentId documentId, CancellationToken cancellationToken = default);
}

/// <summary>Identidad del usuario que realiza la petición actual.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Identificador estable del usuario (claim <c>sub</c>).</summary>
    string? UserId { get; }

    string? UserName { get; }
}

/// <summary>Usuario autenticado con sus roles.</summary>
public sealed record AuthenticatedUser(string UserId, string UserName, IReadOnlyCollection<string> Roles);

/// <summary>Valida credenciales contra el almacén de identidades.</summary>
public interface IIdentityService
{
    Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default);
}

/// <summary>Token de acceso emitido.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>Emite tokens JWT firmados.</summary>
public interface IJwtTokenProvider
{
    AccessToken CreateToken(AuthenticatedUser user);
}
