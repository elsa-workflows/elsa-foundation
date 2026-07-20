# Elsa.Activities.Bpmn

`BpmnProcess` is a scoped composite activity that executes a BPMN 2.0 element graph with token
semantics — the third structural container next to `Flowchart` and `Sequence`. `BpmnExecutionEngine`
schedules bound Elsa child activities through BPMN-owned execution state that tracks tokens, active
children, and diagnostics.

## Execution model

- Start events emit tokens; sequence flows carry them; end events absorb them. The process completes
  when no live tokens remain and no children are running.
- Gateways and events are **engine-interpreted**: they never schedule an Elsa activity. Only
  task-family elements (`task`, `userTask`, `serviceTask`, …) and `subProcess` elements bind a child
  activity from the `Bpmn.Activities` slot (`childNodeId`). A BPMN task is a visual/semantic wrapper
  around any Elsa activity; an embedded subprocess typically binds a nested `BpmnProcess`.
- Flow selection matches the completing child's outcome names: unconditional flows are always taken
  (BPMN's implicit AND-split), a conditional flow (`conditionOutcome`) is taken when the child
  reported that outcome, and the default flow is taken only when nothing else was. Exclusive and
  inclusive gateways may bind a decision child whose outcomes drive the selection.
- Multi-inbound parallel joins wait for one arrival per inbound flow. Multi-inbound inclusive joins
  are activation-aware: they wait only while a live token or running child can still reach an
  un-arrived inbound flow. Everything else is an implicit XOR merge.
- A terminate end event consumes every live token and completes the composite; late completions of
  in-flight children are absorbed (Flowchart Break parity).
- Branch faults are fault-aware (Flowchart #308 parity): a faulted child faults the composite
  deterministically (`bpmn.child.faulted`) instead of leaving a join waiting forever. A parked join
  arrival that can never be satisfied faults as `bpmn.join.deadlock` instead of hanging.
- The engine snapshot is one typed, versioned activity private-state envelope
  (`Elsa.Bpmn.ExecutionState`, schema version 1) with prune-on-persist (consumed tokens, capped
  diagnostics). Terminal decisions raised mid-evaluation defer until the evaluation is quiescent
  (the runtime forbids terminal continuations that also schedule children).

## Slice scope and phasing

This module currently ships the Phase 1 core subset (see `specs/108-bpmn-container-activity/`):
none start/end events, terminate end events, task family, embedded subprocess, and
exclusive/parallel/inclusive gateways over **acyclic** graphs. Cyclic graphs are rejected at
validation. Later phases add timer/message/signal events, boundary events, event-based gateways,
multi-instance, compensation, transactions, call activities, expression-based flow conditions, and
BPMN 2.0 XML interchange.

## Expression-driven gateway conditions

`BpmnDecision` is the expression-condition evaluator leaf: its `Outcome` input is evaluated by the
runtime's value binding (literal or any bound expression language) and the activity completes with
the evaluated string as its outcome name. Bind one to an exclusive/inclusive gateway and condition
the outbound flows on the values the expression can produce; a blank result matches nothing, so
routing falls through to the default flow (or faults deterministically when none is declared).

## Behavior contract extension point

Element semantics are extensible through the public `IBpmnElementBehavior` contract. Behaviors
receive a read-only `IBpmnBehaviorContext` and return `BpmnBehaviorDecision` commands;
`BpmnExecutionEngine` validates and applies those commands, keeping mutation and scheduling
authority inside the BPMN runtime. Built-in behavior families are defined in `BpmnElementFamilies`
and registered by `ActivitiesBpmnFeature`.
