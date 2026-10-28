# Elsa.Activities.Bpmn extension points

## Runtime integration

[`IBpmnProcessRuntime`](Contracts/IBpmnProcessRuntime.cs) is the scoped replacement boundary consumed by `BpmnProcess`. `ActivitiesBpmnFeature` uses `TryAddScoped`; an explicit registration before the feature replaces the internal default `BpmnProcessRuntime`. Shell startup rejects competing runtime registrations.

The contract's StartAsync, CompleteAsync, FaultAsync, SignalAsync, ControlAsync and OutcomeAsync operations return Foundation `RuntimeStructuralContinuation` values. A replacement integrates a Foundation runtime context with the released BPMN interpreter. It must preserve the [compatibility contract](../../../../../specs/1006-bpmn-library-adoption/contracts/compatibility.md), exact durable child/semantic-token identity, command ordering and transactional state/owed-work staging. The library remains the semantic authority.

The default adapter uses released `Bpmn.Semantics` 0.5.0 with its default behavior set. Neutral element behaviors, graph rules and semantic state types belong to that package and `Bpmn.Model`. Foundation's former IBpmnElementBehavior, IBpmnBehaviorRegistry, IBpmnBehaviorContext and BpmnExecutionEngine extension APIs are retired with their duplicated implementations. They are not compatibility aliases for the library contracts. Custom neutral semantics require an integration using the library's contracts rather than a copy of Foundation's retired engine.

## Direct model hosting

`ActivitiesBpmnHostingFeature` is an inheritable opt-in feature with virtual service registration and `AdmitNewExecutions=false` by default. It registers the scoped [`IBpmnModelRuntime`](../Core/IBpmnModelRuntime.cs) replacement port and rejects conflicting lifetimes/registrations at shell initialization. Admission-off retains installed readers and workers for accepted inputs.

Its internal contributor registers exact protocol `elsa.bpmn/1`, profile `foundation-direct-v1`, and `start`/`complete-human` input kinds through Runtime's one named Sequential initialization aggregator (cross-domain contribution). Its scoped `BpmnDirectExecutionDriver` uses the same internal released-interpreter evaluation component as the embedded adapter; model-owned human handles/state use the shared queue, execution ownership and atomic EF checkpoint/receipt boundary. The initial profile and remaining proof limits are described in the [API README](../Api/README.md).

## Embedded host responsibilities

| Surface | Owner and behavior |
|---|---|
| Authored structure | `BpmnStructureHandler` retains the Foundation DTO/schema/child-slot envelope and variable declarations. |
| Graph binding | `BpmnGraphAdapter` translates complete pinned work inventories to the released graph; structural failures retain a host exception with the original library validation cause. |
| State compatibility | `BpmnStateCodec` preserves accepted alias/version/wire names; legacy/current documents remain distinct. |
| Exact work | `BpmnWorkLedger` checks complete committed child history, parent/node/element/token/iteration identity and legacy stopped handles before library pruning. |
| Values | `BpmnVariableReader` adapts scoped value availability/refusal without hydrating withheld values. |
| Commands and verdicts | `BpmnProcessRuntime` applies the library's ordered evaluation through existing Foundation scheduler, cancellation, signal and checkpoint operations. |
| Calls | The declared `elsa.bpmn` version-1 consumer authenticates contiguous durable batches for the exact waited call. Admitted temporary source unavailability raises `RuntimeSchedulerWorkUnavailableException` before semantic effects. |
| Start delivery | `BpmnProcessTriggerStimulusProvider` and `BpmnProcessRecurringScheduleProvider` inspect the same translated graph. `CanStartWorkflow=false` opts nested processes out of external starts. |
| XML/catalog bindings | Registered import/export and host-readiness binders live in the separate interchange module. |

Native parent joins, durable source/claim/cohort storage, incidents and scheduler recovery remain with their existing Foundation authorities. This module defines neither a second BPMN interpreter nor a general replacement runtime. See the [adapter README](README.md) and [specification](../../../../../specs/1006-bpmn-library-adoption/spec.md) for the contract and qualification links.
