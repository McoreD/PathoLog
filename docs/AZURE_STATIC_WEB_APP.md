# Azure Static Web App & Neon Postgres setup

## Frontend (Azure Static Web Apps)
- Build command: `npm install && npm run build` in `apps/web-ts/frontend`
- App location: `apps/web-ts/frontend`
- Output location: `apps/web-ts/frontend/dist`
- API location: `apps/web-ts/backend` (SWA integrated API)
- In Azure Portal: Static Web App -> Configuration -> Application settings, add:
  - `DATABASE_URL` = Neon Postgres connection string with `sslmode=require`
- Redeploy/trigger build after saving settings.

## Authentication (SWA built-in)
- Enable Microsoft (AAD) and Google providers in Static Web Apps -> Authentication.
- Frontend uses `/.auth/login/aad`, `/.auth/login/google`, and `/.auth/me`.

## Neon Postgres
- Create database in Neon Console.
- Copy connection string (ensure `sslmode=require`) into `DATABASE_URL` app setting.
- Migrations run on backend startup from `apps/web-ts/backend/sql/*.sql`.
