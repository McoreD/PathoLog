#!/usr/bin/env node

import fs from "fs/promises";
import path from "path";
import { PDFParse } from "pdf-parse";
import { applyMigrations } from "./migrations.js";
import { parsePdfWithAi } from "./ai.js";
import {
  createPatient,
  createReport,
  createSourceFile,
  deleteResultsForReport,
  insertResults,
  listPatients,
  updateReportStatus,
  upsertUserByEmail,
} from "./data.js";
import { logger } from "./logger.js";

type CliArgs = {
  file: string;
  patient: string;
  email: string;
  apply: boolean;
  showText: boolean;
};

function usage() {
  console.log(`
PathoLog CLI - import a PDF and (optionally) store a report

Usage:
  npm run cli -- --file <pdf-path> [--patient "Name"] [--email you@example.com] [--apply] [--show-text]

Defaults:
  --patient defaults to "CLI Patient" or $CLI_PATIENT
  --email defaults to "cli@patholog.dev" or $CLI_USER_EMAIL
  Without --apply, the command runs in dry-run mode and does not touch the database.
`);
}

function parseArgs(argv: string[]): CliArgs {
  if (argv.includes("--help") || argv.includes("-h")) {
    usage();
    process.exit(0);
  }

  let file = "";
  let patient = process.env.CLI_PATIENT || "CLI Patient";
  let email = process.env.CLI_USER_EMAIL || "cli@patholog.dev";
  let apply = false;
  let showText = false;

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    switch (arg) {
      case "--file":
        file = argv[++i] || "";
        break;
      case "--patient":
        patient = argv[++i] || patient;
        break;
      case "--email":
        email = argv[++i] || email;
        break;
      case "--apply":
      case "--write":
        apply = true;
        break;
      case "--show-text":
        showText = true;
        break;
      default:
        break;
    }
  }

  if (!file) {
    throw new Error("Missing --file <path to PDF>");
  }

  return { file, patient, email, apply, showText };
}

async function extractText(buffer: Buffer) {
  try {
    const parser = new PDFParse({ data: buffer });
    const parsed = await parser.getText();
    await parser.destroy();
    return (parsed.text || "").trim();
  } catch (err) {
    logger.warn({ err }, "Failed to parse PDF text");
    return "";
  }
}

async function ensurePatient(userId: string, name: string) {
  const existing = await listPatients(userId);
  const found = existing.find((p) => p.full_name.toLowerCase() === name.toLowerCase());
  if (found) return found;
  return createPatient(userId, name, null, null);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const filePath = path.resolve(args.file);
  const buffer = await fs.readFile(filePath);
  const text = await extractText(buffer);
  const openAiKey = process.env.OPENAI_API_KEY ?? null;
  const geminiKey = process.env.GEMINI_API_KEY ?? null;
  const parsed = await parsePdfWithAi({
    buffer,
    preferredProvider: openAiKey ? "openai" : geminiKey ? "gemini" : "openai",
    openAiKey,
    geminiKey,
  });
  const structured = parsed.results;

  const summary = {
    file: filePath,
    bytes: buffer.length,
    textLength: text.length,
    results: structured.length,
    mode: args.apply ? "apply" : "dry-run",
  };

  console.log("PathoLog CLI summary:", summary);
  if (args.showText) {
    console.log("--- Text preview ---");
    console.log(text.slice(0, 800));
    console.log("--- end preview ---");
  }

  if (!args.apply) {
    console.log("Dry run complete. Use --apply to write to the database.");
    return;
  }

  await applyMigrations();
  const user = await upsertUserByEmail({
    email: args.email,
    fullName: args.email,
    provider: "cli",
    providerUserId: args.email,
  });
  const patient = await ensurePatient(user.id, args.patient);
  const sourceFileId = await createSourceFile(path.basename(filePath), "application/pdf", buffer.length, buffer);
  const report = await createReport(patient.id, sourceFileId);
  await deleteResultsForReport(report.id);
  await insertResults(report.id, patient.id, structured);
  await updateReportStatus(report.id, "completed");

  console.log(
    `Stored report ${report.id} for patient "${patient.full_name}" with ${structured.length} extracted result(s).`,
  );
}

main().catch((err) => {
  console.error("CLI failed", err);
  process.exit(1);
});
