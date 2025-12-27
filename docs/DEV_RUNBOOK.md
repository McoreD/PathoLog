# PathoLog - Local Development Runbook

## Prerequisites
- Node.js 20+
- npm
- PostgreSQL running locally (database `patholog` and user with permissions)

## Backend (`backend`)
1. Copy `.env.example` to `.env` and update:
   - `DATABASE_URL`
   - Storage: keep `STORAGE_PROVIDER=local` for dev or set S3 values.
   - Optional: `ALLOW_ANONYMOUS_AUTH=true` to bypass SWA auth locally.
2. Install deps: `npm install`
3. Generate migration SQL (already in `prisma/migrations/001_init.sql`); apply with `psql` or `prisma migrate dev`.
4. Start dev server: `npm run dev` (defaults to port 4000).

## Frontend (`frontend`)
1. Copy `.env.example` to `.env` and set `VITE_API_BASE_URL` if needed (defaults to `/api`).
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## Authentication (SWA Built-in)
- Sign-in uses `/.auth/login/aad` and `/.auth/login/google` with `/.auth/me`.
- For local testing, either use the SWA CLI or set `ALLOW_ANONYMOUS_AUTH=true`.

## File Storage
- Local mode stores files under `storage/` relative to the repo by default.
- S3 mode requires `S3_BUCKET`, region, credentials, and optional custom endpoint for MinIO.

## Development Workflow
- Backend endpoints: `GET /health`, `GET /me`, `POST /patients`, `GET/POST /patients/:patientId/reports`.
- Run frontend and backend concurrently; ensure `VITE_API_BASE_URL` points at the API.
- Add git commits after completing each stage; push to your remote as needed.
