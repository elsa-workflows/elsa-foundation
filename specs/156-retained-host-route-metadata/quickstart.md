# Quickstart: Retained Host Route Metadata

## Prerequisites

- .NET SDK available through `dotnet --version`.
- Repository restored with `dotnet restore Elsa.Server.slnx`.

## Focused validation

```bash
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --filter FullyQualifiedName~EndpointManifestBuilderTests
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj --filter 'FullyQualifiedName~RetainedHostEndpointMetadataTests|FullyQualifiedName~FoundationHostEndpointTests'
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter FullyQualifiedName~HostEndpointMetadataTests
```

The focused mapping test builds the Workbench retained route subset and produces 64 valid physical entries when
console streaming is enabled: 60 non-console entries plus two HTTP routes and the SignalR hub plus negotiate
endpoint. The issue inventory counts the hub conceptually as one surface, so this is 63 semantic Workbench surfaces.
The Foundation Host runtime fixture independently proves its exact four routes, security dispositions, and
missing/invalid/valid management-key behavior. Source/architecture tests cover the optional mapping declarations.

## Full gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```
