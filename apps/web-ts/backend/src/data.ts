import crypto from "crypto";
import { query } from "./db.js";

export type UserRecord = {
  id: string;
  email: string;
  full_name: string | null;
  google_sub: string | null;
  microsoft_sub: string | null;
};

export type PatientRecord = {
  id: string;
  full_name: string;
  dob: string | null;
  sex: string | null;
  created_at: string;
};

export type ReportRecord = {
  id: string;
  patient_id: string;
  parsing_status: string;
  created_at: string;
  original_filename: string | null;
};

export type ResultRecord = {
  id: string;
  analyte_name_original: string;
  analyte_short_code: string | null;
  result_type: string;
  value_numeric: string | null;
  value_text: string | null;
  unit_original: string | null;
  unit_normalised: string | null;
  reported_datetime: string | null;
  extraction_confidence: string | null;
  flag_severity: string | null;
};

export async function upsertUserByEmail(args: {
  email: string;
  fullName: string;
  provider: string;
  providerUserId: string;
}) {
  const id = crypto.randomUUID();
  const googleSub = args.provider === "google" ? args.providerUserId : null;
  const microsoftSub = args.provider === "aad" ? args.providerUserId : null;
  const res = await query<UserRecord>(
    `
insert into users(id, email, full_name, google_sub, microsoft_sub)
values($1, $2, $3, $4, $5)
on conflict (email)
do update set
  full_name = excluded.full_name,
  google_sub = coalesce(excluded.google_sub, users.google_sub),
  microsoft_sub = coalesce(excluded.microsoft_sub, users.microsoft_sub),
  updated_at = now()
returning id, email, full_name, google_sub, microsoft_sub
`,
    [id, args.email, args.fullName, googleSub, microsoftSub],
  );
  return res.rows[0];
}

export async function updateUserName(userId: string, fullName: string | null) {
  const res = await query<UserRecord>(
    `
update users
set full_name = $2,
  updated_at = now()
where id = $1
returning id, email, full_name, google_sub, microsoft_sub
`,
    [userId, fullName],
  );
  return res.rows[0];
}

export async function listPatients(userId: string) {
  const res = await query<PatientRecord>(
    `
select id, full_name, dob::text as dob, sex, created_at::text as created_at
from patients
where owner_user_id = $1
order by created_at desc
`,
    [userId],
  );
  return res.rows;
}

export async function getPatient(userId: string, patientId: string) {
  const res = await query<PatientRecord>(
    `
select id, full_name, dob::text as dob, sex, created_at::text as created_at
from patients
where id = $1 and owner_user_id = $2
`,
    [patientId, userId],
  );
  return res.rows[0] ?? null;
}

export async function createPatient(userId: string, fullName: string, dob: string | null, sex: string | null) {
  const id = crypto.randomUUID();
  const res = await query<PatientRecord>(
    `
insert into patients(id, owner_user_id, full_name, dob, sex)
values($1, $2, $3, $4::date, $5)
returning id, full_name, dob::text as dob, sex, created_at::text as created_at
`,
    [id, userId, fullName, dob, sex],
  );
  return res.rows[0];
}

export async function updatePatient(userId: string, patientId: string, fullName: string, dob: string | null, sex: string | null) {
  const res = await query<PatientRecord>(
    `
update patients
set full_name = $3,
  dob = $4::date,
  sex = $5,
  updated_at = now()
where id = $1 and owner_user_id = $2
returning id, full_name, dob::text as dob, sex, created_at::text as created_at
`,
    [patientId, userId, fullName, dob, sex],
  );
  return res.rows[0] ?? null;
}

export async function deletePatient(userId: string, patientId: string) {
  const res = await query(
    "delete from patients where id = $1 and owner_user_id = $2",
    [patientId, userId],
  );
  return (res.rowCount ?? 0) > 0;
}

export async function createSourceFile(filename: string, contentType: string | null, sizeBytes: number, bytes: Buffer) {
  const id = crypto.randomUUID();
  const res = await query<{ id: string }>(
    `
insert into source_files(id, filename, content_type, size_bytes, bytes)
values($1, $2, $3, $4, $5)
returning id
`,
    [id, filename, contentType, sizeBytes, bytes],
  );
  return res.rows[0].id;
}

