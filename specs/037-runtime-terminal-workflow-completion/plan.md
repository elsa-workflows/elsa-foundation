# Implementation Plan: Runtime Terminal Workflow Completion

**Branch**: `codex/runtime-terminal-workflow-completion` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next completion-propagation step after root completions can reach continuation scheduling. The runtime already inspects pinned executable edges to create downstream post-commit scheduler intents. This slice uses that same executable-artifact traversal to detect terminal continuations: if no outgoing edge matches the completed activity outcomes, enqueue a `WorkflowCompleted` checkpoint and have the checkpoint commit envelope carry a `WorkflowExecutionState` upsert.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Projects**: `src/Elsa/Workflows/Runtime/Core`, runtime tests
**Storage**: Existing checkpoint commit envelope and in-memory checkpoint writer only
**Constraints**: No Design dependency, no durable workflow state store provider, no result mapping

## Constitution Check

| Gate | Result | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses pinned `WorkflowExecutable` and runtime contracts only. |
| Runtime executes pinned artifacts | PASS | Terminal detection comes from executable edges in the pinned artifact. |
| Continuation state split | PASS | Workflow execution state is added through the checkpoint state-change lane. |
| Scope control | PASS | No joins, durable provider, bookmark behavior, retry, or result mapping. |

## Implementation Tasks

- Add Speckit slice artifacts and update active Speckit pointers.
- Extend continuation scheduling to classify matching-edge versus terminal continuation.
- Enqueue `WorkflowCompleted` checkpoint work for terminal continuations.
- Add workflow execution state upsert to `WorkflowCompleted` checkpoint commits.
- Add focused runtime tests.
- Run runtime and architecture validation.

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj --no-restore
dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj --no-restore
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore
git diff --check
```
