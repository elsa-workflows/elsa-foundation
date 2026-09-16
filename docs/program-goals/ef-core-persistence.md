# EF Core Persistence

Status: complete (2026-09-16).

Area: Elsa persistence-family consolidation / four-provider EF Core delivery.

Steward(s): Sipke plus the EF Core persistence control room.

## Purpose

Deliver [Program #1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665): replace every
Elsa-authored legacy persistence implementation with EF Core for SQLite, SQL Server, PostgreSQL,
and MySQL, remove MongoDB, and close only after a requirement-by-requirement audit proves the final
repository and supported compositions.

[Project 49 — EF Core persistence](https://github.com/orgs/elsa-workflows/projects/49) is the sole
execution queue.

[ADR 0073](../adr/0073-ef-core-is-the-only-first-party-persistence-family.md) is the governing
decision. The [completion ledger](../reports/ef-core-persistence-completion-ledger.md) is the audit
boundary. Its entry-level registers cover [storage units and implementations](../reports/ef-core-persistence/storage-unit-register.md),
[tests and backend e2e](../reports/ef-core-persistence/test-and-e2e-register.md),
[repository surfaces](../reports/ef-core-persistence/repository-surface-register.md), and
[legacy requirements](../reports/ef-core-persistence/legacy-requirement-register.md). The
[Zero-EF goal](zero-ef-persistence.md) remains superseded historical provenance.

## Current checkpoint

Complete as of 2026-09-16. Every one of the 95 storage units named in the
[storage-unit register](../reports/ef-core-persistence/storage-unit-register.md) has a merged EF Core
implementation across SQLite, SQL Server, PostgreSQL and MySQL; #1763 (`569903c80`) made EF the default
composition for the Workbench, the Production overlay and the Docker reference host; and #1764
(`a83c41c1c`) removed every first-party Groundwork and MongoDB surface. #1765 and #1766 closed the
entry-level registers.

What this checkpoint does **not** claim:

- **Performance is unmeasured**, by the owner decision recorded below. No benchmark was run and no
  performance claim is made anywhere in this program.
- **Four-provider CI enforcement is partial.** Of the 15 `ef-container-suites` legs, six arm a
  required-provider variable and nine self-skip if a container is unavailable. Those nine were
  executed locally against real PostgreSQL 16, SQL Server 2022 and MySQL 8.4 containers on
  2026-09-16 with zero skips, so the coverage is proven but not continuously enforced.
- **Eight backend e2e suites fail**, each verified to fail identically on a Groundwork-composed host
  built at `16a2f991a`, so they are pre-existing product defects rather than persistence
  regressions. They are tracked as
  [#1761](https://github.com/elsa-workflows/elsa-foundation/issues/1761). One of them,
  `durability/Test-RestartRecovery`, had been misattributed to a stale test harness; re-testing
  showed durable state rehydrates correctly but a post-restart event stimulus matches nothing
  (`resumedCount=0`), and that correction is recorded on the issue.
- **Concurrent migration locking closes by delegation** to EF Core's own
  `IHistoryRepository.AcquireDatabaseLockAsync`, with no first-party parallel-invocation test.
- **Two coverage losses** are named in the test register rather than absorbed: the v1.2
  production-scanner traversal, and an identity concurrency contract suite that went with the
  deleted Groundwork identity project.
- **`IWorkflowActivationAuthority` still exposes no lease or expiry fields**, unchanged by this
  program.

Both constitution files still carry ADR 0042's Groundwork-only direction. Amending a ratified
constitution is an owner decision, not an implementation edit, so it is left open deliberately.

## Performance measurement retired (2026-09-15)

By owner decision under ADR 0073, [#1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668)
removes the performance-measurement infrastructure: the five `benchmarks/` projects, `tools/performance/`,
`tools/ledger/` (the store-performance harness tests and the spec 094 coverage-ledger
validator), the `HTTP workflow performance` and coverage-ledger workflows, the IAM native-plan CI
upload, and the `BenchmarkDotNet` package pin and source mapping. Timing-independent correctness found
in those surfaces was moved into ordinary test projects or mapped to suites that already express it;
the [test register](../reports/ef-core-persistence/test-and-e2e-register.md) records each disposition.
Historical evidence under `docs/reports/evidence/` and in archived reports is retained unchanged, with
the run ADR 0045 cites archived there as well. No benchmark or performance workflow was run for this
change, and no claim is made that performance passed.

## Removal completed (2026-09-16)

The deletion step under #1670 landed as #1764 (`a83c41c1c`). Every first-party Groundwork
and MongoDB source project, test project, tool, workflow job, package pin, NuGet feed mapping,
solution entry and solution filter is gone; 14 source projects and 36 test projects were deleted,
the seven `Groundwork.*` pins, `MongoDB.Driver` and `Testcontainers.MongoDb` were removed from
`Directory.Packages.props`, and the Groundwork feed and `MongoDB.*` source mapping were removed from
`NuGet.config`. The Groundwork-specific architecture guards were replaced by one fail-closed guard,
`RetiredPersistenceFamilyGuardTests`, which refuses any project, package, feed, source file,
directory, solution filter or CI job naming either retired family under `src/`, `tests/`, `tools/`,
`docker/`, `e2e-tests/` or `.github/`. The registers below record what was deleted and what coverage
moved or was dropped.

## Settled boundaries

- EF Core is the only first-party persistence family; OpenIddict remains vendor-owned EF.
- Runtime and distributed Runtime are in scope, but are not presumed to be Secrets-shaped.
- SQLite, SQL Server, PostgreSQL, and MySQL are supported; MongoDB is removed.
- This is a pre-GA clean break with no legacy-to-EF production-data conversion.
- Domain contracts expose no EF, provider SQL, persistence entities, or `IQueryable`.
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
- Every durable unit is a GitHub issue. Existing persistence issues are mapped before they close.
- Every replacement preserves contract, transaction, concurrency, recovery, migration, composition,
  and end-to-end obligations from the ledger.
- A PR is delivered only when its acceptance criteria, current-head review, focused local evidence,
  and unavoidable protected checks are satisfied and it reaches `main`.
- Performance benchmarks, timing workflows, and optional performance CI are never dispatched.

## Completion conditions

Close this goal only when the ledger proves all intended first-party contracts have four-provider EF
coverage; migration artifacts and histories are isolated; Runtime correctness and recovery semantics
are proven; supported compositions use EF by default; active retired-family code, packages,
features, host wiring, tools, workflows, and configuration are absent; relevant rebuilt-host e2e
journeys pass; guards, maps, filters, and documentation describe the final tree; and every residual is
completed, retired by policy, or linked to an owned open issue.
