namespace DocuAI.Domain.Documents;

/// <summary>Ciclo de vida de la ingesta de un documento.</summary>
public enum DocumentStatus
{
    /// <summary>Subido y pendiente de entrar en la cola de ingesta.</summary>
    Pending = 0,

    /// <summary>Fragmentando y generando embeddings.</summary>
    Processing = 1,

    /// <summary>Indexado y disponible para el agente RAG.</summary>
    Processed = 2,

    /// <summary>La ingesta ha fallado; puede reprocesarse.</summary>
    Failed = 3
}
