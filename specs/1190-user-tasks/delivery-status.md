# Delivery checkpoint — blocked before qualification

Program: [#3688](https://github.com/elsa-workflows/elsa-foundation/issues/3688). Acceptance: [#3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577).

Research, design and tasks are complete. Implementation is substantial but **not qualified, merged or delivered**. Keep spec status Draft and T005–T026 open until their complete exit evidence exists. This file is a handoff receipt, not an acceptance certificate.

## Stop and exact current failure

The organization [AGENTS.md stop conditions](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#stop-conditions) require stopping after failing the same check three times in a row. The new HTTP fixture check was:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore --filter FullyQualifiedName~UserTaskApiTests -v:minimal
```

Its three attempts failed in sequence:

1. Build: fixture used nonexistent `ResourceOwnership.FoundationOwned`; corrected to `Foundation`.
2. Startup: fixture replaced the current-subject authorizer without updating its replacement registration marker; corrected using `EnsureReplacementContract` for the replaced identity contracts.
3. Execution: 3 tests passed, 1 failed. `Trusted_canonical_identity_and_current_membership_control_claims_not_body_or_token_roles`, line 41, expected `NotFound` but received `Forbidden` after role revocation. The fixture grants management permission, so the task remains inspectable while completion is refused. Inspect that fixture/expectation before another run; no change was made after the third failure.

Owner direction is required to resume past this stop condition. Claims are released and both issues remain open/blocked. The draft PR must not merge in this state.

## Implementation present

- Runtime contracts, conditional journal operations, immutable form versions and basic field validation; trusted identity/current membership/resource authorization; safe task API DTOs, routes, capabilities and errors.
- EF task/form persistence, CAS, tenant/identity hash predicates, execution deletion retention fence and incremental migrations for SQLite/PostgreSQL/MySQL/SQL Server.
- Invocation-scoped task staging, captured physical bookmark generation, exact resume validation, accepted-response recovery and canonical committed-completion acknowledgement.
- Create User Task activity, dynamic authored action ports, optional Studio inbox/detail/form management and property editors, SDK form renderer contribution.
- Initial lifecycle/persistence/restart/HTTP/mounted UI tests.

## Evidence already observed

These are runs at intermediate source states, **not evidence for the final draft head**:

| Check | Observed result |
|---|---|
| Existing full Runtime API project before the new HTTP tests | 218 passed, 0 failed |
| UserTaskOperationsTests | 8 passed |
| EfUserTaskStoreTests, SQLite | 3 passed |
| UserTaskRuntimeJourneyTests | 2 passed: Durable and Balanced, three host generations over one owned SQLite database |
| Dynamic outcome port tests | 3 passed, including canonical Object expression roundtrip |
| Mounted task form/detail tests | 5 passed |
| User Tasks / affected Workflows TypeScript checks | Passed at the states preceding the latest UI review fixes |
| Latest new HTTP fixture | 3 passed, 1 failed as above |

Archived EF test inputs were restored through the canonical `python3 tools/bpmn/restore_evidence.py` and pinned verification, without modifying user stores. All journey hosts/databases were disposable and owned.

## Review findings addressed in source but still needing regression evidence

- Reconciliation reads raw committed activity/execution state rather than an ambient coalescing session.
- Claimant, tenant and deletion predicates include ordinal identity hashes for case-insensitive database collations.
- Required/readonly prefill, non-text empty values, malformed/null fields and whitespace text limits fail validation safely.
- Paged immutable forms, per-resource task read/action/management checks and protected context-bound inbox cursors.
- Explicit `IUserTaskPersistenceBoundary` qualifies durable journal/execution retention pairs; unqualified custom combinations refuse staging/recovery.
- Transient nested provider errors before resume and during accepted-result reread become scheduler-unavailable errors that preserve owed work.
- Composition startup task rejects duplicate replacement ownership and duplicate form-provider identities.
- Studio opaque/prototype form keys, clearing optional non-text values, ambiguous completion retry identity, backend-qualified links and stale response ordering.
- Forms-only entry routing, explicit unanswered/Yes/No boolean choice and workspace-tab detail location/selection fixes.
- Primitive public API tracking was enabled with its initial baseline. Its new baseline and the latest retention/availability/cursor/startup changes have not yet been built/tested together.

## Work remaining before completion

1. Resume only on owner direction; inspect/correct the HTTP fixture expectation and rerun it.
2. Add decisive overlapping EF claim/completion races and cancellation/deletion races, forged/generation-replaced wait tests, post-bookmark-claim outage/retry, committed completion before acknowledgement, missing-state uncertainty and encrypted/volatile input/provider refusal tests. Current memory claim test uses synchronous collaborators and is not a real overlap witness.
3. Qualify populated form outputs and configured action branches through normal authoring/publication; qualify BPMN native-worker reuse, external form provider/renderer and custom-client/package-only host composition.
4. Add mounted inbox/paging/claim/release/reassignment/forms-only/boolean/navigation/form-authoring/selector tests and browser/keyboard/narrow-layout qualification with real normalized identities.
5. Complete user/integrator guidance and owning extension-point/recovery cadence catalogs. Validate custom-provider ownership; replace the current EF default-journal recognition by type name with an explicit ownership mechanism if required by review.
6. Run whole affected projects and compare listed/executed counts; causal red/green controls; architecture restore/guard, generated maps/tier/reference checks, locked solution restore, supported provider suites, Studio typecheck/test/build and composed backend/browser journeys. All remain gates, not implied passes.
7. Open/retain a draft PR, complete independent exact-head correctness/standards/acceptance review rounds, then ready/required CI/permitted merge, resulting-main rebuild/journeys and Program/feature/parity reconciliation. Do not close either issue prematurely.

Local detailed logs are in `/tmp/elsa-user-tasks-3577` on the originating machine. Durable evidence is the commands/results and source in this checkpoint and PR; those temporary logs are supplementary.
