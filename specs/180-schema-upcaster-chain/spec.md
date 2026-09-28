# Feature Specification: Schema Upcaster Chain

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: Approved
**Input**: Workstream B4, [issue #2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Each
persisted-schema version of a module ships a transform from its predecessor. Reads apply the chain, and a row is
upgraded when it is next written, so a workflow started under one version can resume several versions later.

Decisions of record: [ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md)
(Decision, "Skew is not corruption") and
[ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
("The first consumers", "The schema version gate"). Both are accepted with these decisions through B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096), PR #2118). The owner's decisions of
2026-09-23/24, recorded on #2093, bind this spec.

Companion specs: [spec 181](../181-schema-finalization-gate/spec.md) (B5,
[#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)) decides which version a host may write, and
[spec 182](../182-dormant-features-until-finalization/spec.md) (B6,
[#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)) keeps features that need new-version data
dormant until then. This spec owns the read path, the chain, the rule that a write never stamps more than spec
181 allows, and the write refusal (see Terms) that spec 182 reuses for dormant-feature writes.

## Terms

The code uses "module" for two different units, and this program has to keep them apart.

- **EF module**: an `[EfModule]` declaration (ADR 0076, D2). It owns one migrations-history table, `[UsesEfModule]`
  names it, and the activation guard refuses it. The tree declares 13.
- **Schema family**: the set of tables whose rows stamp one persisted-schema version constant. The skew check names
  a family, for example `RuntimeOperationalState`. ADR 0077's Context calls the fifteen families "EF modules", and
  `EfSchemaVersionSkewException.Module` holds a family name, so the word "module" in those places means a family.
  Each family belongs to exactly one EF module.
- **Stamp**: the persisted-schema version a row carries in its `SchemaVersion` column.
- **Readable set**: the versions of a family a build can read. It holds the build's current version and every
  predecessor the declared chain reaches without a gap.
- **Upcaster**: a transform of one family's stored content from one version to its immediate successor.
- **Write version**: the version a host stamps on the rows it writes. Spec 181 defines it as the family's finalized
  version, as the host last observed it.
- **Write refusal**: the typed error a write raises when the value being saved needs a version later than the write
  version. Its type is unassignable to `InvalidOperationException`, `ArgumentException`, `FormatException`,
  `NotSupportedException`, `JsonException` and `InvalidDataException` — the types store catch filters and API fault
  ladders turn into corruption or a 400 — the same convention `EfSchemaVersionSkewException` follows for reads
  (FR-007). It carries a stable code, the family, the write version and the version the data needs. Every domain API
  that can raise it maps it to HTTP 409 in its own problem envelope, carrying the code. Spec 182 reuses this refusal
  for dormant-feature writes, adding a feature id and an operator-facing reason on top of it (FR-016a).

"Envelope" is retired vocabulary in the [root glossary](../../docs/glossary/root.md). This spec says "integrity
clauses" for the checks the code's messages call the row envelope.

## Current state

Every EF module now stamps its schema families in its rows, delivered by
[#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119) (PR
[#2131](https://github.com/elsa-workflows/elsa-foundation/pull/2131)); the authoritative list of families is the
code — the `SchemaFamily` constants — not a count kept here. At this spec's writing (2026-09-27), fifteen
schema families across three EF modules (`Workflows.Runtime`, `Workflows.Publishing`,
`Elsa3.Activities.Design.Import`) stamped their rows; ten other EF modules and two Publishing tables carried no
stamp — the gap FR-026 below was written to close.
Stamping and checking are each done through one mechanism (`EfSchemaVersion`, `EfSchemaVersionSkewException`), but
call sites are scattered across 30 store files, an ordering guard already polices part of the read path, and no
Runtime or Publishing family has a committed payload fixture yet. The full inventory — the family table, call-site
and file counts, the ordering guard's current reach, JSON-tolerance behaviour and the fixture and compression facts
this spec's reasoning depends on — is in [research.md](./research.md).

## Writing the predecessor format

The rollout decided on 2026-09-23/24 has a new version keep writing the old format until finalization. The design
hypothesis is that this needs no down-transform when the change is expand-only. Before finalization a host stamps
rows with the old version and leaves new-only fields unset. Features that need those fields are dormant (spec 182)
and writes that need them are refused. After finalization the host stamps the new version and writes the new fields.

**Verdict: it holds for every stamped family, under two conditions. It does not hold for rows that carry
no stamp.**

It holds because:

- the stamp is a separate column assigned at write time, so a host can stamp the old version without touching any
  hash or identity (see [research.md](./research.md), "Stamping");
- a predecessor's reader ignores members it does not know, so a document the new build writes with its new members
  null or absent reads correctly under the predecessor;
- under B8 ([#2104](https://github.com/elsa-workflows/elsa-foundation/issues/2104)) a new column is nullable, and
  EF Core never writes a column its model does not know, so the predecessor's inserts and updates leave new columns
  null, which is what "unset" means.

The two conditions:

1. **The content change must be expand-only too.** B8 guards migrations. A change to a JSON document produces no
   migration at all, so B8 cannot see a renamed, retyped or restructured member. For such a change, "leave the new
   fields unset" does not produce the old format, and a down-transform would be needed. FR-022's round-trip fixture
   is the test that catches it.
2. **Integrity clauses must follow the row's version.** Many stores check that projection columns match the content.
   A projection column added at version V is null on every row an older writer wrote, so the check must apply only
   to rows stamped V or later (FR-008).

It does not hold for the ten EF modules, and the two Publishing tables, whose rows carry no stamp. Nothing records
which version wrote such a row, so neither the chain nor finalization can reason about it. Each gains a stamp column
in its 4.0 baseline, before #1976 freezes it (FR-026; Decisions, Q2).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume a workflow several versions later (Priority: P1)

A workflow suspends while the host runs version 1 of the runtime's families. The fleet is upgraded twice. When a
stimulus arrives, a host running version 3 loads the suspended execution, bookmarks and scheduler state, and resumes
it.

**Why this priority**: This is what the chain exists to do. Rows outlive versions (ADR 0078, "The first
consumers"), and without the chain the version-3 host refuses every version-1 row as skew.

**Independent Test**: Commit version-1 fixture rows for a family, run the store's read path in a build whose chain is
1 → 2 → 3, and compare the result with reading the committed version-3 fixture.

**Acceptance Scenarios**:

1. **Given** a row stamped `1` and a build whose readable set is {1, 2, 3}, **When** the store reads it, **Then** the
   content is upcast 1 → 2 → 3 and yields the same domain value as the committed version-3 fixture.
2. **Given** that row, **When** it is read and not written, **Then** its stamp and bytes in the database are unchanged.
3. **Given** a row stamped at the build's current version, **When** it is read, **Then** no upcaster runs.

---

### User Story 2 - Refuse a version the chain cannot place, never guess (Priority: P1)

A host meets a row whose stamp is not in its readable set: a newer version written after a finalization this host
cannot read, a version older than its chain reaches, or no stamp at all. The host reports skew naming the family,
the version found and the versions it can read. It does not report corruption and does not guess.

**Why this priority**: ADR 0077's first rule is that skew is not corruption. The chain must not weaken it: a
version that is neither current nor reachable is exactly the case where guessing would misread rows.

**Independent Test**: For every family, write rows stamped one step past the current version, one step before the
chain's start, and with no stamp, and read each through the store.

**Acceptance Scenarios**:

1. **Given** a build whose readable set is {1, 2}, **When** it reads a row stamped `3`, **Then** it raises
   `EfSchemaVersionSkewException` naming the family, `3` and {1, 2}.
2. **Given** a build whose chain declares versions 1 and 3 with no upcaster from 1 to 2, **When** it is built,
   **Then** the build fails. If such a build nevertheless starts, it refuses to register the family at startup.
3. **Given** any row outside the readable set, **When** it is read, **Then** the exception is never
   `InvalidDataException` and never any type a store's catch filter re-reports as corruption.
4. **Given** a row stamped at a version in the readable set whose content does not match that version's shape,
   **When** it is read, **Then** it is reported as corruption, because the version is known and the row is damaged.

---

### User Story 3 - Upgrade a row only to a finalized version (Priority: P1)

A host running version 2 reads a row stamped `1` and writes it back. Before version 2 is finalized it writes the row
at version 1, with version 2's members unset. After finalization it writes the row at version 2. It never writes a
row at a version that is not finalized, and never lowers a row's stamp.

**Why this priority**: Upgrade-on-next-write is how rows move forward, and it must not outrun spec 181. A single row
written at an unfinalized version is a row that a host still running version 1 cannot read.

**Independent Test**: With the finalized version held at `1`, run a version-2 host through a full workflow and assert
that every row in the family is stamped `1`. Finalize, run it again, and assert that the rows it wrote are stamped
`2` while untouched rows stay at `1` and still read.

**Acceptance Scenarios**:

1. **Given** write version `1` and a host whose current version is `2`, **When** it saves a value whose version-2
   members are unset, **Then** the row is stamped `1` and the stored content equals what a version-1 host would have
   written.
2. **Given** write version `1`, **When** the value being saved carries data in a member introduced at version 2,
   **Then** the save is refused with the write refusal (see Terms) and nothing is written.
3. **Given** write version `2`, **When** a row read at `1` is saved, **Then** it is stamped `2`.
4. **Given** a host whose observed write version is `1`, **When** it reads a row stamped `2`, **Then** it re-reads
   the finalized version before writing that row, and refuses the write if the record does not confirm `2` is
   finalized.

---

### User Story 4 - Add a schema version with proof (Priority: P2)

A module author changes a family's stored shape. They bump the family's version, add one upcaster from the previous
version, and commit a fixture pair. The build proves the transform, the chain's contiguity and the old-format round
trip before the change can merge.

**Why this priority**: Without a mechanical proof, every future schema change relies on review alone. The fixture
pair is also the enforcing test for "expand-only content".

**Independent Test**: Delete an upcaster, break a fixture, or rename a JSON member without a down-path, and confirm
the build fails each time with a message naming the family and versions.

**Acceptance Scenarios**:

1. **Given** a new version with an upcaster and a fixture pair, **When** the suite runs, **Then** it proves the upcast
   equals the expected fixture, and that writing the expected value at the source version reproduces the source
   fixture.
2. **Given** a version bump without an upcaster, **When** the build runs, **Then** the contiguity test fails.
3. **Given** a change that renames a JSON member, **When** the round-trip test runs, **Then** it fails, because the
   old format cannot be produced by leaving new members unset.

---

### Edge Cases

- **Two hosts upgrade the same row at once.** Both compute identical content (FR-018). The existing revision and
  concurrency checks decide which write wins, as they do today.
- **A row read in one unit of work under write version 1, written after the host observes finalization.** It is
  stamped with the write version fixed for that unit of work (FR-017). Either stamp is readable by every live host.
- **A content-addressed row** (a workflow executable, whose `ArtifactHash` identity must match its content). An
  upcaster must not change the identity (FR-019). Such rows are written once and never "next written", so they keep
  their stamp until B9's post-finalization backfill (#2116) rewrites them (Decisions, Q4).
- **A payload column that is not JSON.** `WorkflowTriggerBindingProjectionStateEntity.ContentJson` holds a hex
  fingerprint. An upcaster transforms whatever the family stores; the chain does not assume JSON.
- **A compressed payload.** It is decoded before upcasting and re-encoded on write (FR-012).
- **Version labels in different styles.** `"1"` and `"1.0.0"` coexist today. The chain orders versions; nothing parses
  them.
- **A family read by another module.** The Dashboard's EF data sources apply the owning family's chain (FR-010).

## Requirements *(mandatory)*

### Functional Requirements

**Declaration and location**

- **FR-001**: Each schema family MUST have exactly one declaration of its identity, its owning EF module, its current
  version and its ordered chain of upcasters, in the owning EF module's assembly. The declaration MUST be readable
  from assembly metadata without composing a shell or a container, as `[EfModule]` is (ADR 0076, D2), so the
  `dotnet elsa persistence` worker, the activation checks of spec 181 and B3's readability report
  ([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)) all read the same facts.
- **FR-002**: A family's identity MUST be one declared value that every call site references. Call sites MUST NOT
  restate it as a string literal.
- **FR-003**: An upcaster MUST transform exactly one version of one family into its immediate successor. It lives in
  the owning EF module's assembly beside that family's store code. It is a concrete type with a public parameterless
  constructor and no injected services, for the reason `IEfPostMigrationAction` has the same constraint: the
  persistence worker has no shell container.
- **FR-004**: A build's readable set for a family MUST be its current version plus every predecessor the chain reaches
  without a gap. Versions MUST be treated as opaque labels ordered only by the chain. Nothing parses or compares them
  as semantic versions.
- **FR-005**: A chain with a gap, a duplicate version, a branch or a cycle, or one that does not end at the current
  version, MUST fail the build through an architecture test over every declaration in `src/essentials`,
  `src/extensions` and `src/apps`, and MUST fail the family's registration at startup. A gap is never bridged at read
  time.

**Read path**

- **FR-006**: Every read of a family's row MUST establish that the row's stamp is in the readable set before it
  deserializes the content into any current type and before any integrity clause runs.
- **FR-007**: A stamp outside the readable set MUST raise `EfSchemaVersionSkewException`. That covers a stamp newer
  than the build's current version, one older than the chain reaches, one below a gap, and a missing stamp. The
  exception MUST name the family, the version found and the readable set, and its remedy text MUST describe the
  gate of spec 181 rather than saying the versions "cannot run alongside" each other. It MUST stay unassignable to
  the types the stores' catch filters name.
- **FR-008**: Integrity clauses MUST be evaluated against the definition in force at the row's stamped version,
  before the content is upcast. This covers identity hashes, content hashes and projection columns. A projection
  column introduced at version V is checked only on rows stamped V or later.
- **FR-009**: Once integrity passes, the content MUST be upcast one step at a time from the stamped version to the
  build's current version, then deserialized and validated by the current domain validation. When an upcaster fails
  on a row whose stamp is in the readable set, the row is corrupt and the existing `InvalidDataException` path
  reports it. It is not skew.
- **FR-010**: Every reader of a family MUST apply that family's one chain, including readers outside the owning EF
  module such as `EfWorkflowPortfolioDataSource` and `EfWorkflowRunHealthDataSource`. No reader keeps a private
  equality check.
- **FR-011**: `EfSchemaVersionOrderingGuardTests` MUST be extended so that the build fails when a read path
  deserializes a family's content or evaluates an integrity clause before its version check, not only when an
  earlier clause shares the condition. The guard MUST keep a floor that fails if the call sites move to a name the
  guard does not scan.
- **FR-012**: A payload framed by `EfPayloadCodec` MUST be decoded before upcasting and encoded again on write. An
  upcaster sees only decoded content.

**Write path**

- **FR-013**: A write MUST stamp the host's write version for the family, as spec 181 defines it. A host MUST NOT
  stamp a version that is not finalized, even when it is the host's own current version.
- **FR-014**: A row read at an older version and written again MUST be written at the write version. That is the only
  way a row moves forward on its own: no read or startup step rewrites rows as a side effect, and the only background
  task that rewrites rows is B9's post-finalization backfill (spec 186), whose rewrite is not a side effect because it
  is that task's stated purpose, done through the same write path and the same compare-and-set this requirement
  governs (spec 186, FR-004).
- **FR-015**: A write MUST NOT lower a row's stamp. When a host reads a row stamped later than its write version, it
  MUST re-read the finalized version before writing that row, because a row at version V exists only once V is
  finalized. If the record does not confirm V, the write is refused.
- **FR-016**: When the write version is older than the build's current version, the host MUST write that version's
  format, leaving every member and column introduced after it unset. If the value being written carries data in any
  such member, the write MUST be refused with the write refusal (FR-016a) before anything is saved, and never
  written with the data dropped.
- **FR-016a**: The write refusal MUST be its own exception type, unassignable to the six types Terms names for it. It
  MUST carry a stable code, the family, the write version and the version the data needs. A test MUST pin the
  unassignability, as `EfSchemaVersionTests` does for the skew exception. Every domain API that can raise it MUST
  map it to HTTP 409 in its own problem envelope, including the code. Raised directly by a store under FR-016, it
  carries the family and the two versions alone; spec 182 extends it with a feature id and a reason when a
  feature-level check raises it instead.
- **FR-017**: Every row of one family written in one unit of work, such as one checkpoint commit, MUST carry the same
  stamp.
- **FR-018**: A write that replaces an existing row MUST run the version check of FR-006 on the stored row first. A
  host MUST NOT overwrite a row whose stamp it cannot read. This closes the path by which an older writer rewrites
  a newer row and drops its fields.

**Transforms**

- **FR-019**: An upcaster MUST be a pure, total function of its input over every valid document at its source
  version. It uses no clock, randomness, environment, culture, configuration, I/O or service. Two processes upcasting
  the same stored content MUST produce byte-identical output, because content hashes such as `Elsa3ImportRecordCodec`'s
  are recomputed from the written content, and two hosts may upgrade the same row.
- **FR-020**: An upcaster MUST preserve every identity the row carries and every identity derived from its content:
  row id, scope keys, order keys, and a content-addressed executable's artifact hash. A change that would alter such
  an identity cannot be expressed as an upcaster.
- **FR-021**: Applying the chain to a row stamped at the current version MUST be the identity. No transform runs.

**Proof**

- **FR-022**: Every upcaster MUST ship a committed fixture pair: a document at its source version, captured from the
  build that wrote that version, and the expected document at its target version. The suite MUST prove three
  things. First, upcasting the source fixture yields the expected fixture, by semantic JSON equality as
  `GoldenFixtureTestSupport` compares today. Second, writing the expected value at the source version, with the new
  members unset, reproduces the source fixture, which is the old-format round trip. Third, reading the source
  fixture as a stored row through the store's own read path yields the same domain value as reading the expected
  fixture. A fixture is frozen once its version ships, and is never regenerated.
- **FR-023**: For every family, a test MUST prove that a row stamped one step past the current version, a row stamped
  below the chain, and a row with no stamp each raise `EfSchemaVersionSkewException` and are never reported as
  corruption.
- **FR-024**: A version MAY leave a family's readable set, by dropping the oldest upcaster, only when a scan of the
  family's tables proves no row at that version remains (ADR 0078, "The schema version gate"). A build cannot see a
  production database, so this is a release obligation rather than a build check. The removed upcaster's fixture
  pair stays committed. The scan, and how a release records its proof, are B9's (#2116): a post-finalization backfill
  that runs idempotently in batches after finalization and records when it finishes. That finish record is also the
  completeness proof spec 182's FR-005 needs (Decisions, Q4).

**Performance**

- **FR-025**: Performance is stated qualitatively, since performance measurement is retired (#1668, ADR 0073) and
  nothing here is measured. A row at the current version costs one membership test against the readable set in
  place of today's equality check. The chain is resolved once per process, never per read. An older row costs one
  in-memory transform per step, paid on each read until the row is rewritten, and no extra database round trip.
  Upgrade-on-write adds no write that was not already happening.

**4.0 baseline and format stability**

- **FR-026**: Each of the ten EF modules and the two Publishing tables that carry no stamp today (Current state) MUST
  gain a `SchemaVersion` stamp column in its 4.0 baseline (`Initial`) migration, landing before #1976 freezes it.
  From that baseline forward, a missing stamp on those tables is skew (FR-007), exactly as it already is for every
  already-stamped family. The same baseline change also carries a non-unique index on every stamped table's
  `SchemaVersion` column, both for the families that stamp today and for the tables this requirement stamps for the
  first time (spec 186, FR-025).
- **FR-027**: A content change to a family's stored shape that is not additive — a rename, retype or restructure of a
  JSON member — MUST NOT be expressed as a single version with a reverse transform. It MUST be split across two
  versions: the new member is added beside the old one at one version, and the old member is removed only once no
  version the family can still finalize reads it. FR-022's round-trip fixture is the enforcing test for the split.
- **FR-028**: A format change that would alter a content-addressed row's identity hash (for example, a change to
  `WorkflowExecutableHasher`'s output) MUST NOT be expressed as an upcaster (FR-020). Such a change ships as a new,
  separate format version that sits alongside every format version that has ever shipped, and the build MUST keep
  the reader for each of them, so an executable in any format the constitution has ever produced still runs
  (§E2.6.1).

### Key Entities

- **EF module**: an `[EfModule]` declaration. It owns a migrations-history table and one or more schema families.
- **Schema family**: tables stamped by one version constant. It has one declaration, one chain and one owning EF
  module.
- **Stamp**: a row's `SchemaVersion` column. It is not covered by any hash.
- **Readable set**: the build's current version plus the contiguous predecessors the chain reaches. B3 reports this
  set per host.
- **Upcaster**: a pure transform from one version to its successor within one family.
- **Fixture pair**: a frozen source-version document and the expected target-version document for one upcaster.
- **Write version**: the version a host stamps, which spec 181 decides.
- **Write refusal**: the typed error a write raises when it needs a version later than the write version. Spec 182
  reuses it for dormant-feature writes.
- **Backfill (B9, #2116)**: the post-finalization process, out of scope here, that rewrites rows written once and
  proves a family's completeness. FR-024 and spec 182's FR-005 depend on its finish record.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every family that declares a chain longer than one, a fixture row at the oldest readable version
  reads through the current build's store and equals the current-version fixture.
- **SC-002**: For every family, rows stamped above the chain, below it and with no stamp raise skew. None
  of them is reported as corruption.
- **SC-003**: With the finalized version held at the predecessor, a full workflow run on a host at the new version
  leaves zero rows stamped at the new version.
- **SC-004**: After finalization, a row read and written once carries the new stamp. A row that was never written
  keeps its old stamp and still reads.
- **SC-005**: Removing an upcaster from inside a chain, which leaves a gap, fails the build, and so does editing a
  shipped fixture.
- **SC-006**: The extended ordering guard fails on a fixture that deserializes before checking the version, and
  passes on the restructured read paths named in [research.md](./research.md), "Ordering".
- **SC-007**: Upcasting the same fixture in two separate processes produces byte-identical output.
- **SC-008**: Every EF module's 4.0 baseline migration stamps the tables named in FR-026, so no table this program
  governs ships without a stamp column.
- **SC-009**: A change that would alter a content-addressed row's identity ships as a new format version beside the
  old one, and the build keeps a reader for every format version that has ever shipped.

## Assumptions

- Migrations are regenerated until 4.0 ships as a stable release
  ([#1976](https://github.com/elsa-workflows/elsa-foundation/issues/1976); ADR 0078, Consequences). Until then every
  chain has one version. The chain matters from the first schema-changing release after 4.0.
- The first release that carries the chain changes no persisted schema. Rows written before it are therefore stamped
  exactly at the baseline version of each family.
- Schema changes meant for a cluster rollout are expand-only (B8, #2104) for migrations. This spec adds the matching
  rule for content through FR-022's round trip.
- Stamps stay outside every hash, as they are today, so writing a different stamp does not invalidate integrity data.
- Stores keep ignoring unknown JSON members on read. Spec 181 prevents an older writer from ever rewriting a newer
  row, which is the situation in which that tolerance would lose data.

## Out of Scope

- Which version a host writes, finalization, holds and the refusal of hosts that cannot read: spec 181 (B5).
- Feature dormancy declarations, the dormant state, and reporting why a feature is dormant: spec 182 (B6), which
  reuses this spec's write refusal (FR-016a).
- Cluster membership (B1 [#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097), B2
  [#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)) and the readability report (B3,
  [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)), which consumes FR-001's declaration.
- Version-aware placement (B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) and the
  expand-only migration guard (B8, #2104).
- The post-finalization backfill for rows written once, and the scan that proves a version can be retired: B9
  (#2116).
- Rewriting `docs/serialization.md` "Schema evolution (Runtime EF Core module)". Its "clean-break pre-GA change"
  procedure gives way to this chain after 4.0. The implementation updates that section, not this spec.

## Decisions

Recorded 2026-09-27, when the owner answered this spec's open questions on #2093.

- **Q1 — Unit of versioning and finalization: the schema family.** ADR 0077, ADR 0078 and #2099 say "per module", but
  a row stamps a family and the skew check compares families, so the chain and finalization key on that identity.
  Refusals stay reported per EF module.
- **Q2 — The ten unstamped EF modules and two unstamped Publishing tables.** Each gains a `SchemaVersion` stamp
  column in its 4.0 baseline, before #1976 freezes the `Initial` migrations (FR-026). A missing stamp on those
  tables becomes skew from that baseline forward, exactly as it already is for the fifteen stamped families.
- **Q3 — Non-additive content changes.** No reverse transforms. A rename, retype or restructure of a JSON member is
  split across two versions: add the new member, then remove the old one once no finalized version reads it (FR-027).
  FR-022's round-trip fixture enforces the split; B8 stays a migration guard and owns no separate content-shape
  check.
- **Q4 — Rows written once and never rewritten.** A new workstream, B9 (#2116), runs a post-finalization backfill,
  idempotent and in batches, whose finish record is the completeness proof spec 182's FR-005 needs. It is not part
  of B4 (FR-024). The existing `IEfPostMigrationAction` could not have carried this rewrite: `EfModuleMigrator`
  refuses the module at Prepare while an action's audit reports it required, and the rewrite can only run after
  finalization, which needs the module active — using it would deadlock.
- **Q5 — Identity-changing format changes.** A format change that would alter an executable's identity hash is never
  made in place. It ships as a new format version alongside every format version that has ever shipped, and the
  build keeps a reader for each of them (FR-028), preserving the Elsa constitution's §E2.6.1 promise.
- **Q6 — Glossary entries.** Added when the specs were approved (PR #2109), not here.

Recorded 2026-09-28, when the owner answered spec 186's Q27 on #2093, which touches FR-026's baseline change.

- **Q27 — An index on each stamp column.** A non-unique `SchemaVersion` index on every stamped table lands in the
  same 4.0 baseline change as FR-026's stamp columns (#2119), so the upgrade pass, verification pass and hourly audit
  spec 186 runs can select by stamp without a full table scan (spec 186, FR-025).

**2026-09-28 note.** [#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119) (PR
[#2131](https://github.com/elsa-workflows/elsa-foundation/pull/2131)) landed while this spec was open: every EF
module now stamps a schema family, taking the checked total from fifteen to twenty-six. Current state above reflects
this. Q2's "fifteen stamped families" is corrected to twenty-six here rather than in place. FR-026's baseline-column
requirement and Q2's skew rule, both written for the ten EF modules and two Publishing tables this spec's Current
state describes as unstamped at this spec's writing, should be checked against what #2131 already shipped before
this spec's implementation starts.
