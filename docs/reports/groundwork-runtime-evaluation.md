# Groundwork Runtime Evaluation

Program goal state: [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md).

Status: G8 decision artifact. This report does not migrate workflow runtime stores.

## Decision Summary

| Candidate | Recommendation | Decision | Reason |
|---|---|---|---|
| Runtime-defined business data | Groundwork default | Go | Validated in G6 as document/index business data without workflow continuation semantics. |
| Workflow checkpoint state | Benchmark gated | BenchmarkGate | Needs atomic state-change persistence, conflict handling, retry evidence, and checkpoint diagnostics. |
| Bookmark state | Benchmark gated | BenchmarkGate | Resume correctness depends on concurrent resume/start behavior and executable artifact consistency. |
| Durable value and scheduler continuation state | Benchmark gated | BenchmarkGate | Continuation state must prove latency, concurrency, and recovery behavior before using Groundwork. |
| Workflow execution mailbox and agent ownership | Specialized provider | NoGo | Requires mailbox ownership, lease/agent coordination, and provider-specific operational semantics. |
| Post-commit intents and outbox | Specialized provider | NoGo | Requires outbox ordering, retry, idempotency, and partial-processing recovery semantics. |
| Execution logs and audit stream | Specialized provider | NoGo | Operational streams need retention, throughput, and stream/query behavior beyond portable documents. |
| Distributed locks and leases | Specialized provider | NoGo | Requires coordination guarantees that are not part of `IDocumentStore`. |

## Required Evidence Before Runtime Migration

Benchmark-gated runtime continuation candidates require:

- p95 and p99 read/write latency under expected workflow start, resume, and checkpoint concurrency.
- Optimistic concurrency behavior under parallel start/resume/checkpoint attempts.
- Retry and idempotency behavior after write failures and process restarts.
- Diagnostics for failed checkpoint writes, schema/materialization state, and migration state.

Specialized-provider candidates require:

- A provider contract for ordering, ownership, leases, retention, or stream semantics.
- Retry and recovery behavior for partially processed work.
- Operational metrics, traces, and alert guidance.

## Hard Rule

No workflow runtime hot path should move to Groundwork default from this roadmap alone. A future runtime-specific spec must update the evaluation matrix with benchmark and operational evidence before implementing a runtime Groundwork store.

## Validation Surface

The executable matrix lives in `Elsa.Persistence.Groundwork.RuntimeEvaluation` and is covered by `GroundworkRuntimeStoreEvaluatorTests`.
