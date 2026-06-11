# Feature Specification: Runtime Request-Affine Execution

**Feature Branch**: `codex/runtime-request-affine-execution`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Preserve Elsa 4's required synchronous HTTP execution capability while continuing to use actor-style execution agents and scheduler-owned activity invocation.

## Scenarios & Tests

1. Given a workflow starts from an HTTP request or other in-process caller, when the caller supplies request-affine execution services to the in-process agent, then inline scheduler drainage exposes those services to the activity execution context.
2. Given a workflow starts without request-affine services, when scheduler work drains, then activity invocation continues to create an internal runtime scope as before.
3. Given execution is resumed asynchronously after a durable boundary, when no request-affine services are supplied, then request-bound services are not fabricated or restored from durable command state.

## Requirements

- **FR-001**: Runtime command envelopes MUST remain durable-data-only and MUST NOT store `IServiceProvider`, `HttpContext`, or other live request objects.
- **FR-002**: In-process workflow execution agents MUST expose a non-durable dispatch-options path for request-affine services.
- **FR-003**: The scheduler command processor MUST carry supplied request-affine services only into the inline drain request for the accepted command.
- **FR-004**: Scheduler drainage MUST expose request-affine services through an async-flow scoped runtime accessor.
- **FR-005**: Activities Runtime MUST use supplied request-affine services when constructing the `IActivityExecutionContext` for inline activity invocation.
- **FR-006**: Activities Runtime MUST continue to create an internal scope when no request-affine services are supplied.
- **FR-007**: The request-affine path MUST preserve single-writer agent/mailbox semantics and MUST NOT reintroduce a direct executor.
- **FR-008**: The slice MUST NOT implement durable resume access to request-bound services; later resumes run without the initiating HTTP response unless a fresh request supplies a new ambient context.

## Non-Goals

- Implementing `Write HTTP Response`.
- HTTP endpoint changes.
- Full durable suspension/resume semantics.
- Distributed actor provider propagation.
- Replacing the scheduler or activity invocation handler.

## Acceptance Criteria

- Tests prove `IWorkflowExecutionAgent.EnqueueAsync` can accept non-durable ambient services for an inline drain.
- Tests prove an invoked activity can resolve a scoped request-affine service instance from `IActivityExecutionContext`.
- Tests prove the default no-options path remains compatible.
- Runtime command envelopes remain free of live request/service-provider state.
- Focused runtime/activity validation passes.
