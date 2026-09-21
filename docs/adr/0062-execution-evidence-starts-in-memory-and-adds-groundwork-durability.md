---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution Evidence starts in memory and adds EF Core durability

> **Reconciled to EF Core on 2026-09-16 by Sipke Schoorstra.** This ADR was accepted in proposal form
> naming Groundwork as the durable provider. [ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md)
> made EF Core the only first-party persistence family, and #1764 removed Groundwork from the
> repository, so the original plan named a family that no longer exists. The decision below is
> unchanged in substance: evidence starts in memory and gains a durable provider later. Only the
> provider identity changed. The filename keeps its original slug because ADR filenames are stable
> links in this repository.
>
> **Status of the work, as of 2026-09-16.** Neither half of the planned module layout was built as
> written. `src/essentials/Workflows/ExecutionEvidence/` is a single project rather than the `.Core` /
> implementation / `.Api` split described below, and the only store is
> `InMemoryExecutionEvidenceStore`. No durable execution-evidence provider exists, for any family.
> This ADR remains `proposed`: it describes intent, not shipped behavior.

## Context

The first useful QA slice needs a low-friction, single-process collector and API. Crash-safe and
distributed verification later requires evidence and pending delivery work to share the runtime
checkpoint's physical transaction. The repository separately requires EF Core to be the only
first-party durable persistence implementation family (ADR 0073).

Combining contracts, capture, HTTP endpoints, and a relational provider in one project would give
them one dependency envelope and make the initial slice unnecessarily heavy.

## Decision

The planned module sequence is:

- `Elsa.Workflows.ExecutionEvidence.Core` owns provider-neutral contracts, envelope models, the
  baseline catalog, session models, and store abstractions;
- `Elsa.Workflows.ExecutionEvidence` owns capture/session services, source-domain integration
  adapters, host-composition features, and a process-local in-memory implementation;
- `Elsa.Workflows.ExecutionEvidence.Api` owns HTTP session, query, wait, integrity, and retention
  endpoints; and
- `Elsa.Workflows.ExecutionEvidence.Persistence.EntityFrameworkCore` later supplies the only
  first-party durable provider, across the four supported providers (SQLite, SQL Server, PostgreSQL,
  MySQL) and under the shared module-migration policy in `Elsa.Persistence.EntityFramework`.

The in-memory implementation must preserve commit visibility, identity, ordering, and explicit
integrity semantics but does not claim completeness across process failure. The runtime's EF Core
checkpoint store records the complete opaque evidence intent with the associated checkpoint. The
Execution Evidence EF Core provider materializes that intent idempotently into its durable query
store; a separate best-effort intent created after checkpoint commit is not conformant.

## Considered options

- Requiring a durable provider in the first vertical slice was rejected because it delays contract
  and API validation and makes the initial QA composition heavier than necessary.
- Treating the in-memory store as crash-durable was rejected because process loss invalidates that
  guarantee.
- Shipping another first-party durable provider was rejected because it conflicts with the accepted
  Foundation persistence direction.
- Combining API and persistence dependencies with the core implementation was rejected because the
  modules have different dependency envelopes and activation needs.

## Consequences

- The first slice can prove end-to-end capture and remote querying in one process.
- Durable and distributed completeness is an explicit later feature, not an accidental claim of the
  in-memory adapter.
- Store conformance tests must run against both in-memory and EF Core implementations, with
  additional crash/failover scenarios for the durable one, and must prove all four providers.
- The validated generic checkpoint-enricher and post-commit intent seams are sufficient; EF Core
  durability does not require a new Runtime persistence-participation contract.

## Linked decisions

- [EF Core is the only first-party persistence family](0073-ef-core-is-the-only-first-party-persistence-family.md) — the governing decision
- [Elsa Foundation ships only Groundwork persistence implementations](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) — superseded; the direction this ADR was originally written against
- [Execution Evidence integrates through domain-owned adapters](0055-execution-evidence-integrates-through-domain-owned-adapters.md)
- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution Evidence checkpoint-outbox prototype findings](../reports/runtime-execution-evidence-seam-prototype.md)
