# Tasks: Studio base hosting

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [contracts](contracts/hosting.md)

Tests cover risk and preserve valid behavior. No mandatory test-first cadence is declared by the derived
constitution; this plan records that gap and uses behavioral integration checks and causal controls.

## Phase 1: Setup

- [x] T001 Record the scoped design/research/contracts in specs/1008-studio-base-hosting/ before code.
- [x] T002 Create Studio/src/essentials/Elsa.Studio.Hosting/Elsa.Studio.Hosting.csproj with only base dependencies and tracked public surface; update Studio/Elsa.Studio.slnx and tools/ci/studio-package-identities.json.

## Phase 2: Foundations

- [x] T003 Implement frozen configuration validation and public composition builder in Studio/src/essentials/Elsa.Studio.Hosting/StudioBuilder.cs and StudioHostingOptions.cs (FR-001/003).
- [x] T004 Implement lease-only coordination in Studio/src/essentials/Elsa.Studio.Hosting/StudioConfigurationMutationGate.cs and migrate existing Web writers to its single root instance (FR-008/009).

## Phase 3: US1 — Small consumer

Independent test: a public factory selects a third-party feature, with Default/non-default and host-owned/file-backed configuration variants.

- [x] T005 [US1] Register mandatory API/configuration, selected resolver and startup blueprint validation in Studio/src/essentials/Elsa.Studio.Hosting/StudioHostingExtensions.cs (FR-002/004).
- [x] T006 [US1] Add public composition/configuration tests in Studio/tests/Elsa.Studio.Tests/Hosting/StudioHostingTests.cs, including callback precedence, invalid choices, explicit API enablement and no optional closure.

## Phase 4: US2 — Routing and lifetime

Independent test: first/warm protected feature requests and task-gated manifest/reload overlap through the common pipeline.

- [x] T007 [US2] Move selected-shell activation and tracked manifest endpoint behavior into Studio/src/essentials/Elsa.Studio.Hosting/; retry only acquisition disposal races, once (FR-005/006).
- [x] T008 [US2] Add cold/warm authorization, non-default decoy, cancellation and tracked lifetime/disposal-race tests in Studio/tests/Elsa.Studio.Tests/Hosting/StudioHostingTests.cs.

## Phase 5: US3 — Browser settings and files

Independent test: real test-owned files and runtime HTTP requests through Add/Use/Map.

- [x] T009 [US3] Move explicit runtime allowlisting and configurable fallback/revalidation into Studio/src/essentials/Elsa.Studio.Hosting/; retain unknown API404 and route priority (FR-007).
- [x] T010 [US3] Migrate Studio/tests/Elsa.Studio.Tests/StudioRuntimeScriptTests.cs and StudioAssetCachingTests.cs to public HTTP composition; retain optional package asset coverage on its owning path.

## Phase 6: Application adoption and delivery

- [x] T011 Adopt public calls in Studio/src/apps/Elsa.Studio.Web/Program.cs, preserving its existing root auth/bridge/Console Stream and optional registrations; remove superseded Web helpers and update test wiring (FR-010).
- [x] T012 Add mutation concurrency/cancellation/idempotent-release tests in Studio/tests/Elsa.Studio.Tests/Hosting/StudioHostingTests.cs; verify existing writer behavior remains green.
- [ ] T013 Run whole Studio tests/discovery and causal controls; record exact results in specs/1008-studio-base-hosting/quickstart.md and PR evidence.
- [ ] T014 Run locked solution restore, architecture graph restore/guards and generated-map checks; update reached locks and changed filters only.
- [ ] T015 Narrow the canonical gate signature in docs/reports/studio-hosting-contract.md, reconcile spec status and complete independent review/CI on the exact PR head.
- [ ] T016 Verify actual merged main, then update #3357 and Program #3340 state/evidence.

## Dependencies and execution

T001 -> T002 -> T003/T004 -> US1 -> US2/US3 -> adoption -> gates/review -> merged-main proof.
US2 task-gated lifetime tests and US3 file/runtime assertions can be developed independently after the public
factory exists; run shared-machine builds through the normal build-slot discipline. The single implementation
leaf remains #3357. Do not create separate issues for this checklist or delegate its implementation.

The MVP is US1 plus safe middleware/endpoint defaults. All three stories and delivery gates are required to
complete #3357; later program leaves retain their own auth, optional adapters, browser payload and final consumers.
