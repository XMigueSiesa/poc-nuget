using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pos.Infrastructure.Postgres;

public static class MigrationRunner
{
    /// <summary>
    /// Ensures the schema and tables for this specific DbContext exist.
    /// Safe to call for multiple contexts sharing the same database — each
    /// context manages its own schema independently.
    /// </summary>
    public static async Task EnsureCreatedAsync<TContext>(
        IServiceProvider services,
        CancellationToken ct = default) where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        logger.LogInformation("Ensuring database created for {Context}...", typeof(TContext).Name);

        // 1. Create the database if it doesn't exist yet
        await db.Database.EnsureCreatedAsync(ct);

        // 2. Create schema explicitly (EF's CreateTables won't auto-create it)
        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
            // Schema name comes from our own model config — safe to use directly
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", ct);
#pragma warning restore EF1002
        }

        var creator = db.GetService<IRelationalDatabaseCreator>();

        // 3. Create this context's tables directly — bypasses EF's global
        //    HasTables() check that would skip creation if OTHER schemas exist.
        try
        {
            await creator.CreateTablesAsync(ct);
        }
        catch (Exception ex) when (IsAlreadyExistsError(ex))
        {
            logger.LogDebug("Tables already exist for {Context}, skipping", typeof(TContext).Name);
        }

        logger.LogInformation("Database ready for {Context}", typeof(TContext).Name);
    }

    public static async Task RunMigrationsAsync<TContext>(
        IServiceProvider services,
        CancellationToken ct = default) where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        logger.LogInformation("Running migrations for {Context}...", typeof(TContext).Name);
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Migrations complete for {Context}", typeof(TContext).Name);
    }

    // PostgreSQL 42P07 = duplicate_table
    private static bool IsAlreadyExistsError(Exception ex) =>
        ex.Message.Contains("42P07") ||
        ex.Message.Contains("already exists") ||
        (ex.InnerException?.Message.Contains("42P07") ?? false) ||
        (ex.InnerException?.Message.Contains("already exists") ?? false);
}
