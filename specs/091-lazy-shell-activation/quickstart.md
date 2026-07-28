# Quickstart: Verify Shell Readiness and Cold Activation

## Prerequisites

- .NET 10 SDK
- Bash, `curl`, Python 3, `sqlite3`, and a free loopback port
- A stopped reference server and a published `/workflows/http/hello-world` endpoint in the frozen runtime database

## Deterministic validation

```bash
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj
dotnet test tests/Elsa/Tasks/Tests/Elsa.Tasks.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Sqlite/Tests/Elsa.Persistence.Groundwork.Sqlite.Tests.csproj
dotnet test tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Expected outcomes:

- liveness remains 200 while controlled default-shell activation is blocked or failed;
- readiness is immediate 503 until an Active generation exists, then 200;
- concurrent probes do not create additional activation attempts;
- a matching applied-plan stamp skips inspection only when explicitly enabled; the safe default and missing/mismatched stamps perform the complete admission walk;
- existing HTTP, lifecycle, isolation, and architecture tests remain green.

## Build the measured binary

```bash
dotnet build src/Apps/Elsa.Server/Elsa.Server.csproj -c Release --no-restore
```

## Create a frozen baseline

Stop the reference server first, then back up the SQLite files with `sqlite3 .backup` into a directory outside the repository. Keep source databases untouched.

## Measure clean boots

```bash
bash tools/performance/measure-server-cold-start.sh \
  --server-dll src/Apps/Elsa.Server/bin/Release/net10.0/Elsa.Server.dll \
  --content-root src/Apps/Elsa.Server \
  --baseline-dir /tmp/elsa-cold-baseline \
  --base-url http://127.0.0.1:17343 \
  --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world \
  --expected-status 200 \
  --expected-body 'Hello World!' \
  --boots 20 \
  --shutdown-timeout-seconds 30 \
  --output-json /tmp/elsa-cold-start.json \
  --output-markdown /tmp/elsa-cold-start.md \
  --enforce-ready-p95-ms 30000 \
  --enforce-first-request-p95-ms 750
```

The report includes every raw boot plus nearest-rank p50/p95 for listening, activation, shell-ready, first request, and first success. Each boot runs from a fresh copy of the content and frozen baseline; the command retains per-boot logs and mutable data under `--artifacts-dir` (or a printed temporary default). Compare before/after only with matching Git, .NET, and baseline-hash provenance.

## Warm-request regression lane

After readiness succeeds, use the existing harness:

```bash
bash tools/performance/measure-http-workflow.sh \
  --url http://127.0.0.1:17343/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --enforce-p95-ms 50
```

## Rollback

- Set `Elsa:Readiness:WarmDefaultShell` to `false` to restore lazy preparation; readiness remains observational.
- Set `GroundworkRuntimePersistenceSqlite:SkipSchemaInspectionWhenPlanUnchanged` to `false` to restore the full SQLite inspection/validation walk on every activation.
