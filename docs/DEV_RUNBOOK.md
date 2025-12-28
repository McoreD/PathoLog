# PathoLog - Local Development Runbook

## Prerequisites
- Node.js 20+
- npm
- PostgreSQL running locally (database `patholog` and user with permissions)

## API (`api`)
1. Set `DATABASE_URL` (or `DB`) to your Postgres connection string.
2. Optional: `ALLOW_ANONYMOUS_AUTH=true` to bypass SWA auth locally.
3. Build and run Functions locally with Azure Functions Core Tools:
   - `func start`

## Frontend (`frontend`)
1. Copy `.env.example` to `.env` and set `VITE_API_BASE_URL` if needed (defaults to `/api`).
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## Authentication (SWA Built-in)
- Sign-in uses `/.auth/login/aad` and `/.auth/login/google` with `/.auth/me`.
- For local testing, either use the SWA CLI or set `ALLOW_ANONYMOUS_AUTH=true`.

## File Storage
- Files are stored in Postgres `bytea` via the Functions API.

## Development Workflow
- API endpoints: `GET /health`, `GET /me`, `POST /patients`, `GET/POST /patients/:patientId/reports`.
- Run frontend and API concurrently; ensure `VITE_API_BASE_URL` points at the API.
- Add git commits after completing each stage; push to your remote as needed.
