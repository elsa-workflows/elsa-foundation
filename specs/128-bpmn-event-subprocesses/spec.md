# 128 — BPMN event subprocesses, tier 1: escalation + error triggers (dormant catchers; scheduled-start seeding) (BPMN Phase 3)

## Goal

Let a published BPMN process contain **event subprocesses**: flow-less `subProcess` elements marked
`triggeredByEvent`, whose body activates when their **start-event trigger** occurs while the
enclosing scope is active. **This slice ships the two dormant-catcher triggers** — **escalation**
(interrupting and non-interrupting) and **error** (interrupting only, per BPMN) — which activate
from signals the engine already routes (seam-C escalation notifications from spec 127; child faults
via seam B from spec 115/120). It also closes the **scheduled-start seeding gap** every future
trigger shares: the engine can now schedule a nested body and tell it *which event-start element to
seed from*.

Two semantics land with it: **own-scope catching** — an escalation thrown by a sibling in the same
scope is now caught by that scope's own escalation event subprocess *before* bubbling to the parent
(spec 127's unconditional stage-upward becomes check-own-scope-first, preserving the one-hop
bubbling contract); and **interrupting-but-scope-survives** — an interrupting activation stops all
other live work in the scope while the scope itself keeps running until the body completes, then
completes normally (the spec-125 stop-others-keep-coordinator loop, lifted out of its
transaction-specific home).

**Stated split**: message/signal/timer-triggered event subprocesses are the follow-up tier (they
need a scope-level listener token shape and completion-liveness rework — a listener must survive
until process end without blocking completion, which collides with `FinishEvaluation`'s
teardown-before-check design; deferred with the analysis recorded here). Compensation and
conditional triggers are stated cuts.

## Context (what exists today, origin/main ≥ 7a457ddb2; verify line numbers at implementation)

- **Flow-less element precedent**: compensation handlers (`ValidateCompensation` rule 2 — flow-less
  task/subProcess with a bound child, no loop characteristics, no attached boundaries). An
  unconnected `subProcess` passes structural validation today but is dead (never seeded).
  `triggeredByEvent` appears nowhere (model, importer, exporter) — an event subprocess currently
  imports as a normal dead subprocess, silently.
- **The seeding gap (shared by all triggers)**: a nested `BpmnProcess` body whose only start is an
  event start **faults** when scheduled (`StartAsync` → `SeedFromDirectInvocation` →
  `bpmn.start.none-available`); `SeedFromTrigger` reads `BpmnStartTrigger.StartElementIdMetadataKey`
  from `TriggerMetadata` but is gated to root trigger deliveries
  (`TriggerNodeId == own node id`); `BpmnScheduler.ScheduleChild` forwards token/element/cause
  command metadata but no start-element hint. The engine-internal channel to extend is **command
  metadata** (the `bpmn.tokenId` precedent).
- **Escalation routing (spec 127)**: `RaiseEscalation` stages to the parent **unconditionally**
  when a parent exists (no own-scope check); the parent-side catch is
  `HandleEscalationNotification` → `MatchEscalationBoundary` (exact beats catch-all; boundaries
  only) → `FireEscalationBoundary` (non-interrupting mint / interrupting cancel-cascade) →
  `BubbleOrUnhandled`. All the resolve/match/fire machinery is reusable; event subprocesses add a
  second catcher kind at both decision points (the raise site and the notification matcher).
- **Error routing (spec 120)**: `OnChildFaultedAsync` gates on
  `AttachedErrorBoundary(faultedChild.ElementId)`; with one, `AbsorbChildFaultThroughErrorBoundary`
  absorbs via seam B (incident resolved, subtree reclaimed, host token cancelled, boundary token
  routed); without one, deterministic `bpmn.child.faulted`. No scope-wide error catcher exists.
  Error-code matching remains cut (spec 120) — error event subprocesses are catch-all this slice.
