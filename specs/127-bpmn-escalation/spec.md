# 127 — BPMN escalation (escalation throw/end events + escalation boundary events, riding runtime seam C) (BPMN Phase 3)

**Status**: Implemented
**Merged**: PR #975

## Goal

Let a published BPMN process **escalate**: an **escalation throw event** (intermediate throw or end
event, carrying an escalation code) inside a nested process signals **upward** — the **escalation
boundary event** attached to the hosting `subProcess` element in the parent scope catches it, by
code (specific match wins over a code-less catch-all), either **non-interrupting** (the nested
process keeps running past the throw; the boundary path routes alongside) or **interrupting** (the
parent tears the nested process down via seam A and routes the boundary path). An escalation
unmatched at one level **bubbles** to the next enclosing scope; an escalation that reaches the root
unmatched is a **no-op with a diagnostic** (escalation is a signal, not an error).

This is the first consumer of **runtime seam C** (spec 126): the throw stages
`RequestParentNotification` on the nested process's own commit; the parent's
`IRuntimeActivityChildNotificationHandler.OnChildNotifiedAsync` maps the notification to an attached
escalation boundary. The engine stays behavior-unaware (throw behaviors emit one new command;
matching, firing, teardown, and bubbling are engine-owned), **no new BPMN state records** are
introduced (escalation boundaries are dormant and graph-derived; notifications are processed
immediately), and the interchange follows the message/signal root-declaration precedent.

## Context (what exists today, origin/main ≥ fe145a5e8; verify line numbers at implementation)

- **Seam C (spec 126), just merged.** `RequestParentNotification(code ≤128, payload ≤8KiB JSON)` on
  `IRuntimeActivityExecutionContext`, staged during any of the child's own evaluations, committed
  atomically (post-commit outbox), delivered to a parent implementing
  `IRuntimeActivityChildNotificationHandler` as `OnChildNotifiedAsync(context,
  ActivityChildNotifiedContext)` — carrying `NotifyingChildActivityExecutionId`,
  `NotifyingChildExecutableNodeId`, `NotifyingChildIterationId`, `Code`, `Payload`. **Root staging
  faults the evaluation** (the thrower must check it has a parent first). Late notifications
  deliver (child may have completed/faulted since). The notification evaluation gets the spec-119
  live-children projection (marker-gated; `BpmnProcess` already implements
  `IRuntimeLiveChildActivityConsumer`) and may stage seam-A subtree cancellations. A `Defer`/
  `Complete`/`Fault` continuation is legal; staged notifications compose with `Defer`/`Complete`
  (so a parent that re-escalates defers with a staged notification of its own — bubbling).
- **Throw surface (spec 124/125).** `intermediateThrowEvent` element type exists, family
  `IntermediateThrowEventCompensation` — `ResolveIntermediateThrowEvent` rejects any non-compensate
  definition; the importer's `ResolveCompensateThrow` drops non-compensate throws with a finding.
  End events resolve none/terminate/compensate/cancel. `BpmnEventDefinitionTypes.Escalation`
  exists, fully unwired. Behaviors return command collections (`BpmnBehaviorDecision.Of(...)`) —
  a throw can emit `[RaiseX, EmitTokens]` and an end `[RaiseX, ConsumeToken]` in one decision.
- **Boundary kinds.** Catch = armed listener child; error = dormant, fault-driven; compensation =
  dormant, completion-registration, zero outbound; cancel = dormant, completion-outcome-driven.
  An **escalation boundary** is dormant + **notification-driven** and routes outbound; it honors
  `CancelActivity` (both interrupting and non-interrupting are meaningful — unlike error). The
  error-boundary firing pattern (mint an `Active` token at the boundary element, inherit the host
  token's iteration key, propagate) and the host-interruption cascade (`CancelTokenAndChild` +
  sibling listener teardown, seam-A keyed `(NodeId, IterationId)`) are the reusable pieces.
