# Quickstart: DispatchWorkflow Parent Audit Remediation

Run from the repository root.

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet test Elsa.slnx --no-restore
git diff --check
```

Expected outcomes:

- Every command matches tests and passes.
- Failure-injection tests converge at every persistence boundary.
- More-than-page-size cleanup and retention tests process all eligible records.
- Provider query tests prove stable bounded retrieval.
- Integrated two-node DispatchWorkflow acceptance reaches a terminal lifecycle visible through authorized inspection.
- No generated-map command is run.
