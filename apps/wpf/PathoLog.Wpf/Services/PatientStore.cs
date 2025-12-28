using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PathoLog.Wpf.Services;

/// <summary>
/// Lightweight local patient persistence using SQLite under Documents\PathoLog.
/// Schema is aligned with the web apps (id, owner_user_id, full_name, dob, sex, created_at, updated_at).
/// </summary>
public sealed class PatientStore
{
    private readonly string _dbPath;
    private const string OwnerUserId = "local";

    public PatientStore()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "PathoLog");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "patholog.db");
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            create table if not exists patients(
              id text primary key,
              owner_user_id text not null,
              full_name text not null,
              dob text,
              sex text,
              created_at text not null,
              updated_at text not null
            );
            create index if not exists idx_patients_owner on patients(owner_user_id);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PatientRecord> ListPatients()
    {
        var list = new List<PatientRecord>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "select id, full_name, dob, sex, created_at from patients where owner_user_id = $owner order by created_at desc";
        cmd.Parameters.AddWithValue("$owner", OwnerUserId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PatientRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }
        return list;
    }

    public PatientRecord AddPatient(string fullName, string? dob, string? sex)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            insert into patients(id, owner_user_id, full_name, dob, sex, created_at, updated_at)
            values($id, $owner, $full_name, $dob, $sex, $created, $created);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$owner", OwnerUserId);
        cmd.Parameters.AddWithValue("$full_name", fullName);
        cmd.Parameters.AddWithValue("$dob", (object?)dob ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sex", (object?)sex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", now);
        cmd.ExecuteNonQuery();
        return new PatientRecord(id, fullName, dob, sex, now);
    }
}

public sealed record PatientRecord(string Id, string FullName, string? Dob, string? Sex, string CreatedAt);
