import fs from "fs/promises";
import path from "path";
import { fileURLToPath } from "url";
import { query } from "./db.js";
import { env } from "./env.js";
import { logger } from "./logger.js";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export async function applyMigrations() {
  if (!env.DATABASE_URL) {
    logger.warn("DATABASE_URL not set; skipping migrations");
    return;
  }
  const sqlDir = path.resolve(__dirname, "..", "sql");
  let entries: string[] = [];
  try {
    entries = (await fs.readdir(sqlDir)).filter((f) => f.toLowerCase().endsWith(".sql"));
  } catch (err) {
    logger.warn({ err }, "SQL migrations directory not found; skipping migrations");
    return;
  }

  if (!entries.length) {
    logger.info("No SQL migrations found");
    return;
  }

  await query("create table if not exists schema_migrations(version text primary key, applied_at timestamptz not null default now())");
  const appliedRes = await query<{ version: string }>("select version from schema_migrations");
  const applied = new Set(appliedRes.rows.map((r) => r.version));

  const files = entries.sort((a, b) => a.localeCompare(b, "en"));
  for (const file of files) {
    if (applied.has(file)) {
      continue;
    }
    const fullPath = path.join(sqlDir, file);
    const sql = await fs.readFile(fullPath, "utf-8");
    if (!sql.trim()) {
      logger.warn({ file }, "Skipping empty migration");
      continue;
    }
    logger.info({ file }, "Applying migration");
    await query(sql);
    await query("insert into schema_migrations(version) values($1)", [file]);
  }
}
