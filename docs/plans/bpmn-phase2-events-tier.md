# BPMN Phase 2 (Events Tier) — Handover and Plan

Status: Phase 1 shipped 2026-07-20. This document is the handover brief for the session that
implements Phase 2. The full original program design lives in the session plan (mirrored below where
it matters); the Phase-1 scope record is `specs/108-bpmn-container-activity/spec.md`.

## Phase 2 progress (updated 2026-07-22, spec 122 — events-tier runtime scope COMPLETE)

Spec numbers drifted from this document's suggestions: 109–111 and 113–114 were claimed by the
engine-perf program. Shipped so far:

| Unit | Spec | PR | State |
|---|---|---|---|
| Core seam A — child-subtree cancellation | `specs/112-runtime-child-subtree-cancellation` | #903 | merged |
| Core seam B — handled child fault (absorption) | `specs/115-runtime-handled-child-fault` | #904 | merged |
| Catch-events prerequisite — node-scoped resume targets (lifts the W8 one-instance limit) | — (documented W8 follow-up) | #911 | merged |
| Timer/message/signal intermediate catch events (`CatchEventBehavior`; `Event` gains its mid-flow wait form + `CanStartWorkflow`) | `specs/116-bpmn-catch-events` | #917 | merged |
| Event-defined start events (`BpmnProcess` becomes a `[TriggerActivity]` with per-start-element bindings; trigger-metadata runtime seam on `IRuntimeActivityExecutionContext`; `IRecurringTriggerScheduleProvider` goes fan-out) | `specs/117-bpmn-event-start-events` | #935 | merged |
| Interchange eventDefinition wiring (root `message`/`signal` index → `name`; catch-event child synthesis `Delay`/`Event`; timer `timeCycle`/`timeDuration` mapping with `P`/`R` discriminator; export + publish-parity guard; no runtime changes) | `specs/118-bpmn-interchange-event-definitions` | #940 | merged |
| Event-based gateway (first-catch-wins race: additive `BpmnEventRace` state, engine-owned resolution, seam-A loser teardown — first BPMN seam-A consumer; `IRuntimeLiveChildActivityConsumer` + `GetLiveChildActivities()` runtime seam closes the node-id→aei gap) | `specs/119-bpmn-event-based-gateway` | #948 | merged |
| Boundary events (listener-child catch boundaries armed engine-side; interrupting/non-interrupting semantics; error boundaries absorb via seam B — first BPMN seam-B consumer; spec-119 carry generalized to `PendingSubtreeCancellations`; interchange attachedToRef/cancelActivity round-trip) | `specs/120-bpmn-boundary-events` | #950 | merged |
| Multi-instance (cardinality mode, sequential + parallel; uniform sub-token model; first-ever concurrent same-node scheduling; `RuntimeLiveChildActivity.IterationId` + `(NodeId, IterationId)` teardown keying; collection mode authoring-modeled but deferred — needs a container-variable read seam) | `specs/121-bpmn-multi-instance` | #954 | merged |
| Cyclic sequence flows (token `IterationKey`; DFS back-edge classification — NOT Flowchart's naive CanReach, chip filed to check Flowchart; join accounting grouped by (element, iteration key); `ValidateAcyclic` removed; importer cycle degradation lifted) | `specs/122-bpmn-cyclic-flows` | #956 | merged |

The events-tier runtime scope is COMPLETE with spec 122.

## Phase 3 progress (control room active 2026-07-22; program-goal bucket: `docs/program-goals/bpmn-engine.md`, PR #963)

| Unit | Spec | PR | State |
|---|---|---|---|
| Runtime scoped-variable read seam + collection-mode multi-instance (marker `IRuntimeScopedVariableReader` + `TryReadScopedVariableValue`, committed-basis, all three handler paths; spec 121 rule-5 cut removed; `BpmnLoopState.Items` snapshot; interchange `elsa:variable` declarations + collection round-trip) | `specs/123-runtime-scoped-variable-read` | #965 | merged |
| Compensation (reverse-order `BpmnCompensable` log registered at host completion; compensation boundary events with first-class flow-less handlers via association; compensate intermediate throw + end events with `activityRef`; claim-then-sequential-replay `BpmnCompensationRun`; interchange association parsing; zero runtime changes) | `specs/124-bpmn-compensation` | #966 | merged |
| Transactions + cancel events (`IsTransaction` on element + structure; cancel end = `CancelTransaction` command → stop-live-work → claim-all → spec-124 replay → `Complete("Cancelled")`; structure-dependent `Cancelled` outcome via `ExecutableNodeCompiler.ResolveOutcomes` Switch-pattern branch; parent maps the outcome to a dormant cancel boundary before Case B; `<transaction>`/`cancelEventDefinition` interchange) | `specs/125-bpmn-transactions` | #970 | merged |

Remaining queued follow-ups (not part of
the tier's runtime scope): **Flowchart backward-edge
classification check** — resolved: confirmed + fixed via PR #958 (2026-07-22);
Studio authoring UX (event definitions, boundary attachment, loop markers; separate repo) — pull in
only if the owner asks. Terminate/fault paths still cancel logically only (`CancelLiveWork`); routing
them through seam A is a noted follow-up. Deferred construct cuts: escalation/compensation boundaries,
error-code matching, non-interrupting timer repetition, event subprocesses, completionCondition,
output aggregation, standardLoopCharacteristics, loopCounter frame exposure, unbounded-loop
guardrails. Phase 3 afterward per the original program design: compensation, transactions/cancel
events, escalation, event subprocesses, call activity, executable collaborations.

Start-events slice notes (spec 117, 2026-07-21): the matched trigger binding's `Metadata` now flows
end-to-end into `IRuntimeActivityExecutionContext.TriggerNodeId`/`TriggerMetadata` (reserved
`trigger-meta:` durable slot — the seam any future container trigger reuses); message/signal starts
share the named-event stimulus with `Event` (parity-pinned in-module duplicate), so one delivery can
both start processes and resume catch events; a nested `BpmnProcess` opts out of the start surface via
`CanStartWorkflow = false` (root position is not recoverable from the published node). Known
pre-existing follow-up (chip filed): `Timer`'s interval-only stimulus hash lets two same-interval
workflows cross-start each other.

Seam facts for the remaining units: `RequestChildSubtreeCancellation` / `RequestChildFaultAbsorption`
are staged on `IRuntimeActivityExecutionContext` during child-completion/child-fault evaluations and
applied atomically in the continuation's own commit (`ActivitySubtreeCancellationPlanner` is the
shared core; see the runtime EXTENSION_POINTS entries and the two specs for the validation rules).

Catch-events slice notes (explored 2026-07-21): `BpmnElement.EventDefinitions` and
`BpmnEventDefinitionTypes.Timer/Message/Signal` already exist and round-trip; an intermediate catch
event is a new behavior family riding the existing `ScheduleChild` → `AwaitingChild` →
`OnChildCompleted` token path with a synthesized suspending child in `BpmnAuthoredStructure.
Activities` bound via `ChildNodeId` (`BpmnGraph.Validate` demands exactly-one binding, and
`BpmnElementFamilies.ResolveStartEvent` currently throws on event-defined start events); the
interchange importer currently **drops** `intermediateCatchEvent`/`boundaryEvent` and degrades
event-defined start events; `Event` (Primitives) is start-only — its mid-flow wait form (add a
`[ResumeTarget]` resume path) is the natural message/signal catch child; correlation-scoped resume
already exists (passive `correlationId` through `IGlobalBookmarkStimulusLookup`); BPMN timer START
events ride `RecurringScheduleDescriptor` + `RecurringTriggerPumpTask` (Timer/Cron template).

## Where the program stands

Phase 1 landed as eight merged PRs in one day:

| PR | Repo | Slice |
|---|---|---|
| elsa-foundation #865 | runtime | `BpmnProcess` container: token engine, exclusive/parallel/inclusive gateways, terminate, fault-aware joins, `bpmn.join.deadlock` |
| elsa-foundation #883 | design-time | `Elsa.Activities.Bpmn.Interchange`: BPMN 2.0 XML + BPMNDI import/export (analyze-then-commit) |
| elsa-foundation #889 | api | `interchange/bpmn/{analyze,import,export}` endpoints, permissions `bpmn-interchange.read/manage` |
| elsa-foundation #890 | runtime | `BpmnDecision` expression-condition evaluator leaf |
| elsa-foundation #891 | runtime-api | Run-view connectivity: flows collapsed to bound-activity connections |
| elsa-foundation-studio #446 | ui | `"bpmn"` designer mode (canvas node id = BPMN elementId) |
| elsa-foundation-studio #447 | ui | BPMN create-workflow root + flow-condition editing |
| elsa-foundation-studio #448 | ui | Import/Export BPMN toolbar actions, DI↔layout bridges |

Key modules: `src/Elsa/Activities/Bpmn/` (runtime; `Interchange/` nested under it per the
domain-tree guard, parent csproj excludes it via the `Compile Remove` pattern),
`tests/Elsa/Activities/Bpmn/{Tests,Interchange/Tests}`, studio
`src/Elsa.Studio.Workflows/Client/src/bpmn/`.

## Phase 2 scope (approved by the program owner)

The events tier, in dependency order:

1. **Core seam A — child-subtree cancellation** (spec first; suggested `specs/109-…`). A structural
   parent requests cancellation of one scheduled child's activity-execution subtree (children,
   bookmarks, durable timers, incidents), staged like `ScheduleChildActivity` on
   `IRuntimeActivityExecutionContext` and applied at checkpoint commit; cleanup rides
   `IActivityScopeCleanupStore`; the scheduler must tolerate late completions of cancelled children
   (the Flowchart Break/#304 tolerance, generalized). This is the linchpin for interrupting boundary
   events, event-based gateways, event subprocesses, and transactions.
2. **Core seam B — handled child fault** (spec first; suggested `specs/110-…`). Today
   `OnChildFaultedAsync` can only fault the composite or leave the incident blocking. Error
   boundaries need the parent's `RuntimeStructuralContinuation` to mark the child's incident
   absorbed/resolved so the composite can consume the fault and route an error token instead.
3. **Timer/message/signal catch events** — NO new wait machinery: catch events compile to
   synthesized internal child activities (`BpmnIntermediateCatchEvent`) using the existing
   `ActivityBookmarkRequest` / `IDurableTimerScheduler` / stimulus-provider path (template:
   `src/Elsa/Activities/Scheduling/Activities/Timer.cs` + `TimerStimulus`). Resume → ordinary
   `OnChildCompletedAsync`. BPMN start-event triggers ride `IActivityTriggerStimulusProvider`
   (template: `Event` + `EventTriggerStimulusProvider` in Primitives). Message correlation needs a
   delivery API — decide whether it lives in the BPMN module or as a shared runtime seam.
4. **Event-based gateway** — schedule all candidate catch children; first completion cancels the
   rest via seam A.
5. **Boundary events** — listener-child pattern: a synthesized `BpmnBoundaryEventListener` child
   scheduled alongside the host child; host completes → cancel listener (seam A); listener fires
   first with `cancelActivity=true` → cancel host. Error boundary events use seam B to absorb the
   fault and emit an error token. Studio: boundary-attachment UX (drop-on-border, `attachedToRef`,
   interrupting = solid vs dashed border).
6. **Multi-instance** — sequential via one child at a time with `LoopIterationScopeRequest`
   iteration frames; parallel via N concurrent schedules of the same executable node with distinct
   frames (verify the scheduler permits concurrent same-node children; Flowchart race scopes suggest
   yes). Loop characteristics also lift the Phase-1 acyclic-graph restriction (`BpmnGraph.
   ValidateAcyclic`) — cycles then need loop-iteration keys on tokens like Flowchart's #382 model.

Phase 3 afterward: compensation (reverse-order log), transactions/cancel events, escalation,
event subprocesses, call activity (rides DispatchWorkflow specs 005/096–104), executable
collaborations.

## Engine facts the implementer must know (learned in Phase 1)

- **Structural protocol**: `BpmnProcess` implements `IRuntimeStructuralActivity` +
  child-completion/fault handlers; everything returns `RuntimeStructuralContinuation` and stages one
  typed state envelope (`Elsa.Bpmn.ExecutionState` v1) via `BpmnStatePersister.StageState`.
- **A terminal continuation cannot co-exist with child schedules staged in the same evaluation**
  (runtime rejects it). Terminate/fault raised mid-propagation defer via
  `BpmnExecutionState.Terminated` / `PendingFault` and resolve on the next callback — extend this
  pattern for Phase-2 cancellations rather than fighting it.
- **Never prune `Canceled` tokens** on persistence: late completions are absorbed via the by-id
  token lookup (same reasoning as the Flowchart persister's Canceled/Faulted path retention).
- **Dynamic outcomes must be declared by the authored `ActivityContract`** (VF-ACT-006), the
  FlowSwitch pattern — relevant for any new leaf with variable outcomes.
- **Determinism discipline**: all record ids derive from `BpmnExecutionState.Sequence`; the only
  mutation home is `BpmnStateMutator`. Keep it that way; golden/determinism tests should pin Phase-2
  state (they were deferred in Phase 1 — adding them alongside the schema growth is wise).
- The state envelope will grow (event subscriptions, MI state, compensation log later): bump the
  payload additively; `StateSchemaVersion` stays 1 unless the shape breaks.
- Tests ride `WorkflowExecutionHarness` (`tests/Elsa/Activities/Testing/`): `BpmnRuntimeFixture`
  (BPMN tests project), `NewProbeNode` (exactly one outcome), `NewFaultingNode`, `ClrConstruction`
  for real CLR leaves (see `BpmnDecisionTests` for the contract-building recipe), and
  `RecordingStimulusRouter` for stimulus/bookmark scenarios.

## Repo-convention gates that bit us (avoid re-learning)

elsa-foundation:
- Domain tree guard: dotted project names map to nested paths (`Bpmn.Interchange` →
  `Bpmn/Interchange/`); a domain root with nested projects needs its own slnx folder and the parent
  csproj needs `Compile/EmbeddedResource/None Remove="<Nested>/**/*"`.
- `EndpointSecurityTests`: new endpoint directories must be added to `CurrentManagementEndpointRoots`
  AND the pinned permission-name dictionary; names match `^[a-z][a-z0-9-]*\.(read|manage|execute)$`.
- API features derive from `FastEndpointsFeatureBase`; endpoints are assembly-scanned; ingestion
  surfaces (like BPMN interchange) do NOT register an API capability.
- Pre-existing flake unrelated to BPMN: none currently known on main; the old
  `ManagementApiContractTests` folder-schema drift was fixed upstream.

elsa-foundation-studio:
- Bundle budgets in `src/Elsa.Studio.Workflows/Client/scripts/check-bundle-size.mjs` are tight;
  editor-only code lands in the deferred `WorkflowEditor` chunk (free), shared adapter/types/styles
  code hits the landing budgets (raise only with documented before/after numbers).
- New vitest configs must include the repo-root `vitest.setup.ts`; CSS must use `--wf-*` tokens
  (stylelint gate); two module tests (`restores the complete browse location…`, `confirms
  empty-folder deletion…`) are flaky under full-suite load and pass in isolation.
- BPMN canvas selection: node id IS the elementId; bound elements resolve to their child
  `ActivityNode` for the activity inspector; pure elements use `BpmnElementInspector`.

## Working setup and delivery loop

- Foundation work: worktrees under `elsa-foundation/.claude/worktrees/` (the `bpmn-phase1` worktree
  has been reused with per-slice branches off `origin/main`; the MAIN checkout is often mid-merge on
  unrelated branches — never build there). Studio work: worktree
  `elsa-foundation-studio/.claude/worktrees/funny-mcnulty-a18f1e` (same per-slice branch pattern).
- Delivery loop per slice: branch off `origin/main` → implement + tests + module docs
  (README/EXTENSION_POINTS per conventions) → verify (module tests, architecture guards, server
  build / studio typecheck+tests+build) → PR with merge-commit merge after CI green (the program
  owner has standing approval for this merge loop). CI: foundation ~5 checks incl. "Build & test";
  studio ~7 incl. browser tests.
- Suggested first Phase-2 step: draft `specs/109-runtime-child-subtree-cancellation/spec.md`
  following the repo's spec conventions, review the checkpoint/cleanup/incident interactions named
  above, and treat the spec as the gate before any boundary-event code — then seam B, then the
  timer catch event as the first executable win.
