using System.IO;
using System.Linq;
using System.Collections.Generic;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

public static class Migrations
{
    public static async Task ApplyAsync(string connectionString, string sqlDirectory, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Skipping migrations because connection string is empty.");
            return;
        }

        if (!Directory.Exists(sqlDirectory))
        {
            logger.LogWarning("SQL directory '{SqlDirectory}' not found; skipping migrations.", sqlDirectory);
            return;
        }

        var files = Directory.GetFiles(sqlDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            logger.LogInformation("No SQL migration files found in {SqlDirectory}; nothing to apply.", sqlDirectory);
            return;
        }

        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();

        await using var tx = await db.BeginTransactionAsync();
        await db.ExecuteAsync(
            "create table if not exists schema_migrations(version text primary key, applied_at timestamptz not null default now());",
            transaction: tx);

        var applied = (await db.QueryAsync<string>("select version from schema_migrations", transaction: tx))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var version = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            if (applied.Contains(version))
            {
                logger.LogInformation("Migration {Version} already applied; skipping.", version);
                continue;
            }

            var sql = await File.ReadAllTextAsync(file);
            if (string.IsNullOrWhiteSpace(sql))
            {
                logger.LogWarning("Migration {Version} is empty; skipping.", version);
                continue;
            }

            logger.LogInformation("Applying migration {Version}...", version);
            await db.ExecuteAsync(sql, transaction: tx);
            await db.ExecuteAsync(
                "insert into schema_migrations(version) values(@Version);",
                new { Version = version },
                transaction: tx);
            logger.LogInformation("Migration {Version} applied.", version);
        }

        await tx.CommitAsync();
    }
}
