# Quickstart: Validate Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

This guide is the executable evidence checklist for issue #641. Commands assume the repository root and Release configuration.

## Prerequisites

- .NET 10 SDK.
- Docker or compatible provider containers for SQL Server, PostgreSQL, and MongoDB.
- A MongoDB replica set or sharded deployment for the transaction-required suite.
- One binary-compatible Groundwork version across `Groundwork.Core`, `Groundwork.Documents`, all four provider packages, and `Groundwork.Tool`.
- Provider connection values supplied through environment variables; never commit or pass secrets in process-visible command arguments.

## 1. Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx -c Release --no-restore
```

Expected: zero build errors. New warnings introduced by the feature are failures; existing accepted advisories must be recorded separately.

## 2. Run focused adapter suites

```bash
dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj -c Release --no-build
```

Expected: registration, serialization, command, atomic rollback, and unified-host scenarios all pass.

## 3. Run the four-provider black-box suite

The implementation phase creates one shared design conformance project whose fixtures select each real provider:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj -c Release --no-build
```

Required fixtures:

- SQLite, including close/reopen restart;
- SQL Server container;
- PostgreSQL container;
- MongoDB replica-set/sharded transaction fixture;
- MongoDB standalone negative-readiness fixture.

Expected: identical result hashes and domain outcomes for all public design stores/commands; isolation, OCC, atomic rollback, ambiguous acknowledgement, cancellation, restart, and schema drift scenarios pass.

## 4. Prove bounded execution

Run the scale dataset/query suite with plan capture enabled. Evidence must cover every row in [the bounded query catalog](contracts/design-persistence-contract.md#bounded-query-catalog).

Expected for each provider/query:

- the declared query identity resolves to one certified handler;
- scope and document-kind discrimination are present;
- the intended physical entity table/index is selected;
- selective queries do not scan a shared collection or load all documents into application memory;
- count/any/first/latest operations remain server-side;
- unsupported shapes fail before provider I/O.

Store raw plan artifacts under the benchmark evidence directory defined by issue #646; do not paste provider plan blobs into this spec.

## 5. Validate schema operations

Build the assembly containing the unified `IPhysicalSchemaManifestSource`, then install the exactly matching local Groundwork tool version.

```bash
dotnet tool restore
dotnet groundwork --version
dotnet groundwork validate \
  --manifest-assembly ./src/Apps/Elsa.Server/bin/Release/net10.0/Elsa.Server.dll \
  --manifest-type ElsaGroundworkSchema \
  --provider sqlite \
  --offline \
  --output json
```

Repeat live `plan`, `status`, `validate`, and `apply --safe` for `sqlite`, `sqlserver`, `postgresql`, and `mongodb` using `--connection-env`. Accept exit code `0` or `2` for the plan gate; require `0` after safe application and live validation.

Expected:

- deterministic manifest/route/plan fingerprints;
- resolved names follow host policy then provider normalization;
- projected-field backfills are restart-safe;
- schema drift or unsupported topology yields a blocking diagnostic;
- no command silently mutates on validate/status.

The exact manifest type name is finalized during implementation and this guide must be updated if it differs from `ElsaGroundworkSchema`.

## 6. Capture EF-oracle and physical-form evidence

Using the fixed workload from issue #646, run identical seeds, payloads, query shapes, concurrency, and result hashing against:

1. temporary EF normalized design tables;
2. Groundwork shared documents plus linked indexes;
3. Groundwork dedicated document tables;
4. Groundwork physical entity tables.

Datasets include the agreed 1K/100K/1M scale points where the runner and provider capacity permit. Record p50/p95/p99, throughput, allocation, round trips/database work, storage, write amplification, migration/backfill cost, and native plan selection.

Exit gate:

- correctness before timing;
- p95 `<= 1.25x` EF;
- throughput `>= 80%` EF;
- p99 `<= 2x` EF;
- selected entity forms beat both other Groundwork forms repeatably.

## 7. Run design behavior and architecture suites

```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: all preserved domain-objective tests pass; core-provider boundaries and the design-EF removal ratchet pass.

## 8. Audit design EF removal

After the evidence gates pass and the EF design projects are removed:

```bash
rg -n 'EntityFrameworkCore|EFCore|DbContext' \
  src/Elsa/Workflows/Design/Persistence \
  src/Elsa/Activities/Design/Persistence \
  tests/Elsa/Workflows/Design \
  tests/Elsa/Activities/Design

dotnet list src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Core/Elsa.Activities.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj package --include-transitive
```

Expected: the search has no design EF implementation/mechanism hits, and none of the four retained projects resolves a `Microsoft.EntityFrameworkCore*` package. Domain documentation may mention the historical migration only where explicitly allowed by the final architecture guard.

## 9. Full verification and handoff

```bash
dotnet test Elsa.Server.slnx -c Release --no-build
git diff --check
```

Refresh the narrowest dependency and extension-point maps, review the generated findings, run an independent FR/SC audit against exact HEAD, and attach:

- test summaries and provider versions;
- schema fingerprints and CLI outputs;
- query-plan evidence index;
- benchmark raw summaries and decision;
- direct/transitive dependency audit;
- final commit and PR/check links.

Issue #641 closes only after the merged `main` state contains the removal and all evidence remains reproducible.
