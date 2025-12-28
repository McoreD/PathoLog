using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteResultRepository : IResultRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteResultRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Result>> ListByReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, report_id, subpanel_id, analyte_name, analyte_short_code, value_type,
       value_number, value_text, unit, flag, source_anchor, extraction_confidence, created_utc
FROM result
WHERE report_id = $reportId
ORDER BY created_utc ASC;";

        var results = new List<Result>();
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

    public async Task UpsertAsync(Result result, CancellationToken cancellationToken = default)
    {
        if (result.Id == Guid.Empty)
        {
            result.Id = Guid.NewGuid();
        }

        if (result.CreatedUtc == default)
        {
            result.CreatedUtc = DateTimeOffset.UtcNow;
        }

        const string sql = @"
INSERT INTO result (
    id, report_id, subpanel_id, analyte_name, analyte_short_code,
    value_type, value_number, value_text, unit, flag,
    source_anchor, extraction_confidence, created_utc
) VALUES (
    $id, $reportId, $subpanelId, $analyteName, $analyteShortCode,
    $valueType, $valueNumber, $valueText, $unit, $flag,
    $sourceAnchor, $extractionConfidence, $createdUtc
)
ON CONFLICT(id) DO UPDATE SET
    report_id = excluded.report_id,
    subpanel_id = excluded.subpanel_id,
    analyte_name = excluded.analyte_name,
    analyte_short_code = excluded.analyte_short_code,
    value_type = excluded.value_type,
    value_number = excluded.value_number,
    value_text = excluded.value_text,
    unit = excluded.unit,
    flag = excluded.flag,
    source_anchor = excluded.source_anchor,
    extraction_confidence = excluded.extraction_confidence;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", result.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$reportId", result.ReportId.ToString()));
        command.Parameters.Add(new SqliteParameter("$subpanelId", (object?)(result.SubpanelId?.ToString()) ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$analyteName", result.AnalyteName));
        command.Parameters.Add(new SqliteParameter("$analyteShortCode", result.AnalyteShortCode));
        command.Parameters.Add(new SqliteParameter("$valueType", result.ValueType.ToString().ToLowerInvariant()));
        command.Parameters.Add(new SqliteParameter("$valueNumber", (object?)result.ValueNumber ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$valueText", (object?)result.ValueText ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$unit", (object?)result.Unit ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$flag", result.Flag == ResultFlag.None ? DBNull.Value : result.Flag.ToString()));
        command.Parameters.Add(new SqliteParameter("$sourceAnchor", (object?)result.SourceAnchor ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$extractionConfidence", (object?)result.ExtractionConfidence ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(result.CreatedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Result Map(DbDataReader reader)
    {
        return new Result
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ReportId = Guid.Parse(reader.GetString(reader.GetOrdinal("report_id"))),
            SubpanelId = reader.IsDBNull(reader.GetOrdinal("subpanel_id"))
                ? null
                : Guid.Parse(reader.GetString(reader.GetOrdinal("subpanel_id"))),
            AnalyteName = reader.GetString(reader.GetOrdinal("analyte_name")),
            AnalyteShortCode = reader.GetString(reader.GetOrdinal("analyte_short_code")),
            ValueType = ParseValueType(reader.GetString(reader.GetOrdinal("value_type"))),
            ValueNumber = reader.IsDBNull(reader.GetOrdinal("value_number")) ? null : reader.GetDecimal(reader.GetOrdinal("value_number")),
            ValueText = reader.IsDBNull(reader.GetOrdinal("value_text")) ? null : reader.GetString(reader.GetOrdinal("value_text")),
            Unit = reader.IsDBNull(reader.GetOrdinal("unit")) ? null : reader.GetString(reader.GetOrdinal("unit")),
            Flag = ParseFlag(reader.IsDBNull(reader.GetOrdinal("flag")) ? null : reader.GetString(reader.GetOrdinal("flag"))),
            SourceAnchor = reader.IsDBNull(reader.GetOrdinal("source_anchor")) ? null : reader.GetString(reader.GetOrdinal("source_anchor")),
            ExtractionConfidence = reader.IsDBNull(reader.GetOrdinal("extraction_confidence")) ? null : reader.GetDouble(reader.GetOrdinal("extraction_confidence")),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("created_utc")))
        };
    }

    private static ResultValueType ParseValueType(string value)
    {
        return value switch
        {
            "numeric" => ResultValueType.Numeric,
            "qualitative" => ResultValueType.Qualitative,
            "text" => ResultValueType.Text,
            _ => ResultValueType.Text
        };
    }

    private static ResultFlag ParseFlag(string? value)
    {
        return value switch
        {
            "Low" => ResultFlag.Low,
            "High" => ResultFlag.High,
            "Critical" => ResultFlag.Critical,
            "Abnormal" => ResultFlag.Abnormal,
            _ => ResultFlag.None
        };
    }
}
