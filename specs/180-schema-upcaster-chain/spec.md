# Feature Specification: Schema Upcaster Chain

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: Draft
**Input**: Workstream B4, [issue #2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Each
persisted-schema version of a module ships a transform from its predecessor. Reads apply the chain, and a row is
upgraded when it is next written, so a workflow started under one version can resume several versions later.

Decisions of record: [ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md)
(Decision, "Skew is not corruption") and
[ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
("The first consumers", "The schema version gate"). Both are Proposed, and B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096)) accepts them. The owner's decisions of
2026-09-23/24, recorded on #2093, bind this spec.

Companion specs: [spec 181](../181-schema-finalization-gate/spec.md) (B5,
[#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)) decides which version a host may write, and
[spec 182](../182-dormant-features-until-finalization/spec.md) (B6,
[#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)) keeps features that need new-version data
dormant until then. This spec owns the read path, the chain, and the rule that a write never stamps more than spec
181 allows.

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

"Envelope" is retired vocabulary in the [root glossary](../../docs/glossary/root.md). This spec says "integrity
clauses" for the checks the code's messages call the row envelope.

## Current state

Grounded in the tree at the time of writing.

| EF module | Schema families (declaring class and current version) |
|---|---|
| `Workflows.Runtime` | `BookmarkStateEfModule`, `RuntimeActivationSlotEfModule`, `RuntimeActivityExecutionEfModule`, `RuntimeArtifactEfModule`, `RuntimeOperationalStateEfModule`, `RuntimePostCommitOutboxEfModule`, `RuntimeSchedulerPoisonEfModule`, `RuntimeTriggerBindingEfModule`, `RuntimeWorkflowAlterationEfModule`, `RuntimeWorkflowDispatchEfModule`, `RuntimeWorkflowExecutionEfModule`, `RuntimeWorkflowTestScopeEfModule`, all `SchemaVersion = "1.0.0"` |
| `Workflows.Publishing` | `PublishingLedgerEfModule.ContentSchemaVersion = "1"` (activity-publication receipts, draft test runs) and `PublishingPolicyProjectionEfModule.SchemaVersion = "1.0.0"` (policies, projection intents). Publication records and snapshot reviews carry no stamp. |
| `Elsa3.Activities.Design.Import` | `Elsa3ImportEfModule.SchemaVersion = "1.0.0"` |
| `Activities.Design`, `Diagnostics.OpenTelemetry`, `Diagnostics.StructuredLogs`, `Identity.Iam`, `Identity.ProviderConfiguration`, `Secrets`, `Studio.Preferences`, `Workflows.Design`, `Workflows.Runtime.Distributed.Placement`, `Workflows.Runtime.Distributed.CommandTransport` | None. Their rows carry no persisted-schema stamp. `StudioPreferenceRecord.SchemaVersion` holds a client-supplied preference schema, and `Activities.Design`'s `MaterialSchemaVersion` versions idempotency material, so neither is a row stamp. |

- **Stamping.** Every write assigns the family's compile-time constant to the row's `SchemaVersion` column. There are
  38 such assignments across 30 store files under `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore`,
  `src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore` and
  `src/extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore`. The stamp is a column beside
  the content. No hash covers it: the identity hashes cover scope and id, `Elsa3ImportRecordCodec`'s `ContentHash`
  covers the stored JSON, and its binding hash covers domain fields only.
- **Checking.** `EfSchemaVersion` in `src/essentials/Persistence/EntityFramework` is the one comparison. It is an
  ordinal equality check, so an older row is refused exactly like a newer one, and a missing stamp is refused as
  skew. It raises `EfSchemaVersionSkewException`, which a test in `EfSchemaVersionTests` keeps unassignable to every
  exception type the stores' catch filters name. There are 36 call sites in 30 files, including two readers outside
  the owning module: `EfWorkflowPortfolioDataSource` and `EfWorkflowRunHealthDataSource` in `Workflows.Dashboard`
  read `RuntimeArtifact` and `RuntimeOperationalState` rows directly. Each call site passes the family name as a
  string literal.
- **Ordering.** #1955 moved the version term to the front of its condition, and `EfSchemaVersionOrderingGuardTests`
  fails the build when another clause precedes a `Readable` or `NotReadable` term *inside one condition*. It does not
  see separate statements. `EfExecutionLivenessStateStore`, `EfWorkflowHoldStateStore`,
  `EfWorkflowAlterationStore` (`ReadPlan`) and `WorkflowTestScopeEfSupport` deserialize the content into the current
  type before the version term runs. `EfActivityPublicationReceiptStore` and `EfActivityDraftTestRunStore` check
  identity projections in an earlier statement.
- **Content.** Most families store their content as a JSON document. `RuntimeArtifactJson` and `PublishingEfJson`
  ignore unknown members on read, since no store sets `JsonUnmappedMemberHandling.Disallow`, and
  `RuntimeArtifactJson` writes null members. A predecessor's reader therefore tolerates members it does not know,
  and a predecessor that rewrites a newer row drops them without an error. ADR 0077's amendment names that
  data-loss mechanism.
- **Fixtures.** No Runtime or Publishing EF family has a committed payload fixture. The only golden fixtures over
  persisted runtime shapes are the Distributed leaf's `Fixtures/v1/executionPlacement.json` and
  `executionCommandTransport.json`, driven by `GoldenFixtureTestSupport`, whose failure message already states the
  rule this spec adopts: "bump the schema version, add an upcaster, add a new versioned fixture, and keep the old
  one".
- **Compression.** Spec 170's `EfPayloadCodec` marks a compressed payload in band (`elsaz1.`), below the schema
  version, so it is independent of this chain.

## Writing the predecessor format

The rollout decided on 2026-09-23/24 has a new version keep writing the old format until finalization. The design
hypothesis is that this needs no down-transform when the change is expand-only. Before finalization a host stamps
rows with the old version and leaves new-only fields unset. Features that need those fields are dormant (spec 182)
and writes that need them are refused. After finalization the host stamps the new version and writes the new fields.

**Verdict: it holds for the fifteen stamped families, under two conditions. It does not hold for rows that carry no
stamp.**

It holds because:

- the stamp is a separate column assigned at write time, so a host can stamp the old version without touching any
  hash or identity (see Current state);
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
which version wrote such a row, so neither the chain nor finalization can reason about it. See Open Questions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume a workflow several versions later (Priority: P1)

A workflow suspends while the host runs version 1 of the runtime's families. The fleet is upgraded twice. When a
stimulus arrives, a host running version 3 loads the suspended execution, bookmarks and scheduler state, and resumes
it.

**Why this priority**: This is the capability the chain exists for. Rows outlive versions (ADR 0078, "The first
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
   **Then** the save is refused with spec 182's dormancy refusal and nothing is written.
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
  their stamp indefinitely (Open Questions).
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
  way a row moves forward: no read, startup step or background task rewrites rows as a side effect.
- **FR-015**: A write MUST NOT lower a row's stamp. When a host reads a row stamped later than its write version, it
  MUST re-read the finalized version before writing that row, because a row at version V exists only once V is
  finalized. If the record does not confirm V, the write is refused.
- **FR-016**: When the write version is older than the build's current version, the host MUST write that version's
  format, leaving every member and column introduced after it unset. If the value being written carries data in any
  such member, the write MUST be refused with spec 182's dormancy refusal before anything is saved, and never
  written with the data dropped.
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
  pair stays committed. The scan, and how a release records its proof, are outside this spec (Open Question 4).

**Performance**

- **FR-025**: Performance is stated qualitatively, since performance measurement is retired (#1668, ADR 0073) and
  nothing here is measured. A row at the current version costs one membership test against the readable set in
  place of today's equality check. The chain is resolved once per process, never per read. An older row costs one
  in-memory transform per step, paid on each read until the row is rewritten, and no extra database round trip.
  Upgrade-on-write adds no write that was not already happening.

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

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every family that declares a chain longer than one, a fixture row at the oldest readable version
  reads through the current build's store and equals the current-version fixture.
- **SC-002**: For all fifteen families, rows stamped above the chain, below it and with no stamp raise skew. None of
  them is reported as corruption.
- **SC-003**: With the finalized version held at the predecessor, a full workflow run on a host at the new version
  leaves zero rows stamped at the new version.
- **SC-004**: After finalization, a row read and written once carries the new stamp. A row that was never written
  keeps its old stamp and still reads.
- **SC-005**: Removing an upcaster from inside a chain, which leaves a gap, fails the build, and so does editing a
  shipped fixture.
- **SC-006**: The extended ordering guard fails on a fixture that deserializes before checking the version, and
  passes on the restructured read paths named in Current state.
- **SC-007**: Upcasting the same fixture in two separate processes produces byte-identical output.

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
- Dormant features and refusal of writes that need new-version data: spec 182 (B6).
- Cluster membership (B1 [#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097), B2
  [#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)) and the readability report (B3,
  [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)), which consumes FR-001's declaration.
- Version-aware placement (B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) and the
  expand-only migration guard (B8, #2104).
- The scan that proves a version can be retired, and any eager rewrite of rows.
- Rewriting `docs/serialization.md` "Schema evolution (Runtime EF Core module)". Its "clean-break pre-GA change"
  procedure gives way to this chain after 4.0. The implementation updates that section, not this spec.

## Open Questions

1. **Which unit is versioned and finalized: the schema family or the EF module?** ADR 0077, ADR 0078 and #2099 say
   "per module". The code stamps per family (15), while migrations, the activation guard and `[UsesEfModule]` work
   per EF module (13). This spec recommends the family, because it is what a row stamps and what the skew check
   compares, so the chain and finalization key on the same identity. Refusal is still surfaced per EF module (spec
   181). The alternative is one version per EF module, with each family's stamp derived from it.
2. **How do the ten unstamped EF modules and the two unstamped Publishing tables join?** Their first schema change
   needs a stamp. Adding a nullable stamp column is expand-only, but a missing stamp would then have to read as a
   declared baseline, which reverses today's rule that a missing stamp is skew (`EfSchemaVersionTests`,
   `A_row_carrying_no_version_is_skew_rather_than_a_null_reference`). The recommendation is to give every such table
   a stamp in its 4.0 baseline, before #1976 freezes the `Initial` migrations. While migrations are still being
   regenerated, that costs one column and no upgrade path.
3. **Should a content change that is not additive ever be allowed?** A rename, retype or restructure of a JSON
   member cannot be written in the old format by leaving fields unset. The recommendation is that no down-transforms
   exist: such a change is split across two versions (add the new member beside the old one, then remove the old one
   once no finalized version reads it), and FR-022's round trip enforces the split. Should B8 own a guard over
   content shape as well, or is the fixture round trip enough?
4. **Rows that are never rewritten.** Content-addressed executables, receipts and similar rows are written once, so
   upgrade-on-next-write never reaches them. Their family can never retire a version without an eager rewrite. The
   existing `IEfPostMigrationAction` cannot carry that rewrite: `EfModuleMigrator` refuses the module at Prepare
   while an action's audit reports it required, and the rewrite can only run after finalization, which needs the
   module active. Using it would deadlock. What mechanism rewrites such rows, and does it belong to B4 or a new
   workstream?
5. **Identity-changing format changes.** If a future executable format would change `WorkflowExecutableHasher`'s
   output, the artifact's identity changes, and FR-020 forbids expressing that as an upcaster. How should such a
   change keep the Elsa constitution's §E2.6.1 (executable-always-runs) promise?
6. **Glossary entries.** "Schema family", "stamp", "readable set", "upcaster" and the terms of specs 181 and 182 have
   no entry in `docs/glossary/elsa.md`. Should they be added when these specs are approved?
