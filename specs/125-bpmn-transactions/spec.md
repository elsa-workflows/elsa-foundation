# 125 — BPMN transactions + cancel events (transaction subprocess, cancel end event, cancel boundary event — riding the spec-124 compensation log) (BPMN Phase 3)

## Goal

Let a published BPMN process model a **transaction subprocess**: a subprocess variant whose nested
process can be **cancelled from within** by a **cancel end event** — all other live work in the
transaction stops, every completed-and-compensable piece of work inside it is compensated in reverse
completion order (the spec-124 log and replay engine, reused), and the transaction then completes
with a distinguishable **`Cancelled` outcome** instead of `Done`. In the parent scope, a **cancel
boundary event** attached to the transaction element fires on that outcome and routes the
cancellation path; a transaction completing normally routes its ordinary outbound flows, exactly
like any subprocess.

The design threads the one isolation-preserving channel between the two engines: the child's
**completion outcome name**. The nested process never leaks token state to the parent, and the
parent never reaches into the nested process's private state — the nested engine compensates its own
scope (its own log), completes `Cancelled`, and the parent maps that outcome to the boundary. The
runtime's `Cancel` continuation kind is deliberately NOT used: it is a cancellation transition that
never reaches the parent's child-completion handler — a cancelled transaction is, to the runtime, a
**successfully completed child with a different outcome**.

## Context (what exists today, origin/main = 261b4e3f6)

- **Spec-124 replay core is throw-agnostic and reusable.** `BpmnCompensable` (`comp:N`,
  `Registered|Claimed|Compensated`, never pruned) + `BpmnCompensationRun` (`comprun:N`, descending
  `PendingCompensableIds`, coordinator token `AwaitingChild`); claim → `ScheduleNextCompensationHandler`
  → interception in `OnChildCompletedAsync` → `HandleCompensationHandlerCompletion`; the
  run-coordinator cascade in `CancelTokenAndChild` releases unrun `Claimed`→`Registered`. Only two
  pieces are throw-COUPLED: the entry (`TriggerCompensation` needs a coordinator element + token —
  a cancel END event provides exactly that) and the completion tail (`CompleteThrow` knows
  end-consume vs throw-route; a third tail — "complete the process with the `Cancelled` outcome" —
  is new).
- **The outcome channel.** A structural activity completes via
  `RuntimeStructuralContinuation.Complete(outcomeName)`; the outcome flows to the parent as
  `completionContext.OutcomeNames` and is validated against the authored `ActivityContract.Outcomes`
  (VF-ACT-006 — an undeclared outcome is rejected). `BpmnProcess` declares only
  `[ActivityOutcome(Done)]` today, and `FinishEvaluation` hardcodes `Complete()`. The parent's
  `BpmnFlowSelector.SelectTaskFlows` already routes by outcome names (`conditionOutcome`), and
  `SubProcessBehavior.OnChildCompleted` uses it.
- **The `Cancel` continuation kind is a false friend**: it maps to a cancellation transition, not a
  completion, and never reaches `OnChildCompletedAsync` — a cancel boundary driven by it would never
  fire. Cancelled transactions must complete-with-outcome.
- **Terminate is the stop-everything template but only half of it.** `TerminateProcess` →
  `CancelLiveWork` (flips every live token `Canceled`, drops active-child records, **logical-only**
  — in-flight children keep running and their late completions/faults are absorbed by the existing
  guards) → `Terminated` flag → `FinishEvaluation` completes `Done`. A cancel end needs: stop live
  work, THEN replay compensation, THEN complete `Cancelled`.
- **Boundary kinds.** Catch = armed listener; error = dormant, fault-driven
  (`AbsorbChildFaultThroughErrorBoundary` mints an `Active` token at the boundary element and
  propagates); compensation = dormant, completion-driven registration, zero outbound flows. A
  **cancel boundary** is a hybrid: dormant attachment (no listener, no arming — the compensation
  shape) with error-style firing (mint token at boundary, route outbound), driven by an **outcome
  test** in the parent's child-completion evaluation. The interception order in
  `OnChildCompletedAsync` is: canceled/terminated guard → MI-instance → compensation-handler →
  event-race → `ApplyBoundaryCompletionSemantics` (Case B: compensable registration + listener
  teardown) → behavior dispatch.
- **Nothing transaction-shaped exists.** No `transaction` element type (the importer's default case
  drops `<transaction>`); no `Cancel` constant in `BpmnEventDefinitionTypes`; no
  `cancelEventDefinition` handling anywhere. The spec-124 interchange (associations, compensation
  boundaries/throws/ends, `isForCompensation`) round-trips — extend, don't rework.
