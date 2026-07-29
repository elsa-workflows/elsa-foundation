# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:05:00Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-on-store-reuse-on` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 2
- Latency (ms): cold `26.091`, min `19.387`, mean `25.733`, p50 `24.643`, p95 `32.515`, p99 `35.218`, max `40.618`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
