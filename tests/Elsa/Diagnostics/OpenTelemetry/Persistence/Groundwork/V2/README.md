# OpenTelemetry Groundwork v2 evidence seam

This fixture is the bounded proof for the clean-break `GroundworkOpenTelemetryStore` adapter. It
consumes the public v2 Groundwork packages, declares seven ordinary scoped units, and exercises the
same lifecycle through SQLite plus the native PostgreSQL, SQL Server, and MongoDB matrix when their
connection-string environment variables are present.

## Verification

```sh
NUGET_PACKAGES=/tmp/otel-nuget dotnet test \
  tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/V2/Tests/\
  Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests.csproj \
  --no-restore -c Release
```

The live matrix uses `GROUNDWORK_V2_SQLITE_CONNECTION_STRING`,
`GROUNDWORK_V2_POSTGRESQL_CONNECTION_STRING`, `GROUNDWORK_V2_SQLSERVER_CONNECTION_STRING`, and
`GROUNDWORK_V2_MONGODB_CONNECTION_STRING`. CI must provide all four (or provision the declared
Testcontainers fixtures); a local run skips only providers whose variables are absent.

The verification recorded for this revision restores Groundwork `0.2.0-preview.1` from the public
Valence Works Feedz source. Falling back to the v1 provider packages is not supported.

The tests prove the trace list's declared `AggregationQuery.SourcePredicate` behavior with repeated
trace groups, exact append replay, catalog/payload round-trip, deterministic ordering, and the
ordinary-unit schema. They do not inspect private provider SQL or count a client-side retained scan.
`GroundworkV2OpenTelemetryHttpEndpointTests` also starts an ASP.NET TestServer, posts the public filter
to `/diagnostics/opentelemetry/traces/search`, and verifies that the JSON response came from a seeded
SQLite v2 store. This is the endpoint behavior proof; the benchmark below intentionally measures the
provider route separately so HTTP host noise does not contaminate the storage comparison.

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
the same ordered-result digest. Groundwork v1 measured 77.678 ms p95; v2 measured 7.802 ms p95, a
0.1004 after/before ratio. These are reproducible local measurements for the Elsa provider route on the
recorded machine—not a claim about every deployment. HTTP routing, serialization, and network latency are
outside the harness.

## Adapter size

At the frozen E3 baseline `4418bb9e38641ec92960e7cf27efbd2e583cda04`, the diagnostics adapter
comprised 24 C# files and 4,827 physical lines. The clean-break v2 adapter comprises 11 C# files and
2,056 physical lines in the identical scope: 13 fewer files and 2,771 fewer lines (57.4%). The scope is
all C# files matching
`^src/Elsa/Diagnostics/(?:[^/]+/)?Persistence/Groundwork/.+\.cs$`; it deliberately includes the shared
diagnostics composition package as well as the OpenTelemetry and Structured Logs adapters, and excludes
tests, generated output, project files, and benchmark harnesses. This anchored count supersedes the
unanchored approximate inventory in #268.

For current implementation inventory, use:

```sh
git ls-tree -r --name-only 4418bb9e38641ec92960e7cf27efbd2e583cda04 \
  src/Elsa/Diagnostics | \
  rg '^src/Elsa/Diagnostics/(?:[^/]+/)?Persistence/Groundwork/.+\.cs$'
rg -n "Groundwork\.DiagnosticRecords" src tests -g '*.{cs,csproj}'
```

The second command is expected to return no references. Groundwork v1 packages used by persistence families
outside diagnostics remain part of issue #269 and cannot share a shipping process with the same-ID v2
provider packages; they are not a compatibility path for this adapter.
