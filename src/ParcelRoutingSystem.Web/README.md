# Parcel Routing System Web

React and TypeScript operator interface for the parcel routing system.

## Commands

- `npm run dev` — start the local Vite development server.
- `npm run typecheck` — validate TypeScript without generating output.
- `npm run lint` — run Oxlint.
- `npm run build` — type-check and create the production frontend bundle.
- `npm run preview` — serve the built bundle for local inspection.

## Responsibility

This project owns accessible parcel entry, batch upload, progress, results, approvals, and rule-administration experiences. It must not duplicate backend routing rules.

Overview, New parcel, Import XML, Insurance, Routing rules, and Activity use
same-origin `/api` contracts. Vite proxies `/api` and `/health` to the configured
development API. The browser stores no credentials or parcel payloads and never
calculates a department.
