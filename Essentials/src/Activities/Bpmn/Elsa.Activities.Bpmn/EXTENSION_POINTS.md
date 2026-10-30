# Elsa.Activities.Bpmn extension points

## Runtime integration

[`IBpmnProcessRuntime`](Contracts/IBpmnProcessRuntime.cs) is the scoped replacement boundary consumed by `BpmnProcess`. `ActivitiesBpmnFeature` uses `TryAddScoped`; an explicit registration before the feature replaces the internal default `BpmnProcessRuntime`. Shell startup rejects competing runtime registrations.

The contract's StartAsync, CompleteAsync, FaultAsync, SignalAsync, ControlAsync and OutcomeAsync operations return Foundation `RuntimeStructuralContinuation` values. A replacement integrates a Foundation runtime context with the released BPMN interpreter. It must preserve the [compatibility contract](../../../../../specs/1006-bpmn-library-adoption/contracts/compatibility.md), exact durable child/semantic-token identity, command ordering and transactional state/owed-work staging. The library remains the semantic authority.

The default adapter uses released `Bpmn.Semantics` 0.5.0 with its default behavior set. Neutral element behaviors, graph rules and semantic state types belong to that package and `Bpmn.Model`. Foundation's former IBpmnElementBehavior, IBpmnBehaviorRegistry, IBpmnBehaviorContext and BpmnExecutionEngine extension APIs are retired with their duplicated implementations. They are not compatibility aliases for the library contracts. Custom neutral semantics require an integration using the library's contracts rather than a copy of Foundation's retired engine.

## Host responsibilities

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
