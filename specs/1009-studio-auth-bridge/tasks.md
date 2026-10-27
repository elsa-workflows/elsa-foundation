# Tasks: Studio authentication and bridge

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [contract](contracts/security.md)

Risk-based integration tests preserve existing behavior. The derived constitution declares no mandatory
test-first cadence; keep all required behavior coverage and use causal controls for changed guards/ownership.

## Phase1 — Scope and contract

- [x] T001 Record spec/design/contracts/research in specs/1009-studio-auth-bridge/ before code.
- [x] T002 Add the five existing policy/permission constants to Studio/src/essentials/Elsa.Studio.Api/Authorization/StudioBridgeAuthorization.cs and track the Api public surface.

## Phase2 — US1: common authorization

Independent test: public common hosting calls and a recording backend reproduce the existing disabled/enabled,
401/403, manage/read, trusted query paths and cache matrix.

- [x] T003 [US1] Move private auth/cache/permission code into Studio/src/essentials/Elsa.Studio.Hosting/StudioBridgeAuth.cs and register it from installed Hosting settings.
- [x] T004 [US1] Migrate Studio/tests/Elsa.Studio.Tests/StudioBridgeAuthTests.cs and common fixtures to public hosting/HttpClientFactory seams; preserve missing-handler and TTL/cap cases.
- [x] T005 [US1] Cover explicit trusted-prefix validation, header/query precedence and installed-posture agreement in Studio/tests/Elsa.Studio.Tests/Hosting/; reject ambiguous root variants.

## Phase3 — US2: server-only privileged relay

Independent test: actual status/registry HTTP requests, recorded backend paths/headers/counts and browser projections.

- [x] T006 [US2] Move private bridge/client/DTO behavior into Studio/src/essentials/Elsa.Studio.Hosting/StudioBackendManagementBridge.cs, reuse Api read contracts and map through MapElsaStudio.
- [x] T007 [US2] Migrate Studio/tests/Elsa.Studio.Tests/StudioBackendManagementBridgeTests.cs and ExtensionBuilder fixtures to public APIs and JSON assertions, preserving all zero-call/key-isolation/status cases.

## Phase4 — US3: borrowed root lifetime

Independent test: public authentication inside shell scopes, manual reload and an owned clock establish root cache/factory/clock identity and disposal semantics.

- [x] T008 [US3] Use CShells sharing for root cache/clock/options and the private root session transport in Studio/src/essentials/Elsa.Studio.Hosting/StudioHostingExtensions.cs.
- [x] T009 [US3] Add behavioral root/shell cache/disposal/reload tests in Studio/tests/Elsa.Studio.Tests/Hosting/ without private implementation references or production friends.

## Phase5 — Adoption and delivery

- [x] T010 Remove Web's two superseded files/manual registration/mapping from Studio/src/apps/Elsa.Studio.Web/; update remaining policy users to the Api contract.
- [x] T011 Run the whole Studio project, public behavior matrix and causal controls; restore sources exactly and compare discovery/execution counts.
- [x] T012 Run packed Api consumer, isolated actual Web smoke, architecture/maps/inventory/locked-restore gates; retain precise evidence on the PR.
- [x] T013 Update Hosting README and docs/reports/studio-hosting-contract.md, reconcile implemented spec status, complete independent review and exact-head CI.
- [x] T014 Verify actual resulting main, then close #3358 and update Program #3340 board/evidence.

## Dependencies and execution

T001 -> T002 -> US1/US2 -> root sharing/lifetime qualification -> Web adoption -> controls/gates -> reviewed delivery.
Auth and bridge fixture rewiring can be prepared independently once the public common entry points exist, but
one implementation leaf remains active. Do not delegate implementation or create duplicate issues for this checklist.
All three stories are required; no final browser/optional/package-only completion claim belongs to this leaf.

## Final record reconciliation

T013, T014 were delivered through [the reviewed delivery receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3383) and merged commit `2fd983021891056b092e69914073cfb4c38862af`, verified as an ancestor of current main. #3366 reconciles these previously stale checkboxes from actual delivery/main/candidate evidence; it does not invent a new execution or formal approval. The current organization protocol governs review/merge authorization.
