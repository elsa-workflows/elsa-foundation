# Plan

Baseline: main e4446f73c, existing command-cost fixture, Release, PostgreSQL 16. Ten WriteLine leaves plus Sequence: Balanced 66 EF commands / 90 statements / 74 wire round trips / 1 checkpoint / 2 transactions / 3 created savepoints; Durable 209 / 269 / 278 / 12 / 15 / 27. Existing fixture measures the complete start/drain path and actual wire protocol. No timers.

## Design

After BeginTransaction succeeds, capture Database.AutoSavepointsEnabled, disable it, and restore it in finally around the existing transaction body. This transaction is always rolled back after save failure; it never retries a save inside the transaction. Retain two SaveChanges calls and marker-last ordering. Restore the setting before leaving the owned transaction method and before outer root-lease release, including when rollback already disposed the transaction. BeginTransaction failure changes no setting. Existing full rollback/tracker restoration and marker reconciliation retain authority.

## Constitution check

Both framework v6.1.0 and Elsa v6.3.0 loaded. Existing provider ownership and dependencies retained (framework 2.1/2.9); no public surface, feature or catalog change (2.22/2.23.3). Risk-based behavioral and causal controls (2.21/2.23), artifact-only runtime and Balanced default retained (Elsa E2.6, ADR0080 D1/D2). No draft/provisional constitutional rule is relied on.

## Validation

1. Extend existing measurement series to one leaf and measure before changing production source.
2. Add transaction/savepoint interception controls with a real staged participant, initial settings true/false and success/failure/ambiguous commit; verify settings and durable rows using a fresh context.
3. Implement suppression, run focused controls then whole EF suite, provider suites and HTTP integration. Existing fence-renewal and retained-root tests cover shared concurrency semantics.
4. Re-measure exact counts, reverse production change to prove controls fail and counts return, then restore/retest.
5. Independent correctness, standards and acceptance reviews; exact-head hosted gates, normal repository delivery and post-merge verification.
