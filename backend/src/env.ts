import { z } from "zod";

const envSchema = z.object({
  NODE_ENV: z.string().default("development"),
  API_PORT: z.string().default("4000"),
  DATABASE_URL: z.string().optional(),
  FRONTEND_ORIGIN: z.string().url().optional(),
  ALLOW_ANONYMOUS_AUTH: z.string().optional(),
  APPINSIGHTS_CONNECTION_STRING: z.string().optional(),
});

const parsed = envSchema.safeParse(process.env);

if (!parsed.success) {
  console.error("Invalid environment variables", parsed.error.flatten().fieldErrors);
  process.exit(1);
}

export const env = parsed.data;

if (!env.DATABASE_URL) {
  console.warn("DATABASE_URL is not set; API calls will fail until it is configured.");
}
