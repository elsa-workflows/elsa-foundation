# Activity activation lifetime benchmark — 2026-07-16

This directory contains the authoritative run used by ADR 0045 to select one child DI scope per CLR
activity attempt.

## Environment and job

- BenchmarkDotNet 0.15.8
- macOS Tahoe 26.5.2 (25F84), Apple M2, 8 logical/physical cores
- .NET SDK 10.0.300; .NET runtime 10.0.8 Arm64 RyuJIT
- Throughput strategy, one launch, three warmups, twelve measured iterations
- Memory diagnostics, median/p50, p95, throughput, and allocated bytes retained

macOS denied BenchmarkDotNet's optional request to raise process priority. The run still completed
all 27 cases successfully and no repository build or test ran concurrently. Treat the timing values
as comparative/directional because several short workloads show substantial variance; apply the
semantic isolation and disposal gates before comparing speed.

## Command

```bash
PATH="/usr/local/share/dotnet:$PATH" dotnet run -c Release \
  --project benchmarks/Elsa/Activities/Runtime/Benchmarks/Elsa.Activities.Runtime.Benchmarks.csproj \
  --no-restore --no-build -- \
  --filter '*' \
  --artifacts benchmarks/Elsa/Activities/Runtime/Benchmarks/results/2026-07-16-m2-net10
```

## Artifacts

- `Elsa.Activities.Runtime.Benchmarks.ActivationScopeBenchmarks-20260716-113413.log`: raw build,
  environment, iteration, GC, and summary output.
- `results/*-report.csv`: machine-readable result table.
- `results/*-report-github.md`: reviewable summary used by ADR 0045.
- `results/*-report.html`: standalone rendered report.

The burst-only candidate failed the observable isolation contract because attempts shared scoped and
transitively scoped dependencies. The conditional candidate required an explicit audited fast-path
allowlist and did not show a stable advantage. Per-attempt child scope passed activity identity,
transient/scoped/transitive identity, failure cleanup, retry, resume, disposal, concurrency, and
intrinsic-zero-activation gates and is the selected production lifetime.
