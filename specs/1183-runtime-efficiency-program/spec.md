# Runtime Efficiency delivery

**Status**: Draft
**Issue**: #2531
**Program route**: Runtime Efficiency

Deliver the [program charter](../../docs/program-goals/runtime-efficiency.md) approved by the owner on 1 November.
This specification covers the remaining integration; #3545 and #3598 retain their own evidence.

## Acceptance

1. Reconcile the older program children against current code, their original acceptance and fresh evidence. Preserve
   applicable requirements; do not close them merely because their terminology or file paths changed.
2. Explain incomplete native starts with captured state/dispatch/outbox evidence and a causal regression where a
   defect is established. Qualify identifier generation, concurrent execution and recovery separately.
3. Materialization loads only the state its default bindings require. Literal/secret-only inputs avoid execution
   history and value reads; narrower bindings avoid unrelated history. Custom resolvers/materializers keep their
   complete context. Closed execution scopes, variable shadowing, lazy structure inputs and current values remain correct.
4. JavaScript preparation and conversion reflection reuse only immutable metadata. Engines, results, registries,
   services, authorization and values remain fresh. Concurrent use and collectible ownership have behavioral proof.
5. Finish the applicable persistence/scheduling/diagnostics objectives linked by the charter. Preserve checkpoint
   atomicity, external-effect boundaries, replay identity and stale-writer rejection. Lower deterministic command
   budgets only after measuring the changed behavior.
6. Qualify representative native, available BPMN and ETL execution with expressions/payloads, sustained work,
   duplicate delivery, owner failure and database interruption. Two- and four-node results use independent machines
   and name database placement. Record completion, errors, recovery, commands, allocation and throughput separately.
7. All changes complete independent review, affected whole-suite accounting, rebuilt relevant backend e2e,
   architecture/maps and exact-head hosted gates. Verify the containing main and final fixture cleanup before closing.

No new public execution model, timing CI gate, generic benchmark service, commit-journal rewrite, release or
production deployment is introduced. Model feature delivery stays with Unified Execution #2892. Qualification does
not turn an unavailable model capability into an implementation claim. Remaining failures stay visible.
