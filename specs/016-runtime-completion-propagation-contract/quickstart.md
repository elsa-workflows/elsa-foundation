# Quickstart: Runtime Completion Propagation Contract

Run focused validation:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
git diff --check
```

Inspect the contract:

- `src/Elsa/Workflows/Runtime/Core/Models/SchedulerState.cs`
- `tests/Elsa/Workflows/Runtime/Tests/RuntimeActivityCompletionPropagationContractTests.cs`
