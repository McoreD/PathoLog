# PathoLog - Local Development Runbook

## Prerequisites
- Node.js 20+
- npm
- PostgreSQL running locally (database `patholog` and user with permissions)

## Repository Layout
- `apps/web-ts` = Vite/React frontend + Node Azure Functions API.
- `apps/web-csharp` = C# Azure Functions API (from the csharp branch).
- `apps/wpf` = WPF desktop app (planned).
- `src/shared-dotnet` = shared C# libraries for `web-csharp` + `wpf` (planned).

## Backend (`apps/web-ts/backend`)
1. Copy `.env.example` to `.env` and update:
   - `DATABASE_URL`
   - Optional: `ALLOW_ANONYMOUS_AUTH=true` to bypass SWA auth locally.
2. Install deps: `npm install`
3. Migrations run on startup from `apps/web-ts/backend/sql/*.sql`.
4. Start dev server: `npm run dev` (defaults to port 4000).

## Frontend (`apps/web-ts/frontend`)
1. Copy `.env.example` to `.env` and set `VITE_API_BASE_URL` if needed (defaults to `/api`).
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## Frontend (`apps/web-csharp/frontend`)
1. Copy `.env.example` to `.env` and set `VITE_API_BASE_URL` to point at the C# API if needed.
2. Install deps: `npm install`
3. Start dev server: `npm run dev` (Vite default port 5173).

## CLI (`apps/web-ts/backend`)
- Dry run parse (no DB writes): `npm run cli --workspace apps/web-ts/backend -- --file ./path/to/report.pdf`
- Save to DB: ensure `DATABASE_URL` is set, then run `npm run cli --workspace apps/web-ts/backend -- --file ./path/to/report.pdf --patient "Name" --email you@example.com --apply`
- `--show-text` prints the first 800 characters of extracted text; defaults to dry-run for safety.

## CLI (`apps/cli`)
- Dry run PDF parse to JSON-like output: `dotnet run --project apps/cli/PathoLog.Cli.csproj -- --file "C:\path\report.pdf" [--show-text]`
- Save structured JSON locally (AppData\PathoLog\cli-reports): add `--save`; optional `--patient "<name>"` and `--email you@example.com`.
## Authentication (SWA Built-in)
- Sign-in uses `/.auth/login/aad` and `/.auth/login/google` with `/.auth/me`.
- For local testing, either use the SWA CLI or set `ALLOW_ANONYMOUS_AUTH=true`.

## File Storage
- Files are stored in Postgres `bytea` via the API.

## Development Workflow
- Backend endpoints: `GET /health`, `GET /me`, `POST /patients`, `GET/POST /patients/:patientId/reports`.
- Run frontend and backend concurrently; ensure `VITE_API_BASE_URL` points at the API.
- Add git commits after completing each stage; push to your remote as needed.