- **Structure marking.** The transaction flag must exist on BOTH sides independently (isolation):
  the parent's element (for boundary validation + outcome mapping) and the nested authored structure
  (for cancel-end validation + contract outcome declaration). `BpmnStructureHandler` projects the
  authored structure into the published contract — the FlowSwitch/VF-ACT-006 pattern for
  structure-dependent outcome declarations.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- **Transaction marking**: `BpmnElement` gains additive `IsTransaction` (`bool`, default `false`) —
  valid only on a `subProcess`-family element that binds a child (the transaction is a subprocess
  variant, not a new family; `SubProcessBehavior` and every host rule apply unchanged).
  `BpmnStructure`/`BpmnAuthoredStructure` gain additive `IsTransaction` (`bool`) marking the nested
  process itself. The two flags are set together by the importer (from `<transaction>`) and by
  authoring; they are independent by isolation and validated independently.
- **Cancel end event**: `BpmnEventDefinitionTypes.Cancel` (new constant); `ResolveEndEvent` gains
  the cancel definition → new `EndEventCancel` family + behavior. Valid **only** when the containing
  structure has `IsTransaction = true` (a cancel end outside a transaction is rejected).
- **Cancel boundary event**: `ResolveBoundaryEvent` + `SupportedBoundaryDefinitionTypes` gain the
  cancel definition. Shape rules: dormant (no bound child — exempt from the listener-child rule,
  like error/compensation); **has** outbound flows (≥1, inherited rule — it routes the cancellation
  path); no inbound, no default (inherited); must be attached to an `IsTransaction` host; at most
  **one** cancel boundary per host; `CancelActivity` is ignored (documented — the host is already
  finished when it fires).
- **Outcome contract**: the published `BpmnProcess` contract declares the additional outcome
  **`Cancelled`** iff the authored structure is a transaction (`IsTransaction = true`) — the
  structure-dependent outcome pattern (VF-ACT-006), via `BpmnStructureHandler`'s contract
  projection. Non-transaction processes keep exactly `Done`. **Tripwire**: if the structure-handler
  surface cannot declare structure-dependent outcomes, stop and report (do not blanket-declare
  `Cancelled` on every process without reporting first).
