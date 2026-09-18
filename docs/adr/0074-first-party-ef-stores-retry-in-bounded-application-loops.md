---
status: accepted
date: 2026-09-18
decision_context: Owner decision on issue #1818 after the per-context inventory posted there, closing the retry question ADR 0073 D6 left open.
---

# First-party EF stores retry in bounded application loops, not EF's retrying execution strategy

Status: accepted (2026-09-18). Sipke Schoorstra accepted the recommendation that came out of the
#1818 inventory: keep the application loops everywhere, enable `EnableRetryOnFailure` nowhere.

Program goal: [EF Core Persistence](../program-goals/ef-core-persistence.md).

Tracking:

- [Decide where EF's built-in retry replaces the application-level retry loops #1818](https://github.com/elsa-workflows/elsa-foundation/issues/1818)
- [Program #1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665)
- [#1801](https://github.com/elsa-workflows/elsa-foundation/pull/1801) and [#1816](https://github.com/elsa-workflows/elsa-foundation/pull/1816), the wrapped-transient fixes that prompted the question
- [Transaction topology spike](../reports/ef-core-persistence/transaction-topology-spike.md)

[ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) D6 explicitly did not select a
retry or execution-strategy design and required the topology spike to resolve it. This is the
follow-up ADR that D6 anticipated. It constrains retry only; nothing else in ADR 0073 is reopened.

## Context

First-party EF stores retry lost write races and transient provider conflicts in one bounded
application loop, `EfWriteRetry` in `src/Elsa/Persistence/EntityFramework/`, used by about 20 store
files with per-store attempt budgets. EF's own retrying execution strategy, `EnableRetryOnFailure`,
is enabled nowhere, and that had never been recorded as a decision: it was simply how the code grew.

The question became live because #1801 and #1816 had to teach ten stores that SQL Server's and
PostgreSQL's *non-retrying* default strategies still wrap a transient error in an
`InvalidOperationException` around the `DbUpdateException`. That is work a retrying strategy would
have absorbed, which made it worth asking whether the strategy should be turned on.

Issue #1818 answered that with a per-context inventory rather than an opinion. The inventory is
posted in full on the issue; what follows are the findings that decide the question.

**Every EF `DbContext` under `src/` was inventoried.** There are 14 roots deriving from `DbContext`
(each with four provider-derived subclasses that add no behavior): 13 Elsa contexts and the
Workbench's `OpenIddictIdentityDbContext`, which has no first-party store. `EnableRetryOnFailure` and
`Database.CreateExecutionStrategy` appear nowhere in the repository, so no context has a retrying
strategy today and no block is written to be re-runnable by one.

**Nine of the thirteen Elsa contexts own transactions a retrying strategy forbids.** A retrying
strategy makes `BeginTransaction` throw unless the whole block runs inside
`Database.CreateExecutionStrategy().ExecuteAsync(...)`, which makes that block re-runnable from the
start. The Runtime context alone has 22 `BeginTransaction` sites, Activities Design has 16. Several
are read-then-write units spanning more than one `SaveChanges`, where re-running from the start is
not free: `EfRuntimeCheckpointCommitStore.cs:64-101` reads the replay marker, detaches stale tracked
entities, stages the fence and every participant and commits under one transaction and a root write
lease; `EfActivityUpgradePlanStore.cs:131-218` rechecks the plan binding and its targets inside the
transaction and flushes after each step across two contexts held by `EfSharedTransaction`. Only four
Elsa contexts are transaction-free: `PublishingSnapshotReview`, `StudioPreferences`,
`ExecutionPlacement`, and `IdentityProviderConfiguration`.

**SQLite has no retrying strategy to enable, and it is the provider the container-free EF suites use.**
`Microsoft.EntityFrameworkCore.Sqlite` 10.0.10 exposes no `EnableRetryOnFailure` and no execution
strategy at all, verified against the packaged assembly. SQL Server, PostgreSQL and MySQL each do,
with a default of 6 retries and a 30 second maximum delay. Two consequences follow. First, narrowing
a store's predicate to lost races only, which rule D4 below requires of any context put on EF retry,
would *delete* that store's SQLite transient coverage, because nothing takes over:
`EfRelationalExceptionClassifier.IsTransientWriteConflict` classifies SQLite `SQLITE_BUSY` and
`SQLITE_LOCKED` at `EfRelationalExceptionClassifier.cs:96-99`, and the application loop is the only
mechanism acting on that classification. Second, a test proving "a wrapped transient is retried
without the store's own loop" cannot run on SQLite, so it needs a Testcontainers provider, which puts
it in a suite the fast PR gate excludes by construction (`.github/workflows/ci.yml:84-95`).

**There is no seam to configure it through.** Every module binds its provider through one shared
path, `EfModuleBinding.Apply` calling `EfRelationalProviderBinding.Use`
(`EfModuleBinding.cs:23-43`, `EfRelationalProviderBinding.cs:66-78`). That path builds the provider
options delegate by reflection over exactly two methods, `MigrationsHistoryTable` and
`MigrationsAssembly` (`EfRelationalProviderBinding.cs:195-236`), and both are declared on the shared
relational options builder, which is why one reflected call works for all four engines.
`EnableRetryOnFailure` is not declared there; it is a per-provider method on each engine's own
options builder, and SQLite's does not have it at all. Enabling it means new per-provider reflected
calls in that shared method, a per-provider absence to handle, and a flag plumbed through
`EfModuleBinding` to reach them.

**The two mechanically eligible contexts are each entangled.** `ExecutionPlacement` owns no
transaction and has no multi-step unit, but its 8-attempt budget is pinned by a test *for wrapped
transients specifically* (`EfExecutionPlacementStoreTests.cs:449-461`, alongside the concurrency pins
at `:388` and `:405`). `IdentityProviderConfiguration` likewise owns no transaction, but it writes
through the *same static* `EfIdentityStoreSupport.UnconditionalWrites` and `TransientWrites`
instances as `IdentityIamDbContext` (`EfIdentityStoreSupport.cs:48,53`), and the IAM context owns a
transaction, so its loop must stay. Neither predicate can be narrowed without first undoing the
entanglement.

The remaining two transaction-free contexts, `PublishingSnapshotReview` and `StudioPreferences`,
carry no `EfWriteRetry` at all and no transient classification
(`EfPublicationRecordStore.cs:43`, `EfStudioPreferenceStore.cs:80`). EF retry would add resilience
there rather than replace anything, but only on the three providers that have a strategy, leaving the
addition unexercised by the container-free lane the fast PR gate runs.

The transaction topology spike had already stated the governing constraint: "A provider execution
strategy may only retry the complete operation from a clean owner/context set; it must not replay an
individual context after an unsafe side effect"
(`docs/reports/ef-core-persistence/transaction-topology-spike.md:71-75`).

## Decision

### D1 — One bounded application loop is the first-party retry mechanism

Every first-party EF store that retries a lost write race or a transient provider conflict does so
through `EfWriteRetry`. The store declares its attempt budget, the `EfWriteConflict` kinds or the
predicate it retries, any backoff, and what exhaustion means for its contract. `EfWriteRetry` counts
attempts and enforces one rule of its own: a transient conflict is never retried while a transaction
the caller owns is still open on the store's context, because the provider may already have rolled it
back (`EfWriteRetry.cs:141-142`). The default budget is `EfWriteRetry.DefaultMaxAttempts`, 16
(`EfWriteRetry.cs:21`), unless a test or specification pins a different one.

### D2 — `EnableRetryOnFailure` stays off on every context

No first-party `DbContext` enables a retrying execution strategy, and no first-party code calls
`Database.CreateExecutionStrategy`.

This is recorded as a consequence of four facts, not as a preference:

1. Nine of the thirteen Elsa contexts own transactions the strategy forbids, several of them
   multi-`SaveChanges` read-then-write units that would have to become re-runnable from the start.
2. SQLite, the default development provider and the provider the container-free suites use, has no
   strategy to enable, so a context put on EF retry would be unprotected there while its own loop
   was narrowed away, and the replacement would go unexercised by the fast PR gate.
3. The one shared provider-binding path has no per-provider seam for the option.
4. The two contexts that are otherwise eligible each carry an entanglement, named in D5.

A context that satisfies none of these is not automatically flipped; it goes through D5.

### D3 — Conflict classification walks the exception chain

A store classifies a race by what `SaveChanges` reported, not by the type of the outermost
exception. `EfRelationalExceptionClassifier.IsSaveConflict` walks the chain to the
`DbUpdateException` and classifies from there
(`EfRelationalExceptionClassifier.cs:72-83`), because the *non-retrying* default strategies of SQL
Server and PostgreSQL raise a transient error, deadlock included, as an `InvalidOperationException`
wrapping the `DbUpdateException`, and a store's own persistence boundary may wrap that again.

This is the rule #1801 and #1816 landed, and it is a standing requirement for new stores. A new store
that keys on the outer exception type will look correct on SQLite, where nothing wraps, and will
silently fail to retry on the two providers where it matters. A race raised by a query rather than by
`SaveChanges` carries no `DbUpdateException` and deliberately never matches.

### D4 — Attempts must not stack

Because D2 keeps the strategy off everywhere, no store multiplies its budget by EF's today. If a
future context is ever put on EF retry under D5, enabling the strategy for that context must land in
the same change as narrowing every predicate on it to lost races only, so that transient handling has
exactly one owner. Enabling it while a store still retries transients would multiply attempts, for
example 8 store attempts against EF's 6 retries.

### D5 — Revisit condition

This decision is revisited when any of the following becomes true. Each is a concrete state of the
code, not a matter of judgment.

1. **The `ExecutionPlacement` entanglement is resolved.** Its 8-attempt budget is pinned for wrapped
   transients specifically by `EfExecutionPlacementStoreTests.cs:449-461`. Putting that context on EF
   retry means that test asserts something else or is deleted, which is a deliberate change to a
   pinned contract rather than a side effect of a configuration flag.
2. **The Identity retry objects stop being shared.** `IdentityProviderConfigurationDbContext` and
   `IdentityIamDbContext` write through the same static `EfIdentityStoreSupport.UnconditionalWrites`
   and `TransientWrites` instances (`EfIdentityStoreSupport.cs:48,53`). The IAM context owns a
   transaction and must keep its loop, so splitting those statics per context is the precondition for
   considering the provider-configuration context on its own.
3. **A new context owns no transaction and carries a contended write path.** The two transaction-free
   contexts that exist today are low-traffic leaves with no retry to narrow. A new one that is
   genuinely contended changes the balance, because EF retry would then be replacing something rather
   than adding an untested option.

A change in SQLite's capabilities, specifically a `Microsoft.EntityFrameworkCore.Sqlite` release that
ships a retrying execution strategy, removes fact 2 from D2 and is also grounds to revisit.

## Consequences

Positive:

- One retry mechanism, one classifier, and the same behavior on all four providers, including SQLite.
- Every retry budget stays visible in the store that owns it and exhaustion stays a store contract
  rather than a provider default.
- The transaction-owning stores keep the transactions they own, and no read-then-write unit has to be
  made re-runnable from the start to satisfy a configuration flag.
- The shared provider-binding path stays as narrow as it is.

Costs and risks:

- Transient handling stays the stores' own work. A new store that forgets `Transient` in its
  predicate, or keys on the outer exception type against D3, retries less than it should on SQL
  Server and PostgreSQL and looks correct on SQLite. D3 exists to make that a reviewable rule, not a
  guarantee.
- Two contexts, `PublishingSnapshotReview` and `StudioPreferences`, keep no transient handling at
  all. That is the state before this decision and this decision does not improve it; a store there
  that needs retry adds an `EfWriteRetry` like any other.
- `SecretsDbContext`'s save retries `Concurrency | UniqueKey` but not `Transient`
  (`EfSecretRepository.cs:15`), so a wrapped deadlock is not retried there. That is a store-level gap
  D1 leaves visible rather than one this decision closes.

## Decision record

| Date | State | Record |
|---|---|---|
| 2026-09-12 | Left open | ADR 0073 D6 declined to select a retry or execution-strategy design and required the topology spike to resolve it. |
| 2026-09-17 | Inventory posted | Issue #1818 recorded every EF `DbContext` under `src/`: stores, transaction sites, multi-`SaveChanges` units, budgets, predicates, and test pins. |
| 2026-09-17 | Recommendation | The split was not clean: 9 contexts keep their loop, 2 are eligible but carry no retry to replace, 2 are entangled. Keeping the current approach and recording it was recommended. |
| 2026-09-18 | ADR 0074 accepted | Sipke Schoorstra accepted the recommendation. The two eligible contexts are deliberately not flipped and execution placement is deliberately not moved. |
