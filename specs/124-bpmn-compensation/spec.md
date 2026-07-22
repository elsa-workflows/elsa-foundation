# 124 — BPMN compensation (reverse-order compensation log; compensation boundary events + compensate throw/end events) (BPMN Phase 3, first large construct)

## Goal

Let a published BPMN process **compensate completed work**: a **compensation boundary event** attaches
a compensation **handler** activity to a host element; when the host's child completes successfully,
the engine durably registers that completion in a **reverse-order compensation log**; later, a
**compensate intermediate throw event** or **compensate end event** replays the registered handlers —
**most-recently-completed first** — either for every registered completion or for one referenced
element (`activityRef`). Handlers run **outside normal sequence flow** (no inbound/outbound flows),
one at a time per throw, and the throwing token continues (intermediate) or ends its path (end event)
when the replay finishes.

This is the Phase 3 construct the transactions/cancel-events unit rides next: the log this spec
introduces is the record a transaction subprocess replays on cancel. Everything stays inside the
proven engine discipline: the log and the replay run are additive state records (schema stays v1),
behaviors stay semantics-unaware (a throw behavior emits one new command; the engine owns
registration, ordering, claiming, and replay), and **no new runtime seam is needed** — the runtime
already lets the engine mint tokens at arbitrary elements and schedule their bound children (the
error-boundary absorption path and `SeedToken` are the precedents; the schedule handler has no
live-token/reachability guard).

## Context (what exists today, origin/main = d559b8884)

- **Completion history is NOT durable.** When a host's child completes, `RemoveActiveChild` drops the
  `BpmnActiveChild`, the behavior's `EmitTokens` flips the host token to `Consumed`, and
  `BpmnStatePersister.PruneForPersistence` drops every `Consumed` token not referenced by a live
  active child. Diagnostics are capped at 200, oldest-dropped. Nothing usable as a compensation
  record survives — the log must be a new record captured at completion time.
- **`Sequence` is the determinism source**: every `BpmnStateMutator` builder bumps it by 1 and every
  record id is `"{prefix}:{Sequence+1}"`. Registration order in the new log is therefore total and
  deterministic for free; reverse replay = descending registration id.
- **Boundary machinery (spec 120).** Catch boundaries are armed engine-side when the host's child is
  scheduled (`ArmCatchBoundaries`: one `AwaitingChild` listener token per catch boundary,
  `ParentTokenId` = host token). Error boundaries stay dormant (no listener) and absorb via seam B.
  `ApplyBoundaryCompletionSemantics` handles both directions on completion — **Case B (the completing
  token is a host)** currently only tears down still-armed catch listeners; host completion is thus
  already a recognized engine moment, which is exactly where compensation registration attaches.
  Validation today **requires** every boundary to have ≥1 outbound flow, and every non-error boundary
  to bind a listener child — compensation boundaries invert both (zero outbound; no listener; the
  handler is a separate element reached by association).
- **Throw events do not exist.** `BpmnElementTypes`/`BpmnElementFamilies` have no
  `intermediateThrowEvent`; `ResolveEndEvent` accepts none|terminate only.
  `BpmnEventDefinitionTypes.Compensation` exists as a constant but is entirely unwired.
- **The command envelope**: behaviors return `BpmnBehaviorCommand`s of kind
  `EmitTokens | ScheduleChild | ConsumeToken | TerminateProcess | Fault`. None expresses "replay the
  compensation log"; a new kind is required (the registry/behavior extension path is documented in
  the module `EXTENSION_POINTS.md` and proven by four prior units).
- **Scheduling outside token flow is already possible.** `BpmnScheduler.ScheduleChild` needs a
  `childNodeId` + `elementId` + a token id + the scheduling aei; the engine mints tokens from nothing
  where needed (`SeedToken`; the error boundary mints an `Active` token at a different element). The
  runtime handler validates node/aei consistency only — no requirement that the target element was
  reached by a flow.
- **Sub-process isolation.** A nested `BpmnProcess` runs its own engine over its own private state;
  a parent cannot see into or re-trigger anything inside a completed nested process. BPMN's
  "compensating a subprocess cascades to its inner handlers" is therefore not buildable without
  breaking isolation — stated cut.
