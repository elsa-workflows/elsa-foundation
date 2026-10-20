# Implementation Plan: Studio Diagnostics Index

**Branch**: `agent/codex/3073-diagnostics-index` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Status**: Approved for task generation; implementation and acceptance evidence remain pending.

**Input**: Approved feature specification in `specs/993-studio-diagnostics-index/spec.md`.

## Summary

Add a task-oriented Diagnostics index over diagnostic destinations registered and exposed by Studio's filtered navigation. In `App.tsx`, apply the existing `filterAccessibleNavigation` helper to the navigation and access values already computed there, then pass the filtered list to Diagnostics. Keep ShellFrame's sidebar/dock filtering and direct-route resolution unchanged; do not refactor the surrounding App or change the SDK. Describe built-in tasks locally and use neutral wording for custom entries without task semantics. Keep backend, permission, loading, failure, and empty-data outcomes in their destination pages. Explain unsupported OpenTelemetry Metrics and Logs both in the index and on the OpenTelemetry page. Offer a direct application-log link only when its destination is confirmed registered and accessible; otherwise use neutral Diagnostics handoff wording. Rename capture-policy discovery to Runtime evidence settings while preserving its route, view access, save gate, policy, and payload. Preserve contributed widgets and their states.

No API, SDK, storage, dependency, backend capability, telemetry ingestion, deployment, or module-composition contract changes are planned.

## Technical Context

**Language/Version**: TypeScript with the repository's existing React and Studio toolchain.

**Primary Dependencies**: Existing Elsa Studio host, navigation contributions and access-filtering helpers, OpenTelemetry and Structured Logs destination modules, Workflows settings page, Vitest, and Playwright. No new dependency.

**Storage**: None added or changed. Existing capture-policy storage and APIs remain unchanged.

**Testing**: Focused Vitest component/host tests for visible navigation and destination states; Playwright against a bounded fixture using actual host/navigation components; and required live before/after checks against both normal and baseline Workbench compositions. Fixture results are not composed-host evidence. Composition checks remain pending until the source-backed isolation procedure is established and executed.

**Target Platform**: Elsa Studio web client; keyboard use and light/dark themes at 320, 768, 1024, and 1440 CSS pixels.

**Project Type**: Existing modular web application with host and contributed Studio feature modules.

**Performance Goals**: No new endpoint probes, polling, or requests on the Diagnostics index. This change has no performance objective beyond preserving existing destination request behavior.

**Constraints**: `App.tsx` already derives navigation and access. Pass its filtered list to Diagnostics using `filterAccessibleNavigation`; retain ShellFrame's current sidebar/dock filtering and route matching. Do not duplicate access rules, change direct-route behavior, or infer backend availability from route registration, widget count, or overview counts. Do not add built-in permissions or custom contribution metadata. Preserve destination/API gates. Do not activate optional modules beyond the normal composition already described by the spec.

**Scale/Scope**: One host Diagnostics surface, the existing OpenTelemetry and Structured Logs destination pages, and the existing Runtime evidence settings discovery label. Both normal and baseline compositions and existing custom entries/widgets are covered. No unrelated routes are discovered by name.

## Constitution Check

*Gate: evaluated before research and again after design against both constitutions and `docs/contributing/review-checklist.md`.*

| Gate | Plan assessment |
|---|---|
| Framework §2.1, §2.2, §2.6 | Pass. No domain type, package, namespace, feature dependency, or project boundary is added. The host consumes existing navigation contributions. |
| Framework §2.21, §2.23.1, §2.23.2 | Pass. Tests first assert user-visible behavior at host and destination seams; current valid widget, subscription, route, classifier, and save-payload behavior remains covered. |
| Framework §2.23.4 | Pass. Tests exercise the real host filtering path with a synthetic gated custom entry; they do not reproduce access rules in a stub. |
| Framework §2.22 | No catalog or extension-point changes are planned; existing contributions and their published contracts remain unchanged. |
| Framework §2.24 | Draft and excluded from ratification; no new pattern is introduced. |
| Elsa §E2.2.3, §E2.6 | Pass. Normal and baseline behavior is checked with isolated copied host configurations; no tracked module composition or runtime behavior is changed. No design/runtime coupling is introduced. |
| Elsa §E5 | Pass. No packages, package versions, or public contracts change. |
| Elsa §E6 | Pass. No Elsa-owned types are added or renamed. |

No gate violation or complexity exception is identified. The live-host launch and cleanup procedure is still a validation-preparation item, not a product-design clarification. The required normal/baseline composed-host gate is pending until that procedure is source-backed, reviewed, and executed. If the procedure remains unresolved, acceptance remains unproven; this does not waive the composed-host check and fixture evidence cannot substitute.

**Post-design re-check**: Pass. The data-model describes transient UI projections and existing destination states only; there is no new persisted or public model, interface, feature, or backend contract.

## Design and Validation Sequence

