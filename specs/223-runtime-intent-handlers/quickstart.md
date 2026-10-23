# Quickstart: Validate Contributed Runtime Intent Handlers

## Prerequisites

- .NET 10 SDK
- Repository checkout on `codex/dispatch-workflow-program`

## Focused validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter 'FullyQualifiedName~RuntimePostCommitIntent|FullyQualifiedName~RuntimeResumptionService'
```

Expected outcomes:

- repeated identical handler registration resolves once;
- conflicting same-kind handlers fail with deterministic actionable context;
- unsupported kinds follow the existing policy-selected safe outbox failure path;
- scheduler work retains its validation and enqueue behavior;
- a marker intent committed through a checkpoint is delivered once by a global resumption sweep.

## Regression validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj
```

Expected outcome: all Workflows Runtime scheduler, post-commit, and resumption tests pass.

## Structural guardrails

Run the architecture test project under `tests/Elsa/Architecture/` and verify no broker package and no WorkflowDefinitionActivity dependency appears in the #675 diff.
