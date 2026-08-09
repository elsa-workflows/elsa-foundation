# 134 — BPMN event subprocesses, tier 2: message/signal/timer triggers (scope listeners, token-kind discriminator, listener-aware completion) (BPMN Phase 3)

**Status**: Implemented
**Merged**: PR #1000

## Goal

Complete the event-subprocess construct: **message-, signal-, and timer-triggered** event
subprocesses. Unlike tier 1's dormant catchers (escalation/error, spec 128 + 132), these need a
**scope listener** — a suspending catch child (the spec-116 `Event`/`Delay` machinery) armed at
process start and re-armed per fire — that waits for an external stimulus while the rest of the
process runs, and that must **never block the process from completing**: when only scope listeners
remain live, the engine tears them down and completes in the same evaluation (the
teardown-before-check pattern, generalized).

Two structural additions make this possible, both pinned by the terrain analysis:

1. **`BpmnToken.Kind`** — an additive nullable token-role discriminator (`Listener` |
   `Activation`). Listener tokens and spec-128 activation tokens both sit at the same
   `TriggeredByEvent` element with `ParentTokenId = null`; position no longer discriminates, and
   both the completion-routing fork and the liveness gates need the distinction. A field is not a
   token **status** — the status set is unchanged; this is intended-additive schema-v1 growth.
2. **`BpmnElement.ListenerNodeId`** — an additive nullable second bound-child channel on the
   event-subprocess element, referencing the synthesized listener node (`Delay` for timer, `Event`
   for message/signal — the spec-116/118 synthesis reused verbatim). `BpmnGraph.Validate`'s
   exactly-one-binding accounting is extended to register it (a listener node is bound by exactly
   one element, via `ListenerNodeId`, and only on `TriggeredByEvent` elements with an external
   trigger).

## Context (verified terrain, origin/main ≥ 13cf5bbb0; line numbers drift — verify at implementation)

- **The liveness problem, precisely.** `FinishEvaluation`: completion requires zero live tokens
  AND zero `ActiveChildren` (~1755); the deadlock detector faults when no `ActiveChildren` remain
  and all live tokens are `WaitingAtJoin` (~1772). An armed listener is an `AwaitingChild` token +
  an `ActiveChild` record — it blocks completion forever. **The two predicates must be patched
  together**: excluding listeners from the completion check alone makes the deadlock detector
  misfire on listener-plus-real-join states (the listener's excluded `ActiveChild` makes
  `ActiveChildren.Count == 0` true while a genuine join-waiter sits there).
- **Teardown sites must NOT exclude listeners**: `CancelLiveWork` (fault/terminate),
  `StopOtherLiveWork` (interrupting activation + transaction cancel) enumerate live tokens to tear
  down — listeners are ordinary live tokens there, which is exactly right (an interrupting fire
  drains sibling listeners for free; terminate kills them). `BpmnTokenCoordinator`'s
  inclusive-join reachability is benign (flow-less elements reach nothing) — verify, don't touch.
- **The tier-1 machinery reused**: `ActivateEventSubprocess` (interrupting stop-others + body
  schedule via the D2 start hint; non-interrupting concurrent activations),
  `HandleEventSubprocessBodyCompletion` (consume activation token, route nothing), the
  `OnChildCompletedAsync` interception ladder (MI → compensation → transaction → **body
  completion** → call-activity failure → race → boundary → dispatch), the graph-derived catcher
  index (`BpmnEventSubprocessCatcher`), `StagePendingSubtreeCancellations` at clean exits.
- **Synthesis precedent**: `BuildDelayCatchChild` (timer `interval`) / `BuildEventCatchChild`
  (message/signal `name`, `CanStartWorkflow=false`) — the importer's catch-child pattern; the
  suspend/resume machinery is identical for a child armed at process start vs mid-flow (bookmark/
  durable timer registers at suspend; delivery via the stimulus router / timer scheduler).
- **Publish trigger surface**: a red herring — the nested body node registers no process-start
  triggers (`CanStartWorkflow=false`; the provider scans the root graph only). Verify no
  `IsExternalStartTrigger` leak for body starts; tier-1's escalation/error start families are
  already carved out.