1. **Make behavior tests fail against the current UI before product edits.** Extend the existing Diagnostics host test to receive the filtered navigation list that `App.tsx` derives from its existing `navigation` and `access` values. Verify exact label/route parity, known built-in task descriptions, neutral custom-entry copy, and the no-links state while retaining widgets. Use a synthetic feature/permission-gated custom child to exercise the existing helper; do not add new built-in gates or move route resolution into Diagnostics.
2. **Add destination DOM assertions.** Render the OpenTelemetry page and assert unsupported Metrics/Logs are explained before entry, its Structured Logs link is conditional and truthful, and its supported trace loading, empty, permission, unavailable, and failure outcomes remain distinct. Render Structured Logs and assert its loading, empty, permission, unavailable, and failure outcomes. Keep existing classifier tests as preservation checks, not as DOM proof.
3. **Lock the settings boundary.** Assert the discovery label while retaining `/workflows/runtime-diagnostics`; assert that a view-only user can still read the settings and cannot save or issue a PUT; retain the authorized manager's exact existing save route and payload assertion.
4. **Implement the minimal UI change.** At the existing `App.tsx` render call, pass `filterAccessibleNavigation(navigation, access)` to Diagnostics. Add task links from that list; preserve custom labels/routes/gates, ShellFrame sidebar/dock behavior, direct-route behavior, and widgets. Mark unsupported OpenTelemetry choices truthfully. Offer a direct Structured Logs link only when its registration and access are established; otherwise use neutral wording linking to `/diagnostics`. Update only the settings discovery label and reuse existing theme/layout primitives. Do not reorganize App or add SDK metadata.
5. **Check the host fixture in a browser.** Cover keyboard access, actual link targets, light and dark themes, and widths 320, 768, 1024, and 1440, including no horizontal overflow or clipped descriptions. This fixture proves rendered UI only.
6. **Run focused package checks and root-owned integration gates.** Use the commands in [quickstart.md](quickstart.md); root owns the full affected suites, type/lint/build checks, maps/architecture checks, independent review, visual/keyboard QA, exact-head CI, and resulting-main gates.
7. **Complete the required composed-host comparison.** The backend shell and Studio client feature configuration are separate: Workbench `shells.baseline.json` alone does not remove the OpenTelemetry and Structured Logs Studio modules. For baseline absence proof, use a fresh isolated Studio content root with a source-backed copy of `Studio/src/apps/Elsa.Studio.Web/shells.json` that omits only `OpenTelemetryStudio` and `StructuredLogsStudio` from `CShells:Shells:Default:Features`, preserving all other client features. Pair it with the baseline Workbench backend shell and verify the Studio module manifest reports both modules disabled before asserting no index links. The normal run uses the normal Studio feature config and backend shell. Follow the separate source-backed procedure for fresh roots, databases, disposable identity setup, invocation, readiness, and cleanup; the procedure is linked in [quickstart.md](quickstart.md). In each copied Workbench host config, enable the existing EF Data Protection key store with SQLite and an absolute per-run key database, and set a distinct `Elsa:DataProtection:ApplicationName` for each isolated run. This is QA-only host configuration, backed by `AddConfiguredDataProtection` and `EfHostStoreOptions`; it introduces no product or permission contract. Compare normal and baseline before and after on owned ports 5689/5695. The normal composition supports a seeded disposable development admin. The baseline omits identity, so its anonymous run validates registration/absence only; it cannot prove authenticated permission filtering or write access. Keep permission and Runtime evidence read-only/write proof in separate component or authenticated normal-composition evidence. Verify direct routes, sidebar/dock destinations, the no-tools state, and destination-owned backend states. Never use user ports 5589/5595/5089 or user data. This is an acceptance gate, not optional evidence: until both composed runs and safe cleanup are demonstrated, acceptance remains pending and fixture evidence cannot substitute.

## Project Structure

### Feature documentation

```text
specs/993-studio-diagnostics-index/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
└── quickstart.md
```

There is no `contracts/` artifact because this feature adds no external interface, API, wire shape, SDK field, or public contribution contract. Task descriptions are local host presentation; existing route, access, settings, and destination behavior remains authoritative.

### Source and test locations

```text
Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx
Studio/src/apps/Elsa.Studio.Web/Client/src/app/navigation/placement.ts
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/navPlacement.test.ts
Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/app-routing.test.ts
Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx
Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts
Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx
Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage.tsx
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/runtimeDiagnosticsSettings.test.tsx
Studio/tests/browser/diagnostics-index.spec.ts       # planned fixture check
Studio/tests/browser/diagnosticsIndexFixture.tsx     # planned bounded host fixture
```

The host access rule at `Studio/src/apps/Elsa.Studio.Web/Client/src/app/navigation/placement.ts` is consumed and tested, not duplicated. `App.tsx` passes the filtered result to Diagnostics while ShellFrame continues its current navigation rendering and filtering. No product code is part of this planning artifact; root reviews the plan before task generation.

## Complexity Tracking

No constitution exceptions or additional architectural complexity are proposed.
