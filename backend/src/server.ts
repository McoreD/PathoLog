import express from "express";
import cors from "cors";
import cookieParser from "cookie-parser";
import multer from "multer";
import crypto from "crypto";
import { env } from "./env.js";
import { authMiddleware, createSessionToken, sessionCookieOptions, verifyGoogleCredential, AuthedRequest, SESSION_COOKIE } from "./auth.js";
import { logger } from "./logger.js";
import { createStorage } from "./storage.js";
import { v4 as uuidv4 } from "uuid";
import { prisma } from "./db.js";
import { parseReport } from "./parser.js";
import { ensureFamilyAccountForUser } from "./family.js";
import { ingestParsedReport } from "./ingest.js";
import { UserInputError } from "./utils/errors.js";
import { assertPatientAccess, assertReportAccess, ensureFamilyMembership } from "./access.js";
import { logAudit } from "./audit.js";
import rateLimit from "express-rate-limit";
import { createSignedUrl, isSignedUrlSupported } from "./storage.js";

const storage = createStorage();
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 25 * 1024 * 1024 } });

const app = express();
app.use(cors({ origin: env.FRONTEND_ORIGIN, credentials: true }));
app.use(express.json({ limit: "5mb" }));
app.use(cookieParser());
app.use(
  rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 300,
    standardHeaders: true,
    legacyHeaders: false,
  }),
);

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

app.get("/health", (_req, res) => {
  res.json({ status: "ok", timestamp: new Date().toISOString() });
});

app.post("/auth/google", async (req, res) => {
  try {
    const credential = req.body?.credential;
    if (!credential) {
      return res.status(400).json({ error: "Missing credential" });
    }
    const { email, sub, fullName } = await verifyGoogleCredential(credential);

    const user = await prisma.user.upsert({
      where: { email },
      update: { googleSub: sub, fullName },
      create: { email, googleSub: sub, fullName },
    });
    await ensureFamilyAccountForUser(user);
    await ensureFamilyMembership(user);
    const token = await createSessionToken(user);
    res.cookie(SESSION_COOKIE, token, sessionCookieOptions());
    res.json({ user: { id: user.id, email: user.email, fullName: user.fullName } });
  } catch (err) {
    logger.error({ err }, "Google auth failed");
    res.status(401).json({ error: "Authentication failed" });
  }
});

app.post("/auth/logout", (_req, res) => {
  res.clearCookie(SESSION_COOKIE, sessionCookieOptions());
  res.json({ success: true });
});

app.get("/me", authMiddleware, async (req: AuthedRequest, res) => {
  res.json({ user: { id: req.user!.id, email: req.user!.email, fullName: req.user!.fullName } });
});

app.get("/patients/:patientId/results", authMiddleware, async (req: AuthedRequest, res) => {
  const { patientId } = req.params;
  const { analyte_short_code, from, to } = req.query as Record<string, string | undefined>;
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }

  const fromDate = parseDate(from);
  const toDate = parseDate(to);

  const results = await prisma.result.findMany({
    where: {
      patientId,
      analyteShortCode: analyte_short_code ?? undefined,
      reportedDatetimeLocal: {
        gte: fromDate ?? undefined,
        lte: toDate ?? undefined,
      },
    },
    orderBy: [{ reportedDatetimeLocal: "desc" }, { createdAtUtc: "desc" }],
    take: 100,
  });
  res.json({ results });
});

app.get("/patients/:patientId/trend", authMiddleware, async (req: AuthedRequest, res) => {
  const { patientId } = req.params;
  const { analyte_short_code, from, to } = req.query as Record<string, string | undefined>;
  if (!analyte_short_code) return res.status(400).json({ error: "analyte_short_code is required" });
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }

  const fromDate = parseDate(from);
  const toDate = parseDate(to);

  const series = await prisma.result.findMany({
    where: {
      patientId,
      analyteShortCode: analyte_short_code,
      reportedDatetimeLocal: {
        gte: fromDate ?? undefined,
        lte: toDate ?? undefined,
      },
    },
    orderBy: { reportedDatetimeLocal: "asc" },
    select: {
      id: true,
      reportedDatetimeLocal: true,
      collectedDatetimeLocal: true,
      valueNumeric: true,
      valueText: true,
      unitOriginal: true,
      unitNormalised: true,
      flagSeverity: true,
      extractionConfidence: true,
      refLow: true,
      refHigh: true,
    },
  });

  res.json({ analyte_short_code, series });
});

