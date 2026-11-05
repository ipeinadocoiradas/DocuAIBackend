namespace DocuAI.Domain.Documents;

/// <summary>Identificador fuertemente tipado de un <see cref="Document"/>.</summary>
/// <param name="Value">Valor GUID subyacente (v7, ordenable temporalmente).</param>
public readonly record struct DocumentId(Guid Value)
{
    /// <summary>Genera un nuevo identificador ordenable (UUID v7).</summary>
    public static DocumentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Identificador fuertemente tipado de un <see cref="DocumentChunk"/>.</summary>
/// <param name="Value">Valor GUID subyacente (v7, ordenable temporalmente).</param>
public readonly record struct DocumentChunkId(Guid Value)
{
    /// <summary>Genera un nuevo identificador ordenable (UUID v7).</summary>
    public static DocumentChunkId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
