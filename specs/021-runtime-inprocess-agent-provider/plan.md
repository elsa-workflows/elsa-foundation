# Implementation Plan: Runtime In-Process Execution Agent Provider

**Branch**: `codex/runtime-inprocess-agent-provider` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the default single-node execution agent provider. The provider is actor-like but framework-neutral: it owns in-process agent activation, one active mailbox per workflow execution ID, serialized command dispatch to a runtime command processor, idempotency duplicate detection, and passivation.

## Technical Context

- `IWorkflowExecutionAgent` and `IWorkflowExecutionAgentProvider` define the provider boundary.
- `WorkflowExecutionCommandEnvelope` carries workflow ID, idempotency key, sequence, and delivery mode.
- Runtime command behavior is not implemented yet; this slice adds a narrow command processor seam so tests can prove mailbox ordering without building scheduler behavior.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Runtime.Core services/contracts only. |
| Framework-neutral runtime boundary | PASS | In-process provider uses BCL concurrency only; no actor framework packages. |
| Focused tests for logic-bearing services | PASS | Add tests for activation, serialization, idempotency, and passivation. |
| Scope control | PASS | No distributed provider, scheduler behavior, durable persistence, or outbox processor. |

## Scope

- Add an in-process command processor seam.
- Add no-op command processor default.
- Add in-process workflow execution agent/provider implementation.
- Update extension-point catalog and Speckit pointer.
- Add focused runtime tests.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
