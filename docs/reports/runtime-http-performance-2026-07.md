# Runtime HTTP performance — July 2026

## Outcome

The synchronous `hello-world` workflow was measured on the same Apple Silicon host and build under both checkpoint
policies. Coalescing reduced the warm p95 from **466.924 ms to 38.529 ms** (91.7%), meeting the 50 ms target, while
reducing physical checkpoint commits from **13 to 1 per request** (92.3%). Response status/body validation passed on
every request.

| Policy | Requests counted | Commits/request | Mean | p50 | p95 | p99 | Max |
|---|---:|---:|---:|---:|---:|---:|---:|
| Immediate | 221 | 13 | 269.702 ms | 247.087 ms | 466.924 ms | 615.299 ms | 679.801 ms |
| Coalesced (cap 50) | 221 | 1 | 24.487 ms | 22.040 ms | 38.529 ms | 49.155 ms | 86.177 ms |

The 221 counted requests are one separately recorded sample, 20 warm-ups, and 200 measured warm requests. The
Immediate database added 2,873 `checkpointCommit` documents; Coalesced added 221. A deterministic TestServer
acceptance independently asserts the same 13-versus-1 shape and verifies identical HTTP response, completed workflow
state, and durable response artifact.

## Why the endpoint was slow

Two costs were being conflated:

1. The first request can trigger lazy shell activation. In this run, a request issued while the shell was activating
   waited 24 seconds; a clean Immediate control activation took about 8 seconds. This is startup/configuration work,
   not the steady-state workflow hot path, and should be tracked separately from warmed latency.
2. Once active, the default Immediate policy persisted 13 complete SQLite checkpoint commits for this small
   synchronous workflow. SQLite serialization and transaction cost dominated the request. Coalescing folds the
   straight-line drain segment into one atomic durable commit without changing the response or terminal state.

## Reproduction

Build: git `00dd9ca23a546c065eb74d183d46b21e26167196` plus this work unit's uncommitted measurement changes; .NET
`10.0.300`; `Darwin arm64`; loopback HTTP; Groundwork SQLite; copied published definition/runtime databases. Both
lanes used the same server build and data baseline. The Immediate lane used a temporary environment overlay that
changed only `WorkflowsRuntimeCheckpointPersistence.Mode`.

```bash
bash tools/performance/measure-http-workflow.sh \
  --url http://localhost:7343/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 20 \
  --requests 200 \
  --policy Coalesced \
  --segment-cap 50 \
  --provider GroundworkSqlite \
  --groundwork-db src/Apps/Elsa.Server/elsa-groundwork-runtime.db \
  --output-json /tmp/elsa-http-coalesced.json \
  --output-markdown /tmp/elsa-http-coalesced.md \
  --enforce-p95-ms 50
```

Timing budgets remain an opt-in local/release gate because host load and storage hardware affect wall-clock results.
CI uses deterministic response/state equivalence, exact physical commit counts, cap behavior, and two-generation
crash-convergence tests.

## Operator knobs

- `Mode = Immediate`: configuration-only rollback; every checkpoint is durable immediately, with the highest write
  cost and the smallest replay window.
- `Mode = Coalesced`: folds a straight-line drain segment, preserving mandatory durability boundaries.
- `MaxSegmentCheckpoints = 50`: reference-server default. Lower values bound crash replay and memory more tightly but
  flush more often; higher values reduce writes further at the cost of a larger at-least-once replay window.
- Warm p95 budget: `--enforce-p95-ms 50` fails the measurement command when the chosen environment misses its target.

Startup activation should be measured and optimized as a separate cold-start objective; changing the checkpoint mode
does not remove shell construction, feature discovery, database migrations, reconciliation, or route-table startup.
