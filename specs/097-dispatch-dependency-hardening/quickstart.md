# Quickstart: Validate Deterministic and Bounded Dispatch

## Prerequisites

- .NET 10 SDK.
- #676 fire-and-forget DispatchWorkflow present.
- No Studio repository or broker transport required.

## Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx --no-restore
```

Expected: zero errors and no new warnings from #677.

## Compiler/publication

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
```

Proves dependency/hash determinism, stale/inaccessible target rejection, static input validation, exact-cycle diagnostics, legal version skew, and compiler goldens.

## Runtime retention/start

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```

Proves existing live-reference behavior, exact retained-dependency authority, pre-materialization policy denial, transitive/shared/diamond retention, closure lease races, final collection, and legacy compatibility.

## DispatchWorkflow

```bash
dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj
```

Proves pinned-child execution after replacement/unpublication, dynamic input validation before staging, channel isolation, default/custom depth boundaries, replay-stable depth, and unchanged #676 fire-and-forget behavior.

## Groundwork

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
```

Proves artifact model round-trip, closure-wide lease fencing/recovery, and in-memory parity. #678 dispatch-record durability remains excluded.

## Architecture and maps

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
bash tools/maps/generate-maps.sh
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

Inspect `docs/maps/manifest.json` and findings. Expected: no Runtime→Design, WorkflowDefinitionActivity, Studio, MassTransit/broker, distributed-placement, or extension-catalog drift.

## Final gate

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build Elsa.Server.slnx --no-restore
git diff --check
```

Complete only when every #677 criterion has a passing test/gate and the exclusion audit is clean.
