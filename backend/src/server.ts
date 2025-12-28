import express from "express";
import cors from "cors";
import multer from "multer";
import rateLimit from "express-rate-limit";
import appInsights from "applicationinsights";
import { authMiddleware, AuthedRequest } from "./auth.js";
import { env } from "./env.js";
import { logger } from "./logger.js";
import {
  createPatient,
  deleteAiKey,
  deletePatient,
  deleteResultsForReport,
  getPatient,
  getReport,
  getReportFile,
  insertResults,
  listAiProviders,
  listNeedsReview,
  listPatients,
  listReportsForPatient,
  listResultsForPatient,
  listResultsForReport,
  logAudit,
  updatePatient,
  updateReportStatus,
  updateResultCorrection,
  updateResultShortCode,
  upsertAiKey,
  upsertMappingEntry,
  updateUserName,
  createReport,
  createSourceFile,
  upsertUserByEmail,
} from "./data.js";
import { applyMigrations } from "./migrations.js";
import { query } from "./db.js";

const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 25 * 1024 * 1024 } });

if (env.APPINSIGHTS_CONNECTION_STRING) {
  appInsights.setup(env.APPINSIGHTS_CONNECTION_STRING).setSendLiveMetrics(true).start();
  logger.info("Application Insights enabled");
}

export const app = express();
app.use((req, _res, next) => {
  if (req.url.startsWith("/api/")) {
    req.url = req.url.slice(4) || "/";
  }
  next();
});
if (env.FRONTEND_ORIGIN) {
  app.use(cors({ origin: env.FRONTEND_ORIGIN, credentials: true }));
} else {
  app.use(cors());
}
app.use(express.json({ limit: "10mb" }));
app.use(
  rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 300,
    standardHeaders: true,
    legacyHeaders: false,
  }),
);

function toUserResponse(user: {
  id: string;
  email: string;
  full_name: string | null;
  google_sub: string | null;
  microsoft_sub: string | null;
}) {
  return {
    id: user.id,
    email: user.email,
    fullName: user.full_name,
    googleLinked: Boolean(user.google_sub),
    microsoftLinked: Boolean(user.microsoft_sub),
  };
}

function toPatientResponse(patient: { id: string; full_name: string; dob: string | null; sex: string | null }) {
  return {
    id: patient.id,
    fullName: patient.full_name,
    dob: patient.dob,
    sex: patient.sex,
  };
}

function toResultResponse(result: {
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
}) {
  return {
    id: result.id,
    analyteNameOriginal: result.analyte_name_original,
    analyteShortCode: result.analyte_short_code,
    resultType: result.result_type,
    valueNumeric: result.value_numeric ? Number(result.value_numeric) : null,
    valueText: result.value_text,
    unitOriginal: result.unit_original,
    unitNormalised: result.unit_normalised,
    reportedDatetimeLocal: result.reported_datetime,
    extractionConfidence: result.extraction_confidence,
    flagSeverity: result.flag_severity,
  };
}

function parseDate(value?: string) {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

function toCsvRow(fields: (string | number | null | undefined)[]) {
  return fields
    .map((f) => {
      if (f === null || f === undefined) return "";
      const s = String(f);
      if (s.includes('"') || s.includes(",") || s.includes("\n")) {
        return `"${s.replace(/"/g, '""')}"`;
      }
      return s;
    })
    .join(",");
}

app.get("/health", async (_req, res) => {
  try {
    await query("select 1");
    res.json({ status: "ok", timestamp: new Date().toISOString() });
  } catch (err) {
    res.status(503).json({ status: "unhealthy", error: err instanceof Error ? err.message : "db_error" });
  }
});

app.get("/me", authMiddleware, async (req: AuthedRequest, res) => {
  res.json({ user: toUserResponse(req.user!) });
});

app.patch("/me", authMiddleware, async (req: AuthedRequest, res) => {
  const rawName = typeof req.body?.fullName === "string" ? req.body.fullName.trim() : "";
  const fullName = rawName ? rawName : null;
  const updated = await updateUserName(req.user!.id, fullName);
  await logAudit({
    entityType: "user",
    entityId: updated.id,
    action: "profile_updated",
    userId: req.user!.id,
    payload: { fullName },
  });
  res.json({ user: toUserResponse(updated) });
});

app.get("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  const patients = await listPatients(req.user!.id);
  res.json({ patients: patients.map(toPatientResponse) });
});

app.get("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  res.json({ patient: toPatientResponse(patient) });
});

