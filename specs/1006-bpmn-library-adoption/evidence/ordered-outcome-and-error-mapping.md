# Ordered BPMN outcomes and coded error mapping

This reconciles T021/T022 against actual Foundation runtime witnesses using released BPMN 0.5.0. The [machine receipt](ordered-outcome-and-error-mapping.json) binds source, binaries, package closure, complete test runs, mutation phases and unchanged frozen evidence. Production code and authoring contracts are unchanged by this qualification increment. [T024](combined-outcome-ef-recovery.md) supplies the separate actual EF dispatch/acknowledgement/retry proof; these cases do not substitute for it or final Workbench/Studio acceptance.

| Required behavior | Current actual-host witness |
|---|---|
| Non-interrupting notification stages a handler, then original uncaught fault wins | Existing combined-outcome theory now observes the returned Fault verdict and **zero staged starts/cancellations before runtime application**, in addition to no handler execution and exact parent `bpmn.work.faulted`. A generic runtime safeguard cannot hide an adapter scheduling leak. |
| First interrupting notification retires the reporter; later notification is consumed | Same theory requires one staged handler start, no request to cancel the already-terminal reporter, one executed handler and exact structural absorption of the original incident. |
| First semantic terminal verdict wins | The routing-fault theory requires the original gateway no-flow-selected verdict and no issued handler. It must not call another normal callback into the abandoned work state. |
| Concurrent same-node/null-iteration invocation stays untouched | New scope-isolation case creates two parallel tokens at **one** bound BPMN host node. Both real structural reporters wait through native Event children. They share node, element and null iteration but have different exact handles/tokens. The selected reporter is explicitly not the first history match. After its normal child resume emits two ordered notifications plus a typed fault, the other reporter, its leaf and complete physical bookmark generation/scope equal JSON frozen **before** the action. Only the selected token is pruned. The remaining invocation later executes its own outcome, handler and incident absorption independently; both handlers run once and the workflow completes. |
| Preserve outward signals through a final inner fault | New nested BPMN case observes actual inner and outer OutcomeAsync inputs through a passive forwarding wrapper. Both immutable notification **codes and raw payloads**, including ordinal1/2 order, reach the outer adapter unchanged with the inner fault. The outer catcher runs once and absorbs only the exact inner incident; original leaf and inner Faulted states remain evidence while the outer workflow completes. |
| Authored BPMN error code differs from incident code/classification | Four new real-host cases use the literal error catcher PAYMENT-DECLINED. A bpmn.error category with matching FaultType catches despite a different incident Fault.Code. A different authored type does not catch even when incident.Code looks matching. A foreign category with matching type/code and an uncategorized matching-looking incident.Code also do not catch. Child metadata and incident FailureType remain intact; propagated parent bpmn.error/FaultType exists only for the genuine authored code. |
| Wrap library failures with identifiers and original cause | The orphan-bound-work control now checks BpmnHostException with the node ID and original library cause, then executes the real runtime and verifies that domain exception type/node ID and zero orphan dispatch. Existing actual legacy corruption/refusal witnesses and state-codec controls remain separately source-bound. |

The exact withheld-variable refusal remains covered by `Collection_WithheldVariable_FaultsWithTheWithheldValueCode` in the same whole project. That pre-existing control deliberately supplies a withheld scoped envelope to the actual adapter directly because this slice has no producer that withholds variables. It is a synthetic boundary control, not an upgraded-host or Studio witness. The runtime mappings continue to use existing scheduling, scoped cancellation, parent notifications and one staged continuation/state; they add no execution authority or duplicated token policy.

Verification: **focused10/0/0; whole BPMN303/0/0** after restoring six production-source mutants byte-identically. The whole count includes six new discovered cases: two scope cases and four coded-error cases. Existing methods/guards remain active.

| Behavioral mutation | Actual result and limit |
|---|---|
| Use incident Fault.Code as the authored error | Four coded classification cases fail (zero pass). |
| Drop outward signals on a final Fault | Nested outer workflow faults instead of completing; one failure. |
| First node/null-iteration lookup instead of exact handle | The valid concurrent case reaches the selected callback, then fails because another invocation's complete durable facts changed; one failure. This is not the older listener capture-stage mutant. |
| Overwrite first semantic fault with another terminal callback | Routing-fault case fails before a coherent command application returns; one failure/two passes. It does not claim the wrong verdict returned normally. |
| Allow unissued starts on final Fault | Direct staged-command observation sees two handler starts instead of none; one failure/two passes. This catches the leak even when the runtime subsequently refuses it. |
| Leak raw library graph exception | The direct wrapper assertion sees the raw library exception; one failure. The normal actual-host path is checked separately in the same test, but this mutant fails before that path executes. |

All six are behavioral failures, not compilation failures. The initial compile-only fixture argument mistake is unqualified. The first two runtime setup attempts reused one binding at two different elements and were rejected before their waits armed. Released graph validation expressly forbids that shape; the final valid graph uses two incoming parallel flows at one bound element. No validator, runtime or library rule was weakened. Review also required notification codes as well as payloads, frozen pre-action observations to avoid object aliasing, and staged command/typed verdict checks rather than only final host status.

Reproduce from normal checkout outputs:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~BpmnCombinedOutcomeTests|FullyQualifiedName~BpmnCodedErrorMappingTests|FullyQualifiedName~BpmnGraphAdapterTests'
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release -p:RestoreLockedMode=true
```

T021/T022 close within this ordered-command, coded-error and combined semantic matrix. T017 and T024 retain their actual-provider qualification at their recorded sources; historical receipts are unchanged. Three independent bounded correctness/standards/acceptance audits support this reconciliation; full-PR rounds remain zero. T025 native parents, T029/T045 binding/called-control, final actual Workbench/current Studio, semantic deletion/guards, final qualification/required exact-head checks/protected merge/resulting-main, separate ADR0063 and independent #2898/#2907/program production evidence remain open. Orleans/task-runner remain deferred.
