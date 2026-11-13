# Research: activity playback readiness

**Date**: 2026-11-13. Source inspection from the specification branch (`f4018bb6b2`) and current-main contract checks during planning. Shared `origin/main` advanced through `b0b496bf14` and `148d48c906`; no runtime readiness is inferred from that movement. No production test or host was run for this research.

## R1 — Use the runtime history owner's recorded sequence

**Decision**: Require a separately reviewed runtime-owned occurrence sequence facet over qualified spec1211 evidence. Dependent playback/user-story implementation is blocked until it exists and is qualified. Coordinated implementation of that runtime prerequisite is explicitly authorized after agreeing its source boundary with the existing owner.

**Evidence**: `Essentials/src/Workflows/Runtime/Core/Models/ActivityExecutionBrowsePage.cs` calls its query a live view and explicitly promises no cross-request snapshot. `.../Persistence/EntityFrameworkCore/Stores/EfActivityExecutionInspectionBrowse.cs` ranks primarily by scheduled ticks, then execution sequence/ID. Scheduler sequence may be a hash (`.../Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowScheduleActivitySchedulerWorkHandler.cs`); it is not universally monotonic history order. `.../Api/Endpoints/ActivityExecutions/List/Endpoint.cs` binds authenticated cursors to filters and offset, not a history watermark. The public request supports known-ID anchors, not arbitrary ordinal seek. It exposes no capture/gap/settlement head.

**Rationale**: FR003/014/018–020 require stable recorded ordering, bounded seeking, frozen membership and truthful gaps. A known count cannot fence late inserts or materialization. A persistence-mode name cannot prove coverage. Existing `RuntimeHistoryEvent` types are not a delivered reader/provider/coverage path.

**Alternatives rejected**: Sort the loaded timeline; walk/download every page before scrubbing; forge cursor offsets; infer gaps from absent rows; use an in-browser snapshot/another capture store; advertise supported playback over a test-only response. Each either violates a named requirement or duplicates the active history owner.

**Ownership**: #3849/spec1211 S02–S05, S11–S14 own envelope/capture/providers/authorized reads/coverage. S06/S07 own primary Timeline/Diagram and exact inspector integration. Current read contracts under `specs/1211-run-history-timeline/contracts/` are dependency proposals, not production proof. Add the small facet to that owner's reviewed boundary without taking over its active claim/slices.

## R2 — One committed selection, separate transport state

**Decision**: Mount `useActivityPlayback` at `WorkflowInstanceDetailsWorkbench` lifetime. Keep `selectedEvidenceId` as the committed occurrence selection and hold pending ordinal/bounds/timer state separately.

**Rationale**: `WorkflowInstances.tsx` already owns exact occurrence, pinned graph, ancestry, inspector layout and navigation retirement. `executionScope.ts` validates run/identity/ancestry with cancellation. Selection currently returns before asynchronous restoration finishes; playback needs an explicit confirmed-navigation result. Manual selection/tab/diagram/scope/incident actions pause synchronously before retiring requests; automatic steps carry distinct provenance so they do not pause themselves. No canvas keyboard-focus request is emitted for a playback tick.

**Alternative rejected**: Mount a timer in the Timeline component. Tab unmounting would lose cursor state and delayed loads could advance a retired context.

## R3 — Cadence and live state are independent

**Decision**: One cancellable presentation timer advances confirmed steps. Pace changes affect the next interval; automatic page buffering can resume only the same still-playing generation. Manual navigation, explicit Activity inspection, error, backgrounding or context change retires progression.

**Rationale**: `useExecutionObservation.ts` intentionally resumes reads on visibility restoration; playback must not do so. `RunObservationControls.tsx` already means live updates, not recorded playback. `executionMotion.ts` confirms a narrow set of causal edges and explicitly is not an execution clock/history queue; retain that restriction.

## R4 — Prove real evidence separately from UI mechanics

**Decision**: Four real-host journeys in all four theme/viewport projects; new narrow cases explicitly set320px. Use controlled presentation time for cadence. Use an owner-controlled backend TimeProvider witness only to create actual recorded long-wait facts, and a real held-retry seed/public alteration for attempts.

**Rationale**: `Studio/tests/acceptance/timelineSelectionJourney.ts` supplies actual catalog/publication/run/cleanup patterns. Existing e2e ForEach/Parallel/Event journeys supply normal seeds. Existing retry unit tests use a controlled intervention parent and do not establish a normal-host retry. Browser clocks cannot manufacture a52-minute backend wait. Deleting projection rows or substituting evidence JSON cannot prove owner-recorded retention gaps.

**Readiness conclusion**: Research choices are resolved. The blocking facts are delivered dependency availability and qualification, not missing user authorization or a request to weaken the specification. User-authorized end-to-end delivery remains outstanding.


## R5 — Coordinated prerequisite implementation

**Authority**: The owner explicitly requested coordination with #3849 to implement T003 before continuing playback end to end. [Implementation handoff request](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858677569); [source-derived controls](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858790220). This authorizes dependency work, while retaining the existing history owner's claim and agreeing source boundaries before edits to active slices.

**Inspected source**: S05b/#4301 at `d90ce8549116` and S06b/#4307 at `84765a2320de`. The latter adds Studio consumers; the relevant runtime read sources are unchanged between those heads. These are unmerged source inputs, not a qualified integration base. The history owner has acknowledged the dependency intake in [the existing issue](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858700336).

**Reusable seams**: `Core/Contracts/IRuntimeHistoryStore.cs`, `Core/Models/RuntimeHistory.cs`, `Services/History/InMemoryRuntimeHistoryStore.cs`, `Services/Checkpoints/InMemoryRuntimeCheckpointStoreState.History.cs`, `Api/Handlers/RuntimeHistoryInspectionService.cs` and `Api/Capabilities/RuntimeHistoryCapabilitySource.cs`. The existing reader uses coherent committed state, purpose-separated continuations and authorization binding; capability discovery requires the actual writer/reader pairing. Add occurrence reads within that boundary rather than walking event pages in Studio.

**Independent correctness research**: The [dependency contract](contracts/playback-inspection.md#implementation-controls-from-the-existing-history-stack) now records concrete controls for mutable ancestry, first-observation semantics, retention rebasing, unlocated gaps, source settlement and bounded ordinal indexing. Existing event receipt positions do not themselves preserve first-occurrence association after content purge. No source, build, provider or product acceptance result is inferred from this inspection.

**Agreed implementation boundary**: [Owner handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858916502) accepts a separately reviewed S14a contribution on exact `f2638c8ac8f23cb3f08071db4d68b793a84d9217` / #4364, branch `agent/codex/3849-history-slice-10i`. The isolated authoring branch is `agent/codex/3849-history-occurrence-facet`. It owns additive Core occurrence models/port/API baseline, bounded in-memory occurrence projection hooks, authorized API reads/capability links, tests and catalogs. #3849 retains capture/checkpoint semantics, EF/providers/migrations, retention mutation, production composition and final qualification. The handoff grants no local .NET/browser campaign, Azure restart, manual dispatch or protection/identity exception. Runtime source authoring has begun in that separate worktree; T003 remains incomplete until its actual provider and acceptance controls are qualified.

**Number reconciliation**: Current main concurrently allocated1225 to workflow-release promotion (#4215). This stream is renumbered1226 after inspecting current main/archive/all-ref history, the initiative branch and all131 open PRs including their file pagination. Highest observed allocation was1225. Existing #4366/#4368 and historical commit links remain the same; no feature requirements change.
