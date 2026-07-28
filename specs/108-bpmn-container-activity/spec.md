# 108 — BPMN Container Activity (Phase 1 slice)

## Goal

Introduce a third structural container next to `Flowchart` and `Sequence`: `BpmnProcess`, executing a
BPMN 2.0 subset with token semantics, delivered as a new activity module `Elsa.Activities.Bpmn`. The
architecture (structure kind, behavior contract, typed state envelope) is designed for the full BPMN 2.0
program (boundary events, event subprocesses, compensation, multi-instance) but this slice ships the
core subset only.

## In scope (this slice)

- `elsa.bpmn.structure` v1.0.0 authored/executable structure (`BpmnElement[]`, `BpmnSequenceFlow[]`,
  pools/lanes and an opaque `diagram` payload carried authored-side only, scoped variables per ADR 0027).
- `BpmnProcess` structural activity (`Mode = "bpmn"`, slot `Bpmn.Activities`) implementing the runtime
  structural protocol, delegating to `BpmnExecutionEngine`.
- Token-based engine with a typed versioned private-state envelope (`Elsa.Bpmn.ExecutionState`, v1),
  mirroring the Flowchart engine's collaborator decomposition, sequence-derived ids, and
  prune-on-persist behavior.
- Element behaviors (public `IBpmnElementBehavior` seam, analog of `IFlowchartPolicy`):
  none start/end events, terminate end event, task family (bound Elsa child activity; pass-through when
  unbound), embedded subprocess (bound child, e.g. a nested `BpmnProcess`), exclusive / parallel /
  inclusive gateways (split + join accounting).
- Sequence-flow selection: unconditional flows always taken; conditional flows match the completing
  child's outcome names (`conditionOutcome`); default-flow fallback; deterministic faults when no flow
  can be taken.
- Child fault → deterministic composite fault (`bpmn.child.faulted`), Flowchart #308 parity.
- Terminate end event consumes all tokens and completes the composite (Break-parity late-completion
  tolerance).

## Out of scope (deferred to later phases; see docs plan)

- Cyclic graphs (validated and rejected in this slice; loops arrive with loop characteristics /
  iteration scopes in the events tier).
- Expression-based flow conditions (this slice matches outcome names; the expression envelope lands
  with the condition-evaluator leaf).
- Timer/message/signal events, boundary events, event-based gateway, multi-instance, compensation,
  transactions, call activity, BPMN XML interchange, Studio designer mode.

## Success criteria

- Module mirrors Flowchart module conventions (feature registration, structure handler, README,
  EXTENSION_POINTS.md, tests with `WorkflowExecutionHarness`).
- Linear, forked, joined (parallel + inclusive activation-aware), exclusive-routed, terminate and
  fault scenarios covered by runtime tests; feature DI registration test present.
- No core runtime changes required by this slice.
