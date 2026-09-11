# Quickstart: Validate Foundation Identity Minimal API Migration

1. Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore` and expect all identity behavior, compatibility, token, authorization, and coexistence tests to pass.
2. Run `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore` and expect the transition registry, security/manifest, dependency, and collectibility gates to pass.
3. Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` and expect no stale generated maps.
4. Run the in-process identity API tests under `tests/Elsa/Foundation/Identity/Tests/Api/` (`MinimalIdentityEndpointMetadataTests`, `IdentityOpenApiContractTests`, `TokenEndpointTests`); the REST `get-endpoints`/`write-endpoints` identity scripts were retired on 2026-09-10.
5. Run the solution build, changed-file formatter verification, and `git diff --check`.
6. Confirm [the Wave 3 report](../../docs/reports/archive/foundation-identity-wave3-minimal-api.md) records all nine routes, exact approvals, security evidence, collectibility cycles, and final results.