app.post("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  try {
    const { fullName, dob, sex } = req.body;
    if (!fullName) {
      return res.status(400).json({ error: "fullName is required" });
    }
    const patient = await createPatient(
      req.user!.id,
      fullName,
      dob ? String(dob) : null,
      sex ? String(sex) : null,
    );
    res.status(201).json({ patient: toPatientResponse(patient) });
  } catch (err) {
    logger.error({ err }, "Create patient failed");
    const message = err instanceof Error ? err.message : "Create patient failed";
    res.status(500).json({ error: "Create patient failed", detail: message });
  }
});

app.patch("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  const current = await getPatient(req.user!.id, req.params.patientId);
  if (!current) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const { fullName, dob, sex } = req.body;
  const updated = await updatePatient(
    req.user!.id,
    req.params.patientId,
    fullName ?? current.full_name,
    dob ? String(dob) : current.dob,
    sex ? String(sex) : current.sex,
  );
  res.json({ patient: updated ? toPatientResponse(updated) : null });
});

app.delete("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  const deleted = await deletePatient(req.user!.id, req.params.patientId);
  if (!deleted) {
    return res.status(404).json({ error: "Patient not found" });
  }
  res.json({ success: true });
});

app.get("/patients/:patientId/reports", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const reports = await listReportsForPatient(req.user!.id, req.params.patientId);
  res.json({
    reports: reports.map((r) => ({
      id: r.id,
      parsingStatus: r.parsing_status,
      createdAtUtc: r.created_at,
      sourceFile: r.original_filename ? { originalFilename: r.original_filename } : null,
    })),
  });
});

app.post(
  "/patients/:patientId/reports",
  authMiddleware,
  upload.single("file"),
  async (req: AuthedRequest, res) => {
    const patient = await getPatient(req.user!.id, req.params.patientId);
    if (!patient) {
      return res.status(404).json({ error: "Patient not found" });
    }
    const file = req.file;
    if (!file) {
      return res.status(400).json({ error: "PDF file is required" });
    }

    const fileId = await createSourceFile(file.originalname, file.mimetype, file.size, file.buffer);
    const report = await createReport(req.params.patientId, fileId);
    await updateReportStatus(report.id, "completed");

    res.status(201).json({
      report: {
        id: report.id,
        parsingStatus: "completed",
        createdAtUtc: report.created_at,
      },
      sourceFile: {
        originalFilename: file.originalname,
      },
    });
  },
);

app.get("/ai/settings", authMiddleware, async (req: AuthedRequest, res) => {
  const settings = await listAiProviders(req.user!.id);
  res.json({
    activeProvider: settings[0]?.provider ?? null,
    providers: settings.map((s) => ({ provider: s.provider, hasKey: s.has_key })),
  });
});

app.post("/ai/settings", authMiddleware, async (req: AuthedRequest, res) => {
  const provider = (req.body?.provider ?? "openai").trim().toLowerCase();
  if (!["openai", "gemini"].includes(provider)) {
    return res.status(400).json({ error: "Unsupported provider" });
  }
  const apiKey = (req.body?.apiKey ?? "").trim();
  if (!apiKey) {
    await deleteAiKey(req.user!.id, provider);
    return res.json({ provider, hasKey: false });
  }
  await upsertAiKey(req.user!.id, provider, apiKey);
  res.json({ provider, hasKey: true });
});

app.post("/reports/:reportId/parsed", authMiddleware, async (req: AuthedRequest, res) => {
  const report = await getReport(req.user!.id, req.params.reportId);
  if (!report) {
    return res.status(404).json({ error: "Report not found" });
  }
  const payload = req.body;
  if (!payload?.results || !Array.isArray(payload.results)) {
    return res.status(400).json({ error: "Invalid parsed payload" });
  }
  await deleteResultsForReport(report.id);
  await insertResults(report.id, report.patient_id, payload.results);
  await updateReportStatus(report.id, "completed");
  res.json({ status: "ingested" });
});

app.get("/reports/:reportId", authMiddleware, async (req: AuthedRequest, res) => {
  const report = await getReport(req.user!.id, req.params.reportId);
  if (!report) {
    return res.status(404).json({ error: "Report not found" });
  }
  const results = await listResultsForReport(req.user!.id, report.id);
  res.json({
    report: {
      id: report.id,
      parsingStatus: report.parsing_status,
      createdAtUtc: report.created_at,
      sourceFile: report.original_filename ? { originalFilename: report.original_filename } : null,
      results: results.map(toResultResponse),
    },
  });
});

app.get("/reports/:reportId/file", authMiddleware, async (req: AuthedRequest, res) => {
  const file = await getReportFile(req.user!.id, req.params.reportId);
  if (!file) return res.status(404).json({ error: "Report not found" });
  res.setHeader("Content-Type", file.content_type || "application/pdf");
  res.setHeader("Content-Disposition", `inline; filename="${file.filename}"`);
  res.send(file.bytes);
});

