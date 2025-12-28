using Dapper;
using Npgsql;

public static class Data
{
    public static NpgsqlConnection Conn(string cs) => new NpgsqlConnection(cs);

    public static async Task<UserRecord> UpsertUser(string cs, AuthPrincipal principal)
    {
        const string sql = @"
insert into users(id, email, full_name, google_sub, microsoft_sub)
values(@Id, @Email, @FullName, @GoogleSub, @MicrosoftSub)
on conflict (email)
do update set
  full_name = excluded.full_name,
  google_sub = coalesce(excluded.google_sub, users.google_sub),
  microsoft_sub = coalesce(excluded.microsoft_sub, users.microsoft_sub),
  updated_at = now()
returning id as Id, email as Email, full_name as FullName, google_sub as GoogleSub, microsoft_sub as MicrosoftSub;";

        var provider = principal.Provider.ToLowerInvariant();
        var data = new
        {
            Id = Guid.NewGuid(),
            Email = principal.Email,
            FullName = principal.DisplayName,
            GoogleSub = provider == "google" ? principal.UserId : null,
            MicrosoftSub = provider == "aad" ? principal.UserId : null
        };

        await using var db = Conn(cs);
        return await db.QuerySingleAsync<UserRecord>(sql, data);
    }

    public static async Task<UserRecord?> GetUserById(string cs, Guid id)
    {
        const string sql = "select id as Id, email as Email, full_name as FullName, google_sub as GoogleSub, microsoft_sub as MicrosoftSub from users where id = @Id";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<UserRecord>(sql, new { Id = id });
    }

    public static async Task<UserRecord> UpdateUserName(string cs, Guid id, string? fullName)
    {
        const string sql = @"
update users
set full_name = @FullName,
  updated_at = now()
where id = @Id
returning id as Id, email as Email, full_name as FullName, google_sub as GoogleSub, microsoft_sub as MicrosoftSub;";
        await using var db = Conn(cs);
        return await db.QuerySingleAsync<UserRecord>(sql, new { Id = id, FullName = fullName });
    }

    public static async Task<IReadOnlyList<PatientRecord>> ListPatients(string cs, Guid userId)
    {
        const string sql = @"
select id as Id, full_name as FullName, dob as Dob, sex as Sex, created_at as CreatedAtUtc
from patients
where owner_user_id = @UserId
order by created_at desc;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<PatientRecord>(sql, new { UserId = userId });
        return rows.ToList();
    }

    public static async Task<PatientRecord?> GetPatient(string cs, Guid userId, Guid patientId)
    {
        const string sql = @"
select id as Id, full_name as FullName, dob as Dob, sex as Sex, created_at as CreatedAtUtc
from patients
where id = @PatientId and owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<PatientRecord>(sql, new { PatientId = patientId, UserId = userId });
    }