- **Interrupting precedent**: spec 125's `CancelTransaction` stops every live token except a kept
  coordinator — but it is welded to the `Cancelling` flag and the `Cancelled` completion outcome.
  `CancelLiveWork` is too blunt (ends the scope). The per-token cancel/cascade helpers
  (`CancelTokenAndChild`, `CancelHostListeners`, seam-A carries staged at clean exits) are the
  reusable pieces.
- **Non-interrupting multi-activation precedent**: MI parallel sub-tokens + the non-interrupting
  escalation boundary's repeated fire (each notification mints a fresh token).
- **Liveness accounting** (why external triggers are the next tier, recorded): `FinishEvaluation`
  completes only when no `Active`/`AwaitingChild`/`WaitingAtJoin` token and no `ActiveChildren`
  remain; armed boundary listeners avoid stranding the process purely by teardown-before-check.
  A scope-level listener must survive until process end — irreconcilable without new liveness
  semantics; NOT attempted in this slice. Escalation/error event subprocesses are **dormant**
  (graph-derived; no armed anything) so they have no liveness footprint.
- **Deferral trail**: spec 120 `:261`, spec 124 `:189`, spec 125 `:158`, spec 127 `:141` ("the
  event-subprocess unit" for escalation start events), phase-2 doc (seam C named the prerequisite).

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- `BpmnElement` gains additive **`TriggeredByEvent`** (`bool`, default `false`) — the
  `IsForCompensation`/`IsTransaction` flag pattern. Valid only on a `subProcess`-family element
  that binds a child.
- **Shape rules** (`ValidateEventSubprocesses`), each deterministic:
  1. A `TriggeredByEvent` element participates in **no sequence flows** (in/out), carries no loop
     characteristics, hosts **no attached boundary events**, is not a boundary host target of
     `attachedToRef`, is not `IsForCompensation`, not `IsTransaction`, and is not referenced as a
     compensation handler. (The compensation-handler rule family, mirrored.)
  2. The **body** (the bound nested structure) must declare **exactly one start event**, and that
     start event must carry exactly one event definition of a **supported trigger**: this slice
     `escalation` (with optional `code`; code-less = catch-all) or `error` (catch-all only, no code
     matching — the spec-120 cut honored). A none-start, multiple starts, or an unsupported
     trigger definition is rejected. *(Validation reaches into the authored body structure the
     same way `BpmnStructure.Variables` is read for MI validation — authoring-time knowledge, no
     runtime cross-scope reach.)*
  3. **Interrupting flag**: the body's start event carries `Interrupting` (authoring channel on
     the start element's event definition properties or the element — mirror how boundary
     `CancelActivity` is modeled; BPMN maps it from the start event's `isInterrupting` attribute,
     default `true`). An **error**-triggered event subprocess must be interrupting
     (non-interrupting error start is rejected). Escalation may be either.
  4. Per scope: escalation event subprocesses must have **distinct codes** and at most **one**
     code-less catch-all; at most **one** error event subprocess.
