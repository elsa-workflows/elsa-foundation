# Checkpoint savepoint reduction (#2564)

**Status**: Implemented — scoped checkpoint savepoint reduction. Parent program #2531. This bounded lane does not close either parent.

## Goal and acceptance

Remove redundant database work inside an EF checkpoint transaction without changing durability. Each failed save already aborts the entire owned transaction; automatic savepoints are never used to continue it.

- Suppress automatic EF savepoints only while the checkpoint store owns its transaction, restoring the prior context setting on success, failure, cancellation and ambiguous acknowledgement.
- Preserve both saves, participant-before-marker order, transaction count, fences, lease/admission outcomes, replay, idempotency and reconciliation. No schema, default mode or public API change.
- Measure current-source Release PostgreSQL and SQLite reference costs, including one and ten WriteLine leaves (the reference Sequence adds one structural activity). Prove exactly four fewer wire round trips per checkpoint where both saves write, with unchanged EF commands/logical writes/commits. Keep a causal source control.
- Prove rollback after participant and marker failures, reusable context/settings, uncertain commits and existing concurrency/lease-loss guards on affected providers. Run affected whole suites and existing required gates; independently review the diff.

No timing gate, generic benchmark infrastructure, new Azure spend, session-baseline shortcut or liveness deletion. Throughput/latency requires suitable isolated resources; shared-Mac timings cannot establish performance.