    public static async Task<PatientRecord> CreatePatient(string cs, Guid userId, string fullName, DateOnly? dob, string? sex)
    {
        const string sql = @"
insert into patients(id, owner_user_id, full_name, dob, sex)
values(@Id, @UserId, @FullName, @Dob, @Sex)
returning id as Id, full_name as FullName, dob as Dob, sex as Sex, created_at as CreatedAtUtc;";
        await using var db = Conn(cs);
        return await db.QuerySingleAsync<PatientRecord>(sql, new
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = fullName,
            Dob = dob,
            Sex = sex
        });
    }

    public static async Task<PatientRecord?> UpdatePatient(string cs, Guid userId, Guid patientId, string fullName, DateOnly? dob, string? sex)
    {
        const string sql = @"
update patients
set full_name = @FullName,
  dob = @Dob,
  sex = @Sex,
  updated_at = now()
where id = @PatientId and owner_user_id = @UserId
returning id as Id, full_name as FullName, dob as Dob, sex as Sex, created_at as CreatedAtUtc;";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<PatientRecord>(sql, new
        {
            PatientId = patientId,
            UserId = userId,
            FullName = fullName,
            Dob = dob,
            Sex = sex
        });
    }

    public static async Task<bool> DeletePatient(string cs, Guid userId, Guid patientId)
    {
        const string sql = "delete from patients where id = @PatientId and owner_user_id = @UserId";
        await using var db = Conn(cs);
        var affected = await db.ExecuteAsync(sql, new { PatientId = patientId, UserId = userId });
        return affected > 0;
    }

    public static async Task<Guid> CreateSourceFile(string cs, string filename, string? contentType, int size, byte[] bytes)
    {
        const string sql = @"
insert into source_files(id, filename, content_type, size_bytes, bytes)
values(@Id, @Filename, @ContentType, @SizeBytes, @Bytes)
returning id;";
        await using var db = Conn(cs);
        return await db.QuerySingleAsync<Guid>(sql, new
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            ContentType = contentType,
            SizeBytes = size,
            Bytes = bytes
        });
    }

    public static async Task<ReportRecord> CreateReport(string cs, Guid patientId, Guid sourceFileId)
    {
        const string sql = @"
insert into reports(id, patient_id, source_file_id, parsing_status)
values(@Id, @PatientId, @SourceFileId, 'pending')
returning id as Id, patient_id as PatientId, parsing_status as ParsingStatus, created_at as CreatedAtUtc;";
        await using var db = Conn(cs);
        return await db.QuerySingleAsync<ReportRecord>(sql, new
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            SourceFileId = sourceFileId
        });
    }

    public static async Task<IReadOnlyList<ReportRecord>> ListReportsForPatient(string cs, Guid userId, Guid patientId)
    {
        const string sql = @"
select r.id as Id,
  r.patient_id as PatientId,
  r.parsing_status as ParsingStatus,
  r.created_at as CreatedAtUtc,
  sf.filename as OriginalFilename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where r.patient_id = @PatientId and p.owner_user_id = @UserId
order by r.created_at desc;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<ReportRecord>(sql, new { PatientId = patientId, UserId = userId });
        return rows.ToList();
    }

    public static async Task<ReportRecord?> GetReport(string cs, Guid userId, Guid reportId)
    {
        const string sql = @"
select r.id as Id,
  r.patient_id as PatientId,
  r.parsing_status as ParsingStatus,
  r.created_at as CreatedAtUtc,
  sf.filename as OriginalFilename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where r.id = @ReportId and p.owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<ReportRecord>(sql, new { ReportId = reportId, UserId = userId });
    }

    public static async Task<IReadOnlyList<ReportRecord>> ListNeedsReview(string cs, Guid userId)
    {
        const string sql = @"
select r.id as Id,
  r.patient_id as PatientId,
  r.parsing_status as ParsingStatus,
  r.created_at as CreatedAtUtc,
  sf.filename as OriginalFilename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where p.owner_user_id = @UserId and r.parsing_status = 'needs_review'
order by r.updated_at desc;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<ReportRecord>(sql, new { UserId = userId });
        return rows.ToList();
    }

    public static async Task UpdateReportStatus(string cs, Guid reportId, string status)
    {
        const string sql = "update reports set parsing_status = @Status, updated_at = now() where id = @ReportId";
        await using var db = Conn(cs);
        await db.ExecuteAsync(sql, new { ReportId = reportId, Status = status });
    }

    public static async Task<(byte[] Bytes, string? ContentType, string Filename)?> GetReportFile(string cs, Guid userId, Guid reportId)
    {
        const string sql = @"
select sf.bytes as Bytes, sf.content_type as ContentType, sf.filename as Filename
from reports r
join patients p on p.id = r.patient_id
join source_files sf on sf.id = r.source_file_id
where r.id = @ReportId and p.owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<(byte[] Bytes, string? ContentType, string Filename)>(sql, new { ReportId = reportId, UserId = userId });
    }

    public static async Task<IReadOnlyList<ResultRecord>> ListResultsForPatient(string cs, Guid userId, Guid patientId, string? analyteShortCode, DateTime? from, DateTime? to)
    {
        const string sql = @"
select r.id as Id,
  r.analyte_name_original as AnalyteNameOriginal,
  r.analyte_short_code as AnalyteShortCode,
  r.result_type as ResultType,
  r.value_numeric as ValueNumeric,
  r.value_text as ValueText,
  r.unit_original as UnitOriginal,
  r.unit_normalised as UnitNormalised,
  r.reported_datetime as ReportedDatetimeLocal,
  r.extraction_confidence as ExtractionConfidence,
  r.flag_severity as FlagSeverity
from results r
join patients p on p.id = r.patient_id
where r.patient_id = @PatientId
  and p.owner_user_id = @UserId
  and (@AnalyteShortCode is null or r.analyte_short_code = @AnalyteShortCode)
  and (@FromDate is null or r.reported_datetime >= @FromDate)
  and (@ToDate is null or r.reported_datetime <= @ToDate)
order by r.reported_datetime desc nulls last;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<ResultRecord>(sql, new
        {
            PatientId = patientId,
            UserId = userId,
            AnalyteShortCode = analyteShortCode,
            FromDate = from,
            ToDate = to
        });
        return rows.ToList();
    }

    public static async Task<IReadOnlyList<ResultRecord>> ListResultsForReport(string cs, Guid userId, Guid reportId)
    {
        const string sql = @"
select r.id as Id,
  r.analyte_name_original as AnalyteNameOriginal,
  r.analyte_short_code as AnalyteShortCode,
  r.result_type as ResultType,
  r.value_numeric as ValueNumeric,
  r.value_text as ValueText,
  r.unit_original as UnitOriginal,
  r.unit_normalised as UnitNormalised,
  r.reported_datetime as ReportedDatetimeLocal,
  r.extraction_confidence as ExtractionConfidence,
  r.flag_severity as FlagSeverity
from results r
join reports rp on rp.id = r.report_id
join patients p on p.id = rp.patient_id
where r.report_id = @ReportId and p.owner_user_id = @UserId
order by r.reported_datetime desc nulls last;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<ResultRecord>(sql, new { ReportId = reportId, UserId = userId });
        return rows.ToList();
    }

    public static async Task<ResultRecord?> GetResult(string cs, Guid userId, Guid resultId)
    {
        const string sql = @"
select r.id as Id,
  r.analyte_name_original as AnalyteNameOriginal,
  r.analyte_short_code as AnalyteShortCode,
  r.result_type as ResultType,
  r.value_numeric as ValueNumeric,
  r.value_text as ValueText,
  r.unit_original as UnitOriginal,
  r.unit_normalised as UnitNormalised,
  r.reported_datetime as ReportedDatetimeLocal,
  r.extraction_confidence as ExtractionConfidence,
  r.flag_severity as FlagSeverity
from results r
join patients p on p.id = r.patient_id
where r.id = @ResultId and p.owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.QuerySingleOrDefaultAsync<ResultRecord>(sql, new { ResultId = resultId, UserId = userId });
    }

    public static async Task InsertResults(string cs, Guid reportId, Guid patientId, IReadOnlyList<ParsedPayloadResult> results)
    {
        const string sql = @"
insert into results(id, report_id, patient_id, analyte_name_original, analyte_short_code, result_type,
  value_numeric, value_text, unit_original, unit_normalised, reported_datetime, extraction_confidence, flag_severity)
values(@Id, @ReportId, @PatientId, @AnalyteNameOriginal, @AnalyteShortCode, @ResultType,
  @ValueNumeric, @ValueText, @UnitOriginal, @UnitNormalised, @ReportedDatetime, @ExtractionConfidence, @FlagSeverity);";

        await using var db = Conn(cs);
        foreach (var r in results)
        {
            DateTime? reported = null;
            if (!string.IsNullOrWhiteSpace(r.ReportedDatetimeLocal) &&
                DateTime.TryParse(r.ReportedDatetimeLocal, out var parsed))
            {
                reported = parsed;
            }

            await db.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                PatientId = patientId,
                AnalyteNameOriginal = r.AnalyteNameOriginal,
                AnalyteShortCode = r.AnalyteShortCode,
                ResultType = r.ResultType,
                ValueNumeric = r.ValueNumeric,
                ValueText = r.ValueText,
                UnitOriginal = r.UnitOriginal,
                UnitNormalised = r.UnitNormalised,
                ReportedDatetime = reported,
                ExtractionConfidence = r.ExtractionConfidence,
                FlagSeverity = r.FlagSeverity
            });
        }
    }

    public static async Task DeleteResultsForReport(string cs, Guid reportId)
    {
        const string sql = "delete from results where report_id = @ReportId;";
        await using var db = Conn(cs);
        await db.ExecuteAsync(sql, new { ReportId = reportId });
    }

    public static async Task<int> UpdateResultShortCode(string cs, Guid userId, Guid resultId, string shortCode)
    {
        const string sql = @"
update results r
set analyte_short_code = @ShortCode,
  updated_at = now()
from patients p
where r.id = @ResultId and r.patient_id = p.id and p.owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.ExecuteAsync(sql, new { ResultId = resultId, UserId = userId, ShortCode = shortCode });
    }

    public static async Task<int> UpdateResultCorrection(string cs, Guid userId, Guid resultId, ResultCorrectionRequest payload)
    {
        const string sql = @"
update results r
set value_numeric = coalesce(@ValueNumeric, r.value_numeric),
  value_text = coalesce(@ValueText, r.value_text),
  unit_original = coalesce(@UnitOriginal, r.unit_original),
  unit_normalised = coalesce(@UnitNormalised, r.unit_normalised),
  flag_severity = coalesce(@FlagSeverity, r.flag_severity),
  extraction_confidence = coalesce(@ExtractionConfidence, r.extraction_confidence),
  updated_at = now()
from patients p
where r.id = @ResultId and r.patient_id = p.id and p.owner_user_id = @UserId;";
        await using var db = Conn(cs);
        return await db.ExecuteAsync(sql, new
        {
            ResultId = resultId,
            UserId = userId,
            payload.ValueNumeric,
            payload.ValueText,
            payload.UnitOriginal,
            payload.UnitNormalised,
            payload.FlagSeverity,
            payload.ExtractionConfidence
        });
    }

    public static async Task UpsertMappingEntry(string cs, Guid userId, string pattern, string shortCode)
    {
        const string sql = @"
insert into mapping_dictionary(id, owner_user_id, analyte_name_pattern, analyte_short_code)
values(@Id, @UserId, @Pattern, @ShortCode)
on conflict (owner_user_id, analyte_name_pattern)
do update set
  analyte_short_code = excluded.analyte_short_code,
  updated_at = now();";
        await using var db = Conn(cs);
        await db.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Pattern = pattern,
            ShortCode = shortCode
        });
    }

    public static async Task<IReadOnlyList<AiProviderStatus>> ListAiProviders(string cs, Guid userId)
    {
        const string sql = @"
select provider as Provider, (api_key is not null and api_key <> '') as HasKey
from ai_settings
where user_id = @UserId
order by updated_at desc;";
        await using var db = Conn(cs);
        var rows = await db.QueryAsync<AiProviderStatus>(sql, new { UserId = userId });
        return rows.ToList();
    }

    public static async Task UpsertAiKey(string cs, Guid userId, string provider, string apiKey)
    {
        const string sql = @"
insert into ai_settings(id, user_id, provider, api_key)
values(@Id, @UserId, @Provider, @ApiKey)
on conflict (user_id, provider)
do update set
  api_key = excluded.api_key,
  updated_at = now();";
        await using var db = Conn(cs);
        await db.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            ApiKey = apiKey
        });
    }

    public static async Task DeleteAiKey(string cs, Guid userId, string provider)
    {
        const string sql = "delete from ai_settings where user_id = @UserId and provider = @Provider;";
        await using var db = Conn(cs);
        await db.ExecuteAsync(sql, new { UserId = userId, Provider = provider });
    }
}
