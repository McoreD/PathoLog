# Azure Static Web App & Neon Postgres setup

## Frontend (Azure Static Web Apps)
- Build command: `npm install && npm run build` in `frontend`
- App location: `frontend`
- Output location: `frontend/dist`
- API location: leave blank (API hosted separately on App Service)
- In Azure Portal: Static Web App -> Configuration -> Application settings, add:
  - `VITE_API_BASE_URL` = `https://<your-appservice>.azurewebsites.net`
  - `VITE_GOOGLE_CLIENT_ID` = your Google OAuth Web client ID
- Redeploy/trigger build after saving settings.

## Backend (Azure App Service for Node)
- Deployment command: `npm ci && npm run build` in `backend`, start command `node dist/server.js`.
- App settings (Configuration -> Application settings):
  - `PORT` (Azure supplies automatically, ensure server uses `process.env.PORT`)
  - `API_PORT` (optional fallback)
  - `DATABASE_URL` = Neon Postgres connection string with `sslmode=require`
  - `AUTH_SECRET` = long random secret
  - `GOOGLE_CLIENT_ID` = same as frontend
  - `FRONTEND_ORIGIN` = `https://<your-static-web-app>.azurestaticapps.net`
  - Storage (Azure Blob via S3-compatible SDK):
    - `STORAGE_PROVIDER` = `s3`
    - `S3_ENDPOINT` = `https://<storage-account>.blob.core.windows.net`
    - `S3_BUCKET` = `<container-name>`
    - `S3_REGION` = `westeurope` (or your region, required by client)
    - `S3_ACCESS_KEY_ID` = storage account name
    - `S3_SECRET_ACCESS_KEY` = storage account access key (or SAS key)
- CORS: App Service -> CORS -> allow the Static Web App origin.
- Monitoring: enable Application Insights; logs flow via stdout from Pino.

## Neon Postgres
- Create database in Neon Console.
- Copy connection string (ensure `sslmode=require`) into `DATABASE_URL` app setting.
- Run migrations from CI or local: `cd backend && npx prisma migrate deploy` (or `prisma migrate dev` for dev).
