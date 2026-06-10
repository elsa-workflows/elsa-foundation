# Contract: Runtime Evaluation Matrix

## Default Recommendations

| Candidate | Recommendation | Decision |
|---|---|---|
| Runtime-defined business data | Groundwork default | Go |
| Workflow checkpoint state | Benchmark gated | Benchmark gate |
| Bookmark state | Benchmark gated | Benchmark gate |
| Durable value/scheduler continuation state | Benchmark gated | Benchmark gate |
| Workflow execution mailbox/agent ownership | Specialized provider | No-go for Groundwork default |
| Post-commit intents/outbox | Specialized provider | No-go for Groundwork default |
| Execution logs/audit stream | Specialized provider | No-go for Groundwork default |
| Distributed locks/leases | Specialized provider | No-go for Groundwork default |

## Required Gates

Benchmark-gated candidates require:

- Write/read p95 and p99 measurements under expected runtime concurrency.
- Optimistic concurrency conflict behavior under parallel resume/start attempts.
- Retry and idempotency behavior for failed writes and process restarts.
- Diagnostics for migration, materialization, and failed checkpoint writes.

Specialized-provider candidates require:

- Provider contract for ordering, lease ownership, retry, retention, or stream semantics.
- Operational metrics and alert guidance.
- Recovery behavior for partially processed work.

## Rule

No workflow runtime hot path may move to Groundwork default until this matrix is updated by a future runtime-specific spec with benchmark evidence.
