# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:05:15Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-off-store-reuse-on` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 1
- Latency (ms): cold `550.678`, min `17.929`, mean `29.418`, p50 `28.755`, p95 `40.126`, p99 `44.715`, max `45.718`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
