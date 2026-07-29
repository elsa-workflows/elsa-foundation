# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:04:54Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-on-store-reuse-on` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 1
- Latency (ms): cold `721.686`, min `17.267`, mean `29.423`, p50 `30.081`, p95 `40.723`, p99 `42.875`, max `50.985`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