- The parent-side element and its nested body are marked consistently by the importer
  (`triggeredByEvent="true"` + the start event's definition); authoring sets both.

### D2 — Scheduled-start seeding (engine-internal; closes the shared gap)

- `BpmnScheduler.ScheduleChild` gains an optional **start-element hint** forwarded as command
  metadata (`bpmn.startElementId` — reusing the existing key convention from the trigger seam, but
  on the scheduling channel). `BpmnProcess.StartAsync` gains a third seeding path: when the
  scheduler work item's command metadata carries the hint, seed exactly that start element
  (validating it exists and is a start event; a bad hint faults deterministically). Root trigger
  deliveries and direct invocation are byte-identical (the hint is absent there).
- This is BPMN-module-internal (command metadata already flows through `SchedulerWorkItem.
  CommandMetadata`); **zero runtime changes**. The event-subprocess body is scheduled with the
  hint naming its single event-start element; the body then runs as a perfectly ordinary nested
  process (its start event seeds one token, flows run, it completes/faults normally).

### D3 — Escalation-triggered event subprocesses

**Catcher registry**: graph-derived, dormant — `BpmnGraph` indexes the scope's `TriggeredByEvent`
elements by trigger kind and code (no state records, no arming).

**Own-scope check (raise site)**: `RaiseEscalation` now consults the throwing scope's OWN
escalation event subprocesses first (exact code beats catch-all). On a match it activates the
event subprocess (D5) **instead of staging upward**; only on no local match does it stage the
seam-C notification (parent exists) or no-op (root). This preserves one-hop bubbling: each scope
consults itself before passing the signal up.

**Notification-side check (parent catch)**: `HandleEscalationNotification`'s matching order
becomes, per the pinned **specificity ladder**: (1) host-attached escalation boundary with exact
code; (2) scope escalation event subprocess with exact code; (3) host boundary catch-all; (4)
scope event subprocess catch-all; (5) no match → bubble (unchanged `BubbleOrUnhandled`). Exact
beats catch-all across kinds; within a specificity level the host boundary (more local to the
throwing child) beats the scope-level subprocess. Boundary firing is byte-identical to spec 127;
an event-subprocess match activates the body (D5).

### D4 — Error-triggered event subprocesses

`OnChildFaultedAsync`'s catcher resolution becomes: (1) host-attached error boundary (existing,
byte-identical); (2) else the scope's error event subprocess — absorb the fault via seam B exactly
like the boundary path (incident resolved, faulted child's subtree reclaimed, the faulted child's
host token cancelled + its listeners torn down), then activate the event subprocess **interrupting**
(D5) instead of routing a boundary token; (3) else the existing deterministic `bpmn.child.faulted`.
The faulted child's own scope handles it first by construction (the fault surfaces to the
immediate parent composite); no cross-scope error semantics change.

### D5 — Activation semantics (both triggers)