- **Spec-128 stale note**: its Deviations still describe error triggers as gated — spec 132
  un-gated them; deferred child schedules + seam-B are proven safe. Tier 2 inherits a working
  deferred-schedule path.
- **Boundary-listener contrast**: catch-boundary listeners are single-shot and position-discriminated
  (they sit at a distinct boundary element); scope listeners are neither — hence the token-kind
  field and explicit re-arm.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- `ValidateEventSubprocesses` extends the supported trigger set: the body's single start event may
  now carry `message`/`signal` (with `name`) or `timer` (with `interval` — `timeDuration`-shaped;
  `cron`/`timeCycle` remain unsupported for event subprocesses, D5) definitions, in addition to
  tier-1 escalation/error. Interrupting flag semantics unchanged (message/signal/timer may be
  interrupting or non-interrupting).
- **`BpmnElement.ListenerNodeId`** (additive, nullable): required on a `TriggeredByEvent` element
  whose trigger is message/signal/timer (the external triggers need an armed listener); must be
  null for escalation/error (dormant catchers, unchanged). The referenced node must exist, must be
  bound by exactly this one element's `ListenerNodeId` (the exactly-one-binding accounting extended:
  a node is bound EITHER as some element's `ChildNodeId` OR as some element's `ListenerNodeId`,
  never both, never twice), and by convention is the synthesized `Delay`/`Event` matching the
  body-start trigger (not type-validated — the callActivity precedent; a mismatched listener child
  simply behaves as authored).
- **`BpmnToken.Kind`** (additive, nullable string or enum: `Listener` | `Activation`): `null` for
  every ordinary token (byte-compatible with all persisted state). The spec-128 activation-token
  mint sites now stamp `Activation`; tier-2 listener mints stamp `Listener`. No new token
  **status**; the never-prune and id-derivation rules unchanged.

### D2 — Arming, firing, re-arm (engine-owned)

- **Arm at seeding**: all `StartAsync` seeding paths (root trigger, direct invocation,
  scheduled-start hint) additionally mint, for each external-trigger catcher in the graph, one
  `Listener`-kind token at the catcher element (`ParentTokenId=null`, `AwaitingChild`) and schedule
  its `ListenerNodeId` child (new scheduling-cause const). Deterministic order: catcher element-id
  ordinal.
- **Listener completion interception**: in `OnChildCompletedAsync`, BEFORE the body-completion
  check (both tokens sit at the same element — discriminate on `Kind == Listener`):
  - **Non-interrupting**: consume the listener token; mint a fresh `Listener` token + schedule a
    fresh listener child (**re-arm** — deterministic ids from `Sequence`); then activate the body
    non-interrupting (spec-128 activation: fresh `Activation` token + body schedule with the start
    hint). Repeated fires and concurrent activations compose exactly as tier-1 non-interrupting.
    Timer repetition falls out of the re-arm loop (each arm is a single `Delay` shot).
  - **Interrupting**: consume the listener token; activate interrupting — `StopOtherLiveWork`
    (keeping the new activation token) drains everything else INCLUDING sibling listener tokens
    (ordinary live tokens there; their suspended children ride the seam-A carries); no re-arm.
- **Body completion**: unchanged tier-1 handling (`Kind == Activation` token consumed, nothing
  routed). After a non-interrupting body completes, the re-armed listener keeps waiting; after an
  interrupting body completes, only-listeners-remain cannot occur for THIS scope's listeners (they
  were drained) — the process completes normally.

### D3 — Listener-aware completion (the liveness fix)

- `FinishEvaluation` computes liveness over **non-listener** tokens and **non-listener**
  `ActiveChildren` (a listener's active child = the child scheduled on a `Kind == Listener`
  token). Both predicates — clean completion AND the join-deadlock detector — use the same
  filtered view, patched together.
- **Only-listeners-remain → teardown-then-complete**: when the filtered view is empty but live
  `Listener` tokens exist, the clean-completion branch (strictly after the fault/pending-fault/
  terminate branches, which own their own teardown via `CancelLiveWork`) cancels each listener
  token via `CancelTokenAndChild` (reason const `bpmn.event-subprocess.listener-superseded-by-completion`,
  seam-A carries for the suspended listener children folded into the same commit's staged
  cancellations) and completes with the normal outcome selection. Deterministic teardown order:
  token-id ordinal.
