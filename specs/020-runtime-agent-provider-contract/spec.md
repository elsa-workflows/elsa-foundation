# Feature Specification: Runtime Execution Agent Provider Contract

**Feature Branch**: `codex/runtime-agent-provider-contract`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum decision: workflow executions use actor-style execution agents (`WorkflowExecutionId -> one active mailbox/agent`) while Elsa checkpoint state remains authoritative.

## Scenarios & Tests

1. Given a workflow execution command is sent to runtime, when it is wrapped for agent delivery, then the envelope carries idempotency/sequence metadata for at-least-once providers.
2. Given runtime resolves an execution agent, when the provider contract is inspected, then resolution is keyed by workflow execution ID and activation purpose without naming an actor framework.
3. Given a provider passivates an execution agent, when the contract is inspected, then passivation is limited to named safe boundaries.
4. Given durable state is inspected, when provider contracts are added, then Elsa checkpoint state remains the source of truth and does not become actor framework persistence.

## Requirements

- **FR-001**: Runtime contracts MUST model a workflow execution agent as the single-writer mailbox for one workflow execution ID.
- **FR-002**: Agent command delivery MUST carry command identity, workflow execution ID, idempotency key, optional sequence, and delivery mode.
- **FR-003**: Agent providers MUST be resolved by workflow execution ID through a framework-neutral provider contract.
- **FR-004**: Provider capabilities MUST be explicit and framework-neutral.
- **FR-005**: Passivation/deactivation requests MUST name safe runtime boundaries.
- **FR-006**: Runtime contracts MUST NOT reference Orleans, Dapr, Proto.Actor, or another actor framework.
- **FR-007**: Elsa durable checkpoint state MUST remain separate from provider/actor persistence.
- **FR-008**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.
- **FR-009**: Agent command kinds MUST include the actor-style execution vocabulary from the locked addendum without shifting previously published command ordinals.

## Non-Goals

- Implementing an in-process mailbox provider.
- Implementing distributed placement.
- Implementing actor framework adapters.
- Implementing command execution behavior.
- Implementing checkpoint persistence.

## Acceptance Criteria

- `IWorkflowExecutionAgent` dispatches command envelopes rather than raw commands.
- `IWorkflowExecutionAgentProvider` resolves agents using an activation request and exposes capabilities.
- Command envelopes validate workflow ID consistency and idempotency/sequence rules.
- Passivation boundary names are explicit.
- Focused runtime and architecture tests pass.
