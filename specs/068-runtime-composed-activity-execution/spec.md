# Feature Specification: Runtime Composed Activity Execution

**Feature Branch**: `codex/runtime-composed-activity-execution`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Elsa 4 runtime execution seam after removing the direct executor. The runtime must prove that an in-process workflow execution agent can start a pinned runtime executable and invoke a real activity through composed Workflows Runtime API + Activities Runtime services.

## Scenarios & Tests

1. Given Workflows Runtime API and Activities Runtime are composed, when an in-process execution agent starts a one-node runtime executable, then scheduler work drains through the activity invocation provider and completes the workflow.
2. Given Activities Runtime is composed, when an InvokeActivity scheduler work item is reached, then the provider-specific `WorkflowInvokeActivitySchedulerWorkHandler` handles it before the missing-provider fallback.
3. Given the workflow starts in an in-process lane, when the activity body runs, then it executes on the same inline dispatch path and can resolve execution services from the activity execution context. Full HTTP request-scope affinity remains a later acceptance slice.

## Requirements

- **FR-001**: A composed runtime service provider MUST support starting a pinned `WorkflowExecutable` through `IWorkflowExecutionAgentProvider`.
- **FR-002**: The scheduler MUST drain `Start -> Checkpoint -> ScheduleActivity -> StartActivity -> InvokeActivity -> CompleteActivity -> CompleteActivity -> Checkpoint` without requiring the removed direct executor.
- **FR-003**: Activities Runtime MUST supply the provider-specific activity invocation handler for `InvokeActivity` work.
- **FR-004**: The missing activity invocation fallback MUST NOT handle `InvokeActivity` when Activities Runtime is composed.
- **FR-005**: A successfully invoked one-node executable with no outgoing edges MUST checkpoint the workflow as completed.
- **FR-006**: The activity body MUST be able to resolve in-process execution services from `IActivityExecutionContext`.
- **FR-007**: The slice MUST NOT introduce runtime dependencies on Design-owned authored workflow models.
- **FR-008**: The actor-style execution abstraction MUST preserve a future request-affine synchronous lane for HTTP-triggered workflows that need live `HttpResponse` access until completion, durable suspension, fault, cancellation, or another explicit boundary.

## Non-Goals

- Full workflow execution product behavior.
- Full HTTP endpoint integration or `Write HTTP Response` implementation.
- Full request-scope propagation design.
- Distributed actor provider implementation.
- Full bookmark store/index, outbox processor, or workflow-as-activity behavior.

## Acceptance Criteria

- Activities Runtime tests prove composed Workflows Runtime API + Activities Runtime can start and complete a one-node executable through the in-process agent.
- The drain result contains `WorkflowInvokeActivitySchedulerWorkHandler` and does not contain `MissingActivityInvocationSchedulerWorkHandler`.
- Activity and workflow execution state stores show the activity and workflow completed.
- Runtime remains free of Design-owned execution-time dependencies.
- Focused runtime/activity validation passes.
