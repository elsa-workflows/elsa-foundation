# Native runtime preparation and execution-view performance

**Status**: Implemented
**Issue**: #3542
**Program route**: none/free-flow

Implement the performance work approved after the [31 October assessment](../../docs/reports/native-runtime-performance-2026-10-31.md). Preserve the current native execution contract and durability guarantees.

## Acceptance

1. Default zero-input activities materialize the same snapshots without loading durable values, activity history or variable frames. Custom materializers still receive the complete current context. Cancellation and invalid bindings remain observable.
2. Immutable node preparation can be reused across executions, without caching evaluated input values, mutable contexts, scoped services or registry-dependent resolutions. Fingerprints and errors remain compatible; executable eviction releases preparation.
3. Coalescing page reads avoid rebuilding and sorting the complete overlay for each emitted item. Replacement, deletion, parent selection, continuation, flush and resume preserve the current logical view. Durable-store freshness and authorization remain intact.
4. Provider setup reuse must not mix schemas, migration assemblies or load contexts. Pooling defaults remain unchanged until tenant/scoped-service qualification supports a change.
5. Existing correctness suites and rebuilt backend e2e pass; causal regressions fail without the corresponding optimization. Controlled A/B measurements report completion, commands, checkpoint/transaction counts, allocation and latency separately. Timing under interference is excluded.

No new distributed cache, persistence mode, schema, public execution model or concurrency policy is introduced. Native measurements do not establish BPMN, ETL or distributed failover performance. The incomplete in-memory start finding remains a separate correctness investigation.

## Evidence

Implementation, measurements, qualification and limits are recorded in the [implementation report](../../docs/reports/native-runtime-performance-implementation-2026-10-31.md). Implemented status takes effect with PR #3545; it does not claim that pending CI or Code Review has completed.
