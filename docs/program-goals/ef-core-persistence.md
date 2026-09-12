# EF Core Persistence

Status: active.

Area: Elsa persistence-family consolidation / four-provider EF Core delivery.

Steward(s): Sipke plus the EF Core persistence control room.

## Purpose

Deliver [Program #1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665): replace every
Elsa-authored Groundwork persistence implementation with EF Core for SQLite, SQL Server, PostgreSQL,
and MySQL, remove MongoDB, and close only after a requirement-by-requirement audit proves the final
repository and supported compositions.

[Project 49 — EF Core persistence](https://github.com/orgs/elsa-workflows/projects/49) is the sole
execution queue.

[ADR 0073](../adr/0073-ef-core-is-the-only-first-party-persistence-family.md) is the governing
decision. The [completion ledger](../reports/ef-core-persistence-completion-ledger.md) is the audit
boundary. The [Zero-EF goal](zero-ef-persistence.md) remains superseded historical provenance.

## Settled boundaries

- EF Core is the only first-party persistence family; OpenIddict remains vendor-owned EF.
- Runtime and distributed Runtime are in scope, but are not presumed to be Secrets-shaped.
- SQLite, SQL Server, PostgreSQL, and MySQL are supported; MongoDB is removed.
- This is a pre-GA clean break with no Groundwork-to-EF production-data conversion.
- Domain contracts expose no EF, Groundwork, provider SQL, persistence entities, or `IQueryable`.
- Performance measurement is retired by owner policy. Timing-independent correctness remains.
- Delivery uses coherent PRs to `main`, one authoritative Project queue, and one merge lane.

## Initial governance objective

At program creation, [ADR issue #1666](https://github.com/elsa-workflows/elsa-foundation/issues/1666)
records ADR 0073, reconciles historical governance, establishes the initial ledger, and creates the
program control surface. After that issue closes, Project 49 is authoritative for the one current
active leaf; this page does not pin a completed issue as active.

## Dependency sequence

1. [Governance, verified inventory, and architecture #1673](https://github.com/elsa-workflows/elsa-foundation/issues/1673),
   including the [completion ledger #1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671)
   and all four proving spikes: [#1675](https://github.com/elsa-workflows/elsa-foundation/issues/1675),
   [#1674](https://github.com/elsa-workflows/elsa-foundation/issues/1674),
   [#1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669), and the cross-Epic
   [hard Runtime proving slice #1676](https://github.com/elsa-workflows/elsa-foundation/issues/1676),
   which remains a child of Runtime Epic #1672 but gates step 2.
2. [Shared EF foundation and independent modules #1667](https://github.com/elsa-workflows/elsa-foundation/issues/1667).
3. [Runtime and distributed Runtime #1672](https://github.com/elsa-workflows/elsa-foundation/issues/1672).
4. [Design, Publishing, import, and Dashboard #1677](https://github.com/elsa-workflows/elsa-foundation/issues/1677).
5. [Default flip, removal, and final audit #1670](https://github.com/elsa-workflows/elsa-foundation/issues/1670),
   including [performance-measurement retirement #1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668).

Later waves remain progressively elaborated. Implementation-ready Tasks are created only after their
architecture, dependencies, semantics, acceptance criteria, and focused validation are stable.

## Program controls

- Program #1665 and its Project are the sole scheduling queue; this page summarizes rather than
  duplicates the backlog.
- Exactly one leaf issue represents the control room's current objective. Independent workers may
  own explicitly assigned, non-overlapping ready children.
- Every durable unit is a GitHub issue. Existing Groundwork issues are mapped before they close.
- Every replacement preserves contract, transaction, concurrency, recovery, migration, composition,
  and end-to-end obligations from the ledger.
- A PR is delivered only when its acceptance criteria, current-head review, focused local evidence,
  and unavoidable protected checks are satisfied and it reaches `main`.
- Performance benchmarks, timing workflows, and optional performance CI are never dispatched.

## Completion conditions

Close this goal only when the ledger proves all intended first-party contracts have four-provider EF
coverage; migration artifacts and histories are isolated; Runtime correctness and recovery semantics
are proven; supported compositions use EF by default; active Groundwork/MongoDB code, packages,
features, host wiring, tools, workflows, and configuration are absent; relevant rebuilt-host e2e
journeys pass; guards, maps, filters, and documentation describe the final tree; and every residual is
completed, retired by policy, or linked to an owned open issue.
