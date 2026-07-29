# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:06:33Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-off-store-reuse-off` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 2
- Latency (ms): cold `72.452`, min `61.947`, mean `70.187`, p50 `69.655`, p95 `76.189`, p99 `85.388`, max `98.253`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
