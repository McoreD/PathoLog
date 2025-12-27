# Operations, Monitoring, and Security

## Observability
- App Insights (optional): set `APPINSIGHTS_CONNECTION_STRING` in App Service; backend auto-starts Application Insights SDK for requests/logs/metrics.
- Logs: Pino to stdout; collect via App Service log streaming or App Insights if enabled.
- Health: `GET /health`.
- Rate limiting: 300 requests / 15 minutes per IP (config in `backend/src/server.ts`).

## Storage
- For production use Azure Blob via S3-compatible endpoint:
  - `STORAGE_PROVIDER=s3`
  - `S3_ENDPOINT=https://<account>.blob.core.windows.net`
  - `S3_BUCKET=<container>`
  - `S3_REGION=<region>`
  - `S3_ACCESS_KEY_ID=<storage account name>`
  - `S3_SECRET_ACCESS_KEY=<key or SAS>`
- Signed URLs: enabled automatically when using S3/Blob; TTL controlled by `SIGNED_URL_TTL_SECONDS`. Local storage is non-durable; avoid in production.

## Database (Neon Postgres)
- Backups: Neon provides PITR; configure retention in Neon console. For manual snapshots/restore, use branch snapshots.
- Migrations: applied via `npx prisma db execute` (raw SQL) or future Prisma migrations; ensure CI applies before deploy.

## Security
- Secrets in App Service/SWA settings; never commit.
- CORS: allow only SWA/custom domains on the API.
- Auth: Google sign-in; session cookie HTTP-only, sameSite=lax.
- Access control: patient/report access tied to owner or family membership; audit logs on mapping confirmations and corrections.
- PDFs: prefer signed URL delivery via Blob; fallback streams only when local storage is used.

## Restore Procedures (outline)
- DB restore: create new Neon branch from PITR time, swap `DATABASE_URL` to new branch, run migrations if needed.
- Storage restore: rely on Blob versioning/soft-delete if enabled; otherwise redeploy from backups.
- Config restore: export App Service and SWA configuration (JSON) after changes; keep infra as code where possible.

## Deployment Notes
- SWA: set `VITE_API_BASE_URL`, `VITE_GOOGLE_CLIENT_ID`.
- API (App Service): set `DATABASE_URL`, `AUTH_SECRET`, `GOOGLE_CLIENT_ID`, `FRONTEND_ORIGIN`, storage vars, optional `APPINSIGHTS_CONNECTION_STRING`.
- Build commands: backend `npm ci && npm run build`; start `node dist/server.js`. Frontend built by SWA action.
