import pg, { type QueryResultRow } from "pg";
import { env } from "./env.js";

const { Pool } = pg;

export const pool = new Pool({
  connectionString: env.DATABASE_URL,
});

export async function query<T extends QueryResultRow = any>(text: string, params?: any[]) {
  const res = await pool.query<T>(text, params);
  return res;
}
