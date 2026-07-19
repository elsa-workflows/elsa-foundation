# Contract: Dispatch Delivery Recovery

## Host policy

The runtime feature exposes positive host configuration for total child-start attempts and retry delay. The finite policy is persisted by the existing child-start contribution. Cancel and parent-resume responsibilities retain unbounded acknowledgement policies.

## Failure classification

Explicit rejection is permanent. Deferred delivery without durable-forwarding evidence and ordinary infrastructure unavailability are transient. Accepted, duplicate, accepted-but-faulted, or durably forwarded delivery is acknowledged. Child business faults never enter this policy. Durable messages/events never use dispatcher reasons, exception detail, payloads, or arbitrary metadata.

## Exhaustion

The effective final attempt atomically commits the original start item as `FailedFinal`, the linked dispatch as `DispatchFailed` with safe dead-letter evidence, and—for wait only—one deterministic pending parent-resume item. The failed start item is the durable dead letter. Identity/generation/claim/fence mismatches fail closed.

## Wait failure

The existing bookmark route accepts `DispatchFailed`, no outputs, fixed `child-start-delivery-failed` / `delivery` / `The child workflow could not be started.` diagnostics, and one deterministic incident ID. The activity completes normally through `DispatchFailed`. Duplicate delivery completes once; terminal parent is not revived. Every wait failure is permanently non-redrivable.

## Detached failure and redrive

Fire-and-forget exhaustion creates no parent work. Redrive accepts only fire-and-forget + delivery-caused `DispatchFailed` + matching `FailedFinal`. It advances generation/fence, records request ID, moves the same dispatch and same outbox to Pending, resets current attempt/failure scheduling state, and preserves all original identities, payload, and retry policy. Same request is `AlreadyApplied`; different active request conflicts. Retention deletion is fenced against the complete terminal snapshot, so an accepted redrive or any other lifecycle change wins over a stale collector candidate.

## Authenticated API

- GET dispatch list/lookup: `workflow-runtime.read`.
- POST redrive: `workflow-runtime.manage`.

Input is route dispatch ID plus request ID only. Tenant scope comes from provider context. Safe responses never expose payload/context/failure detail.

## Observability

Stable events cover attempt failure, retry schedule, dead letter, wait resume, and redrive disposition. Allowed fields are stable IDs, kind, generation, attempt, status, and timestamps only.

## Compatibility and exclusions

Activity surface and base stores remain. Missing optional generation/dead-letter metadata is safe. Provider documents remain on clean current-only baselines. No broker, Studio, #682, #683, activity retry input, or WorkflowDefinitionActivity change is included.
