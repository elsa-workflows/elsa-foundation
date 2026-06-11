# Quickstart: Runtime In-Process Execution Agent Provider

Run focused validation for this slice:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
git diff --check
```

Inspect the provider surface:

```bash
rg -n "InProcessWorkflowExecutionAgentProvider|IWorkflowExecutionCommandProcessor" src/Elsa/Workflows/Runtime/Core tests/Elsa/Workflows/Runtime/Tests
```
