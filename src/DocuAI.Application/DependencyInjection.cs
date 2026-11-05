using DocuAI.Application.Behaviors;
using DocuAI.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocuAI.Application;

/// <summary>Registro de servicios de la capa Application.</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra MediatR (con los behaviors de logging y validación), los validadores
        /// y las opciones de la capa de aplicación.
        /// </summary>
        public IServiceCollection AddApplication(IConfiguration configuration)
        {
            var assembly = typeof(DependencyInjection).Assembly;

            services.AddMediatR(config =>
            {
                config.RegisterServicesFromAssembly(assembly);

                // El orden importa: el logging envuelve a la validación.
                config.AddOpenBehavior(typeof(LoggingBehavior<,>));
                config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

            services.AddOptions<IngestionOptions>()
                .Bind(configuration.GetSection(IngestionOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<ChatOptions>()
                .Bind(configuration.GetSection(ChatOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.TryAddSingleton(TimeProvider.System);

            return services;
        }
    }
}
