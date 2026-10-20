# Tasks: uncertain effects and resumable compensation (M2)

Each task names its files and proof. `[P]` marks a task that can run in parallel with the others in its phase.

## Phase A — runtime seams (PR 1)

- [x] T201 [P] `OperationKey` / `TargetOperationKey` on `ActivityExecutionContext`, `ActivityStartContext` and `ActivityResumeContext`, copied by `StatefulActivity`. They are resolved where the contexts are built: `SimpleActivityExecutionContext`, `BackgroundActivityWorker`, `WorkflowResumeBookmarkSchedulerWorkHandler`. The keys `runtime.operationKey` / `runtime.targetOperationKey` go in `RuntimeMetadataKeys`. Unit tests cover assigned, first-attempt and invocation fallbacks, plus a crash re-attempt that keeps the key.
- [x] T202 [P] `FaultCode` / `FaultCategory` on `ActivityChildFaultedContext`, filled from the committed child state in `WorkflowParentActivityCompletionSchedulerWorkHandler`. A unit test covers a child faulting with category `outcome-unknown`.
- [x] T203 `RequestChildFaultIntervention` on `IRuntimeActivityExecutionContext` and its implementations, applied next to absorption. It uses source `IncidentResolutionSystemSources.StructuralIntervention`. Tests cover:
  - the incident stays `Blocking` with outcome `WaitForIntervention` and metadata;
  - the run stays `Running` under the default `Fault` strategy;
  - the child's subtree is not cancelled;
  - misuse (absorb plus intervene, the wrong incident, a non-`Defer` continuation) faults the evaluation.
- [x] T204 [P] Absorption copies its request metadata into the incident outcome metadata. Covered by a unit test.

## Phase B — `RetryFaultedActivity/1` (PR 1, #2747 core)

- [x] T205 Register the alteration kind constant and its envelope schema. Add `WorkflowAlterationRuntimeCheckpointStateBuilder.AddIncident` and wire it into the alteration checkpoint committer.
- [x] T206 `RetryFaultedActivityAlterationHandler`:
  - preflight rejections per [contracts/retry-faulted-activity.md](contracts/retry-faulted-activity.md);
  - successor built with the shared reschedule construction, keeping the scheduling cause;
  - incidents resolved with `OperatorRetry` / `OperatorRetryAlteration`.

  Extract the shared successor and continuation code from `RescheduleActivityAlterationHandler` without changing its behaviour. Its tests must stay green.
- [x] T207 Integration tests:
  - a faulted leaf activity with a blocking incident under `WaitForIntervention` is retried, completes, and the run continues;
  - the same idempotency key twice gives one successor;
  - a new key after success is rejected `SourceActivityNotFaulted`;
  - a run without permission is refused;
  - a terminal run is rejected;
  - the successor carries the source's operation key;
  - a structural parent sees the successor's completion as a normal child completion (R4 to verify).
- [x] T208 `EXTENSION_POINTS.md` (runtime) lists the seam and the kind. Regenerate the maps.

## Phase C — compensation scope v2 (PR 2)

- [x] T209 `CompensationScopeState` v2 types: phase, statuses, `forwardOutcome`, `operationKey`, `handlerAttempts`, `interventionIncidentId`, cause kind. Add `CompensationScopeStateUpcaster` (v1→v2) and route the persister through it. Unit tests run every upcast row of the [data model](data-model.md), check round-trips, and check that no downgrade happens.
- [x] T210 Operation keys on step and handler scheduling metadata, and the `handlerMaxAttempts` input with validation (≥ 1). Unit tests check the keys and that retries reuse them.
- [x] T211 Transitions per [contracts/semantics.md](contracts/semantics.md): uncertain step with and without a handler, handler retry, exhaustion and intervention, and completion after an operator retry. A latest-attempt guard drops stale and repeat completions (FR-215). Unit tests cover each branch.
- [x] T212 [P] Participant fixture: lost response, late apply, fail-n-times and tombstones (`Tests/Support/Participant.cs`, `Workbench/Fixtures/Reservations`).
- [x] T213 Integration and restart tests J1–J6 in Durable, Balanced and Ephemeral, with the four injection points. Run the negative controls of SC-204 once by hand and record them in the PR.
- [x] T214 Workbench owned-process journey: J1 and J5 over REST, including the retry alteration and a host kill at injection point 4.

## Phase D — Studio (PR 2)

- [x] T215 [P] `workflowTypes.ts`: replace `resolutionAction` with `resolutionOutcome`. The incident panel shows action, source and metadata. Fixes the always-empty row; covered by a Vitest.
- [x] T216 `compensationRuntime.ts`: forward `Uncertain`, recovery `NeedsAction`, phase `AwaitingIntervention`, attempts `{count, max}`, `incidentId`. Vitest covers each state, partial pages and operator-retry successors.
- [x] T217 `CompensationScopeDesigner.tsx` run view:
  - "⚠ Uncertain", "✋ Needs action" and "Attempt n of m" as text plus icon;
  - a link to the blocking incident;
  - a summary phase of "Needs action".

  Extend the mocked Playwright fixture and spec with axe at 390/1200 px, in light and dark.

## Phase E — docs and amendments (PR 2)

- [x] T218 Spec 250 `contracts/semantics.md`:
  - `AwaitingIntervention` holds a blocking incident with `WaitForIntervention`, resumed by `RetryFaultedActivity/1`;
  - the M2 handler-fault line points to spec 251.

  In the program charter, replace "does not use or duplicate incident retry" with the OD5 reuse.
- [x] T219 `website/docs/essentials/compensation.md`: the participant contract (FR-220), operation keys, uncertain outcomes, retries and how to resume. Add glossary rows for operation key, uncertain entry and intervention wait.
- [x] T220 Spec 251 status becomes `Implemented`, with the validation evidence. Close #3045 and the #2747 runtime slice, leaving the Studio button for M4.
