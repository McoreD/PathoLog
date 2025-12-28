using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteSourceFileRepository : ISourceFileRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteSourceFileRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SourceFile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, original_file_name, hash_sha256, stored_path, size_bytes, imported_utc
FROM source_file
WHERE id = $id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", id.ToString()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpsertAsync(SourceFile sourceFile, CancellationToken cancellationToken = default)
    {
        if (sourceFile.Id == Guid.Empty)
        {
            sourceFile.Id = Guid.NewGuid();
        }

        if (sourceFile.ImportedUtc == default)
        {
            sourceFile.ImportedUtc = DateTimeOffset.UtcNow;
        }

        const string sql = @"
INSERT INTO source_file (
    id, original_file_name, hash_sha256, stored_path, size_bytes, imported_utc
) VALUES (
    $id, $fileName, $hash, $storedPath, $sizeBytes, $importedUtc
)
ON CONFLICT(id) DO UPDATE SET
    original_file_name = excluded.original_file_name,
    hash_sha256 = excluded.hash_sha256,
    stored_path = excluded.stored_path,
    size_bytes = excluded.size_bytes,
    imported_utc = excluded.imported_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", sourceFile.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$fileName", sourceFile.OriginalFileName));
        command.Parameters.Add(new SqliteParameter("$hash", sourceFile.HashSha256));
        command.Parameters.Add(new SqliteParameter("$storedPath", sourceFile.StoredPath));
        command.Parameters.Add(new SqliteParameter("$sizeBytes", sourceFile.SizeBytes));
        command.Parameters.Add(new SqliteParameter("$importedUtc", SqliteValueMapper.ToDateTimeOffset(sourceFile.ImportedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SourceFile Map(DbDataReader reader)
    {
        return new SourceFile
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            OriginalFileName = reader.GetString(reader.GetOrdinal("original_file_name")),
            HashSha256 = reader.GetString(reader.GetOrdinal("hash_sha256")),
            StoredPath = reader.GetString(reader.GetOrdinal("stored_path")),
            SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
            ImportedUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("imported_utc")))
        };
    }
}
