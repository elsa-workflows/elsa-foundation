# Quickstart: Runtime Executable Artifact And Execution State Contracts

## Validate The Slice

Run the focused runtime tests:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```

Run architecture guards:

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

## Expected Results

- A workflow execution can be created with a pinned `WorkflowExecutableIdentity`.
- Multiple activity executions can reference the same executable node while keeping distinct `ActivityExecutionId` values.
- Scheduler state references executable nodes and activity executions, not Design nodes.
- Durable value state uses lifecycle/storage policy.
- Runtime.Core does not reference `Elsa.Workflows.Design.*` or authored workflow model names.
