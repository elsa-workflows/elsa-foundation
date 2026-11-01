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

## M4 cancellation design (#2831)

A private control counter belongs to one local actor mailbox. Only a valid matching Cancel waiting on that mailbox
registers it; cancellation or acquisition removes that registration. An actual Cancel turn suppresses yielding to other waiters, so
a same-key retry cannot strand its queued cancellation behind an actor-level Duplicate result. The configured default provider pushes the counter
through an injected internal async context for its active turn. Both the discrete scheduler and replay-safe completion
pump check it before beginning another step. The orchestrator distinguishes `Yielded` from quiescence and flushes
the current segment through the existing ownership-fenced committer before returning. The Cancel then runs through
the usual serialized command/handler path. No second writer or database polling is introduced.

The configured concrete local provider is also the provider used by distributed placement. Both registration paths use
one internal factory, shared within Workflows/Runtime; third-party provider authors resolve the public concrete service.
Behavioral registration-order controls cover core-first and distribution-first composition. Remote delivery and custom
actor implementations retain their own transport/scheduling responsibilities; this signal never grants authority. An
already executing activity is not interrupted. In-memory waiter isolation and real SQLite External/ReplaySafe loop
cases cover all modes, resumption convergence, and deliberate scheduler/fusion/cleanup/flush mutations. See the
[continuation receipt](../../docs/reports/native-runtime-performance-program-2026-11-01.md).

## M4 retained native root design (#2586)

The live coalescing session's `HasDurableRecord` gates an optional optimization, never authority. An eligible native,
fenced checkpoint with a clean context tries an update-only transaction. The ordinary workflow staging query validates
the actual persisted row and compares its full pin with the proposed pin. If the row is missing or changed, a private
staging signal rolls back before any SaveChanges and retries through the original root-write lease. The normal workflow
revision CAS and execution fence remain in the same transaction as participants and the marker.

Initial roots without the hint, model/admission writes, unfenced calls and dirty/externally transacted contexts keep the
ordinary path. Replay is resolved before lease work. An ambiguous acknowledgement still reconciles only through the
immutable marker. Guards cover stale hints, equal-ID/different-hash pins, replaced/deleted rows, stale ownership and
uncertain commits. Source mutations disable the optimization, actual-row check and workflow revision token separately.
