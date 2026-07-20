# Design persistence baseline evidence

**Work unit**: `093-groundwork-design-persistence`
**Base**: `origin/main` `d1548991f`
**Captured**: 2026-07-20

## Version and publication check

`Directory.Packages.props` and `.config/dotnet-tools.json` both pin the Groundwork
family to `0.0.1-preview.72`, including `Groundwork.SqlServer`,
`Groundwork.MongoDb`, and `Groundwork.Tool`. The command below reported
`Groundwork.Tool` `0.0.1-preview.73` as the newest public package:

```bash
dotnet package search Groundwork.Tool --prerelease --take 20 --format json
```

The requested `0.0.1-preview.75` was not public at capture time. No package version was
changed. T001 is complete because it records the actual baseline; T009 selects and pins
the later released binary-compatible family needed for physical-storage/query work.

## Provider image baseline

| Provider | Captured image / mode | Source of record |
|---|---|---|
| SQLite | Embedded | Provider has no container image. |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04` | SQL Server provider driver and unified-host fixture. |
| PostgreSQL | `postgres:17.6-alpine3.22` | Pinned provider driver. |
| MongoDB | `mongo:7.0.24` | MongoDB provider driver and replica-set fixture. |

One existing PostgreSQL unified-host fixture uses `postgres:16-alpine`; T053 selects
and records its fixture image with the provider evidence.

## Focused behavior inventory

These commands restored and discovered the existing temporary EF-oracle suites:

```bash
dotnet restore tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --list-tests

dotnet restore tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --list-tests
```

| Suite | Discovered tests | Status |
|---|---:|---|
| `Elsa.Workflows.Design.Tests` | 354 | Discovery succeeded. |
| `Elsa.Activities.Design.Tests` | 467 | Discovery succeeded. |

The full solution's built Release outputs were discovered without executing tests:

```bash
dotnet test Elsa.Server.slnx -c Release --no-build --no-restore \
  --disable-build-servers -m:1 --list-tests --logger 'console;verbosity=quiet'
```

Result: 6,588 discovered tests across the solution.

The new shared fixture scaffold was verified with:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj -c Release
```

Result: 8 passed, 0 failed, 0 skipped.

The existing focused Groundwork adapter suites were also executed at the same base:

| Suite | Passed | Failed | Notes |
|---|---:|---:|---|
| `Elsa.Workflows.Design.Persistence.Groundwork.Tests` | 46 | 0 | Existing legacy-storage obsolescence warnings only. |
| `Elsa.Activities.Design.Persistence.Groundwork.Tests` | 47 | 0 | Existing legacy-storage and descriptor obsolescence warnings only. |

After restoring the clean worktree, `dotnet build Elsa.Server.slnx -c Release
--no-restore -v:q` completed successfully with 0 errors. The build reports existing
Groundwork-storage and legacy-descriptor obsolescence warnings, plus `NU1510` in
`Elsa.Architecture.Tests`; the new DesignConformance project introduced none.

## Deliberate evidence boundary

This is a test-inventory and scaffold baseline, not a workload correctness baseline.
It does **not** claim EF SQLite result hashes, provider parity, query plans, or
performance metrics. T021–T024 must first create the fixed black-box scenarios; T025
then runs the EF SQLite oracle and completes T004 without treating test discovery as an
oracle. T004 is not a Phase-2 gate and does not replace T067–T069's fixed-scale
performance and physical-form-selection matrix.