app.get("/patients/:patientId/results", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const { analyte_short_code, from, to } = req.query as Record<string, string | undefined>;
  const results = await listResultsForPatient(
    req.user!.id,
    req.params.patientId,
    analyte_short_code ?? null,
    parseDate(from),
    parseDate(to),
  );
  res.json({ results: results.map(toResultResponse) });
});

app.get("/patients/:patientId/trend", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const { analyte_short_code } = req.query as Record<string, string | undefined>;
  if (!analyte_short_code) return res.status(400).json({ error: "analyte_short_code is required" });
  const series = await listResultsForPatient(req.user!.id, req.params.patientId, analyte_short_code, null, null);
  res.json({
    analyte_short_code,
    series: series
      .slice()
      .reverse()
      .map((r) => ({
        id: r.id,
        reportedDatetimeLocal: r.reported_datetime ? new Date(r.reported_datetime) : null,
        collectedDatetimeLocal: null,
        valueNumeric: r.value_numeric ? Number(r.value_numeric) : null,
        valueText: r.value_text,
        unitOriginal: r.unit_original,
        unitNormalised: r.unit_normalised,
        flagSeverity: r.flag_severity,
        extractionConfidence: r.extraction_confidence,
        refLow: null,
        refHigh: null,
      })),
  });
});

app.get("/patients/:patientId/integrity/anomalies", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const results = await listResultsForPatient(req.user!.id, req.params.patientId, null, null, null);
  const anomalies: any[] = [];
  const byCode: Record<string, typeof results> = {};
  for (const r of results) {
    const code = r.analyte_short_code || r.analyte_name_original;
    if (!byCode[code]) byCode[code] = [];
    byCode[code].push(r);
  }
  for (const [code, list] of Object.entries(byCode)) {
    const units = new Set(list.map((r) => r.unit_normalised || r.unit_original || "").filter(Boolean));
    if (units.size > 1) {
      anomalies.push({ analyte_short_code: code, type: "unit_mismatch", detail: Array.from(units) });
    }
    const numerics = list
      .map((r) => ({ ...r, value_numeric: r.value_numeric ? Number(r.value_numeric) : null }))
      .filter((r) => r.value_numeric !== null);
    for (let i = 1; i < numerics.length; i++) {
      const prev = numerics[i - 1].value_numeric!;
      const curr = numerics[i].value_numeric!;
      if (prev === 0) continue;
      const ratio = curr / prev;
      if (ratio > 3 || ratio < 0.33) {
        anomalies.push({
          analyte_short_code: code,
          type: "sudden_change",
          detail: { previous: prev, current: curr, reported_at: numerics[i].reported_datetime },
        });
        break;
      }
    }
  }
  res.json({ anomalies });
});

app.get("/patients/:patientId/results/export", authMiddleware, async (req: AuthedRequest, res) => {
  const patient = await getPatient(req.user!.id, req.params.patientId);
  if (!patient) {
    return res.status(404).json({ error: "Patient not found" });
  }
  const { analyte_short_code, from, to } = req.query as Record<string, string | undefined>;
  const results = await listResultsForPatient(
    req.user!.id,
    req.params.patientId,
    analyte_short_code ?? null,
    parseDate(from),
    parseDate(to),
  );
  const rows = [
    toCsvRow(["reported_datetime", "analyte", "short_code", "value_numeric", "value_text", "unit", "flag_severity", "ref_low", "ref_high"]),
    ...results.map((r) =>
      toCsvRow([
        r.reported_datetime ?? "",
        r.analyte_name_original,
        r.analyte_short_code,
        r.value_numeric,
        r.value_text,
        r.unit_normalised ?? r.unit_original,
        r.flag_severity,
        "",
        "",
      ]),
    ),
  ];
  res.setHeader("Content-Type", "text/csv");
  res.setHeader("Content-Disposition", `attachment; filename="patient-${req.params.patientId}-results.csv"`);
  res.send(rows.join("\n"));
});

app.get("/reports/:reportId/export", authMiddleware, async (req: AuthedRequest, res) => {
  const report = await getReport(req.user!.id, req.params.reportId);
  if (!report) return res.status(404).json({ error: "Report not found" });
  const results = await listResultsForReport(req.user!.id, report.id);
  const rows = [
    toCsvRow(["analyte", "short_code", "value_numeric", "value_text", "unit", "flag_severity", "ref_low", "ref_high", "reported_datetime"]),
    ...results.map((r) =>
      toCsvRow([
        r.analyte_name_original,
        r.analyte_short_code,
        r.value_numeric,
        r.value_text,
        r.unit_normalised ?? r.unit_original,
        r.flag_severity,
        "",
        "",
        r.reported_datetime ?? "",
      ]),
    ),
  ];
  res.setHeader("Content-Type", "text/csv");
  res.setHeader("Content-Disposition", `attachment; filename="report-${req.params.reportId}.csv"`);
  res.send(rows.join("\n"));
});