export async function createReport(patientId: string, sourceFileId: string) {
  const id = crypto.randomUUID();
  const res = await query<ReportRecord>(
    `
insert into reports(id, patient_id, source_file_id, parsing_status)
values($1, $2, $3, 'pending')
returning id, patient_id, parsing_status, created_at::text as created_at, null::text as original_filename
`,
    [id, patientId, sourceFileId],
  );
  return res.rows[0];
}

export async function updateReportStatus(reportId: string, status: string) {
  await query("update reports set parsing_status = $2, updated_at = now() where id = $1", [reportId, status]);
}

export async function listReportsForPatient(userId: string, patientId: string) {
  const res = await query<ReportRecord>(
    `
select r.id,
  r.patient_id,
  r.parsing_status,
  r.created_at::text as created_at,
  sf.filename as original_filename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where r.patient_id = $1 and p.owner_user_id = $2
order by r.created_at desc
`,
    [patientId, userId],
  );
  return res.rows;
}

export async function listNeedsReview(userId: string) {
  const res = await query<ReportRecord>(
    `
select r.id,
  r.patient_id,
  r.parsing_status,
  r.created_at::text as created_at,
  sf.filename as original_filename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where p.owner_user_id = $1 and r.parsing_status = 'needs_review'
order by r.updated_at desc
`,
    [userId],
  );
  return res.rows;
}

export async function getReport(userId: string, reportId: string) {
  const res = await query<ReportRecord>(
    `
select r.id,
  r.patient_id,
  r.parsing_status,
  r.created_at::text as created_at,
  sf.filename as original_filename
from reports r
join patients p on p.id = r.patient_id
left join source_files sf on sf.id = r.source_file_id
where r.id = $1 and p.owner_user_id = $2
`,
    [reportId, userId],
  );
  return res.rows[0] ?? null;
}

export async function getReportFile(userId: string, reportId: string) {
  const res = await query<{ bytes: Buffer; content_type: string | null; filename: string }>(
    `
select sf.bytes, sf.content_type, sf.filename
from reports r
join patients p on p.id = r.patient_id
join source_files sf on sf.id = r.source_file_id
where r.id = $1 and p.owner_user_id = $2
`,
    [reportId, userId],
  );
  return res.rows[0] ?? null;
}

export async function listResultsForPatient(userId: string, patientId: string, analyteShortCode: string | null, from: Date | null, to: Date | null) {
  const res = await query<ResultRecord>(
    `
select r.id,
  r.analyte_name_original,
  r.analyte_short_code,
  r.result_type,
  r.value_numeric::text as value_numeric,
  r.value_text,
  r.unit_original,
  r.unit_normalised,
  r.reported_datetime::text as reported_datetime,
  r.extraction_confidence,
  r.flag_severity
from results r
join patients p on p.id = r.patient_id
where r.patient_id = $1
  and p.owner_user_id = $2
  and ($3::text is null or r.analyte_short_code = $3)
  and ($4::timestamptz is null or r.reported_datetime >= $4)
  and ($5::timestamptz is null or r.reported_datetime <= $5)
order by r.reported_datetime desc nulls last
`,
    [patientId, userId, analyteShortCode, from, to],
  );
  return res.rows;
}

export async function listResultsForReport(userId: string, reportId: string) {
  const res = await query<ResultRecord>(
    `
select r.id,
  r.analyte_name_original,
  r.analyte_short_code,
  r.result_type,
  r.value_numeric::text as value_numeric,
  r.value_text,
  r.unit_original,
  r.unit_normalised,
  r.reported_datetime::text as reported_datetime,
  r.extraction_confidence,
  r.flag_severity
from results r
join reports rp on rp.id = r.report_id
join patients p on p.id = rp.patient_id
where r.report_id = $1 and p.owner_user_id = $2
order by r.reported_datetime desc nulls last
`,
    [reportId, userId],
  );
  return res.rows;
}

export async function deleteResultsForReport(reportId: string) {
  await query("delete from results where report_id = $1", [reportId]);
}

