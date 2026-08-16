# Quickstart: Dynamic HTTP Publication Evidence

1. Run the focused route publication tests:

   `dotnet test tests/Elsa/Http/Tests/Elsa.Http.Tests.csproj --no-restore --filter FullyQualifiedName~DynamicHttpRoutePublication`

2. Run workflow resolver and synchronizer tests:

   `dotnet test tests/Elsa/Workflows/Runtime/Http/Tests/Elsa.Workflows.Runtime.Http.Tests.csproj --no-restore`

3. Run workflow HTTP middleware/integration tests:

   `dotnet test tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj --no-restore`

4. Run the architecture suite and generated-map freshness check before review.

Expected evidence includes metadata on every resolver route, deterministic collision errors, atomic rollback, no empty snapshot during replacement, and old-generation drain after lease release.
