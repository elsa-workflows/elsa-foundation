# Wave 1 Validation Quickstart

From the repository root:

```bash
dotnet test tests/Elsa/Api/Capabilities/Tests/Elsa.Api.Capabilities.Tests.csproj
dotnet test tests/Elsa/Attention/Api/Tests/Elsa.Attention.Api.Tests.csproj
dotnet test tests/Elsa/Expressions/Api/Tests/Elsa.Expressions.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Dashboard/Tests/Elsa.Workflows.Dashboard.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~FastEndpointsTransitionTests|FullyQualifiedName~Wave1'
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet build Elsa.Server.slnx --no-restore
```

Expected results:

- six owner mappers publish exactly eight routes;
- anonymous requests return 401 and authenticated callers without permission return 403;
- exact, implied, and wildcard grants authorize through the same Foundation evaluator;
- metadata manifests validate one owner, Minimal API authoring model, and one permission disposition;
- the transition inventory has no registrations owned by the six Wave 1 assemblies;
- all map snapshots remain fresh and the full build has no errors.
