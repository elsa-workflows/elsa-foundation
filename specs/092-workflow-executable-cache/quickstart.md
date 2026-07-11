# Quickstart: Verify Workflow Executable Caching

## Focused behavior

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
```

Verify positive reuse, same-key concurrency, caller cancellation, null/failure retry, LRU eviction, save/delete, listing, disabled mode, telemetry, registration, and provider restart.

## Regression lanes

```bash
dotnet build Elsa.Server.slnx --no-restore
dotnet test tests/Elsa/Workflows/Runtime/Http/Tests/Elsa.Workflows.Runtime.Http.Tests.csproj --no-restore
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

Also run the affected Groundwork SQLite/PostgreSQL registration and HTTP integration lanes identified by the final diff.

## Performance acceptance

Build the Release server, run a new 20-boot lane with `tools/performance/measure-server-cold-start.sh` against spec 091's frozen baseline, and run 200 warm requests with `tools/performance/measure-http-workflow.sh`.

Required p95 budgets:

- shell ready: ≤30 seconds and at least 30% below the recorded pre-091 baseline;
- first workflow response after ready: ≤750 ms;
- warm workflow response: ≤50 ms.

Preserve raw reports and exact repository/binary/data provenance in `docs/reports/shell-activation-performance-2026-07.md`.
