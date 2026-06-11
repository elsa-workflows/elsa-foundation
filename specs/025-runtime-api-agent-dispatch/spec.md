# Runtime API Agent Dispatch

**Feature Branch**: `codex/runtime-api-agent-dispatch`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after scheduler command drain dispatch.

## User Scenarios & Testing

1. Given a caller asks Runtime API to execute an executable artifact, when the artifact exists, then the request is converted into a `Start` command dispatched through the workflow execution agent for the workflow execution ID.
2. Given a start request is dispatched, when the command payload is inspected, then it references the pinned executable artifact identity and does not reference authored workflow document models.
3. Given an artifact ID is unknown, when start dispatch is requested, then no execution agent is activated and the caller receives a validation failure.
4. Given provider-specific agent implementations exist, when Runtime API starts a workflow execution, then it depends only on the `IWorkflowExecutionAgentProvider` boundary.

## Requirements

- **FR-001**: Runtime.Core MUST expose a start-dispatch contract that accepts an executable artifact ID and returns command dispatch details.
- **FR-002**: Start dispatch MUST load the runtime-owned executable artifact and pin the exact executable artifact identity in the `Start` command payload.
- **FR-003**: Start dispatch MUST create a workflow execution ID before agent activation and use that ID consistently for activation, command, and envelope.
- **FR-004**: Start dispatch MUST send a `WorkflowExecutionCommandKind.Start` command through `IWorkflowExecutionAgentProvider`; Runtime API MUST NOT call `IWorkflowExecutor` directly for execute requests.
- **FR-005**: Unknown executable artifacts MUST fail before agent activation or command dispatch.
- **FR-006**: The default Runtime API composition MUST register the start dispatcher and the default in-process agent path.
- **FR-007**: This slice MUST NOT execute activities, implement scheduler behavior beyond the existing command queue/drain seam, write checkpoints, process bookmarks, or implement durable retry.

## Success Criteria

- Runtime tests prove an execute request dispatches a `Start` command through a workflow execution agent.
- Runtime tests prove the command payload contains the pinned executable identity.
- Runtime tests prove unknown artifacts do not activate an agent.
- Runtime tests prove Runtime API no longer depends on `IWorkflowExecutor` for execute request handling.
