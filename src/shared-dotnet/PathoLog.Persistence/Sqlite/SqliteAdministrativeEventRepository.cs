using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqliteAdministrativeEventRepository : IAdministrativeEventRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteAdministrativeEventRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AdministrativeEvent>> ListByPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, patient_id, report_id, event_type, description, event_date_utc, created_utc
FROM administrative_event
WHERE patient_id = $patientId
ORDER BY event_date_utc DESC, created_utc DESC;";

        var results = new List<AdministrativeEvent>();
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

    public async Task UpsertAsync(AdministrativeEvent administrativeEvent, CancellationToken cancellationToken = default)
    {
        if (administrativeEvent.Id == Guid.Empty)
        {
            administrativeEvent.Id = Guid.NewGuid();
        }

        if (administrativeEvent.CreatedUtc == default)
        {
            administrativeEvent.CreatedUtc = DateTimeOffset.UtcNow;
        }

        if (administrativeEvent.EventDateUtc == default)
        {
            administrativeEvent.EventDateUtc = DateTimeOffset.UtcNow;
        }

        const string sql = @"
INSERT INTO administrative_event (
    id, patient_id, report_id, event_type, description, event_date_utc, created_utc
) VALUES (
    $id, $patientId, $reportId, $eventType, $description, $eventDateUtc, $createdUtc
)
ON CONFLICT(id) DO UPDATE SET
    patient_id = excluded.patient_id,
    report_id = excluded.report_id,
    event_type = excluded.event_type,
    description = excluded.description,
    event_date_utc = excluded.event_date_utc,
    created_utc = excluded.created_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", administrativeEvent.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$patientId", administrativeEvent.PatientId.ToString()));
        command.Parameters.Add(new SqliteParameter("$reportId", (object?)(administrativeEvent.ReportId?.ToString()) ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$eventType", administrativeEvent.EventType.ToString()));
        command.Parameters.Add(new SqliteParameter("$description", administrativeEvent.Description));
        command.Parameters.Add(new SqliteParameter("$eventDateUtc", SqliteValueMapper.ToDateTimeOffset(administrativeEvent.EventDateUtc)));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(administrativeEvent.CreatedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AdministrativeEvent Map(DbDataReader reader)
    {
        return new AdministrativeEvent
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            PatientId = Guid.Parse(reader.GetString(reader.GetOrdinal("patient_id"))),
            ReportId = reader.IsDBNull(reader.GetOrdinal("report_id"))
                ? null
                : Guid.Parse(reader.GetString(reader.GetOrdinal("report_id"))),
            EventType = ParseEventType(reader.GetString(reader.GetOrdinal("event_type"))),
            Description = reader.GetString(reader.GetOrdinal("description")),
            EventDateUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("event_date_utc"))),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(reader.GetString(reader.GetOrdinal("created_utc")))
        };
    }

    private static AdministrativeEventType ParseEventType(string value)
    {
        return Enum.TryParse<AdministrativeEventType>(value, out var parsed)
            ? parsed
            : AdministrativeEventType.Note;
    }
}