- Teardown/stop sites (`CancelLiveWork`, `StopOtherLiveWork`, transaction cancel) intentionally
  unfiltered — listeners die with the scope. `CancelTokenAndChild` cascades need no change
  (listeners have no sub-tokens).

### D4 — Interchange

- **Import**: `<subProcess triggeredByEvent="true">` whose body start carries
  `messageEventDefinition`/`signalEventDefinition` (root-index name resolution as usual) or
  `timerEventDefinition` with `<timeDuration>` → imports with the synthesized listener node
  (`BuildEventCatchChild`/`BuildDelayCatchChild` reused; node id convention `node-{id}-listener`)
  + `ListenerNodeId`; the body imports exactly as tier 1 (nested structure + start hint metadata).
  `timeCycle`/`timeDate` timer bodies, `cron` shapes → **Dropped** + finding (unchanged degrade
  family). The tier-1 "tier 2 / unsupported" findings for message/signal/timer flip to real
  imports.
- **Export**: the body start's definition exports as usual (name / `timeDuration` from
  `interval`); the listener node is NOT exported (synthesized, the catch-child precedent);
  `isInterrupting="false"` when non-interrupting. Round-trips: message + signal + timer,
  interrupting + non-interrupting.
- Importer never emits a graph the validator rejects.

### D5 — Stated cuts

`timeCycle`-native repetition (re-arm gives non-interrupting repetition; cycle import stays a
degrade — consistent with the module-wide cut); `cron` event-subprocess timers; correlation-scoped
message delivery beyond what the shipped `Event` stimulus provides; conditional/compensation
triggers; listener re-arm throttling/dedup; Studio authoring UX. Tier-1 escalation/error semantics
byte-identical (no listener, no `ListenerNodeId`, dormant as shipped).

## In scope

- Model: `BpmnToken.Kind`, `BpmnElement.ListenerNodeId` (both additive); validation extensions
  (trigger set, listener-node binding rules, exactly-one-binding accounting).
- Engine: arming at all seeding paths; listener-completion interception (re-arm + activation);
  `Kind` stamping at spec-128 activation mints; the D3 filtered liveness + teardown-then-complete;
  reason + scheduling-cause consts; diagnostics (`ScopeListenerArmed`/`ScopeListenerFired`/
  `ScopeListenerRetired`).
