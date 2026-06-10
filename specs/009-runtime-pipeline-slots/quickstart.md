# Quickstart: Runtime Pipeline Slots

Validate the slice with:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Manual inspection points:

- Workflow and activity builders expose separate plan models.
- Middleware is registered by stable slot name and order.
- Built-in placeholders are visible in plans.
- Runtime projects still have no `Elsa.Workflows.Design.*` references.
