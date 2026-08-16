# Wave 4 Agent API Validation Quickstart

From the repository root:

```bash
dotnet test tests/Elsa/Agent/Tests/Elsa.Agent.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~Wave4Agent|FullyQualifiedName~FastEndpointsTransitionTests'
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet build Elsa.Server.slnx --no-restore
```

Expected outcomes:

- exactly eleven Agent routes are mapped and all before HTTP/OpenAPI projections compare with no
  approval;
- anonymous callers receive `401`, denied callers `403`, and exact/implied/wildcard grants follow
  the Foundation evaluator;
- an FE canary and Agent Minimal API route coexist in one host;
- SSE framing, headers, cancellation, and disposal pass; no heartbeat/resume behavior is claimed;
- three collectible route/DI/serializer/disposal cycles pass;
- the transition ratchet is 145 registrations with no Agent owner and maps are fresh.
