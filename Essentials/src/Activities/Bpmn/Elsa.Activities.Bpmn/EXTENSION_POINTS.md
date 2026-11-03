# Elsa.Activities.Bpmn extension points

## Runtime integration

[`IBpmnProcessRuntime`](Contracts/IBpmnProcessRuntime.cs) is the scoped replacement boundary consumed by `BpmnProcess`. `ActivitiesBpmnFeature` uses `TryAddScoped`; an explicit registration before the feature replaces the internal default `BpmnProcessRuntime`. Shell startup rejects competing runtime registrations.

The contract's StartAsync, CompleteAsync, FaultAsync, SignalAsync, ControlAsync and OutcomeAsync operations return Foundation `RuntimeStructuralContinuation` values. A replacement integrates a Foundation runtime context with the released BPMN interpreter. It must preserve the [compatibility contract](../../../../../specs/1006-bpmn-library-adoption/contracts/compatibility.md), exact durable child/semantic-token identity, command ordering and transactional state/owed-work staging. The library remains the semantic authority.

The default adapter uses released `Bpmn.Semantics` 0.5.0 with its default behavior set. Neutral element behaviors, graph rules and semantic state types belong to that package and `Bpmn.Model`. Foundation's former IBpmnElementBehavior, IBpmnBehaviorRegistry, IBpmnBehaviorContext and BpmnExecutionEngine extension APIs are retired with their duplicated implementations. They are not compatibility aliases for the library contracts. Custom neutral semantics require an integration using the library's contracts rather than a copy of Foundation's retired engine.

## Direct model hosting

`ActivitiesBpmnHostingFeature` is an inheritable opt-in feature with virtual service registration and `AdmitNewExecutions=false` by default. It registers the scoped [`IBpmnModelRuntime`](../Core/IBpmnModelRuntime.cs) replacement port and rejects conflicting lifetimes/registrations at shell initialization. Admission-off retains installed readers and workers for accepted inputs.

Its internal contributor registers exact protocol `elsa.bpmn/1` through Runtime's one named Sequential initialization aggregator (cross-domain contribution). The [declared model formats](../Core/README.md#declared-model-formats) identify the human, native-service, retry, duration, signal and claim-required (`foundation-direct-claims-v1`) profiles. Registered input kinds are `start`, `complete-human`, `claim-human`, `complete-claimed-human`, `cancel`, `retry-service`, `native-result`, `native-start-retired`, `timer-elapsed`, `start-embedded`, `cancel-embedded` and `native-parent-settled`. The scoped driver uses the shared released-interpreter evaluation component and the existing queue, ownership, fenced EF checkpoint and receipt boundary. The [API README](../Api/README.md) and [work-unit evidence](../../../../../specs/1017-direct-bpmn-hosting/tasks.md) distinguish qualified cuts from remaining acceptance.

## Native model activity

[`IBpmnModelActivityRuntime`](Contracts/IBpmnModelActivityRuntime.cs) is the scoped replacement consumed by the new [`RunBpmn`](Activities/RunBpmn.cs) activity. The hosting feature uses `TryAddScoped`; startup rejects competing registrations or a different lifetime. Its default `BpmnModelActivityRuntime` suspends and resumes a typed native activity around the same model driver used for direct execution.

The separate [Design feature](../Design/EXTENSION_POINTS.md) registers `BpmnEmbeddingPinSource` with Publishing's existing `IExecutableCompilationSource` aggregation. The literal `Model` input selects an accessible published model source; publication retains its complete material pin/incarnation and a native-to-model dependency edge. Execution reads that retained material rather than a mutable draft or current source selection. Direct and embedded models use the same declared profiles and refusal rules. Coded service-error boundaries use released-library matching after an authoritative terminal result. Held technical failures retain the pre-failure interpreter snapshot; one exact retry consumes the retained failure without overwriting its original evidence. [Spec1017 progress](../../../../../specs/1017-direct-bpmn-hosting/tasks.md) indexes actual service/ETL, retry, timer, signal, cancellation and parent-settlement recovery receipts, including their source/provider limits.

`BpmnEmbeddingCheckpointEnricher` freezes the actual native registration with its start intent and captures cancellation before native state retirement. `BpmnEmbeddingBoundary` requires one qualified physical checkpoint/model reader/native wait tuple. Initial admission compares all three native participants without consuming the parent bookmark. Versioned model documents retain their immutable parent address; terminal classification remains pending settlement until native consumption or terminal responsibility is proven, then an exact model receipt permits source acknowledgement. Unknown provider tuples refuse by default. Opaque invocation text has its own lossless encoding; legacy `BpmnProcess` and absent new fields retain their existing layouts. [Four-engine storage qualification](../../../../../specs/1017-direct-bpmn-hosting/evidence/providers/model-storage/README.md) and [actual SQLite recovery](../../../../../specs/1017-direct-bpmn-hosting/evidence/final-compatibility/README.md) are separate gates.

## Embedded host responsibilities

| Surface | Owner and behavior |
|---|---|
| Authored structure | The separate [Design package](../Design/README.md) owns `BpmnStructureHandler`, which retains the Foundation DTO/schema/child-slot envelope and variable declarations. Through `IActivityStructureHandler.ProjectChildren`, it supplies typed per-child Decision outcomes derived from outgoing non-default sequence flows; generic Publishing consumes those projections without reading BPMN fields. |
| Graph binding | `BpmnGraphAdapter` translates complete pinned work inventories to the released graph; structural failures retain a host exception with the original library validation cause. |
| State compatibility | `BpmnStateCodec` preserves accepted alias/version/wire names; legacy/current documents remain distinct. |
| Exact work | `BpmnWorkLedger` checks complete committed child history, parent/node/element/token/iteration identity and legacy stopped handles before library pruning. |
| Values | `BpmnVariableReader` adapts scoped value availability/refusal without hydrating withheld values. |
| Commands and verdicts | `BpmnProcessRuntime` applies the library's ordered evaluation through existing Foundation scheduler, cancellation, signal and checkpoint operations. |
| Calls | The declared `elsa.bpmn` version-1 consumer authenticates contiguous durable batches for the exact waited call. Admitted temporary source unavailability raises `RuntimeSchedulerWorkUnavailableException` before semantic effects. |
| Start delivery | `BpmnProcessTriggerStimulusProvider` and `BpmnProcessRecurringScheduleProvider` inspect the same translated graph. `CanStartWorkflow=false` opts nested processes out of external starts. |
| XML/catalog bindings | Registered import/export and host-readiness binders live in the separate interchange module. |

Native parent joins, durable source/claim/cohort storage, incidents and scheduler recovery remain with their existing Foundation authorities. This module defines neither a second BPMN interpreter nor a general replacement runtime. See the [adapter README](README.md) and [specification](../../../../../specs/1006-bpmn-library-adoption/spec.md) for the contract and qualification links.
