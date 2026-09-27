# Feature Specification: Schema Finalization Gate

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: Draft
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
composes with runtime module installation", "Known gap"). Both are Proposed, and B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096)) accepts them. The refusal paths this spec
reuses are decided in [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md), D9 and D13,
and specified in [spec 171](../171-persistence-script-cli/spec.md).

Companion specs: [spec 180](../180-schema-upcaster-chain/spec.md) (B4,
[#2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100)) defines schema families, stamps, readable
sets and the write path that stamps only what this spec finalizes. [Spec 182](../182-dormant-features-until-finalization/spec.md)
(B6, [#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)) keeps features that need new-version
data dormant until this gate finalizes, and carries the gate's status to operators.

## Terms

Spec 180's Terms apply: EF module, schema family, stamp, readable set, write version. In addition:

- **Finalized version**: per database and schema family, the newest version every host may write. It is durable
  and only moves forward.
- **Counted member**: a live member of the fleet whose readability report declares the family, or whose report is
  unknown. A member counts whether it is joining, active or draining, and whether or not it is displaced, as long as
  it has not left or expired. Counting a displaced-but-live incarnation and one with an unknown report is
  conservative: neither can ever make finalization happen too early, only delay it (spec 183, FR-023).
- **Hold**: an operator's durable instruction that a family must not finalize, optionally limited to one version.
- **Observed finalized version**: the finalized version as a host last read it. A host's write version for a family
  is its observed finalized version.

The fleet view, members, incarnations and liveness are defined by B1
([#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097)), and the readability report by B3
([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)). Neither has a spec yet. This spec states
what it needs from them under "Requirements on membership" and does not design them. It says "readability report"
where ADR 0078 and #2097 say "capabilities", because the [root glossary](../../docs/glossary/root.md) retires
"Capability" in favour of "feature".

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
5. **Active hosts keep checking** (FR-012 and FR-018). A host that missed a finalization, for instance because it was
   partitioned and counted as expired, finds out at its next refresh, or when it meets a newer row. It then refuses
   every write to that family instead of rewriting rows it cannot read.

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
  it is alone and would finalize at once. See FR-021 and Open Questions.
- **One host, two shells, two databases.** The record lives in each database, so each database finalizes on its own.
  The host's readability report is the same for both.
- **An EF module whose families reach different versions.** Each family finalizes independently. Refusal is per EF
  module: one unreadable family refuses the whole module.

## Requirements *(mandatory)*

### Functional Requirements

**State and storage**

- **FR-001**: For each schema family, the gate MUST keep a durable finalization record: the finalized version, at most
  one in-flight intent (a version, the member that wrote it, and when), the active holds, and an append-only history
  of transitions. Each history entry names the member (host id and incarnation) or the operator responsible.
- **FR-002**: The record MUST live in the database that holds the family's tables, beside its EF module's
  migrations-history table, whichever membership provider is active (ADR 0078, "The first consumers"). It is created
  through the same migration mechanism as the module's own tables, so `Validate` refuses a module whose database
  lacks it.
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
  a backstop. Outcomes are idempotent, so concurrent evaluators cannot contradict each other.
- **FR-006**: An evaluation MUST move a version from Pending to Readable everywhere only when no hold applies and
  every counted member in its fleet view reports the version in its readable set. It MUST write the intent durably
  before its confirming read of the fleet view.
- **FR-007**: The confirming read MUST happen after the intent is durable. If every counted member in that read can
  still read the version and no hold has appeared, the evaluator commits Finalized. Otherwise it abandons the intent
  and the version returns to Pending.
- **FR-008**: An intent MUST be resolvable by any counted member, so a crashed evaluator cannot leave a family stuck.

**Writers**

- **FR-009**: A host's write version for a family MUST be its observed finalized version (spec 180, FR-013). A host
  whose current version is newer keeps writing the observed version's format.
- **FR-010**: A host MUST refresh its observed finalized version when the module activates, after every evaluation it
  runs, on a bounded, configurable interval, and before writing a row stamped later than its write version (spec
  180, FR-015).
- **FR-011**: Switching writers MUST need no restart and no shell reload, so it behaves the same on
  `Elsa.Foundation.Host` and on `Elsa.Workbench`, whose reloader does nothing.
- **FR-012**: When a refresh finds a finalized version outside the host's readable set, the host MUST refuse every
  write to that family with a typed error naming the family and both versions. It MUST report the condition through
  the gate's status (FR-022). It MUST NOT keep writing at its old version.

**Refusing a host that cannot read**

- **FR-013**: Before a member activates an EF module, its readability report for that module's families MUST be
  published to the fleet view. Only then does it read the finalization record (mechanism 2).
- **FR-014**: If the record shows an intent for a version outside the member's readable set, the member MUST wait for
  the intent to resolve, up to a bounded time. It proceeds only if the intent was abandoned, and refuses if the
  intent was committed or is still unresolved.
- **FR-015**: At activation, if any family of an EF module has a finalized version outside the host's readable set,
  the host MUST refuse the module. The check runs in the Prepare-phase initializer that `EfModuleMigrator` occupies,
  and in its hosted-service form on plain hosts, after migrations are applied or validated and after the
  post-migration audit, and before any shell task, seeder or store touches the module's tables. It throws a typed
  refusal, so the shell does not activate, as a pending migration under `Validate` does today.
- **FR-016**: At enable time under `Validate`, an `IFeatureActivationGuard` MUST apply the same check to every feature
  that `[UsesEfModule]` maps to the module, returning a `FeatureActivationRefusal` that the Modularity API renders as
  409, with nothing saved. Under `AutoMigrate` it opens no database, following spec 171's FR-068 and FR-069, so
  FR-015 is the refusal point instead; whether this should instead read the record under both policies is Open
  Question 4.
- **FR-017**: A refusal MUST name the feature where there is one, the EF module, the family, the finalized version
  and the host's readable set, and give the remedy: run a version that can read the finalized version, or restore a
  pre-finalization backup. Like every activation refusal, it MUST NOT contain a connection string or any other
  restored secret (spec 171, FR-061).
- **FR-018**: A member that learns its membership has lapsed MUST stop writing through every gated EF module until it
  has rejoined as a new incarnation and passed FR-013 to FR-015 again.

**Holds**

- **FR-019**: An operator MUST be able to place a hold on a family, optionally limited to one version, with a
  required reason, and to release it. Placing a hold on a finalized version MUST be refused. Hold and release are
  recorded in the history with the operator identity given and a timestamp.
- **FR-020**: Hold, release and status MUST be available at least through the `dotnet elsa persistence` CLI, as new
  subcommands that follow the existing ones: `--host`, one of `--modules`, `--all` or `--from-host`, `--provider`,
  `--connection-env` or `--connection-stdin`, and the existing exit-code table. The CLI writes the record directly,
  so a hold can be placed before any gate-aware host runs, which a canary requires. No command finalizes, forces
  finalization or lowers a finalized version. Whether an additional surface, such as an HTTP API, also carries these
  is decided by Open Question 3.

**Single host and provider kind**

- **FR-021**: Under the in-process membership provider, the fleet view is the host alone, so the module's families
  finalize during the Prepare-phase check unless a hold applies. It MUST write no membership table and no heartbeat.
  The finalization record is still written, because it is a fact about the database (ADR 0078). When the host
  composes a feature that only makes sense on a cluster (today `WorkflowsRuntimeDistributed`) while membership is
  in-process, the gate MUST NOT auto-finalize. It MUST report that clustered hosting needs a durable membership
  provider, rather than finalize as if the host were alone.

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

- **Finalization record**: per database and family. It holds the finalized version, at most one intent, holds, and
  history.
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

- **B1** (#2097), the membership contract, and **B3** (#2099), the readability report. Membership has **no spec
  yet**. This spec cannot be implemented until MR-001 to MR-007 are met, and it needs the in-process provider at
  minimum.
- **B2** ([#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)), the durable membership provider,
  for any clustered test or deployment.
- **Spec 180** (B4, #2100), for the family declaration, readable sets and the write path.

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

## Open Questions

1. **Unit of finalization.** This follows spec 180's Open Question 1. If the owner chooses the EF module over the
   schema family, the record is keyed per EF module and each family's stamp is derived from it.
2. **Detecting a misconfigured cluster.** FR-021 only catches the case where `WorkflowsRuntimeDistributed` is composed.
   Two ordinary hosts sharing a database with in-process membership would each finalize at once, and ADR 0078 has
   the in-process provider write nothing durable, so nothing can see the second host. Should B1 make this
   detectable, for instance with a durable marker per host, which ADR 0078's "Durable membership on every host"
   rejection argues against? Or should a clustered deployment be required to declare itself?
3. **An HTTP surface for hold and release.** FR-020 provides the CLI only, because persistence-state commands are
   CLI-only today and a hold has to be placeable before a gate-aware host runs. Should the Modularity API, under the
   `module-management.manage` host-control permission, offer hold and release too?
4. **Enable-time refusal under `AutoMigrate`.** FR-016 keeps spec 171's rule that the guard opens no database under
   `AutoMigrate`, so an enable request for a module that will be refused is saved and then fails at Prepare. Is that
   acceptable, or should the finalization check be allowed to read the record under both policies?
5. **Fleets that span databases.** Counted members are every live member whose report declares the family, whatever
   database each one uses. That is conservative: a host serving a different database can delay finalization, but
   never cause an early one. Should the report carry a database identity so that only members sharing the database
   count?
6. **Intervals and bounds.** Defaults are needed for the evaluation interval, the refresh interval and FR-014's wait
   bound. The refresh interval bounds how long a partitioned host keeps writing after it reconnects.
7. **Landing the record table.** Should the finalization table land in every EF module's 4.0 baseline before #1976
   freezes the `Initial` migrations, so that no module needs a post-4.0 migration just to take part in the gate?
