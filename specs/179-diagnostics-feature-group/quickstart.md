# Quickstart: validate diagnostics group selection

From the repository root with .NET 10 installed:

```bash
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj --filter FullyQualifiedName~FoundationSelectionCatalogTests
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --filter FullyQualifiedName~CompositionInitCliTests
```

The focused tests verify the exact group, immutable catalog digests, both required edges, profile-plus-group initialization, old catalog planning/generation, and negative removal/pin paths. The [reference](../../docs/reference/diagnostics-ef-group.md) shows the developer command sequence. Its plan intentionally reports inventory and persistence as unverified when no evidence files are supplied.

Before a PR is merged, run the repository architecture, map-freshness, and solution-filter gates and inspect the exact-head CI result. No new database acceptance claim is made here; [#1969](https://github.com/elsa-workflows/elsa-foundation/issues/1969) owns the two-target runtime/tooling evidence.
