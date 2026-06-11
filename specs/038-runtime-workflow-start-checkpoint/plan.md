# Implementation Plan: Runtime Workflow Start Checkpoint

> Supersession note (2026-06-11): executable start-node scheduling in this plan is superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). Start
> checkpoint dispatch now targets the executable root activity.

**Branch**: `codex/runtime-workflow-start-checkpoint` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Move workflow start from direct start-node scheduling to an explicit `WorkflowStarted` checkpoint boundary. The start handler will validate the pinned executable and enqueue checkpoint work whose post-commit intents schedule executable start nodes. The checkpoint handler will add a `WorkflowExecutionState` upsert for `WorkflowStarted`, mirroring the terminal state lane added by the previous slice while keeping persistence-provider work deferred.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Projects**: `src/Elsa/Workflows/Runtime/Core`, runtime tests
**Storage**: Existing checkpoint commit envelope and in-memory checkpoint writer only
**Constraints**: No Design dependency, no durable workflow state store provider, no scheduler semantics beyond start-node post-commit enqueue

## Constitution Check

| Gate | Result | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses pinned `WorkflowExecutable` and runtime scheduler contracts only. |
| Runtime executes pinned artifacts | PASS | Start-node scheduling uses executable start nodes from the pinned artifact. |
| Continuation state split | PASS | Workflow execution state is emitted through checkpoint state changes. |
| Post-commit side effects | PASS | Start-node scheduling is delayed until checkpoint commit succeeds. |

## Implementation Tasks

- Add Speckit slice artifacts and update active Speckit pointers.
- Mark the previous terminal-completion PR-loop task complete.
- Change start handling to enqueue `WorkflowStarted` checkpoint work with scheduler post-commit intents.
- Extend checkpoint state-change building for `WorkflowStarted`.
- Add focused runtime tests for start checkpoint commit and failed checkpoint behavior.
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
