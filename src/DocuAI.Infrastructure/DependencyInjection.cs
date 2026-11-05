using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using DocuAI.Infrastructure.AI.Agents;
using DocuAI.Infrastructure.AI.Chat;
using DocuAI.Infrastructure.AI.Embeddings;
using DocuAI.Infrastructure.AI.Tools;
using DocuAI.Infrastructure.Identity;
using DocuAI.Infrastructure.Ingestion;
using DocuAI.Infrastructure.Options;
using DocuAI.Infrastructure.Persistence;
using DocuAI.Infrastructure.Persistence.Repositories;
using DocuAI.Infrastructure.Resilience;
using DocuAI.Infrastructure.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace DocuAI.Infrastructure;

/// <summary>Registro de servicios de la capa Infrastructure.</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra persistencia (EF Core + pgvector), IA (Ollama para embeddings y
        /// Microsoft Agent Framework + Proveedor para chat), la ingesta en segundo plano
        /// y la autenticación JWT.
        /// </summary>
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            AddPersistence(services, configuration);
            AddArtificialIntelligence(services, configuration);
            AddIngestion(services);
            AddIdentity(services, configuration);

            return services;
        }
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Error de configuración en el arranque (no es lógica de negocio).
            throw new InvalidOperationException(
                $"Falta la cadena de conexión 'ConnectionStrings:{DatabaseOptions.ConnectionStringName}'.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
            }));

        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentReadService, DocumentReadService>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("postgresql", tags: ["ready"]);

        // Debe registrarse antes que el worker de ingesta: los hosted services arrancan en orden.
        services.AddHostedService<DatabaseMigrator>();
    }

    private static void AddArtificialIntelligence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatProviderOptions>()
            .Bind(configuration.GetSection(ChatProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Embeddings: proveedor independiente del chat (Ollama local por defecto),
        // con su propio pipeline de resiliencia.
        services.AddHttpClient<IEmbeddingService, OpenAiCompatibleEmbeddingService>(ProviderHttpClientExtensions.EmbeddingsClientName)
            .ConfigureEmbeddingClient()
            .AddProviderResilience(
                "embeddings",
                serviceProvider => serviceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value.Resilience);

        // Cliente sin reintentos para el health check (debe responder rápido o fallar).
        services.AddHttpClient(ProviderHttpClientExtensions.EmbeddingsHealthClientName)
            .ConfigureEmbeddingClient();

        if (configuration.GetValue(
                $"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.HealthCheckEnabled)}",
                defaultValue: true))
        {
            services.AddHealthChecks()
                .AddCheck<EmbeddingProviderHealthCheck>("embeddings", tags: ["ready"]);
        }

        // Chat: cliente HTTP nombrado con resiliencia Polly v8, usado como transporte del SDK de OpenAI
        // (la API key la añade el SDK). Sobre él se construye el IChatClient que consume el agente.
        services.AddHttpClient(ProviderHttpClientExtensions.ChatClientName)
            .ConfigureChatProviderClient()
            .AddProviderResilience(
                "chat-provider",
                serviceProvider => serviceProvider.GetRequiredService<IOptions<ChatProviderOptions>>().Value.Resilience);

        // Scoped: cada petición obtiene un HttpClient de la factoría (rotación de handlers y DNS).
        services.AddScoped(ChatClientFactory.Create);

        services.AddScoped<IKnowledgeRetriever, KnowledgeRetriever>();
        services.AddScoped<IChatAgent, DocumentChatAgent>();

        services.AddSingleton<ITextExtractor, DocumentTextExtractor>();
        services.AddSingleton<ITextChunker, SemanticTextChunker>();
    }

    private static void AddIngestion(IServiceCollection services)
    {
        services.AddSingleton<DocumentIngestionQueue>();
        services.AddSingleton<IDocumentIngestionQueue>(serviceProvider =>
            serviceProvider.GetRequiredService<DocumentIngestionQueue>());

        services.AddHostedService<DocumentIngestionWorker>();
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DevelopmentUsersOptions>()
            .Bind(configuration.GetSection(DevelopmentUsersOptions.SectionName));

        services.AddSingleton<IJwtTokenProvider, JwtTokenProvider>();
        services.AddSingleton<IIdentityService, ConfigurationIdentityService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Se conservan los nombres de claim originales ("sub", "role"…).
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = DocuAIClaimTypes.Name,
                    RoleClaimType = DocuAIClaimTypes.Role
                };
            });
    }
}
