using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteMappingDictionaryRepository : IMappingDictionaryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteMappingDictionaryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MappingDictionaryEntry?> TryResolveAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, source_name, analyte_short_code, mapping_method, mapping_confidence, last_confirmed_utc
FROM mapping_dictionary
WHERE source_name = $sourceName;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$sourceName", sourceName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpsertAsync(MappingDictionaryEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }

        const string sql = @"
INSERT INTO mapping_dictionary (
    id, source_name, analyte_short_code, mapping_method, mapping_confidence, last_confirmed_utc
) VALUES (
    $id, $sourceName, $shortCode, $method, $confidence, $confirmedUtc
)
ON CONFLICT(source_name) DO UPDATE SET
    analyte_short_code = excluded.analyte_short_code,
    mapping_method = excluded.mapping_method,
    mapping_confidence = excluded.mapping_confidence,
    last_confirmed_utc = excluded.last_confirmed_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", entry.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$sourceName", entry.SourceName));
        command.Parameters.Add(new SqliteParameter("$shortCode", entry.AnalyteShortCode));
        command.Parameters.Add(new SqliteParameter("$method", entry.MappingMethod.ToString()));
        command.Parameters.Add(new SqliteParameter("$confidence", (object?)entry.MappingConfidence ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$confirmedUtc", entry.LastConfirmedUtc.HasValue
            ? SqliteValueMapper.ToDateTimeOffset(entry.LastConfirmedUtc.Value)
            : DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MappingDictionaryEntry Map(DbDataReader reader)
    {
        return new MappingDictionaryEntry
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            SourceName = reader.GetString(reader.GetOrdinal("source_name")),
            AnalyteShortCode = reader.GetString(reader.GetOrdinal("analyte_short_code")),
            MappingMethod = ParseMappingMethod(reader.GetString(reader.GetOrdinal("mapping_method"))),
            MappingConfidence = reader.IsDBNull(reader.GetOrdinal("mapping_confidence"))
                ? null
                : reader.GetDouble(reader.GetOrdinal("mapping_confidence")),
            LastConfirmedUtc = reader.IsDBNull(reader.GetOrdinal("last_confirmed_utc"))
                ? null
                : SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("last_confirmed_utc")))
        };
    }

    private static MappingMethod ParseMappingMethod(string value)
    {
        return Enum.TryParse<MappingMethod>(value, out var parsed) ? parsed : MappingMethod.Deterministic;
    }
}
