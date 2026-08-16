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

The verification recorded for this revision restores Groundwork `0.1.0-preview.1` from the public
Valence Works Feedz source. Falling back to the v1 provider packages is not supported.

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

## Trace-list benchmark evidence

`benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark` runs the same deterministic corpus and
trace-list route against the frozen Groundwork v1 child and this v2 adapter in separate processes. It binds
the comparison to source/package provenance, canonical input, result count, and ordered result digest before
printing timing statistics.

The retained local run is
[`docs/reports/groundwork-v2-diagnostics-benchmark.json`](../../../../../../../docs/reports/groundwork-v2-diagnostics-benchmark.json).
With 5 warmups, 30 samples, and 1,000 traces, both implementations returned the same 125 matches and
the same ordered-result digest. Groundwork v1 measured 101.579 ms p95; v2 measured 6.070 ms p95, a
0.0598 after/before ratio. These are reproducible local measurements for the Elsa provider route on the
recorded machine—not a claim about every deployment. HTTP routing, serialization, and network latency are
outside the harness.

## Adapter size

The #268 baseline records 25 source files and 4,834 physical lines under
`src/Elsa/Diagnostics/*/Persistence/Groundwork`. The clean-break v2 adapter comprises 11 C# files and
2,056 physical lines in the same scope: 14 fewer files and 2,778 fewer lines (57.5%). These counts include
feature, declaration, codec, and store files; they exclude tests, generated output, and benchmark harnesses.

For current implementation inventory, use:

```sh
git diff --stat 4418bb9e38641ec92960e7cf27efbd2e583cda04 -- \
  'src/Elsa/Diagnostics/*/Persistence/Groundwork'
rg -n "Groundwork\.DiagnosticRecords" src tests -g '*.{cs,csproj}'
```

The second command is expected to return no references. Groundwork v1 packages used by persistence families
outside diagnostics remain part of issue #269 and cannot share a shipping process with the same-ID v2
provider packages; they are not a compatibility path for this adapter.
