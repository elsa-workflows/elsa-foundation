# Final HTTP workflow performance evidence

This directory preserves the acceptance evidence for specs 091 and 092. Every request returned
HTTP 200 with the exact body `Hello World!`.

## Provenance

- Implementation revision: `382301460ce34ada7f43a0946706680b6eeea563`
- Release server output closure SHA-256:
  `6bd8340bff2dca25fc4f79e3b928c681e1325ed9a96b4d4fcdeffaae4c105c8b`
- SDK: .NET `10.0.300`
- Host: Apple Silicon macOS
- Persistence: Groundwork SQLite `0.0.1-preview.95`
- Cold-run content SHA-256:
  `72aa7f89c7c38eae652f9c581638172bfd87d786353d5b6561745e676547f71a`
- Cold-run baseline SHA-256:
  `3416278cc1c53f9ecbaedd32f41ed110246292968d0e8ca6dcde53297ba9bbc8`

The cold lane used 20 isolated process boots. Each boot copied the same frozen database, waited
for `/health/ready`, then made one request to `/workflows/http/hello-world`.

The warm lane used four independent copies of the same frozen database. It measured the 2x2
factorial of executable caching and reusable access-bound SQLite stores, with 20 warmups and
200 measured requests at concurrency 1 and 2. Each report records the revision, configuration,
raw samples, response contract, and physical checkpoint-commit delta.

## Acceptance summary

| Lane | Concurrency | p50 | p95 | p99 |
|---|---:|---:|---:|---:|
| executable on, store reuse on | 1 | 30.081 ms | 40.723 ms | 42.875 ms |
| executable off, store reuse on | 1 | 28.755 ms | 40.126 ms | 44.715 ms |
| executable on, store reuse off | 1 | 60.866 ms | 120.487 ms | 128.707 ms |
| executable off, store reuse off | 1 | 61.386 ms | 75.959 ms | 84.222 ms |
| executable on, store reuse on | 2 | 24.643 ms | 32.515 ms | 35.218 ms |
| executable off, store reuse on | 2 | 25.053 ms | 32.898 ms | 34.992 ms |
| executable on, store reuse off | 2 | 67.449 ms | 74.405 ms | 79.767 ms |
| executable off, store reuse off | 2 | 69.655 ms | 76.189 ms | 85.388 ms |

The default-on lane satisfies the 50 ms warm p95 budget at both tested concurrency levels.
The executable-cache toggle is neutral for this deliberately tiny artifact after warmup; its
benefit is avoiding deserialization/materialization for larger artifacts and cold-per-artifact
lookups. Reusable SQLite stores are the decisive improvement for this workload.

The 20-boot lane produced:

| Milestone | p50 | p95 |
|---|---:|---:|
| Listening | 432.488 ms | 441.332 ms |
| Shell ready | 2,451.364 ms | 2,566.966 ms |
| First workflow request after ready | 582.265 ms | 627.631 ms |
| First success from launch | 3,032.672 ms | 3,167.738 ms |

The first-after-ready p95 satisfies the 750 ms budget. Raw reports and samples are in
[`cold/`](cold/) and [`warm/`](warm/).
