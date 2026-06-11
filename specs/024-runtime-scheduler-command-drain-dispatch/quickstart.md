# Quickstart: Runtime Scheduler Command Drain Dispatch

Run focused validation for this slice:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
git diff --check
```

Inspect the command-drain surface:

```bash
rg -n "IWorkflowSchedulerDrainPolicy|IWorkflowSchedulerDrainObserver|WorkflowSchedulerCommandProcessor" src/Elsa/Workflows/Runtime/Core tests/Elsa/Workflows/Runtime/Tests
```
