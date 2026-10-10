# 133 — BPMN call activity (invoke a published workflow via DispatchWorkflow; engine-owned fault-outcome translation) (BPMN Phase 3)

**Status**: Implemented
**Merged**: PR #999

## Goal

Let a published BPMN process contain a **call activity**: a `callActivity` element that invokes a
**separately published Elsa workflow** by definition id — the bound child is a
`Elsa.DispatchWorkflow` activity node — waits for it (default; fire-and-forget is an authored
opt-out), and, when the called workflow **faults or is cancelled**, routes that failure through
BPMN error handling: the host's **error boundary** or the scope's **error event subprocess**
(specs 120/128), else a deterministic composite fault. Multi-instance and boundary composition ride
the existing machinery.

The wait/resume/identity plumbing is entirely shipped (DispatchWorkflow + #982's node-scoped
registration keys make multiple call activities disjoint by construction; #973 composed the feature
into Elsa.Server). The one genuinely new engine piece is **fault-outcome translation**: a faulted
called workflow COMPLETES the DispatchWorkflow activity with outcome `Faulted` — it never enters
`OnChildFaultedAsync`, produces no incident, and therefore can't ride seam B. The engine intercepts
these outcome completions on callActivity-bound children and routes the error-catcher ladder
directly (no incident absorption is needed — the child is already terminal and the host token is
consumed either way).

## Context (verified terrain, origin/main ≥ aca13a3a4; line numbers drift — verify at implementation)

