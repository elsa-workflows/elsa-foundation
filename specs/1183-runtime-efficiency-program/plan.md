# Implementation plan

The owner authorized implementation and end-to-end delivery. Use parent #2531 for additional work while the ready
queue exceeds its cap. Keep one implementation lane and retain the acceptance of matching older children.

1. Audit old issues and existing guards; record which code and measurements satisfy each requirement. Keep spec 234's
   reviewed crash-window design as the starting point for hop collapse, adapting retired mode names and moved paths.
2. Run original incomplete-start diagnostics separately from timings. Capture the first anomalous dispatch before
   adding a speculative fix. Add deterministic controls for any discovered defect and assess identifiers independently.
3. Add counting-store controls before broadening the default materialization fast path. Analyze immutable bindings;
   retain the complete path for extension implementations whose state requirements cannot be inferred safely.
4. Reuse JavaScript prepared syntax under weak source ownership, with separate binding/script semantics and fresh
   engines. Reuse conversion member metadata under weak actual-Type ownership while resolving aliases each call.
   Verify fresh arguments/policies, concurrent first use, rejection behavior and collectible ownership.
5. Implement applicable storage, pump and cancellation work sequentially, with original acceptance and causal controls.
   Qualify pooling rather than changing its default from a narrow experiment.
6. Build final qualification artifacts from reviewed source. Reuse the original Azure runner in an isolated task
   directory; use four temporary D2s_v6 workers for independent-host runs within existing quota. Preserve old data,
   remove task-owned resources and restore the runner's initial deallocated state after collecting evidence.
7. Review the full integration, run affected whole suites and rebuilt REST e2e, complete hosted gates, merge normally,
   and verify containing main. Publish evidence and reconcile program/child status only after those results exist.

## Constitution check

Use existing role-owned runtime/expression/persistence modules (framework §2.1/§2.6). Immutable metadata caches do
not capture scoped services (§2.5.1); actual Type/source ownership avoids cross-module aliases and process-lifetime
roots. Preserve existing tests and add risk-based red/green controls (§2.21/§2.23, ADR0080 D1). Runtime remains
artifact-only (Elsa §E2.6). Balanced remains default; no draft constitutional decision is promoted. Recheck fencing,
retention, tenant isolation, crash boundaries and extension behavior for each storage/scheduling change.
