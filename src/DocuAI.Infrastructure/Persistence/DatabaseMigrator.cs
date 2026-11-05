using DocuAI.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuAI.Infrastructure.Persistence;

/// <summary>
/// Aplica las migraciones pendientes al arrancar cuando está habilitado
/// (<c>Database:ApplyMigrationsOnStartup</c>). Se registra antes del worker de ingesta.
/// </summary>
internal sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<DatabaseMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!databaseOptions.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            LogUpToDate(logger);
            return;
        }

        LogApplying(logger, pending.Length, string.Join(", ", pending));
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Base de datos actualizada; no hay migraciones pendientes")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aplicando {Count} migraciones: {Migrations}")]
    private static partial void LogApplying(ILogger logger, int count, string migrations);
}
