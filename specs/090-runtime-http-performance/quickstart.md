# Quickstart: Validate Runtime HTTP Performance

## 1. Run deterministic tests

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Expected outcomes:

- feature composition selects Immediate or Coalesced from shell settings;
- the SQLite-backed HTTP fixture returns the authored response under both policies;
- Coalesced uses at least 75% fewer physical checkpoint commits;
- cap, mandatory-boundary, fencing, and crash-convergence tests pass.

## 2. Start the reference server

The committed `src/Apps/Elsa.Server/shells.json` enables:

```json
"WorkflowsRuntimeCheckpointPersistence": {
  "Mode": "Coalesced",
  "MaxSegmentCheckpoints": 50
}
```

Start the server and publish a synchronous endpoint containing `HttpEndpoint → WriteHttpResponse`.

## 3. Measure the published endpoint

```bash
bash tools/performance/measure-http-workflow.sh \
  --url https://localhost:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 20 \
  --requests 200 \
  --policy Coalesced \
  --segment-cap 50 \
  --provider GroundworkSqlite \
  --groundwork-db src/Apps/Elsa.Server/elsa-groundwork-runtime.db \
  --output-json /tmp/elsa-http-performance.json \
  --output-markdown /tmp/elsa-http-performance.md \
  --enforce-p95-ms 50
```

The report separates the first measured request from warmed samples and records environment metadata. Remove `--enforce-p95-ms` for an informational run on an uncontrolled machine.

## 4. Roll back to Immediate

Change only the feature setting:

```json
"Mode": "Immediate"
```

Restart the shell. Immediate mode does not decorate the selected runtime provider stores.