- **Validation** (`ValidateTransaction`, plus the boundary-rule amendments), each rule deterministic:
  1. `IsTransaction` only on a subProcess-family element with a bound child.
  2. Cancel end event only inside a transaction structure; at most one cancel **boundary** per
     transaction host; a cancel boundary only on an `IsTransaction` host.
  3. A cancel end event follows end-event rules (no outbound, no bound child); the existing
     compensation rules apply inside transactions unchanged (a transaction's inner compensation
     boundaries/handlers are ordinary spec-124 constructs in the nested scope).
  4. An `IsTransaction` element may not carry loop characteristics (MI transactions are a stated
     cut, consistent with spec 124's MI-host cut).

### D2 — Runtime semantics (nested side: cancel end; parent side: cancel boundary)

**D2a — Cancel end event (nested engine).** The behavior emits a new command
`CancelTransaction` (behaviors stay decision-only). The engine, on that command:

1. Consumes the cancel-end's arriving token, then **stops all other live work logically**:
   `CancelLiveWork`-style flip of every other live token (the MI/race/run coordinator cascades in
   `CancelTokenAndChild` apply so loops/races/replays tear down consistently), except the cancel-end
   token itself, which stays live as the coming replay's coordinator. In-flight children keep
   running and are absorbed on late completion (the terminate precedent, unchanged discipline).
   No further compensables can register after this point (their hosts' tokens are `Canceled`, and
   Case B is unreachable for them).
2. **Claims every `Registered` compensable** (no `activityRef` — a transaction cancel compensates
   the whole scope) in reverse registration order and opens a `BpmnCompensationRun` with the
   cancel-end token as coordinator — the spec-124 claim/replay engine reused verbatim, including
   sequential handler scheduling and the completion interception. A new additive state flag
   `Cancelling` (`bool`, parallel to `Terminated`) records the verdict.
3. **Empty claim** → skip the run; the process completes `Cancelled` immediately (step 4).
4. **Completion tail**: when the run's last handler completes (or the claim was empty), the engine
   drops the run and the process completes with the **`Cancelled` outcome** — `FinishEvaluation`
   grows the outcome-aware exit: when `Cancelling` is set and nothing is live, stage
   `Complete(CancelledOutcomeName)` instead of `Complete()`. (`CancelledOutcomeName = "Cancelled"`,
   a module constant.) A handler child fault during the replay wins as usual (`bpmn.child.faulted`;
   the process faults, no `Cancelled` completion).
5. A cancel end in a transaction that is the **root** process (no parent) is legal: the workflow's
   `BpmnProcess` completes with the `Cancelled` outcome; there is simply no boundary to fire.

**D2b — Cancel boundary event (parent engine).** In the parent's `OnChildCompletedAsync`, when the
completing token's element is an `IsTransaction` host and `completionContext.OutcomeNames` contains
`Cancelled`:

- Intercept **before** normal behavior dispatch and before Case B compensable registration: a
  cancelled transaction is NOT successfully-completed work — it registers **no** compensable (its
  own scope already compensated itself), and it does NOT route its normal outbound flows.
- Consume the host token; tear down the host's still-armed catch listeners (the existing Case B
  teardown, reused); mint an `Active` token at the attached cancel boundary (the error-boundary
  minting pattern, inheriting the host token's iteration key) and propagate — the boundary's
  behavior routes its outbound flows unconditionally.
- **No attached cancel boundary** → deterministic fault
  `bpmn.transaction.cancelled-unhandled` (message naming the transaction element). Cross-scope
  validation cannot see the nested cancel end (isolation), so this is a runtime rule, documented.
- A transaction completing with `Done` behaves byte-identically to a plain subprocess: normal
  routing, Case B compensable registration if it carries a compensation boundary (a completed
  transaction is compensable as a unit — composes with spec 124), catch-listener teardown.

**D2c — Interplay (all existing discipline unchanged).** Catch/error boundaries on a transaction
host work unchanged (an interrupting catch firing mid-transaction cancels the host token — the
nested process is torn down by the seam-A cascade like any subprocess; no compensation runs, a
stated BPMN deviation documented with the auto-compensation-on-interrupt cut). Terminate inside a
transaction stays terminate (completes `Done`; no compensation — cancel is the only compensating
exit). The deadlock detector, join accounting, cycles, MI, and races are untouched. Continuation
discipline unchanged: the cancel-end evaluation schedules handler children so it defers; seam
staging stays at clean `Complete`/`Defer` exits.

### D3 — Stated cuts

Escalation (next unit candidates); event subprocesses; **MI transactions** (D1 rule 4);
**auto-compensation when a transaction is interrupted from outside** (an interrupting catch/error
boundary tears the nested process down WITHOUT compensating it — BPMN would compensate; needs
cross-scope teardown-with-replay, deferred with the `CancelLiveWork`-through-seam-A follow-up);
transaction hazards (`bpmn.transaction.hazard` / error-in-transaction escalation semantics);
compensate throw events targeting a cancelled transaction from the parent (the parent registered no
compensable for it — documented consequence); nested-transaction cross-scope cascades (each scope
cancels/compensates itself only); `waitForCompletion=false` semantics.

### D4 — Interchange

- **Import**: `<transaction>` imports exactly like `<subProcess>` (nested process node synthesis,
  loop-characteristics/isForCompensation attributes rejected-or-degraded per existing rules) plus
  `IsTransaction = true` on the element AND on the nested authored structure.
  `cancelEventDefinition` on an `endEvent` inside a `<transaction>` → cancel end event; on an
  `endEvent` outside a transaction → **Degraded** to a none end event + finding (validator would
  reject it). `cancelEventDefinition` on a `boundaryEvent` attached to a transaction element →
  cancel boundary; attached to a non-transaction host → **Dropped** + finding (flow cascade as
  usual). A second cancel boundary on the same host → Dropped + finding.
- **Export**: an `IsTransaction` element emits `<transaction>` (everything else identical to
  subprocess export); cancel end → `<endEvent><cancelEventDefinition/></endEvent>`; cancel boundary
  → `<boundaryEvent attachedToRef="…"><cancelEventDefinition/></boundaryEvent>`. Standard 36×36
  event DI bounds; no association involved.
- **Round-trip** must hold for: transaction + cancel end + cancel boundary (together), and a
  transaction with inner spec-124 compensation constructs.

## In scope (this slice)

- **Model/validation (D1)**: `BpmnElement.IsTransaction`, `BpmnStructure`/`BpmnAuthoredStructure.
  IsTransaction`, `BpmnEventDefinitionTypes.Cancel`, `EndEventCancel` family/behavior, cancel
  boundary resolution, `ValidateTransaction`, structure-dependent `Cancelled` outcome declaration.
- **State (D2a)**: additive `Cancelling` flag; no new record types (the spec-124 run is reused).
- **Engine (D2)**: `CancelTransaction` command + cancel-end behavior; stop-then-claim-then-replay
  sequencing; the `Cancelled` completion tail in `FinishEvaluation`; the parent-side cancelled-
  outcome interception (consume host, no registration, no normal routing, mint boundary token,
  `bpmn.transaction.cancelled-unhandled` fault when unattached).
- **Interchange (D4)**: `<transaction>`/`cancelEventDefinition` import + export + round-trip +
  degrade findings.
- **Tests + module docs**: validation (every D1 rule); nested cancel end — live work stopped,
  compensables replayed reverse-order, process completes `Cancelled` (state assertions via the
  trailing-catch recipe); empty-log cancel → immediate `Cancelled`; parent boundary fires and routes
  (and normal `Done` completion routes normally, byte-identical); no-boundary → deterministic
  fault; root-process cancel → workflow completes with `Cancelled` outcome; handler fault mid-cancel
  → composite fault; transaction-with-compensation-boundary completing normally still registers its
  compensable (spec-124 composition); interrupting catch boundary on a transaction host mid-run
  (existing teardown, no compensation — pinned as the documented cut); determinism; interchange
  round-trips + degrades. BPMN README + EXTENSION_POINTS; Interchange README. Runtime
  EXTENSION_POINTS only if the outcome-declaration surface documentation needs the one-line contract
  note (no runtime code change expected — see invariants).

## Out of scope

Everything in D3; Studio authoring UX (separate repo); changes to spec 112–124 semantics.

## Functional requirements

**FR-1 — Validation.** Every D1 rule rejects deterministically; non-transaction graphs validate
byte-identically to today.

**FR-2 — Contract outcome.** A published transaction structure declares outcomes `Done` +
`Cancelled`; a non-transaction structure declares exactly `Done` (byte-identical contract).

**FR-3 — Cancel end sequencing.** A cancel end firing stops all other live work in the nested scope
logically (loops/races/replays cascade consistently), claims all `Registered` compensables, replays
them sequentially in reverse registration order via the spec-124 machinery, and then completes the
process with the `Cancelled` outcome. An empty log skips straight to `Cancelled`. No compensable
registers after the cancel begins.

**FR-4 — Parent mapping.** A transaction child completing with `Cancelled` fires the attached cancel
boundary: host token consumed, no compensable registered, no normal outbound routed, catch listeners
torn down, boundary token minted and its outbound routed. Without a cancel boundary the parent
faults `bpmn.transaction.cancelled-unhandled`. A `Done` transaction completion is byte-identical to
a plain subprocess completion.

**FR-5 — Root transaction.** A cancel end in a root transaction process completes the workflow's
`BpmnProcess` with the `Cancelled` outcome (no fault, no boundary).

**FR-6 — Fault discipline.** A handler child fault during the cancel replay faults the composite
(`bpmn.child.faulted`); the process does not complete `Cancelled`. Terminate inside a transaction
stays terminate (`Done`, no compensation).

**FR-7 — Composition.** Inner spec-124 constructs (compensation boundaries/handlers, compensate
throws) work unchanged inside a transaction; a transaction element with its own compensation
boundary registers a compensable on `Done` completion and none on `Cancelled`.

**FR-8 — Determinism.** Identical runs produce identical ids and identical stop/claim/replay order.

**FR-9 — Interchange.** Transaction + cancel end + cancel boundary round-trip; out-of-place cancel
definitions degrade/drop with specific findings; the importer never emits a graph the validator
rejects.

**FR-10 — Continuation discipline.** Unchanged: terminal-vs-scheduled exclusion, seam staging only
at clean exits, `CancelLiveWork` logical-only.

## Invariants that MUST survive

- Schema stays version 1 (`IsTransaction` flags, `Cancelling`, the `Cancel` definition type are
  additive); `BpmnStateMutator` sole mutation home; ids from `Sequence`; `Canceled` tokens and
  compensable records never pruned; **no new token status; no new state record types** (the
  spec-124 run is reused).
- Behaviors stay decision-only (`CancelTransaction` is the cancel end's sole command; the boundary
  behavior only routes).
- **Runtime changes: none expected.** The outcome channel (`Complete(outcomeName)`,
  contract-declared outcomes, parent `OutcomeNames`) already exists end-to-end. The one permitted
  touch is IF the structure-handler contract projection needs a hook for structure-dependent
  outcomes — that is the D1 tripwire: stop and report before editing anything outside the BPMN
  module.
- Spec 119–124 suites pass unmodified. Deterministic ids only. No new HTTP endpoints; domain
  project-tree naming guard and VF-ACT gates hold.

## Success criteria

- Validation tests for every D1 rule (transaction placement, cancel end/boundary placement,
  one-cancel-boundary, MI-transaction rejection).
- Execution: cancel end mid-transaction with two completed compensable hosts → both handlers replay
  newest-first, live branch stopped, process completes `Cancelled`, parent boundary routes; empty
  log variant; no-boundary fault variant; root-process variant; `Done` path byte-identical to
  subprocess; handler-fault variant; spec-124 composition variant (inner compensation + transaction-
  level compensable on `Done`); interrupting-catch-on-transaction variant (documented no-compensation
  cut); determinism.
- Interchange: transaction round-trip (with cancel end + boundary + inner compensation); degrade
  findings (cancel end outside transaction, cancel boundary on non-transaction host, duplicate
  cancel boundary).
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations from the ratified plan

- **Tripwire 1 outcome — the structure-dependent `Cancelled` outcome is declared in
  `ExecutableNodeCompiler.ResolveOutcomes`, not in `BpmnStructureHandler`.** The spec's mental model was that
  `BpmnStructureHandler` "projects the authored structure into the published contract" and declares the
  outcome there. In the actual code there is **no** outcome-projection surface on `IActivityStructureHandler`
  (it projects children, child-contract-member usage, and scoped variables only). Structure-dependent outcomes
  are resolved by **`ExecutableNodeCompiler.ResolveOutcomes`** (`Elsa.Workflows.Publishing.Api`), which reads
  the compiled executable structure — the exact mechanism the Switch activity uses, hardcoded there as a
  special case for `elsa.switch.structure`. The implementation therefore: (1) carries the new
  `BpmnStructure.IsTransaction` flag onto the compiled executable structure via
  `BpmnStructureHandler.CompileExecutableStructure`, and (2) adds **one additive branch** in
  `ResolveOutcomes` — when the structure kind is `elsa.bpmn.structure` and `isTransaction: true`, add
  `"Cancelled"` — byte-for-byte parallel to the existing `elsa.switch.structure` branch. This is the single
  touch outside the BPMN module. A non-transaction `BpmnProcess` is unaffected (its contract keeps exactly
  `Done`, FR-2). VF-ACT-006 is enforced end-to-end (the completion projector rejects an undeclared outcome),
  so this declaration is load-bearing, not cosmetic: the test harness's parallel outcome resolver
  (`WorkflowExecutionHarness.ResolveOutcomes`, which already mirrored the Switch special case for hand-built
  graphs) needed the same one-branch mirror or every `Complete("Cancelled")` faults with VF-ACT-006. No
  runtime-model type, seam, or token status changed; the outcome channel itself was untouched.
- **Tripwire 2/3 — clear.** `Complete("Cancelled")` is accepted end-to-end once the outcome is declared (the
  completion payload carries `structuralContinuation.OutcomeName` → the parent's `OutcomeNames`), and the
  parent-side interception distinguishes the transaction host's completion by testing
  `completionContext.OutcomeNames.Contains("Cancelled")` — the payload does carry the outcome name.
- **Tripwire 4 — clear.** Keeping the cancel-end token live as the compensation-run coordinator (with an active
  handler child) never trips the deadlock detector or the liveness accounting: while replaying there is always
  an active child, and on the empty-log / last-handler path the cancel-end token is consumed so the clean
  liveTokens==0 branch fires (now outcome-aware).
- **Interrupting-catch-on-transaction cut — covered by the unchanged spec-120 semantics, not a bespoke test.**
  A transaction host is byte-identically a `subProcess` host for catch/error boundaries (only cancel boundaries
  are transaction-specific in validation); an interrupting catch/error boundary firing mid-transaction tears
  the nested process down through the existing seam-A cascade with no compensation — this is spec-120 code
  exercised unchanged. Rather than construct a suspending-nested-transaction fixture that mostly re-tests spec
  120, the no-compensation-on-interrupt cut is documented (D2c / README) and the interplay is left to the
  existing boundary suite. This is a coverage decision, not a behavior deviation.
