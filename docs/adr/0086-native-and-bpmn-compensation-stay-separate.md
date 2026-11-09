# Native And BPMN Compensation Stay Separate Semantics Over Shared Runtime Identity

Status: accepted (2026-10-19). Records owner decision D2 of [PRD #3036](https://github.com/elsa-workflows/elsa-foundation/issues/3036)
with its reasoning, at the owner's request after the program review of
[Program #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035).

Related: [ADR 0063](0063-bpmn-moves-to-a-host-agnostic-library.md) (BPMN composite uses the shared semantics
library), [spec 124](../../specs/124-bpmn-compensation/spec.md) (BPMN compensation), [ADR 0080 D0](0080-elsa-4-simplification-decisions.md#d0--focus)
(program authorization), [Unified Execution](../program-goals/unified-execution.md).

## Context

Elsa 4 gets two compensation mechanisms:

- **BPMN compensation** already ships (spec 124). The BPMN token engine keeps a durable, never-pruned compensation
  log in `BpmnExecutionState.Compensables`, claims entries newest first and runs `isForCompensation` handlers one
  at a time through the ordinary child scheduler. ADR 0063 moves that engine to `Bpmn.Semantics`, whose
  [responsibility split](https://github.com/valence-works/bpmn/blob/main/docs/concepts/interpreter-not-engine.md)
  gives compensation ordering to the library and only *running the handler* to the host. The library asks the host
  for nothing compensation-specific: a handler is bound work like any other.
- **Native compensation** is new (Program #3035): a native composite records which of its steps completed and runs
  their recovery handlers, with explicit uncertainty, retained context and Studio authoring.

The program review asked whether the second mechanism duplicates the first, and whether native compensation
should provide BPMN's "host-provided compensation".

## Decision

1. **The two are separate semantics and share no compensation abstraction.** BPMN's ordering, `activityRef`
   targeting, per-pass registration, transaction cancel and claim rules are fixed by the BPMN specification and
   owned by the BPMN library. Native compensation owns its own scope membership, handler association, ordering,
   uncertainty and intervention rules. Neither model reinterprets the other's log.
2. **They share runtime identity and durability, not semantics.** Both build on the existing activity execution
   contracts: `ActivityExecutionId` as invocation identity, `ActivityAttempt`, `ActivityInputSnapshot`,
   `SideEffectProfile.External`'s durable claim before the body runs, structural private state committed in the
   same checkpoint, and fault absorption. These predate both programs and stay owned by the runtime core.
3. **BPMN "host-provided compensation" means the host runs handler work.** Elsa's BPMN host binds a handler
   element to an activity node and schedules it, as for any bound element. A BPMN handler's bound work may be any
   native activity, including one that contains a native compensation scope. That is composition through the
   ordinary binding, not a cascade: a BPMN compensation run never triggers native recovery implicitly, and native
   recovery never claims BPMN log entries.
4. **No adapter is built** between the two logs, and no shared "compensation service" is extracted. If a later
   requirement needs one (for example a cross-model operator view), it gets its own ADR with that requirement
   as evidence.

## Consequences

- No BPMN code, types or library changes are part of Program #3035, and BPMN work under #2735 does not wait on it.
- Studio shows the two differently. BPMN compensation stays inside the BPMN canvas (boundary events and
  handlers); native compensation gets its own scope authoring and recovery inspection. Shared presentation
  components (status badges, timeline rows) are reused where they already exist, without a shared data model.
- A user who mixes the two gets two separate, explicit logs, each with its own outcome. Documentation says so.
- If runtime identity contracts change (for example a neutral invocation contract from #2892), both mechanisms
  adopt the change through their normal owners. Neither blocks on the other.

## Alternatives rejected

- **Native compensation as the BPMN provider.** BPMN already owns ordering in the library; making the host own it
  would contradict ADR 0063 and its "refuse rather than degrade" rule, and the BPMN host port has no compensation
  operation to implement.
- **One shared compensation engine for both.** The models disagree on registration (per BPMN pass versus per
  native step completion), targeting (`activityRef` versus scope or step) and uncertainty (BPMN has none). A shared
  engine would need a policy per model for each, which is two engines behind one name.
