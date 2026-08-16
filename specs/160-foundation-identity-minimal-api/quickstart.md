# Quickstart: Validate Foundation Identity Minimal API Migration

1. Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore` and expect all identity behavior, compatibility, token, authorization, and coexistence tests to pass.
2. Run `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore` and expect the transition registry, security/manifest, dependency, and collectibility gates to pass.
3. Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` and expect no stale generated maps.
4. Rebuild `Elsa.Workbench` with a fresh database and run `e2e-tests/get-endpoints/Test-IdentityGets.ps1` and `e2e-tests/write-endpoints/Test-IdentityWrites.ps1`.
5. Run the solution build, changed-file formatter verification, and `git diff --check`.
6. Confirm [the Wave 3 report](../../docs/reports/foundation-identity-wave3-minimal-api.md) records all nine routes, exact approvals, security evidence, collectibility cycles, and final results.
