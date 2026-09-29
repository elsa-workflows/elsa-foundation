# Feature Specification: Schema Finalization Gate

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: In progress — B5 ([#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)) built the gate, the write check, both activation refusals and the CLI; B5b ([#2143](https://github.com/elsa-workflows/elsa-foundation/issues/2143)) composed the fleet on `Elsa.Foundation.Host` and made it reachable from feed-loaded EF modules on both hosts (see the 2026-09-29 note); FR-020a, the Modularity API step, is not built yet (see the 2026-09-28 note).
**Input**: Workstream B5, [issue #2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). A host
reads both formats as soon as it runs a new persisted-schema version, and keeps writing the old format until that
version is finalized. Finalization is automatic once every live host can read the new version, unless an operator
holds it. After finalization, writers switch. Finalization is the rollback boundary. A host that later comes back
unable to read a finalized version refuses the module. A single host is a cluster of one and finalizes immediately.

Decisions of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
("The first consumers", "Features that need the new data wait for finalization", "Invariants every membership or
actor provider must preserve") and
[ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) ("How this
composes with runtime module installation", "Known gap"). Both are accepted with these decisions through B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096), PR #2118). The refusal paths this spec
reuses are decided in [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md), D9 and D13,
and specified in [spec 171](../171-persistence-script-cli/spec.md).

Companion specs: [spec 180](../180-schema-upcaster-chain/spec.md) (B4,
[#2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100)) defines schema families, stamps, readable
sets and the write path that stamps only what this spec finalizes. [Spec 182](../182-dormant-features-until-finalization/spec.md)
(B6, [#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)) keeps features that need new-version
data dormant until this gate finalizes, and carries the gate's status to operators. [Spec 183](../183-cluster-membership/spec.md)
(B1 to B3) supplies the fleet view and readability report this spec's "Requirements on membership" depends on.

## Terms

Spec 180's Terms apply: EF module, schema family, stamp, readable set, write version. In addition:

- **Finalized version**: per database and schema family, the newest version every host may write. It is durable
  and only moves forward.
- **Counted member**: a live member of the fleet whose readability report declares the family and names this
  database's identity or names none, or whose report is unknown. A member counts whether it is joining, active or
  draining, and whether or not it is displaced, as long as it has not left or expired. Counting a displaced-but-live
  incarnation, one with an unknown report, and one that has not yet read the record's database identity is
  conservative: none of them can ever make finalization happen too early, only delay it (spec 183, FR-023; Decisions,
  Q11).
- **Hold**: an operator's durable instruction that a family must not finalize, optionally limited to one version.
- **Observed finalized version**: the finalized version as a host last read it. A host's write version for a family
  is its observed finalized version.

The fleet view, members, incarnations and liveness are defined by B1
([#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097)), and the readability report by B3
([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)), both specified in
[spec 183](../183-cluster-membership/spec.md). This spec states what it needs from them under "Requirements on
membership" and does not design them. It says "readability report" where ADR 0078 and #2097 say "capabilities",
because the [root glossary](../../docs/glossary/root.md) retires "Capability" in favour of "feature".

## Current state

- **Refusal at enable time.** `EfPendingMigrationActivationGuard` (`src/essentials/Modularity/EntityFramework`)
  implements `IFeatureActivationGuard`. It maps a feature to its EF modules through `[UsesEfModule]` and, under
  `Migrate:Policy=Validate`, refuses a feature whose module has a pending migration.
  `FeatureManagementService.ApplyAsync` runs the guards before it saves, and `ModularityFaultRenderer` maps
  `FeatureActivationRefusedException` to HTTP 409, so nothing is saved or reloaded. Under `AutoMigrate` the guard
  opens nothing and passes (spec 171, FR-068 and FR-069).
- **Refusal at activation.** `EfModuleMigrator<TContext>` runs as a CShells shell initializer at
  `LifecyclePhase.Prepare`, and as an `IHostedService` on plain hosts. It applies or validates migrations, then audits
  post-migration actions. `EfPendingMigrationsException` or `EfPostMigrationRequiredException` stops the shell from
  activating before any shell task or seeder touches a store. That is shell-granular: the whole shell is refused,
  not only the module.
- **Refusal at package load.** ADR 0076's D13 and ADR 0077 describe an Elsa implementation of Nuplane's
  `IPackageActivationGate` that reads `[EfModule]` metadata without loading the package. **None exists in the tree.**
  The #1951 refusal was delivered at Nuplane resolution instead (PR #1979). This spec therefore does not depend on a
  load-time gate.
- **Membership.** Nothing records which hosts exist. `WorkflowsRuntimeDistributedFeature` has a `NodeId` setting that
  defaults to a machine- and process-derived value, and per-execution placement leases carry an owner id and an
  expiry (`IExecutionPlacementStore`). There is no incarnation, status or readability report.
- **Reload.** `Elsa.Foundation.Host` reloads shells after a Nuplane reconciliation (`ShellReloadOnPackagesChanged`)
  and through `ShellReloader`. `Elsa.Workbench` registers `NullShellReloader`, which does nothing. Anything that must
  take effect without a restart therefore cannot depend on a shell reload.

## The invariant and how it holds

**Invariant.** No row is ever stamped with a version that some counted member cannot read. No member that cannot
read a family's finalized version keeps that family's EF module active.

It holds through five mechanisms, each tested separately (Success Criteria):

1. **Writers stamp only the finalized version** (spec 180, FR-013 and FR-015). Before finalization a row at the new
   version does not exist, and after it every counted member can read it.
2. **Finalization is an intent followed by a confirmation.** An evaluator first records durably that it intends to
   finalize V. Only then does it read the fleet view again, and it commits only if every counted member in that
   second read can read V. A member that is about to activate the module does the mirror image: it publishes its
   readability report first, then reads the finalization record. If both stores give read-after-write
   consistency, at least one side always sees the other. Either the evaluator sees the newcomer and abandons the
   intent, or the newcomer sees the intent or the finalization and does not activate. This works across two
   separate stores, so it does not need the membership provider and the module database to share a transaction.
3. **Finalization is monotonic and durable** (FR-003). No surface moves it back.
4. **Activation refuses an unreadable finalized version** through the paths above (FR-015 to FR-017).
5. **Active hosts keep checking** (FR-012). A host that missed a finalization, for instance because it was
   partitioned and counted as expired, finds out at its next refresh, or when it meets a newer row. It then refuses
   every write to that family instead of rewriting rows it cannot read. A member that has merely lapsed, without
   missing a finalization it cannot read, keeps writing at the version it last observed (FR-018).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A rolling upgrade finalizes itself (Priority: P1)

Three hosts run version 1 of a family and share a database. The operator applies an expand-only migration, then
upgrades the hosts one at a time. While any counted member cannot read version 2, every host writes version 1. When
the last host reports it can read version 2, the version finalizes without any operator action, and every host
starts writing version 2.

**Why this priority**: This is the program's acceptance criterion: pods activate in any order, and no host ever reads
a row it cannot understand.

**Independent Test**: Two hosts with the durable membership provider share one database. Upgrade one host and run
workflows on both. Every row stays stamped `1`. Upgrade the second host, wait one evaluation interval, run workflows
again, and assert the new rows are stamped `2`.

**Acceptance Scenarios**:

1. **Given** counted members reading {1} and {1, 2}, **When** evaluation runs, **Then** version 2 stays pending and
   every host's write version stays `1`.
2. **Given** every counted member reads {1, 2} and no hold exists, **When** evaluation runs, **Then** version 2 is
   finalized, the record names the member that finalized it and when, and within one refresh interval every host's
   write version is `2`.
3. **Given** version 2 finalized, **When** a host that has not yet refreshed writes, **Then** it writes version 1,
   which every counted member can read. The one exception is a row already stamped `2`: spec 180's FR-015 makes the
   host refresh before writing it.

---

### User Story 2 - A host that cannot read a finalized version refuses the module (Priority: P1)

After version 2 is finalized, an operator rolls one host back to the version-1 binary, or a new host joins with the
old package. That host refuses the family's EF module, naming the family, the finalized version, the versions it can
read and the remedy. It reads and writes nothing.

**Why this priority**: Finalization is the rollback boundary. Refusal is what keeps a host on the far side of that
boundary from misreading or rewriting new-format rows, and it is ADR 0078's guarantee that a rolled-back single host
fails cleanly.

**Independent Test**: Finalize version 2, then start a host whose readable set is {1}. Assert that its shell does not
activate and that the database is byte-identical before and after the attempt. Then start a host whose readable set
is {1, 2} and assert it activates, so the refusal is not blanket.

**Acceptance Scenarios**:

1. **Given** version 2 finalized, **When** a host with readable set {1} starts, **Then** the Prepare-phase check
   refuses the EF module and the shell does not activate, exactly as a pending migration under `Validate` does today.
2. **Given** the same host running with the module not yet enabled, **When** an operator enables a feature that uses
   the module under `Validate`, **Then** the Modularity API answers 409 with the refusal and nothing is saved.
3. **Given** a host with readable set {1, 2}, **When** it starts, **Then** it activates normally.

---

### User Story 3 - A single host finalizes immediately (Priority: P1)

A non-clustered host uses the in-process membership provider. It is upgraded to version 2 and, when the module
activates, version 2 is finalized at once. It writes no membership table and no heartbeat. Features that need
version 2 are available on the first request.

**Why this priority**: Non-clustered hosting must pay nothing for this program (#2093 acceptance; #2097).

**Independent Test**: Start a single host at version 2 against a database finalized at version 1, and assert that the
record says `2` once activation completes and that no membership table exists.

**Acceptance Scenarios**:

1. **Given** the in-process provider and no hold, **When** the module activates, **Then** its families finalize at
   the host's current versions before the shell starts serving requests.
2. **Given** the in-process provider and a hold, **When** the module activates, **Then** nothing finalizes and the
   status names the hold.

---

### User Story 4 - An operator holds finalization during a canary (Priority: P2)

Before starting a rollout, an operator places a hold on a family and gives a reason. The rollout completes, every
host can read version 2, and nothing finalizes. After observing the canary, the operator releases the hold, and the
version finalizes at the next evaluation. If the canary fails, the operator rolls back the binaries, which is safe
because no row at version 2 exists.

**Why this priority**: It keeps the rollback boundary under operator control exactly when rollback must stay clean
(ADR 0078).

**Independent Test**: Place a hold through the CLI with no gate-aware host running, roll both hosts to version 2,
confirm nothing finalizes across several evaluation intervals, release, and confirm finalization.

**Acceptance Scenarios**:

1. **Given** a hold on the family, **When** every counted member reads version 2, **Then** version 2 stays pending
   and the status names the hold, its reason and who placed it.
2. **Given** a hold released, **When** evaluation next runs, **Then** version 2 finalizes if every counted member
   reads it.
3. **Given** version 2 already finalized, **When** an operator tries to hold it, **Then** the command is refused and
   says the rollback boundary has been crossed.
4. **Given** an intent to finalize version 2 in progress, **When** a hold is placed, **Then** the intent is abandoned
   and version 2 stays pending.

---

### User Story 5 - Membership changes during evaluation never finalize too early (Priority: P2)

While an evaluator is finalizing version 2, a host that can read only version 1 joins. Whatever the interleaving,
the outcome is never "version 2 finalized and the version-1 host active".

**Why this priority**: This is the case that looks like success while being wrong. The gate would report
"finalized" and a host would still be serving version-1 rows.

**Independent Test**: Drive the evaluator's steps (write intent, read view, commit) and the joiner's steps (publish
report, read record, activate) through every interleaving, with fakes for both stores, and assert the invariant
after each one.

**Acceptance Scenarios**:

1. **Given** the joiner publishes before the evaluator reads the view, **When** the evaluator confirms, **Then** it
   abandons the intent.
2. **Given** the joiner reads the record after the intent was written, **When** it checks, **Then** it waits for the
   intent to resolve, and refuses if the intent resolves to finalized or does not resolve within the bound.
3. **Given** a member whose liveness expires during evaluation, **When** the evaluator confirms, **Then** that member
   is not counted, and FR-018 governs it if it comes back.

---

### Edge Cases

- **An evaluator crashes after writing the intent.** Any counted member may resolve it by running the confirmation
  again. Resolution is idempotent.
- **Two evaluators race.** Both write the same intent. Commit and abandonment are compare-and-set moves from the
  intent. Whichever lands first decides, and mechanism 2 makes either outcome safe.
- **Several versions pending at once.** Finalization may advance more than one version in one step. It lands on the
  newest version every counted member can read, which can never be past any member's current version.
- **A database with no record yet.** The first activation records the activating host's oldest readable version as
  finalized, then evaluates normally. On a fresh database, or one written only by the first gate-aware release, that
  is exactly the version its rows carry.
- **A host partitioned during finalization.** It was not counted. When it reconnects, FR-012 and FR-018 apply: it
  refreshes, finds a finalized version outside its readable set, and refuses writes to that family. Its reads of
  newer rows already raise skew (spec 180, FR-007).
- **A misconfigured cluster.** Several hosts share a database and each uses the in-process provider, so each thinks
  it is alone and would finalize at once. Nothing detects or warns about this; a cluster declares itself only by
  composing a durable membership provider (FR-021; Decisions, Q8).
- **One host, two shells, two databases.** The record lives in each database, so each database finalizes on its own.
  The host's readability report is the same for both.
- **An EF module whose families reach different versions.** Each family finalizes independently. Refusal is per EF
  module: one unreadable family refuses the whole module.

## Requirements *(mandatory)*

### Functional Requirements

**State and storage**

- **FR-001**: For each schema family, the gate MUST keep a durable finalization record: an opaque per-database
  identity, created once when the record is first written and never derived from a connection string; the finalized
  version; at most one in-flight intent (a version, the member that wrote it, and when); the active holds; and an
  append-only history of transitions. Each history entry names the member (host id and incarnation) or the operator
  responsible. Amended 2026-09-28 (spec 186, Decisions): the record also carries a finish record, per spec 186's FR-014
  to FR-016: the completion version, the verification pass's start and end instants, and the member (host id and
  incarnation) that ran it, with its own append-only history of completion and withdrawal.
- **FR-002**: The record MUST live in the database that holds the family's tables, beside its EF module's
  migrations-history table, whichever membership provider is active (ADR 0078, "The first consumers"). It is created
  through the same migration mechanism as the module's own tables, so `Validate` refuses a module whose database
  lacks it. It MUST land in every EF module's 4.0 baseline (`Initial`) migration, before #1976 freezes it, so no
  module needs a post-4.0 migration just to take part in the gate.
- **FR-003**: The finalized version MUST only move forward along the family's chain. Every change is a
  compare-and-set against the previous value. No surface, operator command included, may lower it or remove it.
  Rolling back past it requires restoring a database backup taken before finalization, which this spec does not
  provide.
- **FR-004**: For each version above the finalized one, the gate's state MUST be one of **Pending** (some counted
  member cannot read it, or a hold applies) or **Readable everywhere** (an intent exists and is being confirmed).
  **Finalized** is terminal. A hold is an overlay: it keeps a version in Pending and abandons an intent.

**Evaluation**

- **FR-005**: Any counted member with the family's EF module active MAY evaluate. Evaluation runs when the module
  activates, when the fleet view changes if the provider signals changes, and on a bounded, configurable interval as
  a backstop, defaulting to 30 seconds. Outcomes are idempotent, so concurrent evaluators cannot contradict each
  other.
- **FR-006**: An evaluation MUST move a version from Pending to Readable everywhere only when no hold applies and
  every counted member in its fleet view — restricted to members whose readability report names this database's
  identity or names none — reports the version in its readable set. It MUST write the intent durably before its
  confirming read of the fleet view.
- **FR-007**: The confirming read MUST happen after the intent is durable. If every counted member in that read can
  still read the version and no hold has appeared, the evaluator commits Finalized. Otherwise it abandons the intent
  and the version returns to Pending.
- **FR-008**: An intent MUST be resolvable by any counted member, so a crashed evaluator cannot leave a family stuck.

**Writers**

- **FR-009**: A host's write version for a family MUST be its observed finalized version (spec 180, FR-013). A host
  whose current version is newer keeps writing the observed version's format.
- **FR-010**: A host MUST refresh its observed finalized version when the module activates, after every evaluation it
  runs, on a bounded, configurable interval defaulting to 15 seconds, and before writing a row stamped later than its
  write version (spec 180, FR-015).
- **FR-011**: Switching writers MUST need no restart and no shell reload, so it behaves the same on
  `Elsa.Foundation.Host` and on `Elsa.Workbench`, whose reloader does nothing.
- **FR-012**: When a refresh finds a finalized version outside the host's readable set, the host MUST refuse every
  write to that family with a typed error naming the family and both versions. It MUST report the condition through
  the gate's status (FR-022). It MUST NOT keep writing at its old version.

**Refusing a host that cannot read**

- **FR-013**: Before a member activates an EF module, its readability report for that module's families MUST be
  published to the fleet view. Only then does it read the finalization record (mechanism 2).
- **FR-014**: If the record shows an intent for a version outside the member's readable set, the member MUST wait for
  the intent to resolve, up to a bounded time defaulting to 2 minutes. It proceeds only if the intent was abandoned,
  and refuses if the intent was committed or is still unresolved.
- **FR-015**: At activation, if any family of an EF module has a finalized version outside the host's readable set,
  the host MUST refuse the module. The check runs in the Prepare-phase initializer that `EfModuleMigrator` occupies,
  and in its hosted-service form on plain hosts, after migrations are applied or validated and after the
  post-migration audit, and before any shell task, seeder or store touches the module's tables. It throws a typed
  refusal, so the shell does not activate, as a pending migration under `Validate` does today. Amended 2026-09-28
  (spec 186, Decisions): the same check MUST also refuse the module when a family's finish record names a completion
  version outside the host's readable set (spec 186, FR-020), for the same reason and at the same point.
- **FR-016**: At enable time, under both `Validate` and `AutoMigrate`, an `IFeatureActivationGuard` MUST read the
  finalization record and apply the same check to every feature that `[UsesEfModule]` maps to the module, returning a
  `FeatureActivationRefusal` that the Modularity API renders as 409, with nothing saved. This is the narrowing spec
  171's FR-068 and FR-069 state (Dependencies): before this spec is built, the guard opens no database and passes
  under `AutoMigrate`, following the pre-narrowing text those requirements describe, and FR-015 remains the refusal
  point under `AutoMigrate` until then. Saving an enable request that will fail at Prepare is exactly the half-applied
  state this design exists to avoid.
- **FR-017**: A refusal MUST name the feature where there is one, the EF module, the family, the finalized version
  and the host's readable set, and give the remedy: run a version that can read the finalized version, or restore a
  pre-finalization backup. Like every activation refusal, it MUST NOT contain a connection string or any other
  restored secret (spec 171, FR-061).
- **FR-018**: A member that learns its membership has lapsed MAY keep writing through a gated EF module at the write
  version it last observed as finalized (FR-009): that version is still readable by every counted member, and spec
  180's FR-018 already refuses it overwriting a row it cannot read. It MUST NOT write any version newer than that
  observed version until it has rejoined as a new incarnation and passed FR-013 to FR-015 again (spec 183, Decisions,
  Q22). This narrows the rule so that a membership-store outage no longer stops gated writes fleet-wide by itself.

**Holds**

- **FR-019**: An operator MUST be able to place a hold on a family, optionally limited to one version, with a
  required reason, and to release it. Placing a hold on a finalized version MUST be refused. Hold and release are
  recorded in the history with the operator identity given and a timestamp.
- **FR-020**: Hold, release and status MUST be available at least through the `dotnet elsa persistence` CLI, as new
  subcommands that follow the existing ones: `--host`, one of `--modules`, `--all` or `--from-host`, `--provider`,
  `--connection-env` or `--connection-stdin`, and the existing exit-code table. The CLI writes the record directly,
  so a hold can be placed before any gate-aware host runs, which a canary requires. No command finalizes, forces
  finalization or lowers a finalized version. The CLI remains the only surface available before any gate-aware host
  runs (FR-020a).
- **FR-020a**: As a second step, the Modularity API MUST also offer hold, release and status, under the existing
  `module-management.manage` host-control permission, mapping the same typed refusals (for instance, placing a hold
  on a finalized version) to the same HTTP responses the CLI's exit codes report. This does not replace the CLI: a
  hold must still be placeable before any host is running.

**Single host and provider kind**

- **FR-021**: Under the in-process membership provider, the fleet view is the host alone, so the module's families
  finalize during the Prepare-phase check unless a hold applies, whatever features the host composes. It MUST write
  no membership table and no heartbeat. The finalization record is still written, because it is a fact about the
  database (ADR 0078). A cluster declares itself solely by composing a durable membership provider (spec 183); using
  the in-process provider is itself the declaration that a host is alone. This spec adds no check for, and no
  startup warning about, a host that composes a feature needing a cluster while membership stays in-process: that
  mismatch is not diagnosed here. A host that was missed this way is caught loudly, not silently, once the
  mismatch bites: it refuses the family's writes at its next refresh (FR-012) or refuses the module outright once
  it observes a finalized version outside its readable set (FR-015) (Decisions, Q8).

**Reporting**

- **FR-022**: For each family, the gate MUST expose its status: the finalized version, each pending version and why
  it is pending (a hold with its reason, or the counted members that cannot read it), and any intent in flight.
  Spec 182 carries this to the feature catalog and Attention. The CLI's status subcommand prints it.

### Requirements on membership (B1, B3)

These are what the gate needs from #2097 and #2099. They are stated as requirements on those workstreams, not as a
design of membership.

- **MR-001**: A fleet view: the set of members, each with a host id, an incarnation, a status (joining, active,
  draining, left) and liveness by heartbeat and expiry (ADR 0078, Decision).
- **MR-002**: A readability report per member: for each schema family whose declaration the member has loaded, its
  readable set (spec 180, FR-001 and FR-004). The report is derived from those declarations and no configuration can
  change it.
- **MR-003**: Read-after-write consistency: once a member's report is published, every later read of the fleet view
  includes it. Mechanism 2 depends on this, and on the same property of the finalization record.
- **MR-004**: Publish before activate: a member's report reaches the fleet view before any module it covers
  activates, including a module that arrives through a Nuplane reconciliation on a running host.
- **MR-005**: Lapse awareness: a member can tell that its own membership has lapsed, so FR-018 can act on it.
- **MR-006**: The provider kind is visible, so FR-021 can tell the in-process cluster of one from a durable fleet.
- **MR-007**: The query from #2099's acceptance: "can every live member read family F at version V?". Its answer
  also lists the members that cannot, for FR-022.

### Key Entities

- **Finalization record**: per database and family. It holds an opaque per-database identity, the finalized version,
  at most one intent, holds, and history.
- **Intent**: a durable "about to finalize V", written before the confirming membership read.
- **Hold**: family, optional version, reason, placed by, placed at.
- **Counted member**: a live member whose report declares the family, or whose report is unknown; includes a
  displaced-but-live incarnation (Terms).
- **Observed finalized version**: a host's cached copy of the record, and its write version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a two-host test over one database, upgrading one host leaves zero rows at the new version. Upgrading
  the second finalizes the version within one evaluation interval, with no operator action.
- **SC-002**: With a hold in place, no finalization happens across at least three evaluation intervals while every
  host reads the new version. Releasing the hold finalizes it within one interval.
- **SC-003**: A host whose readable set excludes the finalized version is refused at Prepare, and under `Validate`
  also with a 409 at enable time. The database is unchanged by the attempt. A host that can read the version is not
  refused.
- **SC-004**: Across every interleaving of the evaluator's three steps and a joiner's three steps, the invariant holds.
- **SC-005**: A single host finalizes at first activation and creates no membership table.
- **SC-006**: No CLI command or API lowers or removes a finalized version. The attempt fails and the record is
  unchanged.
- **SC-007**: After finalization, writers switch without a restart on both `Elsa.Foundation.Host` and
  `Elsa.Workbench`.
- **SC-008**: A host that was counted as expired during finalization, and then reconnects, refuses writes to the
  family within one refresh interval and rewrites no newer row.

## Assumptions

- The gate lands in a release that changes no persisted schema, and every host in a cluster runs a gate-aware release
  before the first schema-changing release after 4.0 (#2093, "Deadline"). A pre-gate binary knows nothing of the
  record, so the gate cannot protect against it. The binary an operator rolls back to must be gate-aware too.
- Migrations are regenerated until 4.0 ships as a stable release, per
  [spec 180's Assumptions](../180-schema-upcaster-chain/spec.md#assumptions).
- Migrations for a cluster rollout are expand-only (B8, [#2104](https://github.com/elsa-workflows/elsa-foundation/issues/2104)),
  so hosts on the older version keep working against the migrated schema until finalization.
- The finalization record's database supports compare-and-set updates. All four supported EF engines do.

## Dependencies

- **B1** (#2097), the membership contract, and **B3** (#2099), the readability report, both specified in
  [spec 183](../183-cluster-membership/spec.md). This spec cannot be implemented until MR-001 to MR-007 are met, and
  it needs the in-process provider at minimum.
- **B2** ([#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)), the durable membership provider,
  for any clustered test or deployment.
- **Spec 180** (B4, #2100), for the family declaration, readable sets and the write path.
- [**Spec 171**](../171-persistence-script-cli/spec.md) (persistence CLI), whose FR-068 and FR-069 are narrowed, by
  this program (#2093), so `EfPendingMigrationActivationGuard` reads the finalization record under both `AutoMigrate`
  and `Validate` (FR-016; Decisions, Q10). That narrowing is recorded in spec 171's own text; this spec's FR-015 and
  FR-016 are what it defers to for the check.

## Out of Scope

- Designing membership, its providers or its conformance suite (B1, B2, B3).
- Version-aware placement (B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)). Finalization
  stops a version being written too early, and placement stops work landing on a host that cannot run it. ADR 0078
  requires both.
- The expand-only migration guard (B8, #2104).
- Dormancy and the refusal of writes that need new-version data (spec 182, B6).
- Restoring a database to before finalization.
- An Elsa `IPackageActivationGate` implementation. If one is built under ADR 0076's D13, it SHOULD apply FR-015's
  check from package metadata, but nothing here depends on it.

## Decisions

Recorded 2026-09-27, when the owner answered this spec's open questions on #2093.

- **Q7 — Unit of finalization: the schema family.** Follows spec 180's Q1: a row stamps a family and the skew check
  compares families, so the record is keyed per family.
- **Q8 — Detecting a misconfigured cluster.** A cluster must declare itself: composing a durable membership provider
  is the declaration. There is no startup warning and no detection of hosts sharing a database while each uses the
  in-process provider (FR-021) — against the draft's own recommendation, a read-only startup diagnostic. A missed
  declaration fails loudly, not silently, once it bites: through FR-012's write refusal or FR-015's activation
  refusal.
- **Q9 — An HTTP surface for hold and release.** Yes, as a second step, under the existing `module-management.manage`
  host-control permission (FR-020a). The CLI stays the pre-host path (FR-020).
- **Q10 — Enable-time refusal under `AutoMigrate`.** The check reads the finalization record under both policies
  (FR-016), narrowing spec 171's FR-068 and FR-069, amended by this program (#2093) (Dependencies).
- **Q11 — Database identity in reports.** Yes: an opaque identity, created once in the finalization record (FR-001).
  Evaluation counts only members whose readability report names it or names none (FR-006; Terms, Counted member).
- **Q12 — Default intervals.** Evaluation every 30 seconds, refresh every 15 seconds, and a joining host's wait bound
  of 2 minutes (FR-005, FR-010, FR-014).
- **Q13 — Landing the record table.** In every EF module's 4.0 baseline, before #1976 freezes the `Initial`
  migrations (FR-002).

Recorded 2026-09-28, as a minimal amendment for spec 186 (B9), which builds fields onto this spec's approved record
rather than a record of its own.

- **The finalization record gains a finish record.** Spec 186's completeness proof lives inside this record, per
  database and family, so it shares the record's identity, migration and history mechanism (FR-001). Activation and
  enable-time refusal (FR-015, FR-016) gain the matching condition: a family whose finish record names a completion
  version outside the host's readable set refuses the module, the same way an unreadable finalized version does.

**2026-09-28 note.** Found while building B5 (#2101); lands with the B5 PR, whose merge is the owner's approval.

- **A report entry names a database only while it is the one this host serves.** Spec 183's FR-019 says the entry
  names the identity of the record the host "read most recently". A host that serves the family in two databases
  would then name one of them, and evaluators of the other would stop counting it (FR-006), so that database could
  finalize a version this host cannot read. The entry therefore names an identity only while every record of the
  family this host has read carries that one identity and no activation of the family is between publishing its
  report and reading its record (FR-013); otherwise it names none, which counts for every database. The observed
  finalized version is reported only while those databases agree on it.
- **An activating host resolves an intent it cannot read by abandoning it.** FR-014's joiner is a counted member
  that cannot read the intended version, so running the confirmation itself (FR-008) abandons the intent at once
  rather than waiting up to the bound for an evaluator that may have crashed. If the evaluator's commit lands first,
  the joiner's next read finds the version finalized and refuses. The bound still applies to an intent the joiner
  cannot resolve.
- **A host that composes no fleet never finalizes past the version a record was created at.** Without
  `Elsa.Cluster.Readability`'s `AddEfSchemaReadability` the gate cannot count anyone, and treating "no membership
  composed" as "alone" would finalize at once on a host whose durable provider was composed without the bridge, the
  silent direction. Such a host still creates records, refuses what it cannot read, refreshes, and holds its writes to
  what it observed. `Elsa.Foundation.Host` composes no membership today (B1 to B3 did not add it), so FR-021's
  finalization at activation needs membership composed there first; every chain has one version until 4.0 ships, so
  nothing observable differs yet.
- **The membership module is admitted after its join.** It is composed on the host container and migrated before the
  member joins, and the join is its publish, so its gate reads the record first and admits it again, publish first,
  at the first refresh after the join, adopting no newer version until then. The window between is closed loudly by
  FR-012.
- **FR-020a, the Modularity API's hold, release and status, is the second step and is not built here.** The CLI
  (FR-020) is. So is the gate's status as an in-process API (`EfSchemaModuleGate.ReadStatusAsync`), which spec 182
  carries to the feature catalog and Attention.
- **Every domain API answers both write refusals with a 409** (spec 180, FR-016a), carrying the code, the family and
  both versions. An API resolves no EF Core, so both refusals derive from `Elsa.Primitives`'
  `SchemaWriteRefusedException`, and `Elsa.Api.AspNetCore`'s `SchemaWriteRefusalProblem` is the one place the
  refusal's part of every envelope is decided. Stores that wrap a failed save in a failure of their own (Identity's,
  OpenTelemetry's, Structured Logs' and the Elsa 3 import's) now exclude the refusal from that wrapping and let it
  pass through unwrapped, the same way `EfSchemaVersionSkewException` already does (commit c41f8a250, "Let schema
  write refusals pass through store failure wrappers (#2101)").
- **The write check refuses deletes as well** while a family's writes are refused (FR-012), and it covers every
  write through `SaveChanges`. `ExecuteUpdate` and `ExecuteDelete` bypass it; no first-party store writes a stamp
  that way.
- **Spec 184's two requirements on this gate are built with it.** The Runtime EF module exposes its gate to the
  runtime as `IRuntimeSchemaFinalization`: the runnability entry names the database identity the gate read (spec 184,
  FR-008), a member whose writes to a Runtime family the gate refuses claims nothing and hands off what it holds
  (spec 184, FR-012), and a placement query names the database its execution lives in (spec 184, FR-009).

**2026-09-29 note.** Found and settled while building B5b ([#2143](https://github.com/elsa-workflows/elsa-foundation/issues/2143));
lands with the B5b PR, whose merge is the owner's approval.

- **Feed-loaded EF modules reach the host's fleet.** An EF module that arrives from a feed carries its own copy of
  `Elsa.Persistence.EntityFramework` in a load context of its own, so a fleet the host registered under that
  assembly's `IEfSchemaFleet` was a different type from the one the module asked for, and its gate found none: it
  admitted, and never finalized past the version its record was created at. `IEfSchemaFleet` and its answer types,
  `EfSchemaFinalizationObservations`, the record's model and status, the gate registry and the family catalog now live
  in `Elsa.Persistence.Schema`, which references no EF Core, under that assembly's own namespaces (`Elsa.Persistence.Schema`
  for the catalog and its descriptors, `Elsa.Persistence.Schema.SchemaFinalization` for the rest), and every host shares it
  with `Elsa.Cluster.Core` ([ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md),
  amended 2026-09-29, "Host-composed shares"). `Elsa.Cluster.Readability` therefore takes no EF Core either.
- **`Elsa.Foundation.Host` composes the fleet.** It calls `AddEfSchemaReadability` on its host container, as
  `Elsa.Workbench` does, so the 2026-09-28 note's "composes no membership today" no longer holds: FR-021's finalization
  at activation holds there, for its EF modules as for a feed-loaded module on `Elsa.Workbench`. It composes the
  in-process default only; the durable EF provider would bring EF Core into a host that carries none (ADR 0076), so
  `Elsa.Foundation.Host` is a cluster of one.
- **The gate keeps running after shell activation.** CShells resolves a shell's initializers in a scope it disposes
  once they have run, and `AddShellInitializer` exposes an initializer through a transient factory, so that scope
  disposed the singleton `EfModuleMigrator`, and with it the gate's background evaluation and refresh (FR-005, FR-010,
  FR-011), right after activation, on both hosts. A version this host could finalize after activation, once a hold was
  released or the last older member was upgraded, never was, and a finalization another host committed was adopted only
  on demand. The migrator's initializer exposure is now a singleton, which the shell's container alone owns, as the
  Tasks feature already keeps its task manager out of that scope.
