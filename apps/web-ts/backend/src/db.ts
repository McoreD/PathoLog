import pg, { type QueryResultRow } from "pg";
import { env } from "./env.js";

const { Pool } = pg;

export const pool = new Pool({
  connectionString: env.DATABASE_URL,
});

export async function query<T extends QueryResultRow = any>(text: string, params?: any[]) {
  if (!env.DATABASE_URL) {
    throw new Error("DATABASE_URL is not configured");
  }
  const res = await pool.query<T>(text, params);
  return res;
}
