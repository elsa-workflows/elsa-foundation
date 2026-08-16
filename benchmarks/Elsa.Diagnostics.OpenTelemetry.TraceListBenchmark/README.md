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

Restore must resolve the exact `0.1.0-preview.1` v2 Groundwork packages used by the adapter. The repository `NuGet.config` maps `Groundwork.*` to the Groundwork preview source; for an isolated local package directory, use an equivalent NuGet config whose `Groundwork Preview` source points at that directory (for example, the #284 feed used during development was `/tmp/groundwork-v2-284-feed.1gmkLr`).

The command prints the corpus fingerprint, expected result count, mean/p50/p95/p99 for both implementations, and the target/oracle p95 ratio. Keep the complete stdout with the commit SHA and machine/runtime details when attaching a measurement; no latency numbers are committed here because they are machine-dependent. A full endpoint measurement still requires an Elsa host-level TestServer or deployment run using the same seeded databases.