app.get("/reports/:reportId/results", authMiddleware, async (req: AuthedRequest, res) => {
  const report = await getReport(req.user!.id, req.params.reportId);
  if (!report) {
    return res.status(404).json({ error: "Report not found" });
  }
  const results = await listResultsForReport(req.user!.id, report.id);
  res.json({ results: results.map(toResultResponse) });
});

app.post("/mapping-dictionary", authMiddleware, async (req: AuthedRequest, res) => {
  const { analyte_name_pattern, analyte_short_code } = req.body || {};
  if (!analyte_name_pattern || !analyte_short_code) {
    return res.status(400).json({ error: "analyte_name_pattern and analyte_short_code are required" });
  }
  await upsertMappingEntry(req.user!.id, analyte_name_pattern, analyte_short_code);
  res.status(201).json({ entry: { analyte_name_pattern, analyte_short_code } });
});

app.get("/diagnostics/db", authMiddleware, async (_req: AuthedRequest, res) => {
  try {
    const tables = await query<{ table_name: string }>(
      "select table_name from information_schema.tables where table_schema = 'public' order by table_name",
    );
    const counts = {
      users: (await query("select count(*)::int as count from users")).rows[0]?.count ?? 0,
      patients: (await query("select count(*)::int as count from patients")).rows[0]?.count ?? 0,
      reports: (await query("select count(*)::int as count from reports")).rows[0]?.count ?? 0,
      results: (await query("select count(*)::int as count from results")).rows[0]?.count ?? 0,
      mappingDictionary: (await query("select count(*)::int as count from mapping_dictionary")).rows[0]?.count ?? 0,
      aiSettings: (await query("select count(*)::int as count from ai_settings")).rows[0]?.count ?? 0,
    };
    res.json({ ok: true, tables: tables.rows.map((t) => t.table_name), counts });
  } catch (err) {
    logger.error({ err }, "Diagnostics failed");
    res.status(500).json({ error: "Diagnostics failed", detail: err instanceof Error ? err.message : "Unknown error" });
  }
});

app.patch("/results/:resultId/confirm-mapping", authMiddleware, async (req: AuthedRequest, res) => {
  const { analyte_short_code } = req.body || {};
  if (!analyte_short_code) return res.status(400).json({ error: "analyte_short_code required" });
  const ok = await updateResultShortCode(req.user!.id, req.params.resultId, analyte_short_code);
  if (!ok) return res.status(404).json({ error: "Result not found" });
  await logAudit({
    entityType: "result",
    entityId: req.params.resultId,
    action: "mapping_confirmed",
    userId: req.user!.id,
    payload: { analyte_short_code },
  });
  res.json({ result: { id: req.params.resultId, analyte_short_code } });
});

app.patch("/results/:resultId/correction", authMiddleware, async (req: AuthedRequest, res) => {
  const ok = await updateResultCorrection(req.user!.id, req.params.resultId, req.body || {});
  if (!ok) return res.status(404).json({ error: "Result not found" });
  await logAudit({
    entityType: "result",
    entityId: req.params.resultId,
    action: "result_corrected",
    userId: req.user!.id,
    payload: req.body || {},
  });
  res.json({ result: { id: req.params.resultId } });
});

app.get("/reports/needs-review", authMiddleware, async (req: AuthedRequest, res) => {
  const reports = await listNeedsReview(req.user!.id);
  res.json({
    reports: await Promise.all(
      reports.map(async (r) => {
        const patient = await getPatient(req.user!.id, r.patient_id);
        return {
          id: r.id,
          parsingStatus: r.parsing_status,
          createdAtUtc: r.created_at,
          sourceFile: r.original_filename ? { originalFilename: r.original_filename } : null,
          patient: patient ? { fullName: patient.full_name } : null,
        };
      }),
    ),
  });
});

async function ensureDefaultUser() {
  if (process.env.ALLOW_ANONYMOUS_AUTH === "true") {
    await upsertUserByEmail({
      email: "local@patholog.dev",
      fullName: "Local User",
      provider: "local",
      providerUserId: "local",
    });
  }
}

async function start() {
  await applyMigrations();
  await ensureDefaultUser();
}

start().catch((err) => {
  logger.error({ err }, "Startup failed");
  process.exit(1);
});

if (process.env.PATHOLOG_LISTEN !== "false") {
  const port = Number(process.env.PORT || env.API_PORT || 4000);
  app.listen(port, () => {
    logger.info(`API listening on http://localhost:${port}`);
  });
}
