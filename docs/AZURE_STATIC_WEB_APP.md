# Azure Static Web App & Neon Postgres setup

## Frontend (Azure Static Web Apps)
- Build command: `npm install && npm run build` in `frontend`
- App location: `frontend`
- Output location: `frontend/dist`
- API location: `api` (SWA integrated API)
- In Azure Portal: Static Web App -> Configuration -> Application settings, add:
  - `DATABASE_URL` = Neon Postgres connection string with `sslmode=require`
- Redeploy/trigger build after saving settings.

## Authentication (SWA built-in)
- Enable Microsoft (AAD) and Google providers in Static Web Apps -> Authentication.
- Frontend uses `/.auth/login/aad`, `/.auth/login/google`, and `/.auth/me`.

## Neon Postgres
- Create database in Neon Console.
- Copy connection string (ensure `sslmode=require`) into `DATABASE_URL` app setting.