- **Activation = one sub-token + one scheduled body child**: mint an activation token at the
  event-subprocess element (`Active`→`AwaitingChild` shape identical to a host token;
  `ParentTokenId` null — it is scope-level work; inherits the triggering context's iteration key
  where one exists: the escalation host token's key on the notification path, the throw token's
  key on the own-scope path, the faulted child host token's key on the error path), and schedule
  the bound body child with the D2 start-element hint + the trigger's payload facts
  (escalation code/name) as command metadata for diagnostics. Body completion routes NO outbound
  (the element has no flows): the activation token is consumed, an `EventSubprocessCompleted`
  diagnostic is written, and the scope's normal liveness/completion accounting proceeds. Body
  fault: the existing `bpmn.child.faulted` path (an error event subprocess does NOT catch its own
  body's faults; no recursion).
- **Interrupting activation**: before scheduling the body, stop all other live work in the scope —
  every live token except the activation token is cancelled through `CancelTokenAndChild` (the
  MI/race/run coordinator cascades and seam-A carries fire as usual; sibling scope catchers stay
  dormant). Extract the spec-125 stop-others loop into a shared engine helper
  (`StopOtherLiveWork(state, keepTokenId, reason, …)`) used by both `CancelTransaction` and this
  path — behavior of the transaction path byte-identical. Reason const
  `bpmn.event-subprocess.scope-interrupted`. The scope completes normally when the body (and any
  in-flight absorbed stragglers) finish — no outcome change, no `Cancelling`-style flag needed
  (the activation token keeps the scope alive; when it consumes, ordinary completion fires).
- **Non-interrupting activation** (escalation only): each trigger occurrence mints a fresh
  activation token + body schedule (repeated fires legal, concurrent activations legal — distinct
  activation tokens, the MI-parallel precedent; the D2 hint + fresh aei per schedule make bodies
  independent). Other scope work untouched.
- **Late/degenerate cases**: an escalation matched to an interrupting event subprocess whose scope
  is already winding down (no other live tokens) simply activates (nothing to stop). Notification
  late races follow spec 127's pins (non-interrupting fires; interrupting-on-terminal-host —
  n/a here, the scope is the catcher and the scope is Running by seam-C contract).

### D6 — Stated cuts

**Message/signal/timer-triggered event subprocesses** (tier 2 — scope-listener token shape +
completion-liveness rework; the analysis in Context is the handoff); compensation and conditional
triggers; escalation/error **start events at the root process level** beyond what D3/D4 give
scope-locally (no new trigger surface — root processes gain own-scope catching only);
error-code matching (still cut, everywhere); nested event subprocesses inside event-subprocess
bodies (validation rejects `TriggeredByEvent` elements inside a body structure this slice — keeps
activation semantics single-level; lift later); Studio authoring UX.

### D7 — Interchange

- **Import**: `<subProcess triggeredByEvent="true">` → `TriggeredByEvent` element + nested body
  (the body's start event imports with its event definition as usual — escalation refs resolve
  through the spec-127 root `<escalation>` index; `isInterrupting` on the start event maps to the
  interrupting flag, absent → `true`). Degrades (imports as a normal subprocess... no — a flow-less
  normal subprocess is dead; instead **Dropped** with a finding, flow-cascade n/a) when: the body
  has no/multiple start events, an unsupported trigger definition (message/signal/timer/
  conditional/compensation → named "tier 2 / unsupported" finding), a non-interrupting error
  start, code collisions per scope, or a second catch-all/error subprocess per scope. The importer
  never emits a graph the validator rejects.
- **Export**: `triggeredByEvent="true"` + the body's start event with its definition +
  `isInterrupting="false"` when non-interrupting (default-true convention); escalation root
  declarations dedupe via the spec-127 pattern.
- **Round-trip**: escalation (interrupting + non-interrupting + catch-all) and error event
  subprocesses.

## In scope (this slice)

- **Model/validation (D1)**: `TriggeredByEvent` flag; `ValidateEventSubprocesses`; body
  start-event shape validation; interrupting flag modeling.
- **Engine (D2–D5)**: scheduled-start seeding (scheduler hint + third `StartAsync` path);
  graph catcher indexes; own-scope raise check; notification-side specificity ladder;
  error-path scope catcher + seam-B absorption reuse; activation (interrupting via extracted
  `StopOtherLiveWork` shared with `CancelTransaction`; non-interrupting repeated activations);
  diagnostics (`EventSubprocessActivated`/`EventSubprocessCompleted` + reason const).
- **Interchange (D7)**: import/export/round-trip/degrade for both triggers.
- **Tests + module docs**: validation (every D1 rule); own-scope sibling escalation caught locally
  (interrupting: other branch stopped, scope completes after body; non-interrupting: both run);
  child-thrown escalation caught by parent's event subprocess via seam C (and the specificity
  ladder: boundary-exact beats subprocess-exact beats boundary-catch-all beats
  subprocess-catch-all — pin at least boundary-exact-vs-subprocess-catch-all and
  subprocess-exact-vs-boundary-catch-all); bubbling still works when neither matches; error event
  subprocess absorbs a child fault (incident resolved, scope interrupted, body runs, scope
  completes) vs no-catcher composite fault byte-identical; repeated non-interrupting activations;
  body fault → composite fault; scheduled-start seeding (bad hint faults; body seeds at its event
  start); transaction regression (extracted helper byte-identical — spec 125 suite);
  determinism; interchange round-trips + degrades. BPMN README + EXTENSION_POINTS; Interchange
  README.

## Out of scope

Everything in D6; zero runtime-module changes (seam C/B consumed as shipped — any gap is
stop-and-report).

## Functional requirements

**FR-1 — Validation.** Every D1 rule rejects deterministically; graphs without `TriggeredByEvent`
elements validate byte-identically.

**FR-2 — Seeding.** A scheduled body seeds exactly its hinted event-start element; root trigger
delivery and direct invocation are byte-identical; a bad hint faults deterministically.

**FR-3 — Own-scope escalation.** A sibling throw is caught by the scope's own matching event
subprocess (exact beats catch-all) without any seam-C staging; only unmatched throws stage upward
(parent) or no-op (root). One-hop bubbling semantics are otherwise unchanged.

