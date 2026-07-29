# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:05:21Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-off-store-reuse-on` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 2
- Latency (ms): cold `28.179`, min `20.544`, mean `26.171`, p50 `25.053`, p95 `32.898`, p99 `34.992`, max `35.936`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