app.get("/patients/:patientId/review/low-confidence", authMiddleware, async (req: AuthedRequest, res) => {
  const { patientId } = req.params;
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }

  const results = await prisma.result.findMany({
    where: {
      patientId,
      OR: [
        { extractionConfidence: "low" },
        { mappingConfidence: "low" },
        { analyteShortCode: null },
      ],
    },
    orderBy: { updatedAtUtc: "desc" },
    take: 50,
  });
  res.json({ results });
});

app.get("/patients/:patientId/integrity/anomalies", authMiddleware, async (req: AuthedRequest, res) => {
  const { patientId } = req.params;
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
  const results = await prisma.result.findMany({
    where: { patientId },
    orderBy: { reportedDatetimeLocal: "asc" },
  });
  const anomalies: any[] = [];
  const byCode: Record<string, typeof results> = {};
  for (const r of results) {
    const code = r.analyteShortCode || r.analyteNameOriginal;
    if (!byCode[code]) byCode[code] = [];
    byCode[code].push(r as any);
  }
  for (const [code, list] of Object.entries(byCode)) {
    const units = new Set(list.map((r) => r.unitNormalised || r.unitOriginal || "").filter(Boolean));
    if (units.size > 1) {
      anomalies.push({ analyte_short_code: code, type: "unit_mismatch", detail: Array.from(units) });
    }
    const numerics = list.filter((r) => r.valueNumeric !== null && r.valueNumeric !== undefined);
    for (let i = 1; i < numerics.length; i++) {
      const prev = numerics[i - 1].valueNumeric!;
      const curr = numerics[i].valueNumeric!;
      if (prev === 0) continue;
      const ratio = curr / prev;
      if (ratio > 3 || ratio < 0.33) {
        anomalies.push({
          analyte_short_code: code,
          type: "sudden_change",
          detail: { previous: prev, current: curr, reported_at: numerics[i].reportedDatetimeLocal },
        });
        break;
      }
    }
  }
  res.json({ anomalies });
});

app.get("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  const patients = await prisma.patient.findMany({
    where: {
      OR: [
        { ownerUserId: req.user!.id },
        { familyAccount: { members: { some: { userId: req.user!.id } } } },
      ],
    },
    orderBy: { createdAtUtc: "desc" },
  });
  res.json({ patients });
});

app.get("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  try {
    const patient = await assertPatientAccess(req.user!, req.params.patientId);
    res.json({ patient });
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
});

app.post("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  const { fullName, dob, sex } = req.body;
  if (!fullName) {
    return res.status(400).json({ error: "fullName is required" });
  }
  const family = await ensureFamilyAccountForUser(req.user!);
  const patient = await prisma.patient.create({
    data: {
      fullName,
      dob: dob ? new Date(dob) : null,
      sex: sex ?? null,
      ownerUserId: req.user!.id,
      familyAccountId: family.id,
    },
  });
  res.status(201).json({ patient });
});