**FR-4 — Notification-side ladder.** A child-thrown escalation reaching a scope via seam C resolves
per the D3 specificity ladder; boundary behavior is byte-identical to spec 127 when a boundary
wins; unmatched escalations bubble unchanged.

**FR-5 — Error catch.** A child fault with no host error boundary but a scope error event
subprocess absorbs via seam B (incident `Resolved`, subtree reclaimed) and activates the subprocess
interrupting; with neither, the composite faults byte-identically to today.

**FR-6 — Interrupting activation.** All other live work in the scope stops (coordinator cascades +
seam-A carries honored); the scope survives until the body completes, then completes normally with
its ordinary outcome.

**FR-7 — Non-interrupting activation.** Each occurrence activates an independent body; other scope
work is untouched; concurrent activations do not interfere (distinct tokens/aeis).

**FR-8 — Body lifecycle.** Body completion consumes the activation token and routes nothing; body
fault rides the ordinary composite-fault path (no self-catching).

**FR-9 — Determinism.** Identical runs produce identical tokens/ids/diagnostics order.

**FR-10 — Interchange.** Round-trips and degrade findings per D7; the importer never emits a graph
the validator rejects.

**FR-11 — Transaction parity.** The `StopOtherLiveWork` extraction leaves spec-125 behavior
byte-identical (its full suite passes unmodified).

## Invariants that MUST survive

- Schema stays v1: `TriggeredByEvent` + diagnostics are additive; **no new state records, no new
  token status**; `BpmnStateMutator` sole mutation home; ids from `Sequence`; never-prune rules
  unchanged.
- Behaviors stay decision-only (the raise/fault paths are engine-owned; no new behavior emits
  anything beyond existing commands).
- Zero runtime changes; seams B/C consumed as shipped.
- Continuation discipline byte-identical (activation defers; seam staging at clean exits only).
- Spec 119–127 suites pass unmodified.
- Deterministic ids; no new HTTP endpoints; domain-tree and VF-ACT gates hold.

## Success criteria

- All FR tests green, including the ladder pins, own-scope vs bubbling, error absorption vs
  composite-fault parity, and the spec-125 regression.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations from the ratified plan

- **Interrupting flag modeled on the body start event's `CancelActivity`.** Rather than a new event-definition
  property, the body start event's `isInterrupting` maps onto the existing `BpmnElement.CancelActivity` flag
  (default `true` = interrupting), mirroring how a boundary's `CancelActivity` is modeled and matching the BPMN
  default-true convention. `ValidateEventSubprocesses` and the catcher index read it directly.
- **Escalation/error body starts get two `StartEventBehavior` families.** The body's escalation/error start event
  was previously rejected by `ResolveStartEvent` (only none/timer/message/signal). Added
  `StartEventEscalation`/`StartEventError` families (routing outbound like a none start, registered behaviors) and
  excluded them from the publish-time start-trigger surface (`BpmnElementFamilies.IsExternalStartTrigger`); they
  seed only via the D2 scheduled-start hint. Zero effect on any external start.
- **Hint contamination guarded by scheduling cause, not by always-writing the key.** Child work items inherit the
  parent's command metadata, so a body's `bpmn.startElementId` would otherwise leak to a grandchild nested process.
  The third `StartAsync` seeding path reads the hint only when the work item's scheduling cause is the new
  `EventSubprocessBodySchedulingCause` (which is per-schedule and overwrites the inherited value), so an inherited
  hint is never honored by an ordinary nested process. No always-write of an empty key was needed; the no-hint
  paths stay byte-identical.
- **`ResolveTokenId` hardened for inline nested-body completions.** A nested `BpmnProcess` body that completes
  **inline** surfaces its OWN last internal `bpmn.tokenId` on the completion work item (runtime metadata
  inheritance), not the parent's activation token id. `ResolveTokenId` now trusts the metadata token only when the
  completing node has no active children (the terminate/cancel late-completion case) or the token names one of the
  node's active children; otherwise it falls to the by-node resolution. This is additive (all pre-existing cases
  match) and was required for own-scope/notification event-subprocess body completions to resolve correctly.
