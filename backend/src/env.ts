import { z } from "zod";

const envSchema = z.object({
  NODE_ENV: z.string().default("development"),
  API_PORT: z.string().default("4000"),
  DATABASE_URL: z.string(),
  AUTH_SECRET: z.string().min(30, "AUTH_SECRET should be long and random"),
  GOOGLE_CLIENT_ID: z.string(),
  FRONTEND_ORIGIN: z.string().url(),
  STORAGE_PROVIDER: z.enum(["local", "s3"]).default("local"),
  LOCAL_STORAGE_PATH: z.string().default("../storage"),
  S3_REGION: z.string().optional(),
  S3_BUCKET: z.string().optional(),
  S3_ENDPOINT: z.string().optional(),
  S3_ACCESS_KEY_ID: z.string().optional(),
  S3_SECRET_ACCESS_KEY: z.string().optional(),
  SIGNED_URL_TTL_SECONDS: z.coerce.number().min(60).max(60 * 60 * 24).default(900),
  APPINSIGHTS_CONNECTION_STRING: z.string().optional(),
});

const parsed = envSchema.safeParse(process.env);

if (!parsed.success) {
  console.error("Invalid environment variables", parsed.error.flatten().fieldErrors);
  process.exit(1);
}

export const env = parsed.data;
