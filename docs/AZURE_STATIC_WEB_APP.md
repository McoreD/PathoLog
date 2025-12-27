# Azure Static Web App & Neon Postgres setup

## Frontend (Azure Static Web Apps)
- Build command: `npm install && npm run build` in `frontend`
- App location: `frontend`
- Output location: `frontend/dist`
- API location: `backend` (SWA integrated API)
- In Azure Portal: Static Web App -> Configuration -> Application settings, add:
  - `DATABASE_URL` = Neon Postgres connection string with `sslmode=require`
  - `STORAGE_PROVIDER` = `local` (or S3 values below)
- Redeploy/trigger build after saving settings.

## Authentication (SWA built-in)
- Enable Microsoft (AAD) and Google providers in Static Web Apps -> Authentication.
- Frontend uses `/.auth/login/aad`, `/.auth/login/google`, and `/.auth/me`.

## Storage (optional S3)
- Storage (Azure Blob via S3-compatible SDK):
  - `STORAGE_PROVIDER` = `s3`
  - `S3_ENDPOINT` = `https://<storage-account>.blob.core.windows.net`
  - `S3_BUCKET` = `<container-name>`
  - `S3_REGION` = `westeurope` (or your region, required by client)
  - `S3_ACCESS_KEY_ID` = storage account name
  - `S3_SECRET_ACCESS_KEY` = storage account access key (or SAS key)

## Neon Postgres
- Create database in Neon Console.
- Copy connection string (ensure `sslmode=require`) into `DATABASE_URL` app setting.
- Run migrations from CI or local: `cd backend && npx prisma migrate deploy` (or `prisma migrate dev` for dev).
