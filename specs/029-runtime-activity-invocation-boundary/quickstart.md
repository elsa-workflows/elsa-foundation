# Quickstart: Runtime Activity Invocation Boundary

Validate the slice with:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj
git diff --check
```

Expected outcome:

- Workflows Runtime alone drains through `InvokeActivity` to a clear missing-provider fault.
- Activities Runtime can contribute the default invoke handler and complete a single running activity execution without graph traversal.
