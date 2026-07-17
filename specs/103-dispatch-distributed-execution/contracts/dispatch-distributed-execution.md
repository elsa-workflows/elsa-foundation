# Contract: Dispatch Distributed Execution

## Activity-facing execution seam

`DispatchWorkflow` continues to create the existing child-start intent and the child-start handler continues to call `IWorkflowStartDispatcher`. The activity contract exposes no node, queue, route, priority, affinity, broker, or transport-selection setting.

## Distributed forwarding evidence

When the configured actor provider durably forwards child execution to a distributed node, the workflow start command result must include durable forwarding evidence sufficient for the child-start outbox handler to acknowledge delivery without pretending that child execution already happened.

Required safe metadata:

- owning node identity;
- durable transport item identity;
- no raw workflow inputs, output payloads, exception text, stack traces, secrets, or authority internals.

Incomplete durable forwarding evidence is a transient delivery failure and remains retryable by host policy.

## Placement and fencing

The distributed provider owns placement and command fencing. A draining node may claim work; stale owners must fail or no-op at the checkpoint/write boundary. Duplicate child-start delivery must reuse the original dispatch identity, child execution ID, and idempotency key.

## Readiness

Dispatch readiness must classify these modes:

- in-memory development: asynchronous local dispatch, no crash durability, no distributed placement;
- durable single-node Groundwork: crash-durable checkpoint/outbox recovery, no two-node placement guarantee;
- distributed Groundwork: durable checkpoint/outbox recovery plus distributed actor provider, command transport, placement, and fencing.

## Inspection

Runtime inspection reads durable workflow execution and dispatch records and reports consistent lifecycle state independent of the executing node. It does not expose transport internals beyond safe lifecycle/provenance fields already authorized by the runtime API.

## Exclusions

No MassTransit, service-bus, broker-specific abstraction, Studio UI, `WorkflowDefinitionActivity` integration, or DispatchWorkflow activity input for transport control is part of this contract.
