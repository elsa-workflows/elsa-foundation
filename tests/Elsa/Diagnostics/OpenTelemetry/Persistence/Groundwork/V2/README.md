# OpenTelemetry Groundwork v2 evidence seam

This fixture is the bounded proof for the clean-break `GroundworkOpenTelemetryStore` adapter. It
consumes the public v2 Groundwork packages, declares seven ordinary scoped units, and exercises the
same lifecycle through SQLite plus the native PostgreSQL, SQL Server, and MongoDB matrix when their
connection-string environment variables are present.

## Verification

```sh
NUGET_PACKAGES=/tmp/otel-nuget dotnet test \
  tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/V2/\
  Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests.csproj \
  --no-restore -c Release
```

The live matrix uses `GROUNDWORK_V2_SQLITE_CONNECTION_STRING`,
`GROUNDWORK_V2_POSTGRESQL_CONNECTION_STRING`, `GROUNDWORK_V2_SQLSERVER_CONNECTION_STRING`, and
`GROUNDWORK_V2_MONGODB_CONNECTION_STRING`. CI must provide all four (or provision the declared
Testcontainers fixtures); a local run skips only providers whose variables are absent.

The tests prove the trace list's declared `AggregationQuery.SourcePredicate` behavior with repeated
trace groups, exact append replay, catalog/payload round-trip, deterministic ordering, and the
ordinary-unit schema. They do not inspect private provider SQL or count a client-side retained scan.

The adapter package closure is checked with a Release pack and nuspec inspection. The resulting
dependency list must contain `Groundwork.Kernel`, `Groundwork.Query.Model`, and `Groundwork.Store`,
and must not contain `Groundwork.DiagnosticRecords` or `Groundwork.Documents`:

```sh
package_dir="$(mktemp -d)"
dotnet pack src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/\
  Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.csproj -c Release -o "$package_dir"
unzip -p "$package_dir"/*.nupkg '*.nuspec' | rg \
  'Groundwork\.(DiagnosticRecords|Documents|Kernel|Query\.Model|Store)'
```

## Trace-list benchmark plan

The adapter's before/after comparison is intentionally a plan until a stable Elsa workload fixture is
available; no latency figures are claimed here. Run the same seeded corpus and filter mix against the
old diagnostic-record adapter and this adapter on each provider, in warm and cold modes, recording
wall-clock p50/p95, allocated bytes, result count, provider round trips, and rows examined. The
before path is the retained-record scan plus client reduction; the v2 path is the declared trace
aggregation with source filtering before reduction. Persist raw runs and the commit/package versions
beside the report before publishing a comparison.

For current implementation inventory, use:

```sh
git diff --stat ade5578a6 -- src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork
rg -n "Groundwork\.DiagnosticRecords|Groundwork\.Documents" \
  src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork
```

The second command is expected to return no production adapter references. Broader Elsa diagnostics
deployment manifests still own the legacy deployment contract and are an explicit integration seam;
they are not silently treated as migrated by this isolated adapter proof.
