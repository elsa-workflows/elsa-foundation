# Feature Specification: Post-Finalization Backfill

**Feature Branch**: `claude/2093-specs-b7-b9`
**Created**: 2026-09-27
**Status**: Implemented — B9 ([#2116](https://github.com/elsa-workflows/elsa-foundation/issues/2116)), proven on a synthetic family at versions 1 to 3, since every first-party family is still at one version; no first-party family has a rewriter yet (see the 2026-09-29 note).
**Input**: Workstream B9, [issue #2116](https://github.com/elsa-workflows/elsa-foundation/issues/2116), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Rows that
are written once and never written again are never reached by "upgrade on next write", so a family could never retire
an old version, and a feature that queries new fields could never know that every row has them. Once a family's new
version is finalized, the runtime runs a backfill that upgrades every row below it through the upcaster chain, in
idempotent batches, and records when it finishes. That finish record is the completeness proof spec 182 reads.

Decisions of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
("The first consumers": the schema version gate, which lets a version leave the readable set only once a scan proves no
row at it remains; and the 2026-09-27 amendment to "Features that need the new data wait for finalization", which adds
completeness, proved by this backfill) and
[ADR 0038](../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)
(executables are content-addressed). Both ADR 0078 and ADR 0077 are accepted through B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096), PR #2118). The owner's decisions on #2093
(2026-09-27) bind this spec: Q4 and Q15 (this workstream, and its finish record as the completeness proof) and Q5
(an executable is never rewritten in place).

Companion specs: [spec 180](../180-schema-upcaster-chain/spec.md) (B4) supplies the chain and the write path this
backfill writes through, and assigns it the retirement scan (FR-024). [Spec 181](../181-schema-finalization-gate/spec.md)
(B5) supplies the finalization record this spec's finish record lives in, counted members, and each host's observed
finalized version. [Spec 182](../182-dormant-features-until-finalization/spec.md) (B6) reads the finish record (FR-005).
[Spec 183](../183-cluster-membership/spec.md) (B1 to B3) supplies the fleet view and readability report the settle
condition reads.

## Terms

Spec 180's, spec 181's, spec 182's and spec 183's Terms apply, notably schema family, stamp, readable set, upcaster,
write version, finalized version, observed finalized version, counted member, completeness and readability report. In
addition:

- **Backfill**: the runtime process this spec defines, which upgrades a family's rows below its finalized version.
- **Finish record**: the part of a family's finalization record, per database, that says up to which version the
  family is complete (FR-015). Spec 182 and the [Elsa glossary](../../docs/glossary/elsa.md) ("Completeness") call it
  B9's finish record.
- **Completion version**: the version a finish record names. No row of the family below it remains in that database.
- **Content-addressed row**: a row whose identity is derived from its stored content, so rewriting it would change or
  forge its identity: a workflow executable or an executable activity template (ADR 0038). A family declares which of
  its tables hold them (FR-010b).
- **Rewriter**: a family's operation that reads one row through the family's read path and writes it back through its
  write path (FR-004).
- **Upgrade pass**, **verification pass**: the two scans of one run (FR-005, FR-013). The first rewrites; the second
  proves nothing is left.
- **Settle condition**: the point after which no counted member can still be writing below the finalized version
  (FR-012).
- **Straggler**: a row written below the completion version after the verification pass examined its place, by a
  writer that had not yet observed the finalization (FR-018).

## Current state

- **Rows move forward only when they are written.** Spec 180's FR-014 upgrades a row on its next write. Nothing
  rewrites a row nobody writes, so an executable, a publication receipt or a finished run's state keeps its stamp
  forever.
- **The post-migration seam exists, and does something else.** `IEfPostMigrationAction` declares an audited data step
  on `[EfModule]`. `EfModuleMigrator` audits every declared action at `LifecyclePhase.Prepare`, under both migrate
  policies, and refuses the module while an audit reports it required. `RunAsync` is reached only from
  `dotnet elsa persistence post-migrate`, out of process, with a `DbContext` and no dependency injection. Secrets'
  projection reindex is the one instance.
- **Content-addressed tables sit inside a larger family.** The `RuntimeArtifact` family stamps executables, executable
  activity templates and their hash claims, and also the per-publish source references and coordination rows.
- **The finalization record is specified, not built.** Spec 181's FR-001 and FR-002 define it, in every EF module's
  4.0 baseline ([#2120](https://github.com/elsa-workflows/elsa-foundation/issues/2120)). Spec 182's FR-005 reads "B9's
  finish record", which does not exist yet.

The inventory, with paths, is in [research.md](./research.md).

## Why this is not a post-migration action

#2116 rules out `IEfPostMigrationAction`. There are four reasons, and the first is enough on its own.

1. **It would deadlock.** `EfModuleMigrator` refuses a module at Prepare while any of its declared actions audits as
   required. A backfill's audit would report "required" whenever a row below the finalized version exists, which is
   the normal state right after finalization. But the backfill may run only after finalization, and finalization needs
   the module active: an evaluator must be a counted member with the module active (spec 181, FR-005), and a single
   host finalizes during that same Prepare-phase check (spec 181, FR-021). So the audit keeps the module from
   activating, the module being inactive keeps finalization from happening, and without finalization the backfill
   may never run.
2. **It is tied to the wrong event.** A post-migration action follows a migration. The backfill follows a
   finalization, a fleet event that happens with no migration at all, possibly days later.
3. **It has no services.** An action takes a `DbContext` alone, because the `dotnet elsa persistence` worker has no
   shell container. The backfill must read and write through each family's store, which needs the shell's services:
   serializers, the payload codec and the stores' integrity rules.
4. **It never runs by itself.** An action runs only when an operator invokes `post-migrate`. Completeness gates
   features (spec 182), so it has to come about without an operator.

## The invariant and how it holds

**Invariant.** While a finish record names completion version C for a family in a database, no row of that family
below C exists there that the runtime has not reported. A straggler is found by the audit, reported as critical, and
withdraws the finish record until a new verification pass succeeds.

"Reported" is the honest limit. No lease can stop a writer that chose its write version before observing the
finalization and then stalled before committing (spec 183, "Failure modes": a stall between a check and a write
cannot be closed by any lease). The mechanisms below make such a row rare, and make it loud when it happens, so a
feature that depends on completeness is never left answering from part of the rows without anything saying so.

It holds through seven mechanisms, each tested separately (Success Criteria):

1. **Nothing runs before finalization** (FR-002). A row written at a version before it is finalized is exactly what spec
   180's FR-013 forbids.
2. **The backfill is an ordinary writer** (FR-004). It reads through the family's read path, so the chain and the
   integrity clauses apply, and writes through the write path, so the stamp is the write version and every write is a
   compare-and-set on the row's revision.
3. **Content-addressed rows are never rewritten, and a family that holds any below V is never recorded complete at V**
   (FR-010a, FR-011a). Completeness is therefore never claimed over rows nothing may upgrade.
4. **The settle condition comes before verification** (FR-012). Every counted member has reported observing the
   finalized version, and a margin has passed for writes already in flight.
5. **Completion is recorded only from a verification pass that found nothing** (FR-013, FR-014), by compare-and-set.
6. **The audit keeps looking** (FR-018), and a straggler withdraws the finish record.
7. **A host that cannot read everything a finish record allows refuses the module** (FR-020). An upcaster dropped too
   early fails loudly at activation instead of at the first old row.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Write-once rows reach the finalized version, and the family is recorded complete (Priority: P1)

The fleet finalizes version 2 of the `PublishingLedger` family. Thousands of publication receipts are stamped `1` and
will never be written again. The backfill rewrites them in batches through the family's chain and write path. When the
settle condition holds and a verification pass finds no receipt below `2`, it records the family complete at `2`.

**Why this priority**: This is #2116's purpose, and without it neither retirement (spec 180, FR-024) nor completeness
(spec 182, FR-005) is possible.

**Independent Test**: Seed rows at version 1, finalize version 2, run the backfill to completion, and assert every row
is stamped `2`, reads equal to the version-2 fixture, and the finish record names `2`.

**Acceptance Scenarios**:

1. **Given** version 2 finalized and rows at `1`, **When** the backfill runs, **Then** every rewritable row is stamped
   `2`, and each rewritten row's domain value equals what reading it before the rewrite returned.
2. **Given** the upgrade pass is done and the settle condition holds, **When** the verification pass finds no row below
   `2`, **Then** the finish record names `2`, with the pass's start and end and the member that ran it.
3. **Given** version 2 is not yet finalized, **When** the backfill's task runs, **Then** it writes nothing.

---

### User Story 2 - A feature that queries new data leaves dormancy only once the family is complete (Priority: P1)

A feature lists executions by a projection column introduced at version 2 and declares that it needs completeness
(spec 182, FR-005). It stays dormant after finalization, and its reason says existing records are still being upgraded.
Once the finish record names `2` and the host has observed it, the feature's query is served, from every row.

**Why this priority**: This is the completeness proof spec 182 depends on. A query answered from the rows that happen
to have been rewritten looks like success and is not.

**Independent Test**: Spec 182's User Story 6, with the backfill running instead of a stub.

**Acceptance Scenarios**:

1. **Given** version 2 finalized and the backfill still running, **When** the query arrives, **Then** it is refused as
   dormant with the upgrade-in-progress reason.
2. **Given** the finish record names `2` and the host has refreshed it, **When** the query arrives, **Then** it is
   served, and its answer includes every row.

---

### User Story 3 - A crash in the middle loses nothing, and a rerun repeats nothing harmful (Priority: P1)

The host running the backfill is killed halfway through. Another host starts the backfill for the same family and runs
it to completion. Rows the first host already rewrote are skipped, rows it was writing when it died are either written
or not, and no row is written twice with different content.

**Why this priority**: #2116 requires the backfill to be idempotent and batched. A backfill that could not survive a
restart would never finish on a large database.

**Independent Test**: Kill the backfill between batches and within a batch, run it again on another host, and compare
the table with a run that was never interrupted.

**Acceptance Scenarios**:

1. **Given** a run killed after some batches, **When** a new run starts, **Then** it skips rows already at `2` and
   finishes the rest.
2. **Given** two hosts running the backfill at once, **When** both finish, **Then** each row was written at most once
   per upgrade, later writes by either lost the compare-and-set harmlessly, and the finish record was written once.
3. **Given** a live write to a row between the backfill reading it and writing it, **When** the backfill writes,
   **Then** its compare-and-set fails, it reads the row again, and finds it already at `2`.

---

### User Story 4 - A straggler after completion is found, reported and withdraws completeness (Priority: P1)

After the finish record names `2`, a host that had stalled before it observed the finalization commits a new receipt
stamped `1`. The next audit finds it, rewrites it, reports a critical Attention item naming the family and the count,
and withdraws the finish record. Features that need completeness go dormant again until a new verification pass
records `2` once more.

**Why this priority**: This is the case that looks like success. A finish record that stood while a row below it
existed would let a completeness-dependent query miss that row, with nothing saying so.

**Independent Test**: Record completion, insert a row at `1` directly through a store whose observed write version is
held at `1`, run the audit, and assert the report, the withdrawal and the dormant feature. Then run verification again
and assert the record returns.

**Acceptance Scenarios**:

1. **Given** a finish record at `2` and a row at `1`, **When** the audit runs, **Then** it rewrites the row, reports
   critical, and the finish record no longer names `2`.
2. **Given** that withdrawal, **When** a completeness-dependent query arrives after the host's next refresh, **Then** it
   is refused as dormant.
3. **Given** a finish record at `2` and no row below it, **When** the audit runs, **Then** it reports nothing and the
   record is unchanged.

---

### User Story 5 - Executables are never rewritten (Priority: P2)

The `RuntimeArtifact` family finalizes version 2 for a change to its source-reference rows. The backfill rewrites the
source references and never touches an executable or an activity template. Executables stamped `1` remain, so the
family is not recorded complete at `2`, and its status says why: content-addressed rows below `2` remain, and nothing
may upgrade them.

**Why this priority**: Q5 on #2093: an executable's format never changes in place. Rewriting one could change the
identity its hash proves.

**Independent Test**: Seed executables and source references at `1`, finalize `2`, run the backfill, and assert the
executable rows are byte-identical and the family's status names them.

**Acceptance Scenarios**:

1. **Given** executables at `1`, **When** the backfill runs, **Then** their rows are unchanged, byte for byte.
2. **Given** the same, **When** the upgrade pass finishes, **Then** no finish record names `2`, and the status says
   content-addressed rows below `2` remain.
3. **Given** a feature that declares completeness on a family with content-addressed tables, **When** the build runs,
   **Then** it fails, because that feature could never leave dormancy.

---

### User Story 6 - A release that dropped an old upcaster refuses to activate where rows may still need it (Priority: P2)

A release drops the upcaster from version 1 of a family (spec 180, FR-024), so its readable set starts at `2`. It is
deployed against a database whose finish record for that family still names `1`. The host refuses the family's EF
module, naming the family, the completion version, its readable set and the remedy.

**Why this priority**: Spec 180 leaves version retirement as a release obligation. Turning the finish record into an
activation check means a skipped obligation fails at startup, not at the first old row a customer's workflow touches.

**Independent Test**: A family whose build reads {2, 3}, against finish records naming `1` and then `2`.

**Acceptance Scenarios**:

1. **Given** a finish record naming `1` and a readable set {2, 3}, **When** the host activates the module, **Then** it
   refuses, as spec 181's FR-015 refuses an unreadable finalized version.
2. **Given** a finish record naming `2`, **When** the same host activates, **Then** it activates normally.

---

### User Story 7 - A single host backfills after its own finalization (Priority: P2)

A single host with the in-process membership provider is upgraded to version 2 and finalizes at activation (spec 181,
FR-021). The backfill starts once the shell is running. The settle condition holds at once, because the host is the
only counted member and has observed its own finalization.

**Why this priority**: Non-clustered hosting pays nothing for this program (#2093, Acceptance), and its window before
completeness should last only as long as the backfill itself.

**Independent Test**: Start a single host at version 2 against rows at `1`, and assert the finish record names `2`
after one run with no membership table.

**Acceptance Scenarios**:

1. **Given** the in-process provider, **When** the upgrade pass ends, **Then** verification starts after the settle
   margin with no wait for other members.
2. **Given** the same, **When** the host has run, **Then** no membership table exists (spec 183, FR-018).

---

### Edge Cases

- **A family whose chain has one version.** Nothing is below its finalized version. Its finish record names that
  version when the record is created (FR-016).
- **Finalization advances during a run.** Rows are written at the host's write version, which may be newer than the
  version the run started for. The run completes for the newest finalized version its verification pass proves.
- **Several versions finalized at once.** One run upgrades every row below the newest one, through the whole chain.
- **A row with no stamp, or a stamp outside the host's readable set.** It is not rewritten. It is reported as skew
  (spec 180, FR-007) and blocks completion until resolved.
- **A row an upcaster fails on.** It is reported as corruption, naming the family, the table and the row, and blocks
  completion. It is never skipped silently.
- **A row deleted during a run.** The rewrite's compare-and-set finds nothing and moves on.
- **A very large table.** The run takes as long as it takes, in throttled batches. Completeness-dependent features stay
  dormant meanwhile, with a reason that says records are still being upgraded.
- **A member that stops reporting its observed version.** It is still counted until its entry expires or leaves, so the
  settle condition waits for it (spec 181, Terms, "Counted member"). That delays completion and never hastens it. A
  member whose module is not active is the exception, since it writes none of the family's rows (FR-012, amended
  2026-09-30).
- **One host serving two databases.** Each database has its own finalization and finish records, and its own run.
- **A database restored from a backup.** The finish record is restored with the rows it describes, so the two agree.
- **A database written only by releases before the gate.** Its rows carry the baseline version (spec 180,
  Assumptions), so the first record names that version for both finalization and completion (FR-016).

## Requirements *(mandatory)*

### Functional Requirements

**When it runs**

- **FR-001**: The backfill MUST run inside the runtime, as a recurring background task in every shell where the
  family's EF module is active, with the shell's services. It MUST NOT be an `IEfPostMigrationAction` ("Why this is not
  a post-migration action").
- **FR-002**: For a family in a database, the backfill has work when the host's observed finalized version is later
  than the finish record's completion version, or the record names no completion. It MUST NOT write any row before
  the host has observed the version it upgrades to as finalized.
- **FR-003**: Rows MUST be written at the host's write version at the moment of each write (spec 180, FR-013). If
  finalization advances during a run, later rows are written at the newer version, and the run completes for the
  newest finalized version its verification pass proves (FR-013).

**Rewriting**

- **FR-004**: Each schema family MUST supply a rewriter in its owning EF module's assembly, beside its store code, as
  its upcasters are (spec 180, FR-003). The rewriter reads one row through the family's read path, so the version check,
  the integrity clauses for the stamped version and the chain all apply (spec 180, FR-006 to FR-012), and writes it back
  through the family's write path, so projections introduced at the new version are computed, the stamp is the write
  version and the write is a compare-and-set on the row's revision (spec 180, FR-013 to FR-018). Unlike an upcaster it
  MAY use injected services, because it runs in the shell.
- **FR-005**: The upgrade pass MUST select, in bounded batches ordered by a stable key, the rows of each rewritable
  table of the family whose stamp is one of the chain's versions before the finalized one. It matches stamps as opaque
  labels taken from the family's declaration, and never parses or compares them (spec 180, FR-004).
- **FR-006**: A row whose stamp is missing, or outside the host's readable set, MUST NOT be rewritten. It is reported as
  skew and blocks completion. A row an upcaster fails on MUST be reported as corruption, naming the family, the table
  and the row, and blocks completion. Neither is skipped silently.
- **FR-007**: The backfill MUST be idempotent. A row already at or after the target version is skipped. A lost
  compare-and-set reads the row again and decides again. A rerun after a crash repeats reads only.
- **FR-008**: The backfill MUST be safe when several hosts run it for one family and database at once. It MAY keep a
  run claim with an expiry in the finish record, to spare duplicate work, but correctness MUST NOT depend on it.
- **FR-009**: Each batch MUST be its own unit of work, with a configurable batch size and a configurable pause between
  batches, so a long run neither holds a transaction open nor starves live traffic.

**Content-addressed rows**

- **FR-010a**: The backfill MUST NOT rewrite a content-addressed row (Q5 on #2093; spec 180, FR-020 and FR-028). Such
  rows keep their stamps and are read through the chain every time.
- **FR-010b**: Each family's declaration (spec 180, FR-001) MUST name its content-addressed tables, and an
  architecture test MUST fail the build when a table holding executables or executable activity templates is not
  named.
- **FR-011a**: A family with any content-addressed row below version V MUST NOT be recorded complete at V.
- **FR-011b**: Its status MUST say that content-addressed rows below V remain and cannot be upgraded.
- **FR-011c**: A feature that declares a completeness requirement (spec 182, FR-005) on a family that names
  content-addressed tables MUST fail the build, because it could never leave dormancy.

**Recording completion**

- **FR-012**: The verification pass MUST NOT start until the settle condition holds: every counted member of the
  database (spec 181, Terms) reports, in its readability entry for the family, an observed finalized version at or
  after the target version (MR-001), and a settle margin has passed since the last of them began to report it. A
  member is counted for this only while its module is active in it for the family, as its readability entry says
  (spec 183, FR-019; amended 2026-09-30, Decisions): a member that loads the family's declaration without activating
  its module writes none of the family's rows and is not waited for. The
  margin is configurable, defaults to the membership expiry period plus the skew allowance (spec 183, FR-006), and is
  never shorter than that (2026-09-30 note, #2153). Under
  the in-process provider the host is the only counted member.
- **FR-013**: The verification pass MUST be a complete pass over every rewritable table of the family, starting after
  the settle condition holds, that finds no row below the target version. A row it finds is rewritten, and the
  verification pass starts again.
- **FR-014**: When verification succeeds, the backfill MUST write the finish record by compare-and-set: the completion
  version, the verification pass's start and end instants, and the member (host id and incarnation) that ran it, with
  an entry in the record's history. It MUST NOT name a version later than the finalized version.
- **FR-015**: The finish record MUST live in the family's finalization record, per database and family (spec 181,
  FR-001 and FR-002), so it shares that record's database, its migration and its opaque database identity. It lands in
  the finalization record's 4.0 baseline with #2120; landing later, it would be an expand-only addition (spec 185).
- **FR-016**: When the finalization record is first created (spec 181, Edge Cases, "A database with no record yet"),
  the completion version MUST be recorded equal to the first finalized version, because no row of the family below that
  version can exist (spec 180, Assumptions).
- **FR-017**: A host's refresh of its observed finalized version (spec 181, FR-010) MUST also refresh its observed
  completion version, so spec 182's shared dormancy check answers completeness without a database round trip (spec
  182, FR-003).

**Afterwards**

- **FR-018**: While a finish record names a completion version, the backfill MUST keep auditing the family on a
  configurable interval, defaulting to one hour, across every table of the family, for rows below it and rows whose
  stamp is missing or outside the host's readable set. Any such row withdraws the completion, the one recorded when the
  finalization record was created (FR-016) included: a rewritable row below it, a row below it in a table whose family
  names no rewriter or in a content-addressed table, and a row with an unreadable stamp alike. The completion is
  withdrawn by compare-and-set, with a history entry, before any row is rewritten, and the withdrawal is reported as
  critical through Attention with the family, the tables and the counts. The rewritable stragglers are then rewritten;
  the others stay, and block completion as FR-004, FR-006 and FR-011a say. The completion stays withdrawn until a new
  verification pass (FR-012 to FR-014) succeeds. Features that need completeness are dormant meanwhile (spec 182,
  FR-005).
- **FR-019**: Withdrawing completion MUST NOT move the finalized version. Finalization stays monotonic (spec 181,
  FR-003); completion is a proof about the rows present, and can stop holding.

**Retirement**

- **FR-020**: A host MUST refuse to activate an EF module when, for any of its families, the database's completion
  version is not in the host's readable set. Given that the finalized version is in that set (spec 181, FR-015) and the
  set is contiguous, a completion version outside it lies before the set's oldest version, so rows the host cannot read
  may still exist. The check runs wherever spec 181's FR-015 and FR-016 run theirs, and the refusal names the family,
  its EF module, the completion version, the readable set and the remedy: run a release that can still read the
  completion version until the backfill completes past it. This makes spec 180's FR-024 retirement obligation a check.

**Reporting**

- **FR-021**: For each family, the gate's status (spec 181, FR-022) MUST carry the completion version, a run in progress
  and its progress, the counted members the settle condition is waiting for (on operator surfaces only, as spec 182's
  FR-011 requires), and what blocks completion: skew, corruption or content-addressed rows.
- **FR-022**: Spec 182's reason for a feature waiting on completeness (spec 182, FR-008, the third case) MUST come from
  this status, and MUST say when completeness cannot be reached because of content-addressed rows.

**Performance**

- **FR-023**: Performance is stated qualitatively, because performance measurement is retired (#1668,
  [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)). A run costs one read and one
  write per row below the target version, once, in throttled batches. The audit costs one selection by stamp per
  interval. Selecting by stamp is cheap only where the stamp column is indexed, which FR-025 requires.

**Tests**

- **FR-024**: Each mechanism MUST be tested in both directions: a row below the target is rewritten and a row at it is
  not; a finish record is written after a verification that finds nothing and not after one that finds a row; the audit
  withdraws on a straggler and leaves the record alone without one; content-addressed rows are untouched while the rest
  of their family is upgraded; a host whose readable set excludes the completion version is refused and one that
  includes it activates.

**Indexing**

- **FR-025**: Every stamped table MUST carry a non-unique index on its `SchemaVersion` column, in the same 4.0
  baseline change that stamps the table (spec 180, FR-026; [#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119)).
  Without it, the upgrade pass, the verification pass and every hourly audit (FR-018) select by a full scan of each
  stamped table, repeated for as long as the family exists. Adding the index after the freeze is still possible, as an
  expand-only migration per table (spec 185), but landing it in the 4.0 baseline avoids that later, separate migration
  for the families that already stamp today and the tables spec 180's FR-026 stamps for the first time.

### Requirements on membership (B3) and the gate (B5)

| Requirement | On | Change |
|---|---|---|
| **MR-001**: each readability entry carries the member's observed finalized version for the family, from the record whose database identity the entry names | spec 183, FR-019 | A new field in the entry. The member already reads that record (spec 181, FR-010); FR-012 needs others to see what it read. |
| The finish record's fields and history entries in the finalization record | spec 181, FR-001 | New fields, in the same 4.0 baseline (#2120). |
| Refreshing the finish record with the finalized version | spec 181, FR-010 | Same refresh, one more field. |
| The completion check at activation and at enable time | spec 181, FR-015 and FR-016 | A second condition beside the finalized-version check. |

### Key Entities

- **Finish record**: per database and family, inside the finalization record: completion version, verification start
  and end, the member that ran it, history.
- **Rewriter**: per family: read through the read path, write through the write path.
- **Content-addressed table**: declared per family; never rewritten.
- **Run**: an upgrade pass, the settle wait, and a verification pass, for one family in one database.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a run, zero rewritable rows of the family remain below the finalized version, and each rewritten
  row reads equal to its value before the rewrite.
- **SC-002**: A run killed at any point and restarted on another host ends in the same table state as an uninterrupted
  run.
- **SC-003**: A finish record is never written while a rewritable row below its version exists at the time the
  verification pass ends.
- **SC-004**: A straggler inserted after completion is reported, and the completion withdrawn, within one audit
  interval.
- **SC-005**: Zero content-addressed rows are written by the backfill, and a family that holds any below V never has
  a finish record at V.
- **SC-006**: A host whose readable set excludes the completion version is refused at activation, and one whose set
  includes it is not.
- **SC-007**: A single host with the in-process provider records completion after one run, and creates no membership
  table.

## Assumptions

- Spec 180 is built first, with a declaration per family and a chain longer than one only after 4.0 (spec 180,
  Assumptions). Before then every chain has one version and the backfill has nothing to do.
- Spec 181 is built, with the finalization record in every EF module's 4.0 baseline (#2120).
- Each family's store can read and write one row by key through its normal paths. Where a store exposes only
  domain-level operations, its rewriter is written beside it.
- Hosts' clocks agree within spec 183's skew allowance.

## Dependencies

- **Spec 180** (B4, [#2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100)): the chain, the write path,
  and the family declaration this spec extends with content-addressed tables.
- **Spec 181** (B5, [#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)): the finalization record,
  counted members and the refresh, with the changes in the table above.
- **Spec 183** (B3, [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)): the readability report,
  with MR-001.
- **Spec 182** (B6, [#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)) is the consumer of FR-017
  and FR-022.
- **[#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119)**, which stamps every unstamped table in
  the 4.0 baselines (spec 180, FR-026) and, with it, adds FR-025's non-unique `SchemaVersion` index to every stamped
  table.

## Out of Scope

- Rewriting a content-addressed row in any form, including to a new format version: a new executable format sits
  beside the old one (Q5; spec 180, FR-028).
- Deciding when a release may drop an upcaster. FR-020 refuses a host that dropped one too early; choosing when to drop
  it stays a release decision.
- Operator commands to start, pause or throttle a run beyond its settings. The CLI's status subcommand (spec 181,
  FR-020) prints what FR-021 carries.
- Data changes that a migration would express. A migration never rewrites rows (spec 185).

## Decisions

Recorded 2026-09-27, when this spec was drafted. The first three are the owner's, on #2093; the rest are this draft's
own, and merging the spec approves them.

- **A backfill after finalization, whose finish record is the completeness proof.** Decided by the owner as Q4 and Q15
  on #2093, and recorded in ADR 0078's 2026-09-27 amendment.
- **Not `IEfPostMigrationAction`.** Stated on #2116, for the deadlock "Why this is not a post-migration action"
  describes.
- **Executables are never rewritten in place.** Decided by the owner as Q5 on #2093.
- **A family with content-addressed rows below V is never complete at V.** Claiming completeness over rows nothing may
  upgrade is the failure this spec exists to prevent. `RuntimeArtifact` is the family this affects today: none of its
  versions can be recorded complete once a later version is finalized while older executables remain, and its old
  upcasters stay, which Q5's "every reader that ever shipped is kept" already requires. A feature that needs
  completeness over rewritable rows beside content-addressed ones should live in a family of its own.
- **The backfill writes through the family's own write path.** A generic row copier would bypass the projections and
  integrity clauses a new version introduces, which is exactly what a feature querying new data reads.
- **A settle condition from members' reports, not only a timer.** Hosts may set different refresh intervals, so no
  single timer bounds when every writer has switched. Reports say it exactly for every counted member; the margin covers
  writes already in flight.
- **An audit that withdraws completion.** Leaving the record in place after a straggler would keep a feature answering
  from part of the rows. Withdrawing sends it back to dormant, which says why, until verification succeeds again.
- **Retirement becomes an activation check.** Spec 180's FR-024 left it a release obligation because a build cannot see
  a database. The finish record is in the database, so a host can check it when the module activates.
- **This backfill is the one background writer spec 180's FR-014 allows.** FR-014 says no background task rewrites rows
  "as a side effect". The backfill's rewrite is its purpose, done through the same write path, and spec 180's FR-024
  and Q4 assign it here.

Recorded 2026-09-28, when the owner answered Q27 on #2093.

- **Q27 — An index on each stamp column, in the 4.0 baselines.** Decided by the owner: a non-unique `SchemaVersion`
  index on every stamped table, in the 4.0 baselines (#2119), and the audit's one-hour default is kept (FR-025). The
  upgrade pass, the verification pass and every audit select rows by stamp; without the index each is a full scan,
  repeated hourly for as long as the family exists. The baselines were already being rewritten to stamp every
  unstamped table (spec 180, FR-026), so the index lands with them instead of as a later, separate expand-only
  migration (spec 185).

**2026-09-28 note.** [#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119) (PR
[#2131](https://github.com/elsa-workflows/elsa-foundation/pull/2131)) landed while this spec was open: every EF
module now stamps a schema family, taking the checked total from fifteen to twenty-six; the "ten unstamped EF
modules and two Publishing tables" this spec and research.md describe (Current state, "Families, and where
content-addressed rows live") should be checked against what #2131 already shipped before FR-004's rewriters are
scoped.

**2026-09-29 note.** Built by B9 ([#2116](https://github.com/elsa-workflows/elsa-foundation/issues/2116)); the PR's merge
is the owner's approval of what follows, found while building.

- **Where it runs.** `EfModuleMigrator` starts an `EfSchemaBackfill` beside each module's gate once the gate has admitted
  the module, in the shell, with the shell's services (FR-001). Its first round is one check interval later, so the
  shell is running by then. Its target is this host's write version, the finalized version it has adopted (FR-002); a
  host that has not adopted the version the finish record names leaves the family alone, since a row it rewrote would
  still be below the completion.
- **The claim (FR-008)** is an optional member of the finish record's JSON, `run`: a member, a worker and an expiry,
  absent while no run holds one. A build that does not know it reads a claimed record unchanged and drops the claim
  when it next rewrites the record, which is harmless since nothing depends on it. A withdrawn completion leaves no
  finish record to hold a claim, so the verification after a withdrawal runs unclaimed; giving the claim a column of
  its own would change every module's finalization table, which B9 does not do. A claim is taken only for an upgrade
  pass that has rows to rewrite, so a family that is settling or blocked does not rewrite its record every round.
- **The settle margin (FR-012)** runs from when the worker first saw every counted member report the target, which is
  no earlier than when the last of them began to; a worker that restarts waits it again. A counted member whose entry
  names no observed version because it serves the family in two databases whose records disagree holds the condition
  back, as FR-012 reads, and only delays. One that loads the family's declaration without activating its module no
  longer does (2026-09-30 note, #2153).
  A withdrawal starts the margin again on every worker, not only the one that made it, so the verification after one
  waits a full margin: a worker notes how many entries the finish history held when its margin began, and a withdrawal
  entry past that position, whoever appended it, restarts the margin and keeps a verification pass that followed it
  from recording a completion. The check is by position, not by time, since two hosts' clocks cannot order a withdrawal
  against a margin.
  MR-001 was already met by B3; B9 adds the counting requirement `ObservesFinalizedSchemaVersion` to the membership
  contract, and `IEfSchemaFleet.SettleMargin` gives the default margin from the membership settings. A host that composes
  no fleet, `Elsa.Foundation.Host` among them today (spec 182, 2026-09-29 note), upgrades rows but never records
  completion; nothing observable differs while every chain has one version.
- **Any pass withdraws, before it rewrites (FR-018).** A row found below the standing completion by any pass, not only
  the hourly audit, withdraws it: a run towards a newer finalized version can meet one below the old completion. The
  audit, the upgrade pass and the verification pass each withdraw before they rewrite the first such row, so a host
  that dies midway leaves the straggler reported rather than under a completion that still stands. A pass reads what
  stands afresh before each row, and so whenever a batch starts, unless it already knows of a completion at or after
  its target: a completion another worker records while the run goes on covers the rows the run meets next, and a row
  rewritten under it without a withdrawal would hide a straggler. That costs a run one read of the finalization record
  per row it rewrites, beside FR-023's read and write of the row. A withdrawal is a compare-and-set against evidence
  read after the record it withdraws: a completion another worker recorded again in the meantime is withdrawn only if
  rows below it still remain. A withdrawal that loses its compare-and-set three times stops the round rather than
  rewrite the row under a completion that still stands; the next round finds the row again. The audit selects every
  table of the family, as FR-018 now reads: rows below the completion in rewritable tables, in tables whose family
  names no rewriter and in content-addressed tables, and rows with a missing or unreadable stamp, each withdraw it,
  and the completion recorded at the first version when the record was created (FR-016) is no exception, since
  nothing shows a row with an unreadable stamp is not below it. A standing completion is audited whatever this host's
  target, so a family whose run towards a newer version is blocked or claimed elsewhere is still audited. The audit
  runs first one check interval after a host starts, then hourly, so hosts that restart more often than hourly still
  audit. A withdrawal after the settle margin a verification pass followed began, before the pass or during it, keeps
  the pass from recording, told by its position in the finish history, not by time, since two hosts' clocks cannot
  order it. The withdrawal reaches Attention from the finish record's history on every host, as a
  critical item naming the tables and counts, until a new verification pass records the completion again.
- **Status (FR-021, FR-022).** The gate's status carries the backfill's state, target, rows rewritten, the members the
  settle condition waits for and what blocks completion. The persistence tool's `status` prints what the record holds:
  the completion version, a claimed run and a withdrawn completion. A feature waiting on completeness is told it cannot
  become available when content-addressed rows below the version remain; FR-011c's guard keeps any first-party feature
  from reaching that.
- **Selection (FR-005).** Batches are keyset pages over the primary key, resuming after the last key, so a row the
  backfill cannot rewrite is passed over rather than selected again. A key part is compared as text, as an enum's stored
  number, or with its type's own order; one stored through any other value converter is refused. A model test runs
  every first-party stamped table's selections on SQLite, and the synthetic family's run on the three server engines.
- **Content-addressed tables (FR-010b).** `RuntimeArtifact` names its executables, its executable activity templates
  and the template hash claims, which are keyed by a template's content hash. Each such entity type is also marked
  `[EfSchemaContentAddressed(reason)]`, and the model guard fails when a first-party stamped table is marked but not
  named by its family, or named but not marked, and pins the three tables above by name, failing when one stops being a
  stamped table. It infers nothing from a table's name. The backfill never rewrites a marked row even where its family
  forgot to name it.
- **A blocked family (FR-023).** A family blocked at its target is surveyed again after an interval, not every check
  interval, unless its target moves: a blocker persists until someone resolves it, and surveying every round would
  select every table by stamp every fifteen seconds. A family blocked by content-addressed rows, a missing rewriter or
  a missing fleet waits the audit interval, since only deleting those rows, a new build or a new composition resolves
  it. One blocked by skew or corruption, rows an operator repairs in place, waits the shorter
  `RepairableBlockerInterval`, five minutes by default, so a repair is seen soon; a family blocked by both waits the
  shorter one.
- **Rewriters (FR-004).** A family names its rewriter with `[EfSchemaFamily(..., Rewriter = typeof(...))]`, and the build
  fails for a family with upcasters and none. Every first-party family still has one version, so none exists yet;
  each family writes its own before its first version bump.

**2026-09-30 note (#2153).** Found in B9's review (#2116); the PR's merge is the owner's approval.

- **The settle condition counts only members whose module is active (FR-012).** As first built, a counted member whose
  entry named no observed finalized version held the condition back, and that included a host that loads a family's
  declaration and never activates its module: it reports no observed version for as long as it stays in the fleet, so it
  blocked completion, and with it the exit from dormancy of every feature that needs completeness, and only the status
  named it. Spec 183's FR-019 amendment of the same date gives the readability entry an explicit "module active in this
  host" fact, and `ObservesFinalizedSchemaVersion` counts, for the counting purpose, only the entries that carry it. The
  readability count is not narrowed, so a loaded declaration still counts for "can read V" and finalization is as safe as
  before. An active member that has not read the record yet still holds the condition back, and the status names it
  from its active entries alone. Leaving a not-active member out cannot hasten a completion wrongly: it writes none of
  the family's rows, and a module admitted afterwards adopts the finalized version it reads, which is no lower than the
  target, so no row below it can be written by a member the condition did not wait for. The margin covers a module's
  last writes, since the host reports its deactivation only from its next publish.
- **When a module is active in the report.** From the report a gate publishes before it reads the family's record, since
  an activation in progress is about to write, through the end of its admission, where it hands over to the gate's own
  activity with no moment between and before the gate makes a write version visible, so the report never says a module is
  not active while it can write a row of the family; a refused admission stops counting once it is refused. Until its
  gate stops, and the gate stops when its loops have ended, so a round that could still write is never reported as over;
  or until its activation fails or is cancelled, which ends it at once, since a shell that failed to start has no
  migrator to stop and would otherwise be reported active for good, holding every backfill of the family back. A
  migrator that stops while its gate is being admitted cancels the admission, so a store call that hangs cannot stall
  the stop or leave the activation active without bound, waits for it to end and deactivates the gate that comes of it,
  and one that has stopped admits nothing more. Each gate is one owner: several shells activating one family, or the two
  generations of a reload, keep it active until the last owner stops. Activity is kept per database: a tenant whose shell stopped is not
  held active by another tenant's, in another database, and stopping the last gate of a database forgets what the host
  read there. Not done: an entry that names no database, because the host serves the family in several whose records
  are read, is active while any of them has a gate, so one tenant still running can hold the others' settle back; one
  entry per database would settle that. A report built with no observations to ask, a hand-built source or a shell with
  its own, says every module is active, since saying otherwise could let a settle pass early.
- **The settle margin has a floor.** The configured `SettleMargin` may lengthen the fleet's margin, the membership expiry
  period plus the skew allowance, and never shortens it: a shorter value is raised to the fleet's, with a warning once
  per worker, rather than refused, so a timing does not stop a host from starting. The floor is FR-012's bound on writes
  begun before the writer observed the finalized version, which can still be in flight when the member reports observing
  it. It is not about shell drains: a shell's migrator is disposed, and its module deactivated, only after the CShells
  drain (30 seconds plus 3 seconds grace) has ended, the margin starts only once the report already shows the member
  inactive, and after deactivation the write check throws on the disposed provider, so no authorised write follows.
  Operators need not lengthen `SettleMargin` when they lengthen the drain.

**2026-10-01 note ([#2199](https://github.com/elsa-workflows/elsa-foundation/issues/2199)).** Found by the #2155 audit;
lands with the #2199 PR, whose merge is the owner's approval. It supersedes the 2026-09-29 note's account of the claim,
which took one only for an upgrade pass with rows to rewrite and none while no completion stood.

- **The claim covers every pass, and is taken first (FR-008).** A worker claims the family before any pass reads its
  rows: the survey, the upgrade pass, the settle condition, the verification passes and the audit. A worker that finds
  the family claimed elsewhere reads none of its rows, nor the record again, until the claim expires, and defers an
  audit that was due by an audit interval, which also ends every host auditing at once after a start. The claimant
  renews the claim between batches, before the settle condition and before each verification pass, so a settling
  family's record is written about once every third of the claim period, which the 2026-09-29 note avoided. An audit's
  claim names the standing completion as its target, so a claim's target is now at or after the completion, never
  before it; the finalized and completion versions keep their forward-only rules unchanged.
- **The claim can be held while no completion stands.** A withdrawn completion leaves no finish record, so the claim is
  then held on the withdrawal that ended it: an optional `run` member of that newest finish history entry, beside the
  same member of the finish record. No entry is added, removed or reordered, and no transition, version, actor, instant
  or reason changes; only that newest withdrawal's claim does, and the next completion clears it. A withdrawal moves a
  claim that still holds onto itself, so the worker that found the straggler goes on rewriting it and the others leave
  the family alone: after a withdrawal one worker upgrades instead of every one. Like the finish record's claim it is
  advisory, so a build that does not know it reads the history unchanged and drops it when it next rewrites the record.
  A column or table of its own was not chosen: every module's finalization tables would need a migration on all four
  engines for an optimisation that nothing correct depends on.
- **The claim only ever narrows who works.** Every claim, renewal, completion and withdrawal stays a compare-and-set on
  the record's revision. A worker whose claim lapsed and was taken over stops at its next renewal, and while another
  worker's claim holds it records no completion, even from a pass that found nothing, so it never finishes a run it no
  longer owns. Nothing correct depends on the claim: with `ClaimDuration` zero, or a claim that loses its
  compare-and-set again and again, workers run unclaimed as before.
- **A member that lapses stops (spec 183, FR-007).** A worker whose member has concluded that it lapsed claims nothing,
  stops its run at the next renewal, releases the claim it holds so another member takes the family over at once, and
  does nothing more until it rejoins. A member that was displaced never rejoins; its host stops (spec 183, 2026-10-01
  note).
- **US4's third acceptance scenario** reads "the record is unchanged" of the proof: an audit that finds nothing leaves
  the finalized version, the completion and both histories as they were, and writes only the claim it ran under.
