# Validation Guide: Studio Diagnostics Index

Run focused checks from the `Studio/` directory after the RED assertions are added and the implementation is ready. The commands below define the required checks; baseline results are recorded separately.

## Prerequisites

- Use the existing workspace versions. OpenTelemetry declares the same `react-dom` and `@types/react-dom` test dev dependencies as Structured Logs; after the offline lockfile update, verify installation with `pnpm install --offline --frozen-lockfile` from `Studio/`. No runtime dependency or new library version is added.
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

This is a separate, required gate. Build the complete frontend workspace, then the Workbench and Studio host projects. Use copied source configuration in fresh owned roots, loopback ports 5695/5689, and private SQLite/key stores. Preserve existing features and do not invoke optional providers or modify tracked/user configuration. The reviewed source-backed procedure and root execution amendments are in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-isolated-workbench-procedure.md`.

For normal, pair Workbench's normal shell with Studio's normal module configuration and sign in with its disposable Development identity. For baseline absence, pair `Essentials/apps/Elsa.Workbench/shells.baseline.json` with a copied Studio shell omitting only `OpenTelemetryStudio` and `StructuredLogsStudio`. Verify both are disabled in the returned `/_elsa/studio/modules` manifest. Backend baseline alone does not remove frontend registrations. Baseline has no matching seeded identity and proves anonymous composition only, not authenticated permission behavior. It retains Runtime evidence settings and Console, so it is not a zero-link case.

Copy Studio's JSONC base appsettings byte-for-byte; write overrides only in its valid Development JSON. Set both existing Studio backend URL values to the owned backend. Copy the Workbench JSON settings and set `ConnectionStrings:Elsa` and normal Diagnostics connection strings to absolute private databases. Replace CORS/normal identity return origins with the owned Studio origin. Disable package loading/reconciliation and directory watching, point existing feeds at empty owned package roots, and keep backend console-log streaming disabled for QA. Do not invent a package management key or activate an agent/provider.

Isolate Data Protection through the existing `Elsa:DataProtection:EntityFrameworkCore` configuration: Enabled true, Provider Sqlite, absolute private keyring connection; use a distinct ApplicationName per profile. The source seam is `Essentials/src/Foundation/DataProtection/EntityFrameworkCore/Hosting/DataProtectionConfigurationExtensions.cs`. Confirm the private keyring table without reading key material. No HOME override or user keyring write is needed.

Launch with explicit `--contentRoot`, loopback `--urls`, and `--no-launch-profile`; require backend `/health/ready` and Studio assets/manifest to return 200. Compare before and after navigation, actual keyboard destinations and each destination's own states. Preserve request method/path/status/content type without bearer/cookie material. For settings, use only the private normal database, record the existing payload and restore its original policy after the writer check. Restricted/no-write behavior needs its separate component or authenticated-principal proof.

After each profile, close its browser, terminate only exact owned host PIDs after checking their command lines, wait for their sessions, confirm both ports free, then remove only the validated runtime config/data roots. Retain evidence separately. The before and after roots must not share databases; the normal after profile was restarted only after a tool interruption authoritatively ended its handles, with state re-read before repeating any write.

Root completed paired before/after proof and cleanup. After evidence is in `execution/3073-live-after/` under the external audit root above. Normal shows all four expected task links; baseline disables both optional telemetry modules and retains the other two. All sixteen composed index width/theme cases have no scoped axe violations or document overflow. Keyboard destinations match current routes and navigation. Normal settings save/restore succeeds; view-only/no-PUT behavior is separately tested. Full affected suites, frontend tools, both host builds, architecture and maps pass; task receipts record counts and failure/recovery limits.

Two pre-existing functional defects remain independently tracked: #3131 for the OpenTelemetry request/DTO contract mismatch and #3132 for Structured Logs stream credentials. They block final qualification #3075. Index registration, destination error-state wording and scoped accessibility results do not establish working trace inspection/live log streaming or complete shell accessibility. Final exact-head reviews/CI, merge and resulting-main qualification remain required.

## Architecture prerequisite

The architecture guard requires evaluated dependency assets in both configurations. Use the same repository procedure as CI before its suite:

```bash
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

A missing evaluated Debug assets error is a setup failure, not a passed guard. Preserve that failure and run the required setup before requalification. Keep heavy checks serial and respect the machine's build-slot wrapper.
