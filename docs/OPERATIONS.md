# Operations, Monitoring, and Security

## Observability
- App Insights (optional): set `APPINSIGHTS_CONNECTION_STRING` in SWA settings; backend auto-starts Application Insights SDK for requests/logs/metrics.
- Logs: Pino to stdout; collect via Function logs or App Insights if enabled.
- Health: `GET /health`.
- Rate limiting: 300 requests / 15 minutes per IP (config in `backend/src/server.ts`).

## Storage
- Files are stored in Postgres `bytea` via the API. Ensure database size limits match expected usage.

## Database (Neon Postgres)
- Backups: Neon provides PITR; configure retention in Neon console. For manual snapshots/restore, use branch snapshots.
- Migrations: applied on backend startup from `backend/sql/*.sql`.

## Security
- Secrets in SWA settings; never commit.
- Auth: SWA built-in Microsoft/Google sign-in via `/.auth/*`.
- Access control: patient/report access tied to owner or family membership; audit logs on mapping confirmations and corrections.
- PDFs: prefer signed URL delivery via Blob; fallback streams only when local storage is used.

## Restore Procedures (outline)
- DB restore: create new Neon branch from PITR time, swap `DATABASE_URL` to new branch, run migrations if needed.
- Storage restore: rely on Blob versioning/soft-delete if enabled; otherwise redeploy from backups.
- Config restore: export SWA configuration (JSON) after changes; keep infra as code where possible.

## Deployment Notes
- SWA: set `DATABASE_URL`, optional `APPINSIGHTS_CONNECTION_STRING`.
- Build commands: backend `npm ci && npm run build`; frontend built by SWA action.
