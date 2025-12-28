using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteCommentRepository : ICommentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteCommentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Comment>> ListByReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, report_id, category, text, created_utc
FROM comment
WHERE report_id = $reportId
ORDER BY created_utc ASC;";

        var results = new List<Comment>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$reportId", reportId.ToString()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        if (comment.Id == Guid.Empty)
        {
            comment.Id = Guid.NewGuid();
        }

        if (comment.CreatedUtc == default)
        {
            comment.CreatedUtc = DateTimeOffset.UtcNow;
        }

        const string sql = @"
INSERT INTO comment (
    id, report_id, category, text, created_utc
) VALUES (
    $id, $reportId, $category, $text, $createdUtc
)
ON CONFLICT(id) DO UPDATE SET
    report_id = excluded.report_id,
    category = excluded.category,
    text = excluded.text,
    created_utc = excluded.created_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", comment.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$reportId", comment.ReportId.ToString()));
        command.Parameters.Add(new SqliteParameter("$category", comment.Category));
        command.Parameters.Add(new SqliteParameter("$text", comment.Text));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(comment.CreatedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Comment Map(DbDataReader reader)
    {
        return new Comment
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ReportId = Guid.Parse(reader.GetString(reader.GetOrdinal("report_id"))),
            Category = reader.GetString(reader.GetOrdinal("category")),
            Text = reader.GetString(reader.GetOrdinal("text")),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("created_utc")))
        };
    }
}