- Interchange per D4. Tests + module docs (BPMN README/EXTENSION_POINTS, Interchange README; fix
  spec-128's stale Deviations note about error triggers in passing — one line, cites spec 132).
- Tests: validation (trigger set, ListenerNodeId rules, escalation/error must-not-have-listener);
  arming at start (listener suspended, process still completes when real work ends —
  teardown-then-complete pinned with durable child cancelled + bookmark gone); message-triggered
  non-interrupting fire (stimulus resume → body runs alongside → re-armed listener fires AGAIN —
  the repeat pin); interrupting fire (other work + sibling listeners drained, body runs, scope
  completes); timer-triggered (Delay child; non-interrupting repetition via re-arm — two fires);
  deadlock-detector consistency (listener + real WaitingAtJoin join-waiter → still deadlock-faults
  correctly; listener alone → completes); interplay: terminate kills listeners; transaction cancel
  kills listeners; interrupting escalation activation drains tier-2 listeners; determinism
  (identical runs → identical listener/activation ids incl. re-arms); interchange round-trips +
  degrades. Stimulus recipes: `BpmnRuntimeFixture.ResumeAsync`/`BookmarksAsync` + the spec-116/117
  catch-event tests.

## Out of scope

Everything in D5; runtime changes (the stimulus/bookmark/timer machinery is consumed as shipped —
gaps are stop-and-report).

## Functional requirements

**FR-1 — Validation.** Extended trigger set validates; `ListenerNodeId` required/forbidden rules
hold; binding accounting rejects double-binding and orphan listener nodes; tier-1 graphs validate
byte-identically.

**FR-2 — Arming.** Every external-trigger catcher arms exactly one listener at seeding, on every
seeding path, in deterministic order; listeners suspend on the shipped bookmark/timer machinery.

**FR-3 — Completion is never blocked.** A process whose real work finishes completes in the same
evaluation, tearing down armed listeners (durable child state cancelled, bookmarks/timers
reclaimed via the staged seam-A carries). The deadlock detector's semantics are preserved under
the filtered view (a real join-deadlock still faults; a lone listener never does).

**FR-4 — Non-interrupting fire + re-arm.** A stimulus fires the listener: body activates
alongside untouched scope work, the listener re-arms deterministically, and a second stimulus
fires it again (timer: repetition via re-arm).

**FR-5 — Interrupting fire.** All other live work — including sibling listeners — drains via the
existing stop-others path; the body runs; the scope completes normally.

**FR-6 — Kind discipline.** `Kind` stamps only `Listener`/`Activation` mints; null for all other
tokens; no new token status; never-prune and `Sequence`-derived ids unchanged; persisted tier-1
state (Kind-less activation tokens) is read compatibly (null Kind at a `TriggeredByEvent` element
with a live body child = activation — pin the fallback or verify no such mid-flight state can
exist across this upgrade; pre-release, no back-compat shims — prefer treating null-as-ordinary
and stamping consistently from this version).

**FR-7 — Interplay.** Terminate, transaction cancel, and interrupting activations of ANY kind
tear listeners down (unfiltered teardown sites); compensation/MI/race/boundary semantics
byte-identical elsewhere.

**FR-8 — Determinism.** Identical runs (including N re-arms) produce identical ids and
diagnostics order.

**FR-9 — Interchange.** D4 round-trips and degrades; the importer never emits a
validator-rejected graph.

## Invariants that MUST survive

- Schema v1, additive only (`Kind`, `ListenerNodeId`, diagnostics, consts); `BpmnStateMutator`
  sole mutation home; ids from `Sequence`; `Canceled` tokens never pruned; **no new token
  status**; behaviors decision-only (arming/firing/re-arm/teardown all engine-owned).
- The liveness filter exists in exactly the two `FinishEvaluation` predicates; teardown
  enumerations untouched.
- Zero runtime-module changes. Specs 119–133 suites pass unmodified (except the one-line spec-128
  stale-note fix).

## Success criteria

- All FR tests green, including the repeat-fire pin, the deadlock-consistency pin, and the
  teardown-then-complete pin.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations from the ratified plan

- **Arming is two-phase (tokens before propagate, children after), not a single arm-at-seeding.** D2 says "arm at
  seeding … mint a Listener token and schedule its ListenerNodeId child" before the seed propagates. The runtime
  forbids a terminal structural decision (Complete/Fault) in an evaluation that also scheduled children (verified:
  a `start → end` scope with a listener faulted *"A terminal structural decision cannot also schedule child
  activities in the same execution."*). Arming the child unconditionally before propagate therefore strands the
  just-scheduled listener child against the same evaluation's completion whenever the seed's real work finishes
  synchronously. The listener **tokens** are still minted BEFORE propagation (so an interrupting activation raised
  by the seed's own propagation — an own-scope escalation — drains them as ordinary live tokens, the FR-7 interplay),
  but their suspending **children** are scheduled AFTER propagation and only when real work remains (an active child
  is pending). A scope whose seed completes synchronously completes with no listener child ever scheduled and the
  listener token torn down (BPMN semantics: the listener never got a chance to fire); a pure synchronous
  join-deadlock arms nothing so the deadlock surfaces cleanly. The re-arm path (`HandleScopeListenerFired`) arms
  token+child together — it always then schedules the body and defers, so it is never a terminal evaluation.
  Determinism is preserved (identical runs → identical ids and diagnostics order).
- **`Terminate` tears down the listener token but not its durable child bookmark.** Terminate rides the unfiltered
  logical-only `CancelLiveWork` (the tier-1 terminate precedent: in-flight/suspended children are absorbed on late
  completion, not seam-A cancelled). So a terminated scope's listener token is Canceled (it can never fire — a late
  resume is absorbed by the canceled-token guard) but its `Event`/`Delay` bookmark lingers, exactly as for any
  terminated suspended child. Teardown-then-complete (the clean path) DOES reclaim the durable child via seam A.
