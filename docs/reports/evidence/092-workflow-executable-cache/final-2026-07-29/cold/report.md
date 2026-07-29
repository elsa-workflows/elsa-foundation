# Elsa server cold-start report

- Boots: 20
- Requested boots: 20
- Startup timeout: `120` seconds
- Shutdown timeout: `30` seconds
- Expected shell: `default`
- Expected workflow status: `200`
- Expected body SHA-256: `7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069`
- Expected body artifact: `/private/tmp/elsa-625-final-382301460-cold20/expected-body`
- Repository HEAD at measurement (not binary attribution): `382301460ce34ada7f43a0946706680b6eeea563`
- .NET SDK: `10.0.300`
- Server output closure SHA-256: `6bd8340bff2dca25fc4f79e3b928c681e1325ed9a96b4d4fcdeffaae4c105c8b`
- Content SHA-256: `72aa7f89c7c38eae652f9c581638172bfd87d786353d5b6561745e676547f71a`
- Baseline SHA-256: `3416278cc1c53f9ecbaedd32f41ed110246292968d0e8ca6dcde53297ba9bbc8`
- Machine: `Darwin Sipkes-MacBook-Air.local 25.5.0 Darwin Kernel Version 25.5.0: Tue Jun  9 22:27:52 PDT 2026; root:xnu-12377.121.10~1/RELEASE_ARM64_T8112 arm64`

| Milestone | p50 (ms) | p95 (ms) | min (ms) | max (ms) |
|---|---:|---:|---:|---:|
| Listening | 432.488 | 441.332 | 333.831 | 443.034 |
| Activation | 2018.365 | 2134.362 | 1909.720 | 2151.126 |
| Shell ready | 2451.364 | 2566.966 | 2348.250 | 2571.133 |
| First workflow request | 582.265 | 627.631 | 568.351 | 663.104 |
| First success | 3032.672 | 3167.738 | 2916.601 | 3174.445 |
| Shutdown | 143.399 | 145.915 | 125.653 | 204.238 |

## Performance budgets

| Budget | Configured (ms) | Actual (ms) | Result |
|---|---:|---:|---|
| Shell ready p95 | n/a | 2566.966 | not configured |
| First workflow request p95 | n/a | 627.631 | not configured |
