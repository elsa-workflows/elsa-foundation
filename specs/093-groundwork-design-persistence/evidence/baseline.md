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
changed; T001 remains open until that exact compatible family is available.

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

The new shared fixture scaffold was verified with:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj -c Release
```

Result: 4 passed, 0 failed, 0 skipped.

The existing focused Groundwork adapter suites were also executed at the same base:

| Suite | Passed | Failed | Notes |
|---|---:|---:|---|
| `Elsa.Workflows.Design.Persistence.Groundwork.Tests` | 46 | 0 | Existing legacy-storage obsolescence warnings only. |
| `Elsa.Activities.Design.Persistence.Groundwork.Tests` | 47 | 0 | Existing legacy-storage and descriptor obsolescence warnings only. |

After restoring the clean worktree, `dotnet build Elsa.Server.slnx -c Release
--no-restore -v:q` completed successfully with 0 errors. Its one warning is the
pre-existing `NU1510` recommendation in `Elsa.Architecture.Tests`.

## Deliberate evidence boundary

This is a test-inventory and scaffold baseline, not a workload correctness baseline.
It does **not** claim EF SQLite result hashes, provider parity, query plans, or
performance metrics. T021–T025 must first create fixed black-box workloads; T004 can
then record their EF SQLite result hashes without treating test discovery as an oracle.
