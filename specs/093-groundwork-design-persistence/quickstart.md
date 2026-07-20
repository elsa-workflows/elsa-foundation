# Quickstart: Validate Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

This guide is the executable evidence checklist for issue #641. Commands assume the repository root and Release configuration.

## Prerequisites

- .NET 10 SDK.
- Docker or compatible provider containers for SQL Server, PostgreSQL, and MongoDB.
- A MongoDB replica set or sharded deployment for the transaction-required suite.
- One binary-compatible Groundwork version across `Groundwork.Core`, `Groundwork.Documents`, all four provider packages, and `Groundwork.Tool`.
- Provider connection values supplied through environment variables; never commit or pass secrets in process-visible command arguments.

## Baseline record (T001 complete; T004 deferred to T025)

The work unit is based on `origin/main` commit `d1548991f`, captured on 2026-07-20.
The repository currently pins the Groundwork library family, including SQL Server and
MongoDB, and `Groundwork.Tool` to `0.0.1-preview.72`. The public preview feed reports
`Groundwork.Tool` `0.0.1-preview.73` as its newest release; `0.0.1-preview.75` is not
available. This records the actual baseline for T001. T009—not T001—selects and pins
the later released binary-compatible library/tool family that provides the required
physical-storage and query APIs.

The captured provider images are SQLite (embedded), SQL Server
`mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04`, PostgreSQL
`postgres:17.6-alpine3.22`, and MongoDB `mongo:7.0.24`. One existing PostgreSQL
unified-host test still uses `postgres:16-alpine`; T053 selects and records its fixture
image as part of the provider contract work.

The pre-extraction EF behavioral suite inventories are 354 workflow-design tests and
467 activity-design tests. Their focused restore and discovery succeeded at this base;
full-solution Release discovery found 6,588 tests without executing them. The shared
fixture scaffold currently has 8 deterministic-fixture tests. See [baseline
evidence](evidence/baseline.md) for the commands and precise evidence scope.

No temporary EF SQLite oracle workload result hashes exist yet: the black-box workloads
that define them are T021–T024. T025 runs the EF SQLite oracle, records the canonical
behavior hashes, and then records the existing Groundwork red baseline. The test
inventory hashes in this baseline are not substitutes for result hashes, so T004 remains
open; it is deliberately not a Phase-2 gate.

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

### Phase 2 bounded-query substrate evidence (T019)

On 2026-07-20, the canonical in-memory Groundwork test substrate was updated to
validate the selected physical bounded-query declaration before counting provider I/O,
while retaining a legacy-declaration fallback for manifests that have not yet been
physicalized. A physical declaration takes precedence when the transitional manifest
also retains the corresponding legacy declaration, matching provider route binding.
The substrate now rejects undeclared operators, paths, disjunction, ordering, paging,
latest-per-key selection, required residual omissions, unsupported terminal
operations, and physical `Documents` queries without a positive page limit before I/O;
it also observes cancellation before I/O. The legacy-declaration fallback remains limited
to manifests that have not yet been physicalized. The ratified contract requires stable
ordering when the public query shape requests it (for example, latest-version and
catalog routes); it does not add an implicit order requirement to every otherwise
unordered document page.

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj \
  -c Release --no-restore
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  -c Release --no-restore
```

#### Stable-candidate verification

The latest tested source/test candidate is
`f269a165d7cdc8e2582c375a42e9d9b4fe6163c7`. Verification completed at
`2026-07-20T12:43:05+02:00` (CEST, `Europe/Amsterdam`) with these exact results:

- `dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj -c Release --no-restore`
  — passed `71/71` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `625/625` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `52/52` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `48/48` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj -c Release --no-restore`
  — passed `88/88` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj -c Release --no-restore`
  — passed `74/74` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-restore`
  — passed `354/354` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-restore`
  — passed `490/490` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore`
  — passed `251/251` (`0` failed, `0` skipped).

The architecture ratchet used the complete repository assets restored earlier in the
work unit. The documentation-only follow-up changes only this quickstart. The commands
above are not rerun on that documentation commit; their evidence remains attached to
the tested source/test candidate SHA stated above.

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

The same test project must boot the actual `src/Apps/Elsa.Server/` reference host for this composition matrix, rather than proving only isolated fixtures:

| Shape | SQLite | SQL Server | PostgreSQL | MongoDB |
|---|---:|---:|---:|---:|
| Design-only | required | required | required | required |
| Runtime-only | required | required | required | required |
| Combined design + runtime | required | required | required | required |

Design-only and combined execute representative design create/read/update/delete/version flows plus readiness before traffic. Runtime-only proves design features are absent while runtime persistence remains operational. Each cell restarts the host and retains one host-level provider selection.

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

The 1K dataset is the required correctness/smoke scale, 100K is the required acceptance scale for every workload and mandatory provider, and 1M is required for every scale-bearing query/form comparison on every mandatory provider. An architect-approved workload exclusion must be recorded before timing; machine capacity does not silently waive a scale.

Run every row in the [Benchmark Acceptance Catalog](contracts/design-persistence-contract.md#benchmark-acceptance-catalog). For each measured case, run three independent processes after one untimed warm-up per process. Each measured process must complete at least 100 operations and 30 seconds of steady-state work. Retain raw per-operation samples, fixed seed, payload hash, result hash, provider/server settings, machine metadata, allocation, round trips/database work, storage, write amplification, migration/backfill cost, and native plans. Compute per-run p50/p95/p99 and throughput, use the median of the three runs for the EF ratio gates, and report 95% bootstrap confidence intervals for form comparisons. Apply gates per catalog row; do not use a workload aggregate to hide a failing operation.

Exit gate:

- correctness before timing;
- p95 `<= 1.25x` EF;
- throughput `>= 80%` EF;
- p99 `<= 2x` EF;
- selected entity forms improve median p95 or median throughput by at least 10% over each other Groundwork form at both 100K and 1M, with the improvement direction present in all three runs and the 95% bootstrap confidence interval excluding zero;
- same-provider EF ratios are required wherever an EF oracle exists; MongoDB records its absolute baseline and must pass correctness, bounded-plan, and form-selection gates without a fabricated EF comparison.

## 7. Run design behavior and architecture suites

```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: all preserved domain-objective tests pass; core-provider boundaries and the design-EF removal ratchet pass.

The exact stable-candidate results for all three commands are recorded in
[Stable-candidate verification](#stable-candidate-verification).

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
