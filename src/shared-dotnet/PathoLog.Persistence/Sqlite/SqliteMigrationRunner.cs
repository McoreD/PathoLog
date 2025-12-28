using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteMigrationRunner : IMigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly string _migrationsDirectory;

    public SqliteMigrationRunner(IDbConnectionFactory connectionFactory, string migrationsDirectory)
    {
        _connectionFactory = connectionFactory;
        _migrationsDirectory = migrationsDirectory;
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_migrationsDirectory))
        {
            throw new DirectoryNotFoundException($"Migrations directory not found: {_migrationsDirectory}");
        }

        var migrationFiles = Directory.EnumerateFiles(_migrationsDirectory, "*.sql")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var file in migrationFiles)
        {
            var name = Path.GetFileName(file);
            if (await MigrationAppliedAsync(connection, name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var sql = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, sql, cancellationToken).ConfigureAwait(false);
            await RecordMigrationAsync(connection, name, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureMigrationsTableAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    id TEXT PRIMARY KEY,
    applied_utc TEXT NOT NULL
);";
        await ExecuteSqlAsync(connection, sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> MigrationAppliedAsync(DbConnection connection, string name, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM schema_migrations WHERE id = $id;";
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var param = command.CreateParameter();
        param.ParameterName = "$id";
        param.Value = name;
        command.Parameters.Add(param);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task RecordMigrationAsync(DbConnection connection, string name, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO schema_migrations (id, applied_utc) VALUES ($id, $appliedUtc);";
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var idParam = command.CreateParameter();
        idParam.ParameterName = "$id";
        idParam.Value = name;
        var appliedParam = command.CreateParameter();
        appliedParam.ParameterName = "$appliedUtc";
        appliedParam.Value = SqliteValueMapper.ToDateTimeOffset(DateTimeOffset.UtcNow);
        command.Parameters.Add(idParam);
        command.Parameters.Add(appliedParam);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteSqlAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