export async function insertResults(reportId: string, patientId: string, results: Array<any>) {
  for (const r of results) {
    const reported = r.reported_datetime_local ? new Date(r.reported_datetime_local) : null;
    await query(
      `
insert into results(id, report_id, patient_id, analyte_name_original, analyte_short_code, result_type,
  value_numeric, value_text, unit_original, unit_normalised, reported_datetime, extraction_confidence, flag_severity)
values($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
`,
      [
        crypto.randomUUID(),
        reportId,
        patientId,
        r.analyte_name_original,
        r.analyte_short_code ?? null,
        r.result_type,
        r.value_numeric ?? null,
        r.value_text ?? null,
        r.unit_original ?? null,
        r.unit_normalised ?? null,
        reported,
        r.extraction_confidence ?? null,
        r.flag_severity ?? null,
      ],
    );
  }
}

export async function updateResultShortCode(userId: string, resultId: string, shortCode: string) {
  const res = await query(
    `
update results r
set analyte_short_code = $3,
  updated_at = now()
from patients p
where r.id = $1 and r.patient_id = p.id and p.owner_user_id = $2
`,
    [resultId, userId, shortCode],
  );
  return (res.rowCount ?? 0) > 0;
}

export async function updateResultCorrection(userId: string, resultId: string, payload: Record<string, any>) {
  const res = await query(
    `
update results r
set value_numeric = coalesce($3, r.value_numeric),
  value_text = coalesce($4, r.value_text),
  unit_original = coalesce($5, r.unit_original),
  unit_normalised = coalesce($6, r.unit_normalised),
  flag_severity = coalesce($7, r.flag_severity),
  extraction_confidence = coalesce($8, r.extraction_confidence),
  updated_at = now()
from patients p
where r.id = $1 and r.patient_id = p.id and p.owner_user_id = $2
`,
    [
      resultId,
      userId,
      payload.value_numeric ?? null,
      payload.value_text ?? null,
      payload.unit_original ?? null,
      payload.unit_normalised ?? null,
      payload.flag_severity ?? null,
      payload.extraction_confidence ?? null,
    ],
  );
  return (res.rowCount ?? 0) > 0;
}

export async function upsertMappingEntry(userId: string, pattern: string, shortCode: string) {
  const id = crypto.randomUUID();
  await query(
    `
insert into mapping_dictionary(id, owner_user_id, analyte_name_pattern, analyte_short_code)
values($1, $2, $3, $4)
on conflict (owner_user_id, analyte_name_pattern)
do update set
  analyte_short_code = excluded.analyte_short_code,
  updated_at = now()
`,
    [id, userId, pattern, shortCode],
  );
}

export async function listAiProviders(userId: string) {
  const res = await query<{ provider: string; has_key: boolean }>(
    `
select provider, (api_key is not null and api_key <> '') as has_key
from ai_settings
where user_id = $1
order by updated_at desc
`,
    [userId],
  );
  return res.rows;
}

export async function getAiKey(userId: string, provider: string) {
  const res = await query<{ api_key: string | null }>(
    `
select api_key
from ai_settings
where user_id = $1 and provider = $2
`,
    [userId, provider],
  );
  return res.rows[0]?.api_key ?? null;
}

export async function upsertAiKey(userId: string, provider: string, apiKey: string) {
  const id = crypto.randomUUID();
  await query(
    `
insert into ai_settings(id, user_id, provider, api_key)
values($1, $2, $3, $4)
on conflict (user_id, provider)
do update set api_key = excluded.api_key, updated_at = now()
`,
    [id, userId, provider, apiKey],
  );
}

export async function deleteAiKey(userId: string, provider: string) {
  await query("delete from ai_settings where user_id = $1 and provider = $2", [userId, provider]);
}

export async function logAudit(entry: {
  entityType: string;
  entityId: string;
  action: string;
  userId?: string;
  payload?: Record<string, any>;
}) {
  const id = crypto.randomUUID();
  await query(
    `
insert into audit_logs(id, entity_type, entity_id, action, user_id, payload)
values($1, $2, $3, $4, $5, $6)
`,
    [id, entry.entityType, entry.entityId, entry.action, entry.userId ?? null, entry.payload ? JSON.stringify(entry.payload) : null],
  );
}
