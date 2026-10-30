# BPMN hosting contracts

This package is the thin consumer boundary for [direct BPMN hosting](../../../../../specs/1017-direct-bpmn-hosting/spec.md). Shared durable execution ports belong to `Elsa.Workflows.Runtime.Core`; BPMN command and inspection contracts belong here. The package does not depend on an interpreter, provider or API host.

[`IBpmnModelRuntime`](IBpmnModelRuntime.cs) is the scoped replacement port for immutable publication, direct start, exact human completion, cancellation, service-incident inspection/retry and execution/work/retained-command inspection. Its model material, caller and result DTOs are consumed by the BPMN implementation and authenticated API. [Extension points](EXTENSION_POINTS.md) identify registration and safety requirements.

## Declared model formats

[`BpmnModelDefinition`](BpmnModelDefinition.cs) is separate from the frozen `BpmnProcess` authoring envelope. The registered driver validates the complete graph and binding inventory before admission; a released-library construct alone does not establish a host binding.

| Definition format | Material profile | Admitted binding shape |
|---|---|---|
| 1 | `foundation-direct-v1` | Model-owned human tasks, plain start/end events and parallel gateways. |
| 2 | `foundation-native-work-v1` | One closed published native service worker, plain start/end events and optional coded error boundaries; technical failure is final. |
| 3 | `foundation-native-work-v1` | The finite service profile with explicit `FinalFault` or `HoldForRetry` policy and an immutable attempt budget. |
| 4 | `foundation-native-work-v1` | Positive duration timers on interrupting boundaries, with explicit human work or one closed service worker with its fault policy. Human and service inventories cannot be mixed. |
| 5 | `foundation-native-work-v1` | Named signal intermediate catches bound to closed native Event workers, plain start/end events and parallel gateways. Human, service and timer inventories are excluded. |

Service policy format 1 counts the initial physical attempt in its budget of 1–32; `FinalFault` requires one. A held failure preserves its original native result and interpreter snapshot. Retry addresses the exact current incident, invocation and failed attempt; it does not revive a final or cancelled execution. Duration sources retain an exact UTC deadline and use the existing delayed outbox. Signal workers have a fixed matching event name, `CanStartWorkflow=false` and no correlation filter; broadcast retains the existing first-delivery recipient snapshot, including an empty snapshot.

Definition format, `elsa.bpmn/1` protocol, material schema and private execution-state version are separate boundaries. Earlier definition formats omit later optional fields and retain their wire/hashes. The [hosting contract](../../../../../specs/1017-direct-bpmn-hosting/contracts/hosting.md) records the detailed refusal, outcome and settlement rules. [Source-bound evidence](../../../../../specs/1017-direct-bpmn-hosting/tasks.md) qualifies the implemented journeys; spec1017 and the wider program remain in progress.
