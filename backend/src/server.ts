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

const storage = createStorage();
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 25 * 1024 * 1024 } });

const app = express();
app.use(cors({ origin: env.FRONTEND_ORIGIN, credentials: true }));
app.use(express.json({ limit: "5mb" }));
app.use(cookieParser());

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

app.get("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  const patients = await prisma.patient.findMany({
    where: { ownerUserId: req.user!.id },
    orderBy: { createdAtUtc: "desc" },
  });
  res.json({ patients });
});

app.post("/patients", authMiddleware, async (req: AuthedRequest, res) => {
  const { fullName, dob, sex } = req.body;
  if (!fullName) {
    return res.status(400).json({ error: "fullName is required" });
  }
  const patient = await prisma.patient.create({
    data: {
      fullName,
      dob: dob ? new Date(dob) : null,
      sex: sex ?? null,
      ownerUserId: req.user!.id,
    },
  });
  res.status(201).json({ patient });
});

app.get("/patients/:patientId/reports", authMiddleware, async (req: AuthedRequest, res) => {
  const patientId = req.params.patientId;
  const patient = await prisma.patient.findFirst({
    where: { id: patientId, ownerUserId: req.user!.id },
  });
  if (!patient) {
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
    const patient = await prisma.patient.findFirst({
      where: { id: patientId, ownerUserId: req.user!.id },
    });
    if (!patient) {
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

    res.status(201).json({ report, sourceFile });
  },
);

const port = Number(env.API_PORT || 4000);
app.listen(port, () => {
  logger.info(`API listening on http://localhost:${port}`);
});