- **Interchange**: `compensateEventDefinition` appears nowhere; `<association>` elements are not
  parsed; `isForCompensation` is not parsed. The attribute-passthrough precedent is
  `Properties["bpmn.errorRef"]`. Spec 120 D7 + Out-of-scope (line 261) and the spec 108 phase list
  carry the citable compensation deferrals.
- **Teardown patterns to reuse**: MI instance completions are intercepted before behavior dispatch;
  `CancelTokenAndChild` cascades a coordinator's sub-tokens (parented via `ParentTokenId`) and
  carries per-instance seam-A cancellations keyed `(NodeId, IterationId)`; fault/terminate paths stay
  logical-only (`CancelLiveWork`); seam staging happens only at clean `Complete`/`Defer` exits of
  `FinishEvaluation`.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

**Handlers are first-class elements.** `BpmnElement` gains two additive optional channels:

- `IsForCompensation` (`bool`, default `false`) — marks a **compensation handler** element: a
  task-family or `subProcess` element that binds a child (`ChildNodeId`), participates in **no**
  sequence flows, and is invoked only by the compensation replay.
- `CompensationHandlerElementId` (`string?`) — set only on a **compensation boundary event** (a
  `boundaryEvent` whose single event definition is `Compensation`): the element id of its handler.
  This models the BPMN boundary→handler **association** (associations are otherwise not modeled;
  import derives the ref from the document's association, export re-emits one).

**Compensate throw events**:

- New element type + family: `intermediateThrowEvent`. **This slice wires it for the compensation
  definition only** — any other definition (or none) is rejected at validation and degraded on
  import. Behavior: on token arrival emit the new `TriggerCompensation` command; when the replay
  completes, the throw token routes its outbound flows through normal task-flow selection
  (`bpmn.flow.none-taken` applies as usual).
- `ResolveEndEvent` gains the compensation definition → **compensate end event**: same
  `TriggerCompensation` trigger; when the replay completes the token is consumed (none-end
  semantics).
- Both may carry `activityRef` (modeled as event-definition property `activityRef` in
  `BpmnEventDefinitionProperties`, joining the `name`/`interval`/`cron` convention): compensate only
  the referenced element's registrations. Absent → compensate everything registered in this process.

**Validation (`BpmnGraph.Validate` gains `ValidateCompensation`; existing boundary rules are
compensation-aware):** each rule a deterministic `BpmnExecutionException` naming the element:

1. **Compensation boundary shape.** A compensation boundary must reference an existing handler via
   `CompensationHandlerElementId`; must NOT bind a child (`ChildNodeId` null — it has no listener; it
   is exempted from the catch-boundary listener-child requirement, like error boundaries); must have
   **zero outbound flows** (exempted from the ≥1-outbound rule — its "outbound" is the association;
   the no-inbound and no-default rules apply unchanged). `CancelActivity` is ignored (documented; a
   compensation boundary fires after completion, so interrupting-ness is meaningless).
   `AttachedToRef` host rules are inherited (task-family/subProcess with a bound child); additionally
   the host must NOT carry loop characteristics (multi-instance host compensation is a stated cut).
2. **Handler shape.** An `IsForCompensation` element must be task-family or `subProcess`, bind a
   child, have zero inbound AND zero outbound sequence flows, carry no loop characteristics, host no
   attached boundary events, and be referenced by **exactly one** compensation boundary. A
   `CompensationHandlerElementId` must reference an `IsForCompensation` element. An
   `IsForCompensation` element referenced by no boundary is rejected (orphan handler).
3. **Non-handler elements never reference handlers.** No sequence flow may source from or target an
   `IsForCompensation` element (subsumed by rule 2's zero-flows, stated for the flow side); an
   event-based gateway target, a boundary host, and a start element may not be `IsForCompensation`.
4. **Throw shape.** An `intermediateThrowEvent` must carry exactly one event definition and it must
   be `Compensation` (this slice); it may not bind a child; normal flow rules apply (it is an
   ordinary flow element otherwise). A compensate end event follows end-event rules.
5. **`activityRef` resolves.** An `activityRef` on a compensate throw/end must name an existing
   element **in this process** that has an attached compensation boundary.

### D2 — Runtime semantics (engine-owned; two additive state records)

**D2a — The compensation log.** `BpmnExecutionState` gains additive `Compensables`:
`BpmnCompensable(CompensableId, HostElementId, HandlerElementId, Status)` — `CompensableId` =
`comp:N` from `Sequence` (registration order, total and deterministic), `Status ∈ Registered |
Claimed | Compensated`. Written via `BpmnStateMutator` only. Records are **never pruned** (parallel
to `Canceled` tokens): `Compensated` records stay for determinism/audit and double-replay
protection.

- **Registration**: in the host-completion path (`ApplyBoundaryCompletionSemantics` Case B — the
  completing token's element is a boundary host), when the host has an attached compensation
  boundary, append one `Registered` compensable (host element id + the boundary's handler element
  id). Registration happens **per completion** — a host completed on multiple loop passes (spec 122
  cycles) registers once per pass, each compensated independently, in reverse completion order.
  Registration only on **successful** completion: the fault/cancel paths never reach Case B.
- The log lives and dies with the process's private state — compensation is intra-process (the
  transactions unit builds on exactly this scope).

**D2b — The replay run.** `BpmnExecutionState` gains additive `CompensationRuns`:
`BpmnCompensationRun(RunId, ThrowTokenId, PendingCompensableIds)` — `RunId` = `comprun:N`;
`PendingCompensableIds` ordered **descending by registration** (reverse completion order).

On a `TriggerCompensation` command (token arrival at a compensate throw/end):

- **Target selection**: all `Registered` compensables (with `activityRef`: only those whose
  `HostElementId` matches), in reverse registration order. Selected records flip to `Claimed`
  atomically in the same evaluation — a concurrent second throw (parallel branches) selects only
  still-`Registered` records, so no handler ever runs twice; two concurrent runs replay disjoint
  targets and may interleave (documented).
- **Empty selection** → the throw completes immediately: intermediate routes outbound via its normal
  behavior; end event consumes its token. Compensating nothing is a no-op, not a fault
  (BPMN-conformant, and the `activityRef`-names-a-never-completed-element case lands here).
- **Non-empty**: the throw token stays live as the **run coordinator** with status `AwaitingChild`
  (no new token status), the run record is written, and the **first** handler is scheduled.

**D2c — One handler execution = one sub-token + one child schedule (sequential per run).** For the
head of `PendingCompensableIds`: mint a sub-token at the **handler element** (`ParentTokenId` = the
throw token id, status `AwaitingChild`, inheriting the throw token's `IterationKey`), and
`ScheduleChild` the handler element's bound child on it (scheduling aei = the process's own aei,
`SchedulingCause` = a new compensation cause const). Handler completions are **intercepted before
behavior dispatch** (the MI-instance interception precedent): consume the handler sub-token, flip the
compensable to `Compensated`, advance the run — schedule the next pending handler, or, when none
remain, drop the run record and complete the throw (route outbound / consume, D2b). Handlers see
**current committed variable state** (no historical snapshot — documented; BPMN data snapshots are a
stated cut). The handler's child resolves scoped variables through the ordinary frame chain; no
iteration frame is seeded this slice.

**D2d — Faults and teardown.**

- A handler child fault rides the existing no-boundary path: deterministic `bpmn.child.faulted`
  composite fault (rule 2 forbids boundaries on handlers, so absorption is unreachable). The run
  record and remaining `Claimed` records stay as-is in the faulted state (fault wins; no unwinding).
- `CancelTokenAndChild` is generalized exactly like the MI coordinator cascade: cancelling a throw
  token that owns a compensation run cascade-cancels its live handler sub-token (seam-A cancellation
  keyed by the handler's node id), drops the run record, and flips that run's remaining `Claimed`
  compensables back to `Registered` (they were never run; a later throw may claim them).
  Terminate/fault paths stay logical-only (`CancelLiveWork` precedent, unchanged).
- Late completions of cancelled handler sub-tokens absorb via the existing by-id token lookup
  (Canceled tokens never pruned, unchanged).

**D2e — Continuation discipline (unchanged).** `TriggerCompensation` schedules children, so its
evaluation defers; a terminal continuation never co-exists with staged child schedules; seam-A/B
staging stays confined to the clean `Complete`/`Defer` exits of `FinishEvaluation`; the deadlock
detector is unaffected (a live run = throw token `AwaitingChild` + an active child).

### D3 — Stated cuts

Transactions / cancel events (next unit — rides `Compensables`); escalation throw/boundary;
compensation **event subprocesses**; **multi-instance host compensation** (D1 rule 1 rejects a
compensation boundary on a loop-characteristics host; per-instance compensation data is the blocker);
**nested-process cascade** (compensating a completed `subProcess` host runs the boundary's OWN
handler only — never inner handlers; parent cannot see into the nested process's completed private
state); auto-compensation on cancel/terminate (explicit throw only this slice); compensation data
snapshots (handlers see current state); boundaries or loop characteristics ON handlers; non-compensate
intermediate throw events (message/signal/escalation/none throws stay unwired); a compensation
intermediate **catch** event (not a BPMN construct on this path); cross-process (`activityRef` into a
nested scope) compensation.

### D4 — Interchange

- **Import.**
  - `<association>` elements are now read (container-level pass, like the message/signal index):
    an association connecting a compensation boundary to an activity resolves the boundary's
    `CompensationHandlerElementId` (either direction accepted; the boundary end identifies the
    boundary). A compensation boundary with no resolvable association, or whose handler target is
    not an importable task-family/`subProcess`, **degrades** (boundary dropped + finding; no flow
    cascade — compensation boundaries have no flows).
  - `boundaryEvent` with `compensateEventDefinition` → compensation boundary (`cancelActivity`
    imported as authored but ignored; finding-free). The handler target imports with
    `IsForCompensation = true`; its `isForCompensation="true"` attribute is honored, and an
    `isForCompensation` activity referenced by **no** compensation boundary is **Dropped** with a
    finding (it cannot ride normal flow).
  - `intermediateThrowEvent` with `compensateEventDefinition` → compensate throw;
    `compensateEventDefinition` on an `endEvent` → compensate end. `activityRef` resolves against
    imported elements in the same scope; unresolvable → the event imports **without** the
    compensate definition (throw → Dropped with finding + flow cascade, end → none end event +
    Degraded finding) so the importer never emits a graph the validator rejects (D1 rule 5).
    `intermediateThrowEvent` with any other (or no) definition stays Dropped with a finding
    (unchanged from today's default).
- **Export.** A compensation boundary exports as `boundaryEvent` + `compensateEventDefinition` +
  an `<association>` to its handler; the handler exports as its task/subprocess form with
  `isForCompensation="true"`; compensate throw/end export their element + `compensateEventDefinition`
  (+ `activityRef` when authored). DI: throw/end/boundary shapes ride the existing 36×36 event
  bounds; the association emits a BPMNEdge like a flow edge — **tripwire**: if the DI edge machinery
  is sequence-flow-specific, skip association DI with a documented limitation instead of forcing it.
- **Round-trip** must hold for: boundary+handler+association, compensate throw (with and without
  `activityRef`), compensate end.

## In scope (this slice)

- **Model/validation (D1)**: `BpmnElement.IsForCompensation` + `CompensationHandlerElementId`
  (additive); `intermediateThrowEvent` element type/family; compensate end resolution;
  `ValidateCompensation` + the compensation-aware exemptions in `ValidateBoundaryEvents`;
  `BpmnEventDefinitionProperties.ActivityRef`.
- **State (D2a/b)**: `BpmnCompensable` + `BpmnCompensationRun` records, `BpmnExecutionState.
  Compensables`/`CompensationRuns`, `BpmnStateMutator` builders (add/claim/compensate/release/drop).
- **Engine (D2)**: registration in the host-completion path; `TriggerCompensation` command kind +
  throw/end behaviors; target selection/claiming; sequential replay with interception; run-coordinator
  cascade in `CancelTokenAndChild`; arming path skips compensation boundaries (dormant, like error
  boundaries).
- **Interchange (D4)**: association parsing, compensation boundary/handler/throw/end import + export
  + round-trip + degrade findings.
- **Tests + module docs**: validation (every D1 rule); registration (per-completion, cycles);
  reverse-order replay (multi-host); `activityRef` targeting; empty-log no-op; concurrent-throw
  claiming (disjoint targets); handler fault → composite fault; interrupting teardown mid-replay
  (claimed records released); determinism (identical runs → identical `comp:`/`comprun:` ids);
  interchange round-trips + degrades. BPMN README + EXTENSION_POINTS; Interchange README. No runtime
  EXTENSION_POINTS change (no runtime seam is touched).

## Out of scope

Everything in D3; Studio authoring UX (separate repo); any change to the spec 119/120/121/122/123
semantics or the runtime seams.

## Functional requirements

**FR-1 — Validation.** Every D1 rule rejects deterministically with the element named; a valid
compensation graph (boundary + handler + throw) validates cleanly; all pre-existing validation is
byte-identical for graphs without compensation elements.

**FR-2 — Registration.** A boundary host completing successfully appends one `Registered`
compensable (per completion, including repeated completions across loop passes); faulted/cancelled
hosts register nothing; hosts without a compensation boundary register nothing. `comp:` ids derive
from `Sequence`.

**FR-3 — Reverse-order replay.** A ref-less compensate throw claims every `Registered` compensable
and runs the handlers strictly one at a time in reverse registration order; each handler's child
executes exactly once; the throw routes outbound only after the last handler completes. A compensate
end event does the same and then consumes its token.

**FR-4 — Targeted replay.** With `activityRef`, only the referenced element's `Registered`
compensables are claimed (all of that element's registrations, reverse order); others stay
`Registered`.

**FR-5 — Empty replay.** A throw with nothing to claim completes immediately (route/consume); no
fault, no run record.

**FR-6 — At-most-once.** A compensable is never replayed twice: claimed/compensated records are
invisible to later selections; two concurrent throws claim disjoint targets.

**FR-7 — Handler isolation.** Handler elements never participate in normal token flow: no tokens
arrive at them via flows (validation), they contribute nothing to join accounting, and their
completions never route boundary/host semantics — only the replay interception.

**FR-8 — Faults and teardown.** A handler child fault → deterministic `bpmn.child.faulted` composite
fault. Cancelling a replaying throw token cascades to its live handler sub-token (seam-A), drops the
run, and releases that run's unrun `Claimed` records back to `Registered`. Terminate mid-replay
stays logical-only.

**FR-9 — Determinism.** Identical runs produce identical `comp:`/`comprun:`/token ids and identical
replay order.

**FR-10 — Continuation discipline.** Unchanged invariants: no terminal continuation with staged
child schedules; no `Fault`/`Cancel` continuation with staged seam requests; seam staging only at
clean `Complete`/`Defer` exits.

**FR-11 — Interchange.** Boundary+handler+association, compensate throw (± `activityRef`), and
compensate end round-trip import→export→import with fidelity; unresolvable associations/refs and
orphan `isForCompensation` activities degrade/drop with specific findings; the importer never emits
a graph the validator rejects.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1 (`Compensables`/`CompensationRuns` additive);
  `BpmnStateMutator` sole mutation home; all ids derive from `Sequence`; `Canceled` tokens and
  `Compensated`/`Claimed` compensables are never pruned; no new token status.
- Behaviors stay decision-only: throw behaviors emit `TriggerCompensation` and nothing else;
  registration, ordering, claiming, replay, and teardown are engine-owned.
- **No runtime changes**: no new runtime seam, no runtime model additions, no handler changes outside
  the BPMN module. (If implementation discovers a runtime gap, that is a stop-and-report, not a
  workaround.)
- `CancelLiveWork` stays logical-only. Seam-A/B staging discipline unchanged. Spec 119/120/121/122/123
  suites pass unmodified.
- Deterministic ids only; no wall-clock identity. No new HTTP endpoints; domain project-tree naming
  guard and VF-ACT gates hold.

## Success criteria

- Validation tests: every D1 rule (boundary shape, handler shape, orphan handler, flow-into-handler,
  throw shape, unresolvable/boundary-less `activityRef`, MI-host rejection).
- Execution: two hosts A then B completed → ref-less throw runs B's handler then A's (order pinned
  via capture children); `activityRef` = A runs only A's; empty log → immediate route; a cycle
  completing one host twice → two registrations, replayed newest-first; handler fault → composite
  fault; interrupting boundary cancelling a mid-replay throw (via an enclosing construct) releases
  unrun claims; parallel branches throwing concurrently → disjoint claims, every handler exactly
  once; determinism (identical runs → identical ids).
- Interchange: three round-trips (boundary+handler, throw±ref, end); degrade findings for missing
  association, orphan `isForCompensation`, unresolvable `activityRef`.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.
