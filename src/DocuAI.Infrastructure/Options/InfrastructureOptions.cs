using System.ComponentModel.DataAnnotations;

namespace DocuAI.Infrastructure.Options;

/// <summary>
/// Proveedor del modelo de chat. Admite cualquier endpoint compatible con la API de OpenAI
/// (Google Gemini, Groq, OpenRouter, Ollama…).
/// </summary>
public sealed class ChatProviderOptions
{
    public const string SectionName = "ChatProvider";

    /// <summary>URL base del endpoint compatible con OpenAI.</summary>
    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>API key del proveedor. Se guarda en user-secrets o en un almacén de secretos.</summary>
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Identificador del modelo de chat en el proveedor.</summary>
    [Required]
    public string Model { get; init; } = string.Empty;

    /// <summary>Cabeceras adicionales propias del proveedor </summary>
    public Dictionary<string, string> DefaultHeaders { get; init; } = [];

    /// <summary>Pipeline de resiliencia Polly v8 del cliente HTTP de chat.</summary>
    [Required]
    public HttpResilienceOptions Resilience { get; init; } = new();
}

/// <summary>
/// Estrategia de resiliencia HTTP reutilizable por proveedor: reintentos con backoff
/// exponencial (respetando Retry-After en 429), circuit breaker y timeouts.
/// </summary>
public sealed class HttpResilienceOptions
{
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 6;

    /// <summary>Retardo base del backoff exponencial (2s, 4s, 8s… con jitter).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Retardo máximo entre reintentos.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Tiempo máximo por intento (hasta recibir cabeceras de respuesta).</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Tiempo máximo total incluyendo todos los reintentos.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(6);

    /// <summary>Tiempo que el circuito permanece abierto tras detectar fallos sostenidos.</summary>
    public TimeSpan CircuitBreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Proveedor de embeddings con API compatible con OpenAI (<c>POST {BaseUrl}embeddings</c>).
/// Por defecto, Ollama local con <c>bge-m3</c>: gratuito, sin cuotas y sin que los
/// documentos salgan de la infraestructura propia. Es independiente del proveedor de chat.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>
    /// URL base compatible con OpenAI. Ollama: <c>http://localhost:11434/v1/</c>;    
    /// </summary>
    [Required, Url]
    public string BaseUrl { get; init; } = "http://localhost:11434/v1/";

    /// <summary>Clave de API opcional (Ollama no la requiere).</summary>
    public string? ApiKey { get; init; }

    /// <summary>Modelo de embeddings (en Ollama, el nombre usado en <c>ollama pull</c>).</summary>
    [Required]
    public string Model { get; init; } = "bge-m3";

    /// <summary>
    /// Dimensión del vector. Debe coincidir con el modelo y con la columna <c>vector(n)</c>;
    /// cambiarla exige una nueva migración y reindexar.
    /// </summary>
    [Range(16, 4000)]
    public int Dimensions { get; init; } = 1024;

    /// <summary>
    /// Envía el parámetro <c>dimensions</c> al proveedor (solo para modelos que admiten
    /// reducción de dimensionalidad, como text-embedding-3-*).
    /// </summary>
    public bool SendDimensionsParameter { get; init; }

    /// <summary>Incluye la comprobación del proveedor y del modelo en <c>/health/ready</c>.</summary>
    public bool HealthCheckEnabled { get; init; } = true;

    /// <summary>
    /// Resiliencia del proveedor local: pocos reintentos y timeout por intento amplio
    /// (la primera petición carga el modelo en memoria y en CPU un lote puede tardar).
    /// </summary>
    [Required]
    public HttpResilienceOptions Resilience { get; init; } = new()
    {
        MaxRetryAttempts = 3,
        BaseDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(10),
        AttemptTimeout = TimeSpan.FromMinutes(2),
        TotalTimeout = TimeSpan.FromMinutes(5),
        CircuitBreakDuration = TimeSpan.FromSeconds(30)
    };
}

/// <summary>Modo de recuperación del agente.</summary>
public enum RetrievalMode
{
    /// <summary>El modelo decide cuándo invocar la herramienta de búsqueda (function calling).</summary>
    FunctionCalling = 0,

    /// <summary>
    /// La búsqueda se ejecuta antes de llamar al modelo y el contexto se inyecta en el prompt.
    /// Útil con modelos gratuitos sin soporte fiable de herramientas.
    /// </summary>
    PreRetrieval = 1
}

/// <summary>Configuración del agente RAG (Microsoft Agent Framework).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public RetrievalMode RetrievalMode { get; init; } = RetrievalMode.FunctionCalling;

    /// <summary>Similitud coseno mínima para considerar relevante un fragmento.</summary>
    [Range(-1.0, 1.0)]
    public double MinSimilarity { get; init; } = 0.25;

    [Range(0.0, 2.0)]
    public double Temperature { get; init; } = 0.1;

    [Range(64, 32_000)]
    public int MaxOutputTokens { get; init; } = 1024;

    /// <summary>
    /// Máximo de rondas modelo ↔ herramienta por pregunta en modo <see cref="RetrievalMode.FunctionCalling"/>.
    /// Cada ronda es una petición al modelo y consume cuota (RPM) del proveedor.
    /// </summary>
    [Range(1, 10)]
    public int MaxToolIterations { get; init; } = 3;
}

/// <summary>Opciones de base de datos.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public const string ConnectionStringName = "DocuAI";

    /// <summary>Aplica migraciones pendientes al arrancar (recomendado solo en desarrollo).</summary>
    public bool ApplyMigrationsOnStartup { get; init; }
}

/// <summary>Parámetros de emisión y validación de JWT.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Clave simétrica HMAC-SHA256 (mínimo 32 caracteres). Guardar como secreto.</summary>
    [Required, MinLength(32, ErrorMessage = "Authentication:Jwt:SigningKey debe tener al menos 32 caracteres.")]
    public string SigningKey { get; init; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>Usuario local para el emisor de tokens de desarrollo.</summary>
public sealed class DevelopmentUser
{
    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string[] Roles { get; init; } = [];
}

/// <summary>Emisor de tokens local (solo desarrollo/pruebas).</summary>
public sealed class DevelopmentUsersOptions
{
    public const string SectionName = "Authentication:DevelopmentUsers";

    public bool Enabled { get; init; }

    public DevelopmentUser[] Users { get; init; } = [];
}