app.patch("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  let patient;
  try {
    patient = await assertPatientAccess(req.user!, req.params.patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
  const { fullName, dob, sex } = req.body;
  const updated = await prisma.patient.update({
    where: { id: patient.id },
    data: {
      fullName: fullName ?? patient.fullName,
      dob: dob ? new Date(dob) : patient.dob,
      sex: sex ?? patient.sex,
    },
  });
  res.json({ patient: updated });
});

app.delete("/patients/:patientId", authMiddleware, async (req: AuthedRequest, res) => {
  try {
    await assertPatientAccess(req.user!, req.params.patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
  await prisma.patient.delete({ where: { id: req.params.patientId } });
  res.json({ success: true });
});

app.get("/patients/:patientId/reports", authMiddleware, async (req: AuthedRequest, res) => {
  const patientId = req.params.patientId;
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
  const reports = await prisma.report.findMany({
    where: { patientId },
    orderBy: { createdAtUtc: "desc" },
    include: { sourceFile: true },
  });
  res.json({ reports });
});

app.post(
  "/patients/:patientId/reports",
  authMiddleware,
  upload.single("file"),
  async (req: AuthedRequest, res) => {
    const patientId = req.params.patientId;
    try {
      await assertPatientAccess(req.user!, patientId);
    } catch {
      return res.status(404).json({ error: "Patient not found" });
    }
    const file = req.file;
    if (!file) {
      return res.status(400).json({ error: "PDF file is required" });
    }
    const hash = crypto.createHash("sha256").update(file.buffer).digest("hex");

    const existing = await prisma.report.findUnique({ where: { sourcePdfHash: hash } });
    if (existing) {
      return res.status(200).json({ report: existing, duplicate: true });
    }

    const storageKey = `reports/${patientId}/${uuidv4()}-${file.originalname}`;
    const saved = await storage.saveFile(storageKey, file.buffer, file.mimetype);

    const sourceFile = await prisma.sourceFile.create({
      data: {
        storageProvider: saved.storageProvider === "local" ? "local" : "s3",
        storageBucket: saved.storageBucket,
        storageKey: saved.storageKey,
        originalFilename: file.originalname,
        contentType: file.mimetype,
        sizeBytes: file.size,
        uploadedByUserId: req.user!.id,
      },
    });

    const report = await prisma.report.create({
      data: {
        patientId,
        sourceFileId: sourceFile.id,
        sourcePdfHash: hash,
        providerName: null,
        parsingStatus: "pending",
        rawTextExtractionMethod: null,
        extractionConfidenceOverall: null,
      },
    });

    // Kick off async parsing without blocking the upload response.
    setImmediate(() => parseReport(report.id).catch((err) => logger.error({ err, reportId: report.id }, "Parse job failed")));

    res.status(201).json({ report, sourceFile });
  },
);

app.post("/reports/:reportId/parsed", authMiddleware, async (req: AuthedRequest, res) => {
  try {
    await ingestParsedReport({ reportId: req.params.reportId, userId: req.user!.id, payload: req.body });
    res.json({ status: "ingested" });
  } catch (err) {
    if (err instanceof UserInputError) {
      return res.status(err.status).json({ error: err.message });
    }
    logger.error({ err }, "Ingest failed");
    res.status(500).json({ error: "Failed to ingest parsed payload" });
  }
});

app.get("/reports/:reportId", authMiddleware, async (req: AuthedRequest, res) => {
  const reportId = req.params.reportId;
  try {
    const report = await assertReportAccess(req.user!, reportId);
    const full = await prisma.report.findFirst({
      where: { id: report.id },
      include: { sourceFile: true, results: true },
    });
    res.json({ report: full });
  } catch {
    return res.status(404).json({ error: "Report not found" });
  }
});

app.get("/reports/:reportId/file", authMiddleware, async (req: AuthedRequest, res) => {
  const reportId = req.params.reportId;
  const report = await assertReportAccess(req.user!, reportId).catch(() => null);
  if (!report) return res.status(404).json({ error: "Report not found" });
  const full = await prisma.report.findFirst({
    where: { id: report.id },
    include: { sourceFile: true },
  });
  try {
    if (!full?.sourceFile) throw new Error("Missing source file");
    if (isSignedUrlSupported()) {
      const signed = await createSignedUrl(full.sourceFile.storageKey, env.SIGNED_URL_TTL_SECONDS);
      if (signed) {
        return res.json({ signedUrl: signed.url, expiresAt: signed.expiresAt });
      }
    }
    const buffer = await storage.getFileBuffer(full.sourceFile.storageKey);
    res.setHeader("Content-Type", full.sourceFile.contentType || "application/pdf");
    res.setHeader("Content-Disposition", `inline; filename="${full.sourceFile.originalFilename}"`);
    res.send(buffer);
  } catch (err) {
    logger.error({ err }, "Failed to read source file");
    res.status(500).json({ error: "Could not fetch file" });
  }
});

app.get("/patients/:patientId/results/export", authMiddleware, async (req: AuthedRequest, res) => {
  const { patientId } = req.params;
  const { analyte_short_code, from, to } = req.query as Record<string, string | undefined>;
  try {
    await assertPatientAccess(req.user!, patientId);
  } catch {
    return res.status(404).json({ error: "Patient not found" });
  }
  const fromDate = parseDate(from);
  const toDate = parseDate(to);
  const results = await prisma.result.findMany({
    where: {
      patientId,
      analyteShortCode: analyte_short_code ?? undefined,
      reportedDatetimeLocal: {
        gte: fromDate ?? undefined,
        lte: toDate ?? undefined,
      },
    },
    orderBy: [{ reportedDatetimeLocal: "desc" }, { createdAtUtc: "desc" }],
  });
  const rows = [
    toCsvRow(["reported_datetime", "analyte", "short_code", "value_numeric", "value_text", "unit", "flag_severity", "ref_low", "ref_high"]),
    ...results.map((r) =>
      toCsvRow([
        r.reportedDatetimeLocal?.toISOString() ?? "",
        r.analyteNameOriginal,
        r.analyteShortCode,
        r.valueNumeric,
        r.valueText,
        r.unitNormalised ?? r.unitOriginal,
        r.flagSeverity,
        r.refLow,
        r.refHigh,
      ]),
    ),
  ];
  res.setHeader("Content-Type", "text/csv");
  res.setHeader("Content-Disposition", `attachment; filename="patient-${patientId}-results.csv"`);
  res.send(rows.join("\n"));
});

app.get("/reports/:reportId/export", authMiddleware, async (req: AuthedRequest, res) => {
  const reportId = req.params.reportId;
  const report = await assertReportAccess(req.user!, reportId).catch(() => null);
  if (!report) return res.status(404).json({ error: "Report not found" });
  const results = await prisma.result.findMany({
    where: { reportId },
    include: { referenceRanges: true },
  });
  const rows = [
    toCsvRow([
      "analyte",
      "short_code",
      "value_numeric",
      "value_text",
      "unit",
      "flag_severity",
      "ref_low",
      "ref_high",
      "reported_datetime",
    ]),
    ...results.map((r) =>
      toCsvRow([
        r.analyteNameOriginal,
        r.analyteShortCode,
        r.valueNumeric,
        r.valueText,
        r.unitNormalised ?? r.unitOriginal,
        r.flagSeverity,
        r.refLow ?? r.referenceRanges?.[0]?.refLow ?? "",
        r.refHigh ?? r.referenceRanges?.[0]?.refHigh ?? "",
        r.reportedDatetimeLocal?.toISOString() ?? "",
      ]),
    ),
  ];
  res.setHeader("Content-Type", "text/csv");
  res.setHeader("Content-Disposition", `attachment; filename="report-${reportId}.csv"`);
  res.send(rows.join("\n"));
});

app.get("/reports/:reportId/results", authMiddleware, async (req: AuthedRequest, res) => {
  const reportId = req.params.reportId;
  try {
    await assertReportAccess(req.user!, reportId);
  } catch {
    return res.status(404).json({ error: "Report not found" });
  }
  const results = await prisma.result.findMany({
    where: { reportId },
    include: { referenceRanges: true },
  });
  res.json({ results });
});

app.post("/mapping-dictionary", authMiddleware, async (req: AuthedRequest, res) => {
  const { analyte_name_pattern, analyte_short_code, analyte_code_standard_system, analyte_code_standard_value, preferred_unit_normalised } =
    req.body || {};
  if (!analyte_name_pattern || !analyte_short_code) {
    return res.status(400).json({ error: "analyte_name_pattern and analyte_short_code are required" });
  }
  const family = await ensureFamilyAccountForUser(req.user!);
  const entry = await prisma.mappingDictionary.upsert({
    where: {
      familyAccountId_analyteNamePattern: {
        familyAccountId: family.id,
        analyteNamePattern: analyte_name_pattern,
      },
    },
    update: {
      analyteShortCode: analyte_short_code,
      analyteCodeStandardSystem: analyte_code_standard_system ?? "custom",
      analyteCodeStandardValue: analyte_code_standard_value ?? null,
      preferredUnitNormalised: preferred_unit_normalised ?? null,
      updatedAtUtc: new Date(),
    },
    create: {
      familyAccountId: family.id,
      analyteNamePattern: analyte_name_pattern,
      analyteShortCode: analyte_short_code,
      analyteCodeStandardSystem: analyte_code_standard_system ?? "custom",
      analyteCodeStandardValue: analyte_code_standard_value ?? null,
      preferredUnitNormalised: preferred_unit_normalised ?? null,
      enabled: true,
      createdByUserId: req.user!.id,
    },
  });
  res.status(201).json({ entry });
});

app.patch("/results/:resultId/confirm-mapping", authMiddleware, async (req: AuthedRequest, res) => {
  const { resultId } = req.params;
  const { analyte_short_code } = req.body || {};
  if (!analyte_short_code) return res.status(400).json({ error: "analyte_short_code required" });
  const result = await prisma.result.findFirst({
    where: { id: resultId },
    include: { patient: true },
  });
  if (!result) return res.status(404).json({ error: "Result not found" });
  try {
    await assertPatientAccess(req.user!, result.patientId);
  } catch {
    return res.status(404).json({ error: "Result not found" });
  }
  const family = await ensureFamilyAccountForUser(req.user!);
  const mapping = await prisma.mappingDictionary.upsert({
    where: {
      familyAccountId_analyteNamePattern: {
        familyAccountId: family.id,
        analyteNamePattern: result.analyteNameOriginal,
      },
    },
    update: { analyteShortCode: analyte_short_code },
    create: {
      familyAccountId: family.id,
      analyteNamePattern: result.analyteNameOriginal,
      analyteShortCode: analyte_short_code,
      analyteCodeStandardSystem: "custom",
      createdByUserId: req.user!.id,
    },
  });

  const updated = await prisma.result.update({
    where: { id: result.id },
    data: {
      analyteShortCode: analyte_short_code,
      mappingMethod: "user_confirmed",
      mappingConfidence: "high",
      mappingDictionaryId: mapping.id,
      mappingConfirmedByUserId: req.user!.id,
      mappingConfirmedAtUtc: new Date(),
    },
  });

  await logAudit({
    entityType: "result",
    entityId: result.id,
    action: "mapping_confirmed",
    userId: req.user!.id,
    payload: { analyte_short_code },
  });

  res.json({ result: updated, mapping });
});

app.patch("/results/:resultId/correction", authMiddleware, async (req: AuthedRequest, res) => {
  const { resultId } = req.params;
  const payload = req.body || {};
  const result = await prisma.result.findFirst({
    where: { id: resultId },
  });
  if (!result) return res.status(404).json({ error: "Result not found" });
  try {
    await assertPatientAccess(req.user!, result.patientId);
  } catch {
    return res.status(404).json({ error: "Result not found" });
  }

  const updated = await prisma.result.update({
    where: { id: result.id },
    data: {
      valueNumeric: payload.value_numeric ?? result.valueNumeric,
      valueText: payload.value_text ?? result.valueText,
      unitOriginal: payload.unit_original ?? result.unitOriginal,
      unitNormalised: payload.unit_normalised ?? result.unitNormalised,
      flagSeverity: payload.flag_severity ?? result.flagSeverity,
      extractionConfidence: payload.extraction_confidence ?? result.extractionConfidence,
      refLow: payload.ref_low ?? result.refLow,
      refHigh: payload.ref_high ?? result.refHigh,
      refText: payload.ref_text ?? result.refText,
      referenceRangeContext: payload.reference_range_context ?? result.referenceRangeContext,
      collectionContext: payload.collection_context ?? result.collectionContext,
    },
  });

  await logAudit({
    entityType: "result",
    entityId: result.id,
    action: "result_corrected",
    userId: req.user!.id,
    payload,
  });

  res.json({ result: updated });
});

app.get("/reports/needs-review", authMiddleware, async (req: AuthedRequest, res) => {
  const reports = await prisma.report.findMany({
    where: {
      parsingStatus: "needs_review",
      patient: { ownerUserId: req.user!.id },
    },
    orderBy: { updatedAtUtc: "desc" },
    include: { patient: true },
  });
  res.json({ reports });
});

const port = Number(process.env.PORT || env.API_PORT || 4000);
app.listen(port, () => {
  logger.info(`API listening on http://localhost:${port}`);
});
