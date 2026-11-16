# Implementation Plan: Linen Studio and host branding

**Branch**: `agent/codex/3496-linen-studio` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

## Summary
Implement Linen in the existing workspace-style and theme-role system; keep palette, appearance, navigation and arrangements independent. Add explicitly allow-listed runtime branding/defaults and a configurable image-led backend login renderer with a single callback replacement seam. Authentication behavior stays backend-owned.

## Technical Context
- TypeScript/React 19, CSS shared roles, Vite 7, pnpm 11; .NET 10 ASP.NET Core.
- Existing localStorage appearance keys and host configuration; no database schema change.
- Vitest behavior/rendered tests; Playwright browser checks; xUnit host/identity suites; architecture and maps guards.
- Browser and modular host/package deployments; no CDN required, no new dependency or project.
- Optimize approved login images as bundled local JPEGs; images are decorative and lazy loading is unnecessary above the fold.
- Preserve component instances, controlled designer state, graph handles, read-only boundaries and all meaningful existing tests.

## Constitution Check
Loaded framework v6.1.0 and Elsa v6.3.0 plus the review checklist. No domain dependency changes or new feature classes. Public options and callback context belong to the identity presentation package; a single optional callback is the declared presentation replacement seam, without duplicate DI registrations. Browser config remains an explicit allow-list. Additive options/API expansion; one computed pre-GA package version line remains. Risk-based behavior tests apply; this is an existing-product change, not a new application. No draft/provisional constitutional decision is needed. Repeat the check after implementation and run guards.

## Project Structure
- `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/`: Linen style/palette, persistence and default precedence.
- `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/`: shell/status branding.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`: workflow-owned surface treatments.
- `Studio/src/essentials/Elsa.Studio.Hosting/StudioRuntimeScript.cs`: explicit public runtime settings.
- `Essentials/src/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity/`: login options/context, renderer, bundled public assets and endpoint integration.
- Existing Studio/Identity test projects; `Studio/docs/branding.md` and a runnable host example configuration.

## Delivery
Complete stories incrementally, run focused bite proof then full affected suites, qualify actual hosted UI and a branded consumer, architecture/maps. Commit, draft PR, three independent review perspectives, fix findings and qualify exact head against current main. Ready checks/review and merge under org protocol, then verify descendant main. No skipped/pending check is described as passed.

## Complexity Tracking
No constitution exception or new dependency envelope.
