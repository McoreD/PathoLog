using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteReferenceRangeRepository : IReferenceRangeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteReferenceRangeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReferenceRange?> GetByResultAsync(Guid resultId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, result_id, low, high, unit, text
FROM reference_range
WHERE result_id = $resultId;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$resultId", resultId.ToString()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpsertAsync(ReferenceRange referenceRange, CancellationToken cancellationToken = default)
    {
        if (referenceRange.Id == Guid.Empty)
        {
            referenceRange.Id = Guid.NewGuid();
        }

        const string sql = @"
INSERT INTO reference_range (
    id, result_id, low, high, unit, text
) VALUES (
    $id, $resultId, $low, $high, $unit, $text
)
ON CONFLICT(id) DO UPDATE SET
    result_id = excluded.result_id,
    low = excluded.low,
    high = excluded.high,
    unit = excluded.unit,
    text = excluded.text;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", referenceRange.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$resultId", referenceRange.ResultId.ToString()));
        command.Parameters.Add(new SqliteParameter("$low", (object?)referenceRange.Low ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$high", (object?)referenceRange.High ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$unit", (object?)referenceRange.Unit ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$text", (object?)referenceRange.Text ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ReferenceRange Map(DbDataReader reader)
    {
        return new ReferenceRange
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ResultId = Guid.Parse(reader.GetString(reader.GetOrdinal("result_id"))),
            Low = reader.IsDBNull(reader.GetOrdinal("low")) ? null : reader.GetDecimal(reader.GetOrdinal("low")),
            High = reader.IsDBNull(reader.GetOrdinal("high")) ? null : reader.GetDecimal(reader.GetOrdinal("high")),
            Unit = reader.IsDBNull(reader.GetOrdinal("unit")) ? null : reader.GetString(reader.GetOrdinal("unit")),
            Text = reader.IsDBNull(reader.GetOrdinal("text")) ? null : reader.GetString(reader.GetOrdinal("text"))
        };
    }
}