- **Who can throw.** Only BPMN elements throw escalations, and they live inside a nested
  `BpmnProcess` bound to a `subProcess`-family element. A task's bound child is a leaf and can
  never escalate — an escalation boundary on a task host is dead by construction.
- **Code matching is greenfield** (spec 120 cut `errorRef` matching and it was never built; the
  recording precedent is `Properties["bpmn.errorRef"]`; event-definition properties carry
  `name`/`interval`/`cron`/`activityRef` via `BpmnEventDefinitionProperties`).
- **Interchange.** Root `<message>`/`<signal>` declarations are indexed by
  `ReadMessageSignalDeclarations` (id→name); there is no `<escalation>` index. Exporters dedupe
  root declarations (`message-{name}` pattern). `escalationEventDefinition` appears nowhere.
- **Root check.** `BpmnProcess` can see whether it has a parent via its committed execution state
  (`ParentActivityExecutionId` null ⇒ root) — required to avoid seam C's root-staging fault.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- **Escalation definition properties** (`BpmnEventDefinitionProperties`): `code` (required,
  non-empty — the matching key) and optional `name` (display label). A code-less **boundary**
  definition is the catch-all; a code-less **throw** is rejected (a throw must say what it
  escalates).
- **Escalation throw events**: `ResolveIntermediateThrowEvent` gains the escalation definition →
  new family `IntermediateThrowEventEscalation` + behavior emitting
  `[RaiseEscalation(code), EmitTokens(task-flow selection)]` — the throw routes onward immediately
  (non-blocking; escalation is fire-and-continue). `ResolveEndEvent` gains escalation → family
  `EndEventEscalation` + behavior emitting `[RaiseEscalation(code), ConsumeToken]` (none-end
  semantics + the signal). Valid in any process structure (root or nested — the root case is a
  runtime no-op, D2a).
- **Escalation boundary events**: `ResolveBoundaryEvent` + `SupportedBoundaryDefinitionTypes` gain
  escalation. Shape rules: dormant (no bound child — exempt from the listener-child rule); ≥1
  outbound (inherited); no inbound/default (inherited); `CancelActivity` **honored** (both values
  legal); host must be **`subProcess`-family with a bound child** (a task host is dead by
  construction — rejected); per host: escalation boundary codes must be **distinct**, and at most
  **one** code-less catch-all.
- **Validation** (`ValidateEscalation`): the rules above, each a deterministic
  `BpmnExecutionException` naming the element. Existing boundary/throw rules byte-identical
  otherwise.

### D2 — Runtime semantics (engine-owned; no new BPMN state records)

**D2a — Raise (thrower side, nested engine).** On `RaiseEscalation(code)` the engine:

- If this process has a parent (`ParentActivityExecutionId` non-null): stage
  `RequestParentNotification` with code `bpmn.escalation` and payload
  `{ "code": <escalationCode>, "name": <name?> }` (one reserved seam-C code for the module; the
  escalation identity travels in the payload — keeps the seam-C code namespace clean and the
  payload self-describing). Add an `EscalationRaised` diagnostic.
- If root: **no-op + `EscalationUnhandled` diagnostic** (documented: an escalation nobody can catch
  is a signal into the void, not an error).
- The evaluation continues normally (the companion `EmitTokens`/`ConsumeToken` command routes/
  consumes as usual); staging composes with the `Defer`/`Complete` continuation per seam-C rules.

**D2b — Catch (parent side).** `BpmnProcess` implements `IRuntimeActivityChildNotificationHandler`.
`OnChildNotifiedAsync` with code `bpmn.escalation`:

- Resolve the notifying child to its host element: the notification's
  `NotifyingChildExecutableNodeId` (+ iteration id) → the `BpmnActiveChild` record → host element
  and host token. (A notifying child that is no longer an active child = the late case, below.)
