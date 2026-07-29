# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:06:22Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-off-store-reuse-off` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 1
- Latency (ms): cold `845.714`, min `57.335`, mean `63.918`, p50 `61.386`, p95 `75.959`, p99 `84.222`, max `99.456`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
