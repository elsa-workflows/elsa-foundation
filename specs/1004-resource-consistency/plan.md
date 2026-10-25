# Implementation Plan: Studio resource-list consistency and Home access

**Branch**: `agent/codex/3071-resource-consistency` | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1004-resource-consistency/spec.md`

## Summary

Align the Secrets and Connections page hierarchy around the approved Studio list pattern while preserving each page's distinct row entry and operation behavior. Preserve Secrets' bounded inventory, server search, loaded-row facets, and query-state semantics; keep Connection creation in the accepted search context; gate only Home's Needs attention link using the existing effective permission matcher; and make attempted writes observable in the metadata-only Secrets browser fixture. The changes are confined to existing Studio UI and browser test surfaces. No backend/API contract, dependency, module, or global authorization change is needed.

## Technical Context

**Language/Version**: TypeScript / React; use repository-pinned Studio toolchain.

**Primary Dependencies**: Existing Studio shared controls and permission matcher; no dependency additions.

**Storage**: Existing Elsa Secrets and Connections endpoints; no storage changes.

**Testing**: Existing Vitest suites for affected Studio modules and Playwright Chromium browser suites; full Studio typecheck, lint/style checks, existing build groups, architecture/maps checks, and safe private-host proof as qualified below. No tests or builds have run for this draft.

**Target Platform**: Studio web client and its existing browser test harness.

**Project Type**: Existing multi-project repository; change is limited to existing Studio frontend surfaces.

**Performance Goals**: No performance change or new performance claim.

**Constraints**: Preserve existing endpoint contracts, operation authorization and confirmation behavior, secret-value masking, accepted/draft/failed search meanings, exact active-incident URL, Details focus behavior, and current Home behavior when authentication is disabled. Use existing controls; do not add a generic grid, new dependency, feature module, backend/API change, global permission model, or remote-provider claim.

**Scale/Scope**: Two resource pages, one Home task group, two browser fixtures, three focused unit suites, and three existing browser suites. No other Studio pages are in scope.

## Constitution Check

*Gate: both constitutions and `docs/contributing/review-checklist.md` were applied before design and rechecked after the design below.*

- **Elsa Foundation constitution**: No new module, endpoint, public API, package, or dependency is introduced. Existing endpoint and authorization contracts remain authoritative. Framework §2.24 and Elsa §E2.8/§E2.9 are draft/provisional and are not used to impose a new design rule; this work does not depend on them.
- **Framework constitution**: This is a repair to an existing UI, not greenfield application development. Framework §2.21.2 does not declare a TDD cadence for this application; do not infer a test-first mandate. Add focused behavioral regressions for the changed search, selection, permission, action, and fixture-counter behavior, and use a source-revert control where the approved program requires causal evidence. Framework §2.23 is not a Foundation feature-registration obligation here; behavior tests are warranted for the affected UI logic. Tests remain Not run until the implementation owner executes them.
- **Security and privacy**: Never reveal or copy secret material. Synthetic secret values stay within a disposable private host; proof artifacts, browser logs, screenshots, and receipts contain no request bodies, headers, credentials, or secret values. Connection examples are loopback-only.
- **Architecture and UX**: Reuse existing Studio controls and page semantics. Keep Secrets Edit primary and secondary operations in the accessible overflow pattern; preserve Connections Details and its existing actions; use only the local effective-permission matcher for the single Home task destination. No generic list abstraction or permission-helper change is justified.
- **Review checklist**: Review security, permission behavior, error and confirmation paths, keyboard access, focus return, empty/loading/failed states, responsive layout, and public contract impact. No backend/API contract is changed.
- **Gate result**: No constitution violation or exception is identified. Recheck the gates against the final implementation before any approval or merge. No checklist item is evidence that a runtime check has passed.

## Project Structure

### Documentation (this feature)

```text
specs/1004-resource-consistency/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md                 # Produced by the subsequent SpecKit tasks step
```

### Existing source and tests (expected implementation touch points)

```text
Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/
├── SecretsPage.tsx
├── styles.css
└── __tests__/secretsPage.test.tsx

Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/
├── ConnectionsPage.tsx
├── ConnectionList.tsx
├── ConnectionDetailDialog.tsx
├── styles.css
└── __tests__/connectionsPage.test.tsx

Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/
├── HomeTaskGroup.tsx
├── styles.css
└── __tests__/homeTaskGroup.test.tsx

