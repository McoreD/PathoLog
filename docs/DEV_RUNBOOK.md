# PathoLog - Local Development Runbook

## Prerequisites
- Node.js 20+
- npm
- PostgreSQL running locally (database `patholog` and user with permissions)

## Backend (`backend`)
1. Copy `.env.example` to `.env` and update:
   - `DATABASE_URL`
   - Optional: `ALLOW_ANONYMOUS_AUTH=true` to bypass SWA auth locally.
2. Install deps: `npm install`
3. Migrations run on startup from `backend/sql/*.sql`.
4. Start dev server: `npm run dev` (defaults to port 4000).

## Frontend (`frontend`)
1. Copy `.env.example` to `.env` and set `VITE_API_BASE_URL` if needed (defaults to `/api`).
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## Authentication (SWA Built-in)
- Sign-in uses `/.auth/login/aad` and `/.auth/login/google` with `/.auth/me`.
- For local testing, either use the SWA CLI or set `ALLOW_ANONYMOUS_AUTH=true`.

## File Storage
- Files are stored in Postgres `bytea` via the API.

## Development Workflow
- Backend endpoints: `GET /health`, `GET /me`, `POST /patients`, `GET/POST /patients/:patientId/reports`.
- Run frontend and backend concurrently; ensure `VITE_API_BASE_URL` points at the API.
- Add git commits after completing each stage; push to your remote as needed.
