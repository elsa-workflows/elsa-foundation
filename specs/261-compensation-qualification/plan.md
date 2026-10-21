# Implementation plan: qualifying native compensation (M5)

**Spec**: [spec.md](spec.md) · **Issue**: [#3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) · **Also closes**: [#3142](https://github.com/elsa-workflows/elsa-foundation/issues/3142), [#3130](https://github.com/elsa-workflows/elsa-foundation/issues/3130)

## Technical context

.NET 10 runtime modules under `Essentials/`, React 19/Vite Studio under `Studio/`, docs under `website/` and `docs/`. No new table and no schema migration. M5 adds:

- one generic runtime seam, R-7 delayed child schedule;
- three scope inputs and scope state v5;
- a design-time side-effect profile reader and a warning validator;
- two Studio changes: warnings in the publication review, and the **Retry scheduled** state;
- the qualification work: live journeys, a provider suite, upcast and retention tests, the #3130 fix and a review pass;
- a sample package, guide fixes and two reports.

Evidence is in [research.md](research.md), state in [data-model.md](data-model.md), seams and records in [contracts/](contracts/), and proofs in [quickstart.md](quickstart.md).

## Placement

| Item | Location |
|---|---|
| R-7 surface | `Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs`, `Core/Models/RuntimeChildActivityScheduleRequest.cs`, `Core/Models/RuntimeCheckpointCommit.cs` (`RuntimePostCommitIntent`), new `RuntimeDelayedScheduleOptions`, `IRuntimeDelayedScheduleCapability`; `SimpleActivityExecutionContext` |
| R-7 commit and delivery | `Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs` (availability, mandatory checkpoint, hop skip), the post-commit dispatchers, `Services/Coalescing/RuntimeCoalescingSession.cs` (flush), `Services/WorkHandlers/WorkflowScheduleActivitySchedulerWorkHandler.cs` (parent-not-running), the structural evaluation that turns child schedule requests into intents (Activities.Runtime) |
| R-7 liveness | `Services/Recovery/RuntimeRecoveryCandidateSelector.cs`, run health; capability registration next to the outbox sweep (`Workflows/Runtime/Resumption`) |
| Scope backoff and v5 | `Activities/Compensation/Activities/CompensationScope.cs`, `Internal/CompensationScopeState.cs`, `…Persister.cs`, `…Upcaster.cs`, `…Validator.cs`, `Internal/CompensationJournalProjector.cs` |
| FR-014 | `Activities/Runtime/Core/Contracts/IActivitySideEffectProfileReader.cs` (new); implementation and shared helper in `Workflows/Publishing/Elsa.Workflows.Publishing/Services/` beside `ExecutableNodeCompiler`; `Activities/Compensation/Internal/CompensationReplaySafeValidator.cs` (new) |
| Studio | `workflowTypes.ts`, `workflow-editor/useWorkflowOperations.ts`, `workflow-editor/PublicationReviewDialog.tsx`, `compensationRuntime.ts`, `graph-authoring/CompensationScopeDesigner.tsx`; `Studio/tests/browser/compensationScopeFixture.tsx`; `Studio/tests/live/support/journey.ts` (new), `compensation-journey.spec.ts`, `nesting-journey.spec.ts` (new), `operator-actions-journey.spec.ts` |
| Provider suite | `Essentials/tests/Activities/Compensation/Tests/Support/CompensationHost.cs` (provider parameter); `Essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests/` (new); `tools/ci/ef-suites.json`; generated solution filters |
| #3130 | `Workflows/Runtime/Core/Models/RuntimeDiagnosticsSettings.cs` (`RuntimeDiagnosticsPermissions`) and `Workflows/Runtime/Api/Handlers/RuntimeDiagnosticsSettingsService.cs`, using the `IWorkflowRuntimeOperatorAccess` rule |
| Samples | `samples/Elsa.Samples.Compensation/` (new); `Essentials/tests/Workbench/Tests/WorkbenchCompensationSampleTests.cs` (new) |
| Docs and records | `website/docs/essentials/compensation.md`, `website/docs/essentials/persistence-modes.md`, `docs/glossary/elsa.md`, `EXTENSION_POINTS.md` files (runtime, compensation), `docs/reports/native-compensation-qualification.md` and `docs/reports/native-compensation-elsa3-follow-on.md` (new), `docs/reports/README.md`, `docs/program-goals/native-compensation.md` |

## Phases

- **A — R-7.** Settle research R2 with failing tests first (T501). It is the stop-and-replan gate: if a check needs new machinery, switch to the structural timer fallback with the owner before writing scope code. Then the seam end to end with a test structural activity.
- **B — Scope backoff.** Inputs, validator, state v5, the formula, the projector, journeys BK0–BK6 in every mode, a Workbench kill while waiting.
- **C — Replay-safe warning and Studio.** The reader, the validator, the Studio publication review and the **Retry scheduled** state, the PR 1 docs, negative controls.
- **D — Qualification.** Live helpers and journeys, the provider suite, in-memory smoke, upcast and retention tests, #3130, pinning in every mode, the review pass, the qualification run and the record's evidence sections.
- **E — Samples, docs and records.** The sample package and its Workbench test, the guide audit and the four-way table, the record's remaining sections, the Elsa 3 assessment, status, then post-merge evidence.

## Constitution and ADR checks

- **§E2.2:** R-7 stays generic in Workflows.Runtime and Activities.Runtime; compensation knowledge stays in Activities.Compensation, which still references only Core projects. The reader contract is in `Elsa.Activities.Runtime.Core`; its implementation is in Publishing.
- **§E2.6.1:** v1–v4 scopes resume under v5 through the upcaster, proven end to end by U1–U4.
- **§E6 naming:** `RuntimeDelayedScheduleOptions` and `IRuntimeDelayedScheduleCapability` follow the `Runtime…` family; `IActivitySideEffectProfileReader`; `CompensationReplaySafeValidator` follows the existing validator names. The input names mirror `handlerMaxAttempts`.
- **ADR 0049:** no alteration kind changes.
- **ADR 0080 D0:** no new alteration or Runtime Alterations surface.
- **ADR 0080 D1 risk-based tests:** seam tests D1–D9; callback tests and journeys per mode; validator unit tests; Vitest per state; provider subset per provider and mode; the live journeys.
- **ADR 0080 D3 internal by default:** new public surface: the overload, the two request and intent properties, the options, the capability marker, the reader interface and the three inputs. Everything else is internal or sealed.
- **ADR 0080 D7:** each PR merges through Code Review with green CI on its exact head.
- **ADR 0086:** nothing is shared with BPMN compensation.
- **Program charter:** compensation adds no scheduler, work ledger or lease. R-7 reuses the existing outbox (research R1).
- **Extension points:** the runtime and compensation `EXTENSION_POINTS.md` list R-7 and the reader. Maps are regenerated locally and not committed (`docs/maps/*.md` are ignored); `docs/maps/dependency-map.json` and the solution filters are committed only if they change.

## Delivery as PRs

1. **PR 1 — Phases A to C** (T501–T517): R-7, scope backoff, the replay-safe warning, the two Studio changes, PR 1 docs, negative controls. It needs only `main`.
2. **PR 2 — Phase D** (T518–T529): qualification, #3142, #3130, the record's evidence sections. It starts when PR 1 and #3140 are both on `main`, because it qualifies the integrated candidate. Closes #3142 and #3130.
3. **PR 3 — Phase E** (T530–T535): samples, guide audit, the record's remaining sections, the Elsa 3 assessment, spec status `Implemented`. Closes #3048. T536 follows the merge with no PR.

PR 3's sample and guide work (T530–T532) depends only on PR 1, so it may be prepared in parallel with PR 2. It merges after PR 2, because the record it completes is opened there.

## If the owner chooses differently

| Decision | Other option | Tasks that change |
|---|---|---|
| OD17 | B (non-zero default) | T507 defaults; T509 BK0 becomes a pinned-input compatibility test (old runs immediate, new runs delayed); T516 guide wording; every M1–M4 journey that counts attempts sets `handlerRetryDelay: 0` explicitly |
| OD17 | C (fixed delay) | T507 drops two inputs; T509 drops the formula; N505 removed |
| OD17 | D (no backoff) | Phases A and B and T515, T520 removed; PR 1 is FR-014 only; the guide keeps the limit |
| OD18 | B (SQLite only) | T521 removed; Q4–Q6 move to "not qualified" |
| OD18 | C (full matrix) | T521 runs every journey per provider; the nightly budget grows about 3× for that suite |
| OD19 | B (scripts) | T530–T531 become `e2e-tests/compensation/` scripts with built-in activities, run by hand; A4 and A6 keep the participant contract text-only |
| OD19 | C (docs only) | T530–T531 removed |
| OD20 | B (literal FR-014) | T513 warns on every `ReplaySafe` step or handler, composites included; the live journeys expect warnings on every Write Line step |
| OD20 | C (defer) | T512–T514 removed; the guide keeps the limit; spec 250 FR-014 is marked deferred beyond the program |

## Risks

| Risk | Mitigation |
|---|---|
| A delivery path ignores `AvailableAt`, and a retry runs early | T501 tests each path first; N501 and N502 prove the guards; fallback to the structural timer (research R1) |
| Ephemeral loses a delayed item on a kill | The mandatory checkpoint (FR-502); BK2 and D7 in Ephemeral; N504 |
| Recovery or run health re-drives a run that only waits | T501 and T505 (D8) |
| A Studio test run expires while a live journey waits | Journeys keep waits short; long waits use a published run; R10 is settled first in T524 |
| Studio already blocks on Connection warnings (R6) | T514 confirms it first and fixes it with a regression test, independent of FR-014 |
| The provider suite is slow or flaky on MySQL | The subset is seven journeys; the suite runs nightly with a required-provider switch; a flake is filed, never skipped |
| The review pass finds more than M5 can fix | Fix what blocks a supported claim; file the rest and list it in the record (FR-521) |
