using DocuAI.Api;
using DocuAI.Api.Common;
using DocuAI.Application;
using DocuAI.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

// Captura fallos inesperados y los traduce a ProblemDetails (GlobalExceptionHandler).
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Documento OpenAPI en /openapi/v1.json
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

// Antes de la redirección HTTPS, la autenticación y el rate limiter: el preflight (OPTIONS)
// se responde aquí sin token, y las cabeceras CORS se aplican al iniciarse la respuesta, por lo
// que también llegan en los 401/403/429 y en los ProblemDetails del manejador de excepciones.
app.UseCors(CorsPolicies.Frontend);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Tras la autenticación: las particiones del limitador se basan en el usuario.
app.UseRateLimiter();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

await app.RunAsync();

/// <summary>Punto de entrada; parcial para habilitar pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
