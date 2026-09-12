---
status: accepted
date: 2026-09-12
decision_context: Owner-ratified all-EF direction recorded by Sipke Schoorstra in Program #1665 and ADR issue #1666 after the completed Secrets pilot and accepted bounded ADR 0072.
---

# EF Core is the only first-party persistence family

Status: accepted (2026-09-12). Sipke Schoorstra ratified the all-EF product direction. Review of
this text verifies fidelity to that decision; it does not reopen the selected destination.

Program goal: [EF Core Persistence](../program-goals/ef-core-persistence.md).

Tracking:

- [Program #1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665)
- [EF Core persistence Project 49](https://github.com/orgs/elsa-workflows/projects/49)
- [ADR fidelity and governance reconciliation #1666](https://github.com/elsa-workflows/elsa-foundation/issues/1666)
- [Bounded inventory predecessor #1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654)
- [Completion ledger](../reports/ef-core-persistence-completion-ledger.md)

This decision supersedes:

- [ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md), which
  selected Groundwork as Elsa Foundation's only first-party family;
- [ADR 0065](0065-groundwork-persistence-targets-are-named-and-lanes-bind-to-them.md), which defined
  Groundwork-specific target and lane topology; and
- [ADR 0072](0072-ef-first-relational-persistence-with-provider-derived-contexts.md), which accepted
  a bounded EF lane after the Secrets pilot while retaining Groundwork elsewhere.

Their evidence and historical reasoning remain intact.

## Context

The Secrets pilot proved a bounded technical recipe: provider-neutral domain contracts,
provider-derived EF contexts and migrations, module-specific migration history, host and CShells
initialization, runtime and out-of-process migration modes, provider checks, and explicit composition
ownership. It did not establish repository-wide product policy or prove Runtime workloads,
cross-module transactions, MySQL, MongoDB parity, or complete Groundwork removal.

ADR 0072 used that evidence conservatively and admitted EF only for separately qualified,
shape-simple relational modules. The owner has since selected a broader product destination: EF Core
will replace every Elsa-authored Groundwork persistence implementation, including Runtime and
distributed Runtime. This is a product choice, not a claim that the Secrets shape applies everywhere
or that EF owns less code.

The repository is pre-GA. A clean implementation break is acceptable, but behavioral correctness is
not optional. Groundwork's current implementations and tests are evidence sources for domain
semantics until the corresponding EF replacement proves those semantics and the deletion gate is
met.

## Decision

### D1 — EF Core is the only first-party persistence implementation family

Elsa Foundation will ship EF Core implementations for every first-party persistence contract. The
supported relational engines are SQLite, SQL Server, PostgreSQL, and MySQL.

Groundwork is a migration source and historical evidence source, not a retained first-party product
lane. MongoDB support is removed rather than replaced. No new first-party persistence family is
introduced beside EF Core.

### D2 — Domain contracts remain provider- and persistence-neutral

Domain contracts, persistence-facing domain models, and observable invariants do not expose
`DbContext`, `IQueryable`, EF entities, provider SQL, provider packages, or Groundwork types. EF
implementations live in provider-suffixed persistence features and honor domain-owned contracts.

OpenIddict remains a vendor-owned EF Core boundary. Elsa does not combine the OpenIddict `DbContext`
with Elsa IAM contexts, and removing Elsa-authored Groundwork does not turn vendor persistence into
an Elsa domain contract.

### D3 — The Secrets recipe is the starting point, not a universal Runtime design

The following proven parts of ADR 0072 remain the default for ordinary relational modules:

- a module-owned relational model;
- provider-derived contexts and isolated migration/snapshot sets;
- module-specific migration-history ownership;
- host and shell-scoped initialization;
- runtime apply/validate and out-of-process apply over the same artifacts;
- explicit provider/artifact pairing and concurrent-migration safety; and
- provider-specific physical mechanics behind fixed provider-neutral behavior.

Runtime, distributed Runtime, publishing, import, and other operational workloads may need different
EF-internal mechanics. They must preserve their domain semantics naturally in EF and provider-specific
SQL rather than mechanically translating Groundwork APIs.

### D4 — Runtime and distributed Runtime are in scope under their own proof stream

Runtime replacement includes workflow execution and checkpoint state, queues and poison handling,
durable timers, bookmarks, triggers and schedules, dispatch and redrive, outbox processing, recovery
and liveness, leases and fencing, idempotency, activation authority, alterations, incidents, holds
and test scopes, executable/template materialization, distributed placement, durable command
transport, and crash/restart behavior.

Before broad Runtime implementation, a representative hard vertical slice must prove several of
conditional claims or leases, fencing, compare-and-delete, idempotency, checkpoint/outbox atomicity,
crash recovery, concurrent callers, SQLite contention, and provider-specific conflict handling.

### D5 — Cutover is a pre-GA clean break with evidence-gated deletion

No Groundwork-to-EF production-data conversion, mixed-version data compatibility, or production
rollback data path is required. That clean-break decision removes data-conversion work; it does not
remove the need to prove fresh-database installation, migration failure behavior, host composition,
or domain correctness.

Normally a module lands its EF feature as opt-in, proves provider contracts and migrations, flips the
supported default in a coherent later change, and deletes the Groundwork adapter only after the
replacement and operational failure/revert evidence required by its issue are complete. This does
not require a Groundwork-to-EF production-data rollback path. A same-change flip and deletion requires
equivalent failure and revert evidence and an explicit control-room finding that separation adds no
safety.

### D6 — Transaction and migration topology must be proved before multiplication

This decision does not select a shared `DbConnection`, `DbTransaction`, `Database.UseTransaction`,
ambient unit of work, retry, savepoint, or context-factory design. A cross-module transaction spike
must define ownership, construction, disposal, lifetime, commit/rollback, partial failure, execution
strategies, isolation, shell and tenant boundaries, migration ordering, split-database refusal, and
SQLite locking behavior before shared-transaction implementations begin.

A migration-lifecycle spike must prove provider/artifact pairing, isolated module/provider histories,
shell-scoped initialization, out-of-process apply and validation, concurrent locking, pending-model
detection, fresh installation, and fail-before-activation behavior. Issue #1657 is resolved before
concurrent `dotnet ef` usage is multiplied.

### D7 — Performance measurement is retired by policy

Performance benchmarks, timing measurements, performance budgets, performance gates, and their
workflows are selected for complete retirement. They are not run locally or remotely from this
decision onward, and this decision makes no claim that historical performance obligations passed.
The tracked source and documentation removal remains pending under
[#1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668).

Historical results remain truthful and may be archived. Before performance infrastructure is removed,
every timing-independent correctness obligation it carries is moved to an EF-neutral or EF-owned
suite. Concurrency, leases, fencing, idempotency, bounded queries, retention, atomicity, deadlock
behavior, crash recovery, and failure handling are correctness requirements, not performance gates.

### D8 — Delivery proceeds through one program queue and coherent PRs to `main`

Program #1665 and its GitHub Project are the authoritative execution queue. One leaf issue is the
control room's active objective; explicitly assigned non-overlapping workers may own other ready leaf
issues. Material uncertainty is resolved by bounded spikes before implementation-ready Tasks are
created. Each coherent PR targets `main`; there is no long-lived integration branch.

The [completion ledger](../reports/ef-core-persistence-completion-ledger.md) is the audit boundary.
Groundwork or MongoDB is not declared removed from a broad text search alone.

## Constitution analysis

No constitutional amendment is required for the selected package direction. Framework §2.20 permits
a provider-specific feature module to depend on its provider implementation. A
`*.Persistence.EntityFrameworkCore` feature is provider-family-specific, while its domain contracts
remain in provider-neutral `.Core` surfaces. `Elsa.Persistence.EntityFramework` contains real shared
EF policy rather than an empty provider-agnostic umbrella. If implementation later violates those
conditions, that concrete conflict must follow the constitution proposal and ratification process;
this ADR does not pre-authorize an exception.

The Elsa constitution still contains EF package examples written for an earlier implementation
state. The now-historical
[Zero-EF constitution review](../reports/zero-ef-constitution-review.md) records the opposite former
direction rather than current findings. Updating the constitution examples is a separate governed
propagation task, not an implied amendment in this decision.

## Decisions requiring reconciliation

- [ADR 0066](0066-reusable-activity-publication-orders-writes-instead-of-one-transaction.md) remains
  accepted evidence for the current Groundwork topology. Reconcile its ordered-write/redrive model
  only after the EF cross-module transaction spike proves whether one transaction replaces it.
- [ADR 0052](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md) preserves
  the provider-neutral checkpoint-atomic and at-least-once semantics; its Groundwork prototype
  references require EF proof.
- [ADR 0062](0062-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md) names
  Groundwork as the only durable Execution Evidence provider and requires a replacement decision.
- Groundwork-specific statements in Runtime, diagnostics, serialization, program-goal, spec, and
  evidence artifacts remain truthful history until their owning issues reconcile them.

## Required architecture spikes

Before broad implementation, Program #1665 must resolve:

1. [MySQL feasibility #1675](https://github.com/elsa-workflows/elsa-foundation/issues/1675):
   the exact EF Core 10-compatible provider and its migrations, types, collations,
   normalization, JSON, concurrency, conflicts, conditional mutations, transactions,
   cross-context enlistment, Testcontainers, and fresh-install behavior;
2. [cross-module transaction topology #1674](https://github.com/elsa-workflows/elsa-foundation/issues/1674);
3. [migration lifecycle and concurrent tooling #1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669),
   resolving [#1657](https://github.com/elsa-workflows/elsa-foundation/issues/1657) first; and
4. [a representative hard Runtime vertical slice #1676](https://github.com/elsa-workflows/elsa-foundation/issues/1676).

Spike conclusions are recorded in follow-up ADRs or explicit amendments when they constrain durable
architecture. They do not reopen D1's EF-only product destination.

## Consequences

Positive:

- Elsa has one first-party persistence family and one four-provider relational support promise.
- Domain semantics and provider-specific enforcement remain explicit and testable.
- MongoDB and the Groundwork framework surface leave the active product.
- Performance infrastructure no longer consumes delivery effort or masquerades as correctness.

Costs and risks:

- EF adds substantial owned model, migration, provider, and test code; the Secrets pilot disproved a
  general code-reduction premise.
- Runtime and shared-transaction correctness must be re-proved without Groundwork's typed primitives.
- Four provider-specific migration sets multiply review and maintenance cost.
- MySQL provider compatibility and provider-specific behavior are unresolved until the spike.
- Removing MongoDB is a deliberate support reduction.

## Decision record

| Date | State | Record |
|---|---|---|
| 2026-09-11 | Bounded direction authorized | The Secrets pilot supported revising ADR 0072 toward an admitted EF relational lane. |
| 2026-09-12 | Bounded direction accepted | ADR 0072 recorded the reviewed bounded lane and retained Groundwork for excluded workloads. |
| 2026-09-12 | Superseded by owner decision | Sipke Schoorstra selected EF Core as the only first-party family, included Runtime, removed MongoDB and data conversion, and retired performance measurement. |
| 2026-09-12 | ADR 0073 accepted | This document records that owner-ratified direction; exact-head review is a fidelity gate. |
