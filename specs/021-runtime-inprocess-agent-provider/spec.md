# Feature Specification: Runtime In-Process Execution Agent Provider

**Feature Branch**: `codex/runtime-inprocess-agent-provider`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum direction: the default single-node runtime is actor-like, with one in-process mailbox per `WorkflowExecutionId`.

## Scenarios & Tests

1. Given two activation requests for the same workflow execution ID, when the in-process provider resolves agents, then both requests return the same active mailbox instance.
2. Given several commands are enqueued concurrently to one in-process agent, when they are accepted, then command processing is serialized for that workflow execution.
3. Given an at-least-once command is delivered more than once with the same idempotency key, when it is enqueued, then the duplicate is not processed a second time and the dispatch result is `Duplicate`.
4. Given an agent is passivated through the provider, when the old agent is used, then it no longer accepts work, and a later activation creates a fresh active agent.

## Requirements

- **FR-001**: Runtime.Core MUST ship a default in-process `IWorkflowExecutionAgentProvider`.
- **FR-002**: The provider MUST keep one active in-process mailbox per workflow execution ID.
- **FR-003**: The in-process agent MUST serialize accepted command processing for a workflow execution ID.
- **FR-004**: The provider MUST expose only framework-neutral in-process capabilities.
- **FR-005**: The agent MUST deduplicate already processed command idempotency keys.
- **FR-006**: Provider passivation MUST remove the active mailbox and mark the old agent unavailable for new work.
- **FR-007**: The provider MUST NOT introduce actor-framework or Design-owned model dependencies.

## Non-Goals

- Implementing distributed placement or leases.
- Implementing scheduler command behavior.
- Implementing durable actor persistence.
- Implementing checkpoint store persistence.
- Implementing retries beyond idempotent duplicate detection.

## Acceptance Criteria

- Runtime tests prove one active in-process agent per workflow execution ID.
- Runtime tests prove command processing for one agent is sequential under concurrent enqueue attempts.
- Runtime tests prove idempotency duplicate detection.
- Runtime tests prove passivation removes the active agent and prevents old-agent acceptance.
- Architecture/runtime dependency checks remain Design-free and actor-framework-free.