- **DispatchWorkflow** (`src/Elsa/Activities/DispatchWorkflow/`): `ActivityType "Elsa.DispatchWorkflow"`;
  inputs `WorkflowDefinitionId` (string, required — MUST be a **literal**: `DispatchPinSource`
  throws at parent-publish unless it resolves to exactly one live Published artifact, pinning
  ArtifactId+Hash into node metadata), `Inputs` (dict, literal or expression-bound; validated
  against the child's `InputContract`), `WaitForCompletion` (default false), 
  `CancelChildOnParentCancellation` (default true), `CorrelationId`. Waited form suspends on an
  aei-scoped bookmark + typed `ActivityTriggerRegistration`; all dispatch identities digest from
  `(parentWorkflowExecutionId, parentActivityExecutionId)` so N call activities (and N MI
  instances) are disjoint. Resume maps child terminal status → **outcomes**:
  `Completed`/`Faulted`/`Cancelled` (+ `Dispatched` for fire-and-forget, `DispatchFailed`). Child
  outputs return on `Result`. **A faulted child never faults the activity** — outcome only.
- **BPMN binding**: a task-family element binds one child node (`ChildNodeId`);
  `TaskBehavior.OnTokenArrived` → `ScheduleChild`; `OnChildCompleted` routes outbound by outcome
  names. The catch-event children prove the waited-leaf suspension path end-to-end. The importer's
  synthesized-child pattern (spec 118: `LiteralArgument`, placeholder version ids) is the template.
- **Error catchers**: error boundary (spec 120) and error event subprocess (spec 128 + 132) both
  fire from `OnChildFaultedAsync` via seam-B absorption of an incident. An outcome-completion has
  no incident — the translation must NOT try to ride seam B; it routes catchers directly from the
  completion evaluation.
- **Cancellation gap (verified)**: `WorkflowDispatchCancellationEnricher` propagates cancel to the
  dispatched child instance only when the WHOLE parent workflow execution reaches `Cancelled`.
  Seam-A subtree teardown (boundary interrupt, gateway loser, terminate, transaction cancel, MI
  teardown) reclaims the parent-side wait but does NOT cancel the child instance. Stated cut +
  filed issue (D6).
- **Interchange**: `callActivity` falls to the importer's default Dropped branch today;
  `calledElement` (a foreign BPMN process id) has no guaranteed mapping to an Elsa definition id;
  the unbound-task import precedent ("imported unbound; bind an Elsa activity", Info finding) is
  the degrade shape. Publish-time pinning means import NEVER needs to resolve the definition.
- **Output capture**: leaf-path output-capture wiring is pending in the ADR-0046/#990 plan —
  child-outputs→process-variables is a stated cut with a pointer.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- New element type `callActivity` (`BpmnElementTypes.CallActivity`), family resolved to the
  existing task family (`TaskBehavior` unchanged — behaviors stay semantics-unaware; the element
  type's only behavioral distinction is the engine-side outcome interception, D3, and interchange
  fidelity, D5). It is a boundary-host family member (error/catch boundaries attach; MI loop
  characteristics legal — host rules inherited unchanged).
- **Binding**: a `callActivity` element must bind a child (`ChildNodeId`, standard host rule). The
  bound child is a `Elsa.DispatchWorkflow` node by convention (Studio authors it; the importer
  synthesizes it when it can). Validation does NOT type-check the bound node's activity type
  (elements never type-check children today; a non-DispatchWorkflow child simply never produces
  the intercepted outcomes and behaves as a plain task — documented).
- **Validation** (`ValidateCallActivities`): `callActivity` follows every task rule; additionally
  a childless `callActivity` is legal at validation (the unbound import shape — like tasks) but
  its normal childless-element treatment applies (never reached ⇒ dead; Studio binds before
  publish in practice).

### D2 — Waited semantics (ride existing machinery byte-identically)

The bound DispatchWorkflow node is authored `WaitForCompletion=true` by convention (the importer
synthesizes it so; Studio defaults it for callActivity bindings). The BPMN engine needs NO wait
changes: `ScheduleChild → AwaitingChild → bookmark suspend → typed-registration resume →
OnChildCompleted` is the shipped path. Fire-and-forget (`WaitForCompletion=false`, an authored
Elsa extension — non-standard BPMN, documented): the `Dispatched` outcome routes normal outbound
immediately.

### D3 — Fault-outcome translation (the new engine piece; engine-owned, no seam B)

In `OnChildCompletedAsync`, BEFORE normal behavior dispatch (joining the existing interception
ladder, after the MI/compensation/event-subprocess interceptions): when the completing token's
element is a `callActivity` and the completion's outcome names contain **`Faulted`,
`DispatchFailed`, or `Cancelled`**:

1. Consume the host token (the child is terminal either way); tear down the host's still-armed
   catch listeners (existing Case-B teardown reuse).
2. Resolve the error-catcher ladder: host-attached **error boundary** → mint an `Active` token at
   the boundary (inherit iteration key) and propagate (the error-boundary firing pattern — NO
   seam-B absorption: there is no incident, nothing to absorb, no interruption cascade needed);
   else scope **error event subprocess** → activate it interrupting (spec-128 activation; stop
   other live work, schedule body); else **deterministic composite fault** — codes
   `bpmn.call-activity.faulted` / `bpmn.call-activity.dispatch-failed` /
   `bpmn.call-activity.cancelled`, message carrying the outcome and the element id.
3. Normal outbound flows do NOT route for these outcomes. `Completed`/`Dispatched` outcomes are
   untouched (normal task-flow selection — a `conditionOutcome` flow can still discriminate them).
- Diagnostics: `CallActivityFailureRouted` (+ the outcome name) / the composite-fault diagnostic.
- MI composition: an MI instance child completing with a failure outcome rides this interception
  per instance — the coordinator semantics are those of spec 121 (instance interception happens
  first; pin one test: MI callActivity instance `Faulted` with a host error boundary fires the
  boundary and interrupts the coordinator — the D3 ladder composes with the D2d cascade). If
  implementation finds the MI-vs-callActivity interception ordering ambiguous, STOP and report
  (tripwire).

### D4 — Inputs/outputs

- **Inputs**: whatever the bound DispatchWorkflow node authors (`Inputs` literal or
  expression-bound) — no BPMN-specific input surface this slice. The importer synthesizes an empty
  `Inputs`.
- **Outputs**: stated cut — child outputs already return on `Result`, but capturing them into
  process variables awaits the ADR-0046 leaf output-capture wiring (#990 plan); BPMN
  `ioSpecification`/data associations are a further cut. Documented with pointers.

### D5 — Interchange

- **Import**: `<callActivity id calledElement>`:
  - When the document carries the Elsa extension attribute `elsa:workflowDefinitionId` (our export
    convention): synthesize the bound DispatchWorkflow child
    (`LiteralArgument("WorkflowDefinitionId", <value>)`, `LiteralArgument("WaitForCompletion",
    true)`, placeholder version id const `Elsa.DispatchWorkflow`) and emit the bound element.
  - Otherwise (plain `calledElement`, unresolvable to an Elsa definition): import the element
    **unbound** + Info finding ("imported unbound; bind a DispatchWorkflow activity to execute
    it") — the serviceTask precedent; `calledElement` recorded as
    `Properties["bpmn.calledElement"]` (the `bpmn.errorRef` passthrough precedent) for authoring
    reference and round-trip.
  - Never emit a graph the validator rejects.
- **Export**: `<callActivity calledElement="{bpmn.calledElement ?? definitionId}"
  elsa:workflowDefinitionId="{bound child's authored WorkflowDefinitionId, when bound}">`; a
  fire-and-forget binding also emits `elsa:waitForCompletion="false"` (import honors it). Standard
  task DI bounds.
- **Round-trip**: bound (with `elsa:workflowDefinitionId`) and unbound (calledElement passthrough)
  both round-trip.

### D6 — Stated cuts (+ one filed issue)

**Seam-A cancellation reach**: mid-process teardown of a waited call activity does not cancel the
dispatched child workflow instance (whole-parent-cancel does; the child otherwise runs to
completion and its late resume delivery is absorbed by the terminal-parent guards). FILE a GitHub
issue during implementation (control room will do it at the gate if the worker doesn't) — the fix
belongs with the carried `CancelLiveWork`-through-seam-A follow-up, runtime-side. Also cut:
child-outputs→process-variables capture (pending #990 leaf wiring); BPMN
`ioSpecification`/data-associations; MI output aggregation (existing cut); `calledElement`
version-selection/cross-tenant semantics; callActivity-specific Studio UX.

## In scope

- Model/validation (D1); the D3 outcome-interception ladder + diagnostics + fault codes;
  interchange (D5); tests + module docs (BPMN README/EXTENSION_POINTS, Interchange README).
- Tests: validation; waited call activity end-to-end (in-process harness with a real dispatched
  child — check `tests/Elsa/Activities/DispatchWorkflow/` for the dispatch test fixture and reuse
  its in-memory actor/delivery setup inside the BPMN fixture; if wiring the dispatch machinery
  into `BpmnRuntimeFixture` proves disproportionate, STOP and report options rather than shimming
  — tripwire); `Completed` routes normal flows; `Faulted` → error boundary fires; `Faulted` →
  error event subprocess activates (interrupting); `Faulted` with no catcher → composite fault
  with the pinned code; `Cancelled`/`DispatchFailed` codes; fire-and-forget `Dispatched` routes
  immediately; MI callActivity instance failure composes (one test); interchange round-trips
  (bound + unbound) + degrade findings; determinism.

## Out of scope

Everything in D6; runtime changes (DispatchWorkflow and the dispatch pipeline are consumed as
shipped — any gap is stop-and-report).

## Functional requirements

**FR-1 — Validation/authoring.** `callActivity` validates as a task-family boundary-host element;
unbound import shape legal; all existing rules byte-identical elsewhere.

**FR-2 — Waited execution.** A bound, waited call activity suspends and resumes through the
shipped dispatch machinery; `Completed` routes normal outbound; two call activities in one process
(and N MI instances) are disjoint.

**FR-3 — Failure translation.** `Faulted`/`DispatchFailed`/`Cancelled` outcomes never route normal
outbound: host error boundary fires (token minted + routed, host consumed, listeners torn), else
scope error event subprocess activates interrupting, else the composite faults with the pinned
per-outcome code. No seam-B request is staged anywhere on this path.

**FR-4 — Fire-and-forget.** `Dispatched` routes normal outbound immediately (documented
non-standard extension).

**FR-5 — MI composition.** Loop characteristics on a callActivity ride spec-121 unchanged; a
failing instance rides FR-3 composed with the coordinator cascade.

**FR-6 — Interchange.** D5 round-trips and degrades; `bpmn.calledElement` passthrough preserved;
importer never emits a validator-rejected graph.

**FR-7 — Determinism + discipline.** Deterministic ids/diagnostics; schema v1 additive only
(element type + diagnostics; no new state records/token statuses); behaviors decision-only (the
interception is engine-owned); continuation/seam-staging rules byte-identical; specs 119–132
suites pass unmodified.

## Success criteria

- All FR tests green, including the three-way catcher ladder and the no-catcher fault codes.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture (+ the DispatchWorkflow tests project if the fixture reuse touches
  it). Full solution build clean.
- The D6 cancellation-reach issue exists on GitHub with the verified analysis.

## Deviations (implementation)

- **Waited end-to-end test — dispatch-harness reuse TRIPWIRE taken (probe test double + no
  DispatchWorkflow-project end-to-end).** Wiring the real dispatch machinery into `BpmnRuntimeFixture`
  proved disproportionate: the two harnesses are structurally different. `BpmnRuntimeFixture` builds on
  `WorkflowExecutionHarness` (no runtime-API feature, no actor provider, no dispatch stager,
  resumption, or checkpoint-recording stores), whereas `DispatchWorkflowRuntimeTestFixture` stands up
  its own `ServiceCollection` with `WorkflowsRuntimeApiFeature`, a `RecordingWorkflowExecutionActorProvider`
  over `InProcessWorkflowExecutionActorProvider`, `DispatchWorkflowRuntimeFeature`, a resumption sweep, and
  a recording commit store — a different composition root and dispatch lifecycle. Per the spec's stated
  fallback, the D3 ladder is instead driven by a **probe leaf that emits the DispatchWorkflow outcome
  names directly** (`Faulted`/`DispatchFailed`/`Cancelled`/`Completed`/`Dispatched`). This is a **faithful**
  double at exactly the seam the new code reads: the engine's interception keys only on the completing
  element being a `callActivity` and the completion's outcome names — it never inspects the child's
  activity type. The DispatchWorkflow status→outcome mapping and the wait/resume path themselves are
  already covered by the DispatchWorkflow test project. No true end-to-end was added there (composing the
  BPMN feature + authoring a nested BPMN structure into that fixture is the same disproportionate
  cross-wiring in the other direction). `BpmnCallActivityTests` covers FR-2/FR-3 (three-way ladder + the
  three pinned fault codes)/FR-4/FR-5/FR-7; validation D1 is in `BpmnGraphValidationTests`; interchange D5
  in `BpmnCallActivityInterchangeTests`.
- **MI-vs-callActivity interception ordering TRIPWIRE — resolved per the spec's pin, not stopped.** The
  spec pins the resolution ("instance interception context first, D3 ladder composed with the spec-121
  coordinator cascade"), so this was not ambiguous enough to stop: the MI-instance interception is entered
  first, and when the completing instance is a call activity with a failure outcome it calls the shared
  `RouteCallActivityFailureOutcome`, whose interrupt target resolves to the loop coordinator
  (`ResolveMultiInstanceCoordinatorTokenId`) — so firing a catcher cascades every remaining instance.
  Pinned by `MultiInstanceCallActivity_InstanceFailure_FiresBoundary_InterruptsCoordinator`.
- **Outcome-name constants defined locally** (`BpmnExecutionEngine.CallActivity*OutcomeName`) rather than
  referencing `DispatchWorkflowOutcomes`, to keep the BPMN module's dependency envelope unchanged (no new
  runtime dependency). They mirror `DispatchWorkflowOutcomes` by convention, documented on each constant.
