# Agent Rules

- Always summarize code changes in the final response.
- Automatically perform `git add .`, `git commit -m "Summary"`, and `git push` after every code update, unless the user specifically asks to review first.

## Semantic Versioning

- Use semantic versioning for all versioned artifacts: MAJOR.MINOR.PATCH.
- MAJOR: breaking changes to public APIs, data contracts, or user-visible behavior.
- MINOR: backward-compatible features or enhancements.
- PATCH: backward-compatible bug fixes or small corrections.
- Pre-release and build metadata follow SemVer 2.0.0 conventions.

## Purpose

- This document provides clear operating instructions for LLM-assisted work in this repository.

## Scope

- Applies to documentation, code, tests, configs, and release notes.
- If a request conflicts with repository guidelines, ask for clarification.

## Communication

- Be concise and factual.
- Prefer short paragraphs and bullet lists.
- Use consistent terminology from existing docs.

## Repository Awareness

- Read existing docs before adding new guidance.
- Avoid duplicating information unless it is a deliberate summary.
- Keep instructions in ASCII unless the target file already uses Unicode.

## Change Safety

- Do not remove or rewrite unrelated content.
- Do not change version numbers unless explicitly requested.
- Flag assumptions clearly when requirements are ambiguous.

## Code and Config Changes

- Follow existing patterns in each project area.
- Keep changes minimal and targeted.
- Add small comments only when necessary to explain non-obvious logic.

## Testing

- If you modify executable code, suggest relevant tests.
- If tests are added, align them with current test conventions.

## Documentation

- Update or add docs when behavior or usage changes.
- Keep filenames and headings descriptive and stable.

## Security and Privacy

- Do not include secrets or tokens.
- Avoid logging sensitive data in examples.

## Debugging

- Never ask the user to check DevTools.
- Instead, surface debug information directly in the web page UI for the user to report back.

## Output Format

- For changes, summarize what changed and where.
- Provide next steps only when they are natural and actionable.

## Deployment URLs

- **web-ts**: https://gentle-desert-0814a2000.2.azurestaticapps.net/
- **web-csharp**: https://delightful-hill-09e4dbc1e.2.azurestaticapps.net/

## TODO

### web-csharp Issues

- Fix "Create patient" 500 error with empty response body when adding patient via `/api/patients` POST endpoint.

### web-ts Issues

- Fix health check error: `Unexpected token '<', "<!DOCTYPE "... is not valid JSON`. Backend is returning HTML instead of JSON.

## Branch mirroring

- Mirror relevant updates to the dev-web-ts and dev-web-csharp branches unless explicitly told not to.
