# Quickstart: Runtime Activity Start State Transition

Run:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```

Expected result: accepted `Start` commands schedule start-node work, record scheduled activity execution state, enqueue start-activity work, and transition that state to `Running` without invoking activity bodies.
