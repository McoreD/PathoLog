using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteReportRepository : IReportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteReportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Report?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, patient_id, source_file_id, report_date, laboratory_name, panel_name,
       specimen_description, created_utc, updated_utc
FROM report
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

    public async Task<IReadOnlyList<Report>> ListByPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, patient_id, source_file_id, report_date, laboratory_name, panel_name,
       specimen_description, created_utc, updated_utc
FROM report
WHERE patient_id = $patientId
ORDER BY report_date DESC, created_utc DESC;";

        var results = new List<Report>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$patientId", patientId.ToString()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(Report report, CancellationToken cancellationToken = default)
    {
        if (report.Id == Guid.Empty)
        {
            report.Id = Guid.NewGuid();
        }

        if (report.CreatedUtc == default)
        {
            report.CreatedUtc = DateTimeOffset.UtcNow;
        }

        report.UpdatedUtc = DateTimeOffset.UtcNow;

        const string sql = @"
INSERT INTO report (
    id, patient_id, source_file_id, report_date, laboratory_name,
    panel_name, specimen_description, created_utc, updated_utc
) VALUES (
    $id, $patientId, $sourceFileId, $reportDate, $laboratoryName,
    $panelName, $specimenDescription, $createdUtc, $updatedUtc
)
ON CONFLICT(id) DO UPDATE SET
    patient_id = excluded.patient_id,
    source_file_id = excluded.source_file_id,
    report_date = excluded.report_date,
    laboratory_name = excluded.laboratory_name,
    panel_name = excluded.panel_name,
    specimen_description = excluded.specimen_description,
    updated_utc = excluded.updated_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", report.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$patientId", report.PatientId.ToString()));
        command.Parameters.Add(new SqliteParameter("$sourceFileId", (object?)(report.SourceFileId?.ToString()) ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$reportDate", (object?)SqliteValueMapper.ToDateOnly(report.ReportDate) ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$laboratoryName", (object?)report.LaboratoryName ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$panelName", (object?)report.PanelName ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$specimenDescription", (object?)report.SpecimenDescription ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(report.CreatedUtc)));
        command.Parameters.Add(new SqliteParameter("$updatedUtc", SqliteValueMapper.ToDateTimeOffset(report.UpdatedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Report Map(DbDataReader reader)
    {
        var created = reader.GetString(reader.GetOrdinal("created_utc"));
        var updated = reader.GetString(reader.GetOrdinal("updated_utc"));

        return new Report
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            PatientId = Guid.Parse(reader.GetString(reader.GetOrdinal("patient_id"))),
            SourceFileId = reader.IsDBNull(reader.GetOrdinal("source_file_id"))
                ? null
                : Guid.Parse(reader.GetString(reader.GetOrdinal("source_file_id"))),
            ReportDate = SqliteValueMapper.FromDateOnly(reader.IsDBNull(reader.GetOrdinal("report_date")) ? null : reader.GetString(reader.GetOrdinal("report_date"))),
            LaboratoryName = reader.IsDBNull(reader.GetOrdinal("laboratory_name")) ? null : reader.GetString(reader.GetOrdinal("laboratory_name")),
            PanelName = reader.IsDBNull(reader.GetOrdinal("panel_name")) ? null : reader.GetString(reader.GetOrdinal("panel_name")),
            SpecimenDescription = reader.IsDBNull(reader.GetOrdinal("specimen_description")) ? null : reader.GetString(reader.GetOrdinal("specimen_description")),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(created),
            UpdatedUtc = SqliteValueMapper.FromDateTimeOffset(updated)
        };
    }
}
