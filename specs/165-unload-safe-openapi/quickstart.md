# Quickstart: Validate the Unload-Safe OpenAPI Boundary

## Prerequisites

- .NET SDK 10.0.300 or the repository-pinned compatible SDK
- Restored repository dependencies
- Worktree checked out on `codex/1392-unload-safe-openapi-boundary`

## 1. Run the focused contract tests

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~OpenApiLifetimeBoundaryTests'
```

Expected: safe host/shared contract metadata passes; each collectible request, response, metadata-object, method, delegate/transformer, and serializer case fails with the exact owner-aware diagnostic.

## 2. Run the combined lifecycle evidence

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~OpenApiLifetimeCollectibilityTests'
```

Expected:

- the unsafe framework control demonstrates why collectible API-visible types are forbidden;
- the stable-contract path maps, invokes, source-generates JSON, enumerates API descriptions, generates real OpenAPI, disposes, unloads, and collects for three cycles;
- candidate rejection leaves the previous accepted endpoint/document generation available;
- no test clears private caches or waits for timed eviction.

## 3. Run the production canary suite

```bash
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~StructuredLogsApiCollectibilityTests|FullyQualifiedName~StructuredLogsApiContractTests'
```

Expected: native OpenAPI contract and the existing combined lifecycle remain green after applying the convention.

## 4. Run architecture and repository gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet build Elsa.Server.slnx --no-restore --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet format Elsa.Server.slnx --no-restore --verify-no-changes
git diff --check origin/main...HEAD
```

Expected: all commands exit zero, with no new boundary-attributable warnings.

## 5. Review evidence

Compare the observed results with:

- [the public boundary contract](contracts/openapi-lifetime-boundary.md);
- [the decision research](research.md);
- `docs/reports/unload-safe-openapi-boundary-2026-08.md`; and
- ADR 0069.

The blocked Workflows Design and Runtime waves may resume only after their API-visible types follow this boundary and their own combined three-cycle evidence passes.
