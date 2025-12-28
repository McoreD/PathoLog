using System.Data.Common;
using Microsoft.Data.Sqlite;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence.Sqlite;

public sealed class SqlitePatientRepository : IPatientRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlitePatientRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Patient?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, external_id, full_name, date_of_birth, sex_at_birth, created_utc, updated_utc
FROM patient
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

    public async Task<IReadOnlyList<Patient>> SearchAsync(string? name, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id, external_id, full_name, date_of_birth, sex_at_birth, created_utc, updated_utc
FROM patient
WHERE $name IS NULL OR full_name LIKE $name
ORDER BY full_name
LIMIT 50;";

        var results = new List<Patient>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var search = string.IsNullOrWhiteSpace(name) ? null : $"%{name}%";
        command.Parameters.Add(new SqliteParameter("$name", (object?)search ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        if (patient.Id == Guid.Empty)
        {
            patient.Id = Guid.NewGuid();
        }

        if (patient.CreatedUtc == default)
        {
            patient.CreatedUtc = DateTimeOffset.UtcNow;
        }

        patient.UpdatedUtc = DateTimeOffset.UtcNow;

        const string sql = @"
INSERT INTO patient (
    id, external_id, full_name, date_of_birth, sex_at_birth, created_utc, updated_utc
) VALUES (
    $id, $externalId, $fullName, $dob, $sex, $createdUtc, $updatedUtc
)
ON CONFLICT(id) DO UPDATE SET
    external_id = excluded.external_id,
    full_name = excluded.full_name,
    date_of_birth = excluded.date_of_birth,
    sex_at_birth = excluded.sex_at_birth,
    updated_utc = excluded.updated_utc;";

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("$id", patient.Id.ToString()));
        command.Parameters.Add(new SqliteParameter("$externalId", (object?)patient.ExternalId ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$fullName", patient.FullName));
        command.Parameters.Add(new SqliteParameter("$dob", (object?)SqliteValueMapper.ToDateOnly(patient.DateOfBirth) ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$sex", (object?)patient.SexAtBirth ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("$createdUtc", SqliteValueMapper.ToDateTimeOffset(patient.CreatedUtc)));
        command.Parameters.Add(new SqliteParameter("$updatedUtc", SqliteValueMapper.ToDateTimeOffset(patient.UpdatedUtc)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Patient Map(DbDataReader reader)
    {
        var created = reader.GetString(reader.GetOrdinal("created_utc"));
        var updated = reader.GetString(reader.GetOrdinal("updated_utc"));

        return new Patient
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ExternalId = reader.IsDBNull(reader.GetOrdinal("external_id")) ? null : reader.GetString(reader.GetOrdinal("external_id")),
            FullName = reader.GetString(reader.GetOrdinal("full_name")),
            DateOfBirth = SqliteValueMapper.FromDateOnly(reader.IsDBNull(reader.GetOrdinal("date_of_birth")) ? null : reader.GetString(reader.GetOrdinal("date_of_birth"))),
            SexAtBirth = reader.IsDBNull(reader.GetOrdinal("sex_at_birth")) ? null : reader.GetString(reader.GetOrdinal("sex_at_birth")),
            CreatedUtc = SqliteValueMapper.FromDateTimeOffset(created),
            UpdatedUtc = SqliteValueMapper.FromDateTimeOffset(updated)
        };
    }
}
