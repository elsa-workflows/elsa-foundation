# Quickstart: Runtime Generator Contract

## Validate

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
git diff --check
```

## Files

- `src/Elsa/Workflows/Runtime/Core/Models/SchedulerState.cs`
- `src/Elsa/Workflows/Runtime/Core/Models/GeneratorState.cs`
- `tests/Elsa/Workflows/Runtime/Tests/RuntimeGeneratorContractTests.cs`
