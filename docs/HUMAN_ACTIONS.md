# Human Deployment/Setup Checklist

- [ ] SWA environment variables:
  - `DATABASE_URL` = your Neon connection string (sslmode=require&channel_binding=require)
  - `STORAGE_PROVIDER` = `local` (note: not durable across redeploy/scale; switch to Azure Blob later)
  - `SIGNED_URL_TTL_SECONDS` = optional (default 900)
  - `ALLOW_ANONYMOUS_AUTH` = optional for local testing
- [ ] Run migrations by starting the backend once with `DATABASE_URL` set.
- [ ] SWA authentication:
  - Configure Microsoft (AAD) provider in the Static Web App Authentication settings.
  - Configure Google provider in the Static Web App Authentication settings.
  - Sign-in uses `/.auth/login/aad`, `/.auth/login/google`, and `/.auth/me`.
- [ ] AI (optional):
  - Enter OpenAI API key in the in-app AI settings to enable PDF parsing and short code lookup.
- [ ] GitHub secrets:
  - `AZURE_STATIC_WEB_APPS_API_TOKEN_<...>` for SWA
- [ ] Deploy pipeline:
  - SWA build/upload (frontend + backend Functions) using the SWA token.
- [ ] Custom domain (optional): add to SWA; ensure Google OAuth origins include the SWA/custom domains.