Studio/tests/browser/
├── secretScopesFixture.tsx
├── secret-scopes.spec.ts
├── creation-dialog-tokens.spec.ts # Header create-label integration only
├── connections-management.spec.ts
├── connection-secrets.spec.ts # Header create-label integration only
├── homeFixture.tsx
└── home.spec.ts
```

**Structure Decision**: Make page-specific changes in these existing modules and their tests. Keep Connections' Details action and Secrets' table semantics intact. The browser fixture remains test-only. Do not update unrelated UX targets or shared controls unless final source review shows a required existing-control integration cannot be achieved locally; if that happens, stop for root review before expanding scope.

## Phase 0: Research Findings

See [research.md](research.md). The selected worktree is at `d2968b0f1bc8330fe99dff927cecd885867e68fb`. The code graph had no matching TSX nodes for the named surfaces, so bounded direct source inspection was used. Current source confirms the proposed behaviors and touch points. No code was changed during this planning pass.

## Phase 1: Design and Contracts

- [data-model.md](data-model.md) records the current UI state concepts and allowed transitions. It introduces no persisted entity or endpoint.
- [contracts/ui-contract.md](contracts/ui-contract.md) defines observable UI, search, operation, authorization, and privacy behavior; backend contracts remain unchanged.
- [quickstart.md](quickstart.md) is the implementation-time verification guide. Its commands and host scenarios are instructions only; none has been run.

## Verification Plan and Evidence Boundaries

The following gates belong to the exact implementation head and must remain individually reported. No result from the earlier #3069 or #3310 proof qualifies as #3071 evidence.

1. **Focused behavior and controls**: Run `secretsPage.test.tsx`, `connectionsPage.test.tsx`, and `homeTaskGroup.test.tsx`; run `secret-scopes.spec.ts`, `connections-management.spec.ts`, and `home.spec.ts`. Include focused negative controls for the changed behaviors and full Workflows package shuffle seeds 1, 11, and 29 as retained by the program. Preserve failed/incomplete attempts; do not weaken assertions to obtain green.
2. **Studio static checks and builds**: Run the full Studio `pnpm typecheck` (including `tests/live`), affected lint/style/layout checks, affected Secrets and Connections client builds, and the five existing build groups (Dashboard client, Workflows client, Studio app client, Studio host, and Workbench host) required by the shared gate matrix. `build:lib` for `@elsa-workflows/studio-workflows` is conditional only if the published library surface/source is actually touched; it is not implied by ordinary typecheck or package build. Do not broaden this UI task to satisfy unrelated conditional checks.
3. **Architecture and maps**: Run the applicable architecture guard, solution-filter check, and generated-map freshness/publish contract checks for the exact head; preserve receipts and unexecuted counts.
4. **Browser and visual review**: Run the full Chromium suite on the exact head. Review before/after at 1600×1000 @2x in Classic Light and a dark theme, then check Dim, High Contrast, and narrow layouts (including 390 px). Also inspect representative 1280 px light and 1440 px dark layouts and keyboard, accessible names, errors, confirmations, and focus return.
5. **Safe private-host proof**: Use a fresh disposable normal host with 101 synthetic Secrets and registered Connections that target loopback only. Verify strict-write behavior, masking, authenticated allow/deny including 403, query retention, operation results, and cleanup. Keep synthetic values, payloads, and credential-bearing values out of logs/screenshots/receipts; dispose of the private store. The built-in HTTP test is local readiness, not an outbound test; Revoke is not remote revocation.
6. **Review and delivery**: Obtain the three independent review axes against the complete draft PR first; after readiness, request the actual Full tier on the exact head, then app approval and merge only under the root-owned delivery process. Verify resulting-main CI, Maps, and Studio after merge. A skipped, unavailable, stale-head, or failed gate is not passed.

The implementation owner must refresh all selected gate commands and current CI requirements against the then-current shared matrix. These planned checks are **Not run** in this artifact set. Root owns checklist review, acceptance, exact-head evidence, implementation integration, and delivery.

## Complexity Tracking

No constitution violations or architectural exceptions are proposed; no complexity exception table entries apply.
