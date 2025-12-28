using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteReviewTaskRepository : IReviewTaskRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteReviewTaskRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ReviewTask>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, report_id, field_path, reason, status, created_utc, resolved_utc
FROM review_task
WHERE status IN ('Open', 'InReview')
ORDER BY created_utc ASC;";

        var results = new List<ReviewTask>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(ReviewTask task, CancellationToken cancellationToken = default)
    {
        if (task.Id == Guid.Empty)
        {
            task.Id = Guid.NewGuid();
        }

        if (task.CreatedUtc == default)
        {
            task.CreatedUtc = DateTimeOffset.UtcNow;
        }

        const string sql = @"
INSERT INTO review_task (
    id, report_id, field_path, reason, status, created_utc, resolved_utc
) VALUES (
    $id, $reportId, $fieldPath, $reason, $status, $createdUtc, $resolvedUtc
)
ON CONFLICT(id) DO UPDATE SET
    report_id = excluded.report_id,
    field_path = excluded.field_path,
    reason = excluded.reason,
    status = excluded.status,
    created_utc = excluded.created_utc,
    resolved_utc = excluded.resolved_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", task.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$reportId", task.ReportId.ToString()));
        command.Parameters.Add(new SqliteParameter("$fieldPath", task.FieldPath));
        command.Parameters.Add(new SqliteParameter("$reason", task.Reason));
        command.Parameters.Add(new SqliteParameter("$status", task.Status.ToString()));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(task.CreatedUtc)));
        command.Parameters.Add(new SqliteParameter("$resolvedUtc", task.ResolvedUtc.HasValue
            ? SqliteValueMapper.ToDateTimeOffset(task.ResolvedUtc.Value)
            : DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ReviewTask Map(DbDataReader reader)
    {
        return new ReviewTask
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ReportId = Guid.Parse(reader.GetString(reader.GetOrdinal("report_id"))),
            FieldPath = reader.GetString(reader.GetOrdinal("field_path")),
            Reason = reader.GetString(reader.GetOrdinal("reason")),
            Status = ParseStatus(reader.GetString(reader.GetOrdinal("status"))),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("created_utc"))),
            ResolvedUtc = reader.IsDBNull(reader.GetOrdinal("resolved_utc"))
                ? null
                : SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("resolved_utc")))
        };
    }

    private static ReviewTaskStatus ParseStatus(string value)
    {
        return Enum.TryParse<ReviewTaskStatus>(value, out var parsed) ? parsed : ReviewTaskStatus.Open;
    }
}
