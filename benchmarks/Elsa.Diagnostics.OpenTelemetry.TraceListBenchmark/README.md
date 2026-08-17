# OpenTelemetry trace-list latency benchmark

This is the smallest reproducible before/after measurement for issue #268's trace-list storage path. It seeds the same deterministic corpus into two isolated, file-backed SQLite databases and invokes the same `DefaultOpenTelemetryProvider.GetTracesAsync` route:

- **Before/oracle:** the frozen EF Core OpenTelemetry store (the v1 persistence implementation).
- **After/target:** `GroundworkOpenTelemetryStore` over v2 ordinary storage units.

The benchmark intentionally does not load both stores into one production host and does not use one implementation as a runtime fallback for the other. EF/v1 is a benchmark oracle only. It measures the provider/handler route after seeding; ASP.NET routing, request binding, JSON serialization, network, and authentication are outside this harness. Therefore its output is evidence for the trace-list persistence/provider route, not a claim about end-to-end HTTP latency.

Run from the repository root:

```sh
dotnet restore benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark.csproj
dotnet run -c Release --project benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark --no-restore -- --warmups 5 --samples 30 --traces 1000 --seed 2682026
```

Restore must resolve the exact `0.2.0-preview.1` v2 Groundwork packages used by the adapter from the
Valence Works Feedz source configured by the repository `NuGet.config`. NuGet.org and local package
directories are not accepted release evidence.

The command prints a canonical input fingerprint (including the filter and every seeded batch), expected result count, ordered trace-ID digest, mean/p50/p95/p99, raw samples, and the target/oracle p95 ratio. Keep the complete stdout: the v1/v2 comparison report also emits frozen source/package provenance and OS/runtime/architecture/CPU-count details. No latency numbers are committed here because they are machine-dependent. A full endpoint measurement still requires an Elsa host-level TestServer or deployment run using the same seeded databases.

## Shipping Groundwork v1 versus v2

The original EF comparison is retained as a third oracle, but it is not the issue #268 “before” implementation. The acceptance comparison is the shipping Groundwork v1 diagnostics adapter, whose trace route uses the v1 grouped-reduction/record-stream implementation, against the v2 ordinary-unit adapter. Their Groundwork package and assembly identities are intentionally never loaded into one process.

Prepare a clean detached v1 worktree at the frozen adapter commit, then build the v1 child and the v2 benchmark with the required package feeds:

```sh
git worktree add --detach /tmp/elsa-otel-groundwork-v1 e30c2d291
dotnet build -c Release benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListV1Child/Elsa.Diagnostics.OpenTelemetry.TraceListV1Child.csproj \
  -p:V1Root=/tmp/elsa-otel-groundwork-v1
dotnet build -c Release benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark.csproj
dotnet run -c Release --project benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark --no-restore -- \
  --v1-v2 --v1-child benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListV1Child/bin/Release/net10.0/Elsa.Diagnostics.OpenTelemetry.TraceListV1Child.dll \
  --warmups 5 --samples 30 --traces 1000 --seed 2682026
```

The v1 child seeds and times the same canonical input independently, emitting only a JSON measurement to its parent. The coordinator rejects input-fingerprint or ordered-result-ID mismatches before reporting `AfterToBeforeP95Ratio`. Build/run v1 and v2 in separate processes; do not add a project reference from the v2 benchmark to the v1 child.
