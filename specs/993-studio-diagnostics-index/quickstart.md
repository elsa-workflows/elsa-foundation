# Validation Guide: Studio Diagnostics Index

Run focused checks from the `Studio/` directory after the RED assertions are added and the implementation is ready. The commands below define the required checks; baseline results are recorded separately.

## Prerequisites

- Use the repository's existing installed Studio dependencies and lockfile. This feature requires no install or dependency change.
- Keep the baseline comparison at the approved product source revision, `e656cc6042fd86d15d61c7278d846222054eb535`.
- Use stubbed API responses for component tests so page-state coverage is deterministic.
- For the Playwright fixture, use the actual Studio host/navigation components in the bounded Vite fixture. The fixture does not boot Elsa Workbench or prove a composed host's module registration, session, or backend.

## Focused checks

```bash
cd Studio
pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/overview.test.tsx src/__tests__/navPlacement.test.ts src/__tests__/app-routing.test.ts
pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry exec vitest run src/__tests__/module.test.ts
pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs exec vitest run src/__tests__/module.test.ts
pnpm --filter @elsa-workflows/studio-workflows exec vitest run src/__tests__/runtimeDiagnosticsSettings.test.tsx
pnpm exec playwright test tests/browser/diagnostics-index.spec.ts
```

## Expected evidence

| Check | Expected result |
|---|---|
| Host Diagnostics | Only registered entries that survive the real host access filter are shown. Visible labels and links match the current navigation destinations. A synthetic gated custom entry is omitted when the host filter omits it. Built-ins have accurate task descriptions; a custom entry without task semantics has neutral copy. The empty link message does not imply outage or empty data. Widgets continue to render and isolate failure. |
| Normal and baseline registrations | Check the Studio module manifest separately from backend shell composition. The normal Studio `shells.json` enables OpenTelemetry and Structured Logs; baseline absence uses an isolated copy with only `OpenTelemetryStudio` and `StructuredLogsStudio` omitted from `CShells:Shells:Default:Features`, paired with backend `shells.baseline.json`. Confirm both modules are disabled in the client manifest before asserting that the index omits them. A backend-only baseline tests missing backend support while the frontend routes may remain registered. Runtime evidence settings retains `/workflows/runtime-diagnostics`; listed routes match sidebar/dock destinations where present. |
| OpenTelemetry page DOM | Trace states remain distinct for loading, successful empty, permission, unavailable, and failure responses. Metrics and OpenTelemetry Logs are clearly unavailable on this page before any placeholder can be entered, or the selectable affordances are omitted with an explanation. A direct Structured Logs link appears only when that page can confirm the destination is registered and accessible; otherwise the handoff returns to Diagnostics with neutral wording. |
| Structured Logs page DOM | Loading, empty, permission, unavailable, and failure states are visibly distinct and remain owned by Structured Logs. |
| Runtime evidence settings | View-only users still find and read the page, cannot save, and produce no PUT after attempted edits. Authorized users retain the exact existing save route and payload. No policy or permission changes occur. |
| Browser fixture | Every visible destination is keyboard reachable. At 320, 768, 1024, and 1440 CSS-pixel widths in light and dark themes, descriptions and focus remain usable with no horizontal overflow or clipped content. |

The task-provided untouched-product baseline passed offline installation with the unchanged lockfile and reports Web 563 tests across 49 files, OpenTelemetry 5 tests in 1 file, Structured Logs 10 tests in 1 file, and Workflows 2,124 tests across 167 files. Those counts are comparison baselines, not proof of the new DOM states or composition behavior.

## Composed Workbench comparison

The real-host check is separate from the fixture check and is a required acceptance gate. Root owns the local QA and has commissioned a read-only, source-backed launch and cleanup procedure. That procedure has not yet been incorporated here, so this guide intentionally supplies no live launch command or invented invocation/configuration details.

The source-derived isolation proposal is recorded at `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-isolated-workbench-procedure.md`. It specifies full frontend and host builds, copied configuration in fresh roots, loopback-only binding, isolated SQLite files and package feeds, readiness checks, and process-scoped cleanup. Use its exact source-backed commands after root reviews them. Do not use tracked shell files as mutable run config, add guessed keys, load optional packages, or copy seed credentials into notes/logs.

The two shell layers must be paired correctly. For normal, use the normal Workbench backend shell and the normal Studio module configuration. For baseline absence, use a separate isolated copy of Studio `shells.json` and omit only `OpenTelemetryStudio` and `StructuredLogsStudio` from `CShells:Shells:Default:Features`; pair that with `Essentials/apps/Elsa.Workbench/shells.baseline.json`. Confirm both modules are disabled in the returned Studio module manifest before checking that the index has no links. The backend baseline by itself tests missing backend support, not absent frontend registration.

The default Data Protection key-ring write is isolated through the existing host EF store rather than an invented directory setting. In each copied Workbench config, set `Elsa:DataProtection:EntityFrameworkCore:Enabled=true`, `Provider=Sqlite`, and `ConnectionString` to an absolute private `data/keyring.db` path; give `Elsa:DataProtection:ApplicationName` a different value for each run. This is QA-only copied host configuration. The source basis is `Essentials/src/Foundation/DataProtection/EntityFrameworkCore/Hosting/DataProtectionConfigurationExtensions.cs:57-70` and `Essentials/src/Persistence/EntityFramework/EfHostStoreOptions.cs:7-16`. Root's follow-up source review resolved the proposal's initial caveat through this supported seam; neither this configuration nor the source-derived run has been executed here.

The normal composition supports a seeded disposable development admin; the baseline omits identity and is anonymous, so it validates registration/absence only and cannot prove authenticated permissions or Runtime evidence save rights. Keep read-only/write authorization proof in component tests or separate authenticated normal-composition evidence.

Compare normal and baseline before and after on owned ports 5689 and 5695 with fresh isolated roots and separate SQLite files. The source-derived setup and exact host invocation/readiness/cleanup commands are in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-isolated-workbench-procedure.md`; root owns their execution. Do not read or alter user data or use ports 5589, 5595, or 5089. Verify the client module manifest, navigation links, direct route/sidebar/dock parity, no-tools state, and destination-owned backend states in each composition. Both live compositions, required before/after evidence, and repeatable cleanup must be demonstrated before acceptance. Until those results are captured, the gate stays pending and the feature is not complete. Do not describe fixture evidence as composed-host proof.

The fixture and composed-host checks are distinct evidence tiers. Full affected suites, types, lint/build, architecture/maps, visual/keyboard QA, independent review, exact-head CI, and resulting-main gates remain root-owned.