- **Match** the payload's escalation code against the host element's attached escalation
  boundaries: exact code match wins; else the code-less catch-all; else **no match**.
- **Match, non-interrupting** (`CancelActivity == false`): mint an `Active` token at the boundary
  (inherit the host token's iteration key), propagate — boundary routes outbound. Host token and
  the nested child untouched; repeated escalations fire the boundary repeatedly (each notification
  = one fire; multi-fire is BPMN-conformant for non-interrupting). `EscalationCaught` diagnostic.
- **Match, interrupting** (`CancelActivity == true`): cancel the host token via the existing
  cascade (`CancelTokenAndChild` — stages the seam-A cancellation of the nested child's subtree
  keyed `(NodeId, IterationId)` from the live-children projection, cascades MI/race/run coordinator
  records, tears sibling listeners), then mint the boundary token and propagate. Reason const
  `bpmn.escalation.host-interrupted`. Continuation `Defer` (or `Complete` if nothing else is live).
- **No match**: **bubble** — if this process itself has a parent, re-stage the same notification
  (code + payload) from this evaluation (`Defer` continuation; seam-C staging rules apply); at the
  root, `EscalationUnhandled` diagnostic no-op. Bubbling is consumer-side recursion, one hop per
  level, exactly the seam-C design intent.
- **Late races** (deterministic, documented): notifying child no longer live / host token terminal
  → **non-interrupting** boundary still fires (the escalation happened while the host ran; the
  boundary path is additive, so a completed host's normal outbound plus a late boundary fire is
  legal non-interrupting semantics); **interrupting** boundary does NOT fire (the host's completion
  already routed its outbound — interrupting it retroactively would double-route; `EscalationLate`
  diagnostic no-op instead). Parent-not-Running and non-implementing cases are seam-C acks
  (unreachable for `BpmnProcess`, documented).
- Any other seam-C code arriving at a `BpmnProcess` (future consumers): ignore with a diagnostic
  (forward-compatible pass-through; never fault).

**D2c — Composition.** Escalation boundaries compose with everything shipped: on a multi-instance
`subProcess` host the notification resolves the throwing instance via `(NodeId, IterationId)` —
non-interrupting fires per escalating instance; interrupting cancels the **coordinator** (the
spec-121 cascade tears all instances). On a transaction host, escalation boundaries and the cancel
boundary coexist (distinct definitions). Inside a transaction, escalation throws work unchanged
(the transaction's cancel machinery is orthogonal). Continuation discipline, seam staging exits,
`CancelLiveWork` logical-only: all unchanged.

### D3 — Stated cuts

Escalation **event subprocesses** and escalation **start events** (the event-subprocess unit);
escalation **intermediate catch** events (not a BPMN construct — boundary/event-subprocess only);
error-code matching for error boundaries (still cut, unchanged — escalation's code matching is
escalation-only this slice); escalation payload/data beyond code+name; cross-instance escalation
(intra-workflow-instance only, by seam-C construction); throttling/dedup of repeated
non-interrupting fires.

### D4 — Interchange

- **Import**: root `<escalation id name escalationCode>` declarations indexed (the
  `ReadMessageSignalDeclarations` pattern, extended or mirrored); `escalationEventDefinition` with
  `escalationRef` resolves to `code` (= `escalationCode`, falling back to the declaration's `name`,
  else the ref id) + `name` properties. On `intermediateThrowEvent`/`endEvent` → escalation
  throw/end (a ref-less or code-less throw **degrades**: throw → Dropped + flow cascade, end →
  none end + Degraded finding). On `boundaryEvent` attached to a `subProcess`-family host →
  escalation boundary (ref-less = catch-all, imported as such); attached to a task-family host, or
  colliding codes / second catch-all on one host → **Dropped** + finding (validate-representable).
  `intermediateCatchEvent` with escalation stays Dropped (existing default) with the unsupported
  finding.
- **Export**: escalation throws/ends/boundaries emit `escalationEventDefinition` with
  `escalationRef`; root `<escalation>` declarations deduped by code (`escalation-{code}` id
  pattern, message/signal precedent; `escalationCode` attribute emitted; `name` when present).
  36×36 event DI bounds; no associations involved.
- **Round-trip** must hold for: nested throw (intermediate + end) + parent boundary (interrupting +
  non-interrupting + catch-all), with root declarations deduped.

## In scope (this slice)

- **Model/validation (D1)**: escalation definition properties; `IntermediateThrowEventEscalation` +
  `EndEventEscalation` families/behaviors; escalation boundary resolution + `ValidateEscalation`.
- **Engine (D2)**: `RaiseEscalation` command kind; raise-side staging (+ root no-op);
  `BpmnProcess : IRuntimeActivityChildNotificationHandler`; `OnChildNotifiedAsync` →
  `BpmnExecutionEngine` catch path (resolve, match, fire non-interrupting/interrupting, bubble,
  late races, unknown-code pass-through); diagnostics (`EscalationRaised`/`EscalationCaught`/
  `EscalationUnhandled`/`EscalationLate`); reason const `bpmn.escalation.host-interrupted`.
- **Interchange (D4)**: root escalation index, throw/end/boundary import + export + round-trip +
  degrade findings.
- **Tests + module docs**: validation (every D1 rule); nested throw → non-interrupting boundary
  fires while nested process continues to completion (both paths complete); interrupting boundary
  tears the nested process down (durable child state cancelled, bookmarks gone) and routes;
  code matching (specific beats catch-all; distinct codes route distinctly); two-level bubbling
  (L3 throw → L2 no match → L1 catches); root unmatched no-op; escalation end event variant;
  repeated non-interrupting fires; MI-host composition (one case: instance throw, interrupting
  cancels coordinator); late-race pins (non-interrupting fires after host completion;
  interrupting no-ops after host completion) where the harness permits; determinism; interchange
  round-trips + degrades. BPMN README + EXTENSION_POINTS; Interchange README. Runtime docs
  untouched (seam C already documented).

## Out of scope

Everything in D3; Studio authoring UX; changes to seams A/B/C or specs 112–126 semantics.

## Functional requirements

**FR-1 — Validation.** Every D1 rule rejects deterministically; non-escalation graphs validate
byte-identically.

**FR-2 — Raise.** An escalation throw stages exactly one seam-C notification (code
`bpmn.escalation`, payload code+name) on the nested process's own commit and continues routing
(intermediate) or consumes (end); a root-process throw is a diagnostic no-op, never a fault.

**FR-3 — Non-interrupting catch.** A matching non-interrupting boundary fires once per
notification: boundary token minted and routed while the host token and nested child continue
unaffected; both the boundary path and the nested process's own path run to completion.

**FR-4 — Interrupting catch.** A matching interrupting boundary cancels the host token (nested
subtree torn down via seam A, sibling listeners torn down, MI/race/run cascades honored) and routes
the boundary path.

**FR-5 — Code matching.** Exact code beats catch-all; distinct codes on one host route to their own
boundaries; an unmatched code bubbles.

**FR-6 — Bubbling.** An escalation unmatched at level N re-stages to level N-1 with identical
code/payload, recursively, until matched or the root; root-unmatched is a diagnostic no-op.

**FR-7 — Late races.** After the host completed: non-interrupting still fires; interrupting no-ops
with a diagnostic. Neither faults.

**FR-8 — Determinism.** Identical runs produce identical ids, fire order, and diagnostics order.

**FR-9 — Interchange.** Round-trips per D4; degrade/drop findings for code-less throws, task-host
boundaries, code collisions, duplicate catch-alls; the importer never emits a graph the validator
rejects.

**FR-10 — Discipline.** No new BPMN state records; no new token status; `BpmnStateMutator` sole
mutation home; continuation/seam-staging exclusion rules unchanged; behaviors decision-only
(`RaiseEscalation` + the existing routing command, nothing else); seams A/B/C surface untouched.

## Invariants that MUST survive

- Zero runtime-module changes (seam C is consumed as shipped; if a seam gap appears, stop and
  report — do not patch the runtime in this unit).
- Schema stays v1; additive diagnostics only; `Canceled` tokens/compensables never pruned.
- Spec 119–126 suites pass unmodified.
- Deterministic ids; no new HTTP endpoints; domain-tree and VF-ACT gates hold.

## Success criteria

- All FR tests green; the two-level bubbling and both boundary flavors demonstrated end-to-end
  with real nested `BpmnProcess` children.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations from the ratified plan

- **`RaiseEscalation` carries no code (read from the element).** The command is argument-less; the engine
  reads `code`/`name` from the throw/end element's escalation event definition in `ApplyDecision`'s
  `RaiseEscalation` case — mirroring how the spec-124 compensate throw reads its `activityRef` from the element.
  This keeps behaviors decision-only (FR-10) and the `BpmnBehaviorCommand` shape unchanged. Same effect the plan
  specifies ("behaviors emit `[RaiseEscalation, EmitTokens|ConsumeToken]`"), reached without widening the
  command payload.
- **Interrupting-teardown determinism in tests (D4 ordering, not a code change).** A nested process that raises
  an escalation **and** schedules a descendant in the **same** evaluation stages the seam-C notification and the
  descendant's child-schedule as sibling post-commit intents with the **same** derived sequence number, so their
  drain order is the documented spec-126 D4 non-determinism. For a *deterministic* interrupting-teardown pin
  (durable child state cancelled, bookmarks gone) the descendant must be a committed-live child on an **earlier**
  evaluation: the interrupting and MI runtime tests use a fork(keeper Event + trigger Event) nested process whose
  keeper suspends on the process's start evaluation, then resume the trigger to raise the escalation. The
  non-interrupting/late/bubbling tests assert only the deterministic *outcome* (the boundary fires and never
  faults), tolerating both orders. No engine change was needed — the seam-A cascade reclaims the whole nested
  subtree when the descendant is committed-live.
- **Task-host escalation boundary drops at the childless-host guard on import.** The importer's escalation
  boundary "host must be a subprocess" rule is only reachable for a *bound* task host, which the importer never
  produces (imported tasks are always childless), so a task-host escalation boundary drops at the existing
  childless-host guard with that finding. The subprocess-host rule is fully exercised at the **validation**
  level (`ValidateEscalation`, `EscalationBoundary_OnTaskHost_IsRejected`). Validate-representable either way.

## Tripwire outcomes (none tripped)

1. **Seam-C context sufficiency — clear.** The notification evaluation's context populated the spec-119
   live-children projection (BpmnProcess is `IRuntimeLiveChildActivityConsumer`), accepted seam-A subtree
   staging and a re-staged bubble notification, and the `{code, name?}` payload round-tripped — the
   interrupting/MI teardown tests confirm the notifying child's subtree is reclaimed via the live-children lookup.
2. **Bubbling re-stage — clear.** `RequestParentNotification` from within a notification evaluation (carried as
   `BpmnPendingParentNotification`, staged at the clean Defer exit) tripped no seam-C validation rule; the
   two-level L3→L2→L1 bubbling test passes end-to-end with real nested `BpmnProcess` children.
3. **Interrupting-cancel-then-mint on `FinishEvaluation` exits — clear.** The cancel cascade + boundary-token
   mint + Propagate rides the existing clean Defer/Complete exits; pending cancellations stage only there.
4. **3-level nested fixture — clear.** `BpmnRuntimeFixture.NestedProcessNode` builds arbitrarily nested
   `BpmnProcess` executable nodes (child slots carrying further nested nodes); no harness shim was needed.
5. **Spec-vs-code contradiction — none.**