- **`StopOtherLiveWork` returns a stopped-count.** The extraction of spec-125's stop-others loop returns
  `(State, StoppedCount)` so `CancelTransaction` can keep its exact "stopped {N} other live token(s)" diagnostic —
  the transaction suite is byte-identical.
- **Error trigger is a validated STATED CUT this slice (control-room gate ruling).** The error-trigger engine
  wiring (seam-B absorption + interrupting activation), model, and exporter emission are all in place but are made
  **author-unreachable**, following the spec-121 stated-cut pattern, because the runtime cannot yet complete it (see
  tripwire #3). `ValidateEventSubprocesses` rejects an error-triggered event subprocess deterministically, naming
  the element: *"BPMN error event subprocess '<id>' is not executable in this slice; an error-triggered event
  subprocess needs a runtime deferred fault-absorption capability that is not yet available (a follow-up unit
  removes this restriction)."* The importer **degrades** an error-triggered `<subProcess triggeredByEvent="true">`
  (Dropped + finding *"error-triggered event subprocesses are not executable in this slice (a follow-up unit adds
  them)"*) so it never emits a validator-rejected graph. `BpmnGraph.ErrorEventSubprocess()` therefore always returns
  `null` and the error engine paths (`AbsorbChildFaultThroughErrorEventSubprocess`, the `OnChildFaultedAsync` error
  branch) are inert and unreachable. **The follow-up unit that lands the runtime deferred-seam-B fix removes exactly
  the one validation rule and the one importer drop** — the wiring is already there. The escalation trigger
  (own-scope + notification, interrupting + non-interrupting) is unaffected and fully works end-to-end.

## Tripwire outcomes

1. **Command-metadata hint to `StartAsync` — CLEAR.** The child-schedule `request.Metadata` is merged into the
   child work item's `commandMetadata` (`WorkflowInvokeActivitySchedulerWorkHandler.NewChildActivityScheduleWorkItems`),
   so `BpmnProcess.StartAsync` reads `bpmn.startElementId` from `context.SchedulerWorkItem.CommandMetadata`. Verified
   end-to-end (bad hint faults `bpmn.start.unresolved-hint`; body seeds at its event-start).
2. **Own-scope activation within one evaluation — CLEAR.** `RaiseEscalation` returns the matched catcher; the
   activation runs AFTER the throw's full decision (its companion `EmitTokens`/`ConsumeToken` runs first), so an
   interrupting activation's `StopOtherLiveWork` also stops the throw's just-emitted successor. It rides the
   `Propagate` loop, so the stop is logical-only (`NoLiveChildren`, the `CancelLiveWork`-logical-only invariant);
   no continuation conflict.
3. **TRIPPED — seam-B absorption from the error path defers, which the runtime does not support.** The boundary
   error path routes a token (often completing); the error event subprocess must schedule its body (defers). A
   deferred seam-B fault absorption resolves the named incident but the runtime then redelivers/misattributes the
   original fault and faults the composite. Reproduced with the **shipped spec-120 error boundary routing to a
   task** (which likewise defers) — it is a runtime seam-B gap, not a module one. Seam-A + a deferred child
   schedule works (the escalation interrupting notification path completes cleanly). Reported; the runtime was not
   patched (invariant: seams B/C consumed as shipped). The error trigger's model/validation/interchange/engine
   wiring are delivered and tested; its end-to-end runtime completion awaits a runtime fix.
4. **Body-structure validation at validation time — CLEAR.** `ValidateEventSubprocesses` reads each body child's
   `ExecutableNode.Structure` (deserialized to `BpmnStructure`) the way MI validation reads `BpmnStructure.Variables`
   — authoring-time knowledge, no runtime cross-scope reach.
5. **Activation token `ParentTokenId` null — CLEAR.** The scope-level activation token (ParentTokenId null) broke
   no existing lookup: teardown, `ResolveTokenId`, and join accounting all key on token id / node id, and the body
   completion is intercepted before behavior dispatch.
6. **Spec-vs-code contradiction — none** beyond the deviations recorded above.
