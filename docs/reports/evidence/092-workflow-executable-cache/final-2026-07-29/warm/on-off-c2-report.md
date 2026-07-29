# Elsa HTTP workflow performance

- Timestamp (UTC): `2026-07-29T04:05:57Z`
- Endpoint: `http://127.0.0.1:17247/workflows/http/hello-world` (TLS verification: `enabled`)
- Policy: `executable-cache-on-store-reuse-off` (segment cap: `256`)
- Provider: `Groundwork-SQLite`
- Samples: 1 cold, 20 warm-up, 200 measured at concurrency 2
- Latency (ms): cold `67.167`, min `58.743`, mean `68.047`, p50 `67.449`, p95 `74.405`, p99 `79.767`, max `90.551`
- Physical checkpoint commits: `663`
- Environment: `Darwin arm64`, .NET `10.0.300`, git `382301460ce34ada7f43a0946706680b6eeea563`
