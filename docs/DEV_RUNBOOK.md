# PathoLog - Local Development Runbook

## Prerequisites
- Node.js 20+
- npm
- PostgreSQL running locally (database `patholog` and user with permissions)
- Google OAuth client ID (Web type) for http://localhost:5173

## Backend (`backend`)
1. Copy `.env.example` to `.env` and update:
   - `DATABASE_URL`
   - `AUTH_SECRET` (long random string)
   - `GOOGLE_CLIENT_ID`
   - `FRONTEND_ORIGIN` (default `http://localhost:5173`)
   - Storage: keep `STORAGE_PROVIDER=local` for dev or set S3 values.
2. Install deps: `npm install`
3. Generate migration SQL (already in `prisma/migrations/001_init.sql`); apply with `psql` or `prisma migrate dev`.
4. Start dev server: `npm run dev` (defaults to port 4000).

## Frontend (`frontend`)
1. Copy `.env.example` to `.env` and set `VITE_GOOGLE_CLIENT_ID` and `VITE_API_BASE_URL`.
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## Google Sign-In
- Create OAuth client in Google Cloud Console: Credentials -> OAuth client ID (Web).
- Authorized JavaScript origins: `http://localhost:5173`
- Authorized redirect URI: `http://localhost:5173`
- Paste the client ID into both `.env` files.

## File Storage
- Local mode stores files under `storage/` relative to the repo by default.
- S3 mode requires `S3_BUCKET`, region, credentials, and optional custom endpoint for MinIO.

## Development Workflow
- Backend endpoints: `GET /health`, `POST /auth/google`, `POST /auth/google/link`, `POST /auth/microsoft`, `POST /auth/microsoft/link`, `POST /patients`, `GET/POST /patients/:patientId/reports`.
- Run frontend and backend concurrently; ensure CORS origin matches `.env`.
- Add git commits after completing each stage; push to your remote as needed.
