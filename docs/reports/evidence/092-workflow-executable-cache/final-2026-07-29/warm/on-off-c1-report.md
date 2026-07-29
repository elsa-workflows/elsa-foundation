# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:05:46Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-on-store-reuse-off` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 1
- Latency (ms): cold `791.173`, min `55.797`, mean `65.477`, p50 `60.866`, p95 `120.487`, p99 `128.707`, max `131.361`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
