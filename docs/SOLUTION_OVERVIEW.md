# PathoLog Solution Overview

This document captures a high-level understanding of the current solution
structure and how the main components fit together.

## Repository layout
- `apps/web-ts`: Vite/React frontend + Node Azure Functions backend (primary SWA
  stack).
- `apps/web-csharp`: Vite/React frontend + C# Azure Functions API (parallel stack).
- `apps/wpf`: WPF desktop UI shell that consumes shared .NET libraries.
- `apps/cli`: .NET CLI for local PDF parsing and optional local JSON persistence.
- `src/shared-dotnet`: reusable C# libraries for domain, contracts, extraction,
  mapping, persistence, and trending.
- `src/shared-prompts`: shared AI prompt assets.

## Web (TypeScript) stack
- Frontend: Vite/React in `apps/web-ts/frontend`.
- Backend: Node Azure Functions in `apps/web-ts/backend`.
- Deployment: Azure Static Web Apps via
  `.github/workflows/azure-static-web-apps-gentle-desert-0814a2000.yml`.
- Database: Postgres (Neon); migrations run on startup from
  `apps/web-ts/backend/sql/*.sql`.

## Web (C#) stack
- Frontend: Vite/React in `apps/web-csharp/frontend`.
- Backend: C# Azure Functions API in `apps/web-csharp/api`.
- Startup: runs DB migrations on startup; uses SWA `x-ms-client-principal`
  authentication (optional anonymous bypass).

## WPF app
- `apps/wpf` is a desktop UI shell; business logic lives in `src/shared-dotnet`.
- PDF extraction uses PdfPig; OCR uses Tesseract with `tessdata` at runtime.

## CLI
- `apps/cli` provides a .NET CLI for local PDF parsing; can save output locally.

## Auth and data contracts
- SWA built-in auth expected; local bypass via `ALLOW_ANONYMOUS_AUTH=true`.
- Files are stored in Postgres `bytea` via the API.
- Parsed report JSON contract is enforced server-side; normalization/mapping
  occurs after validation.

## Ops notes
- Logs to stdout; optional App Insights.
- Node backend includes rate limiting in `apps/web-ts/backend/src/server.ts`.
