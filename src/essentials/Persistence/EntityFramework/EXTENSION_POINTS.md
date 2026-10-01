# Extension points — Persistence.EntityFramework (policy)

Shared policy helpers for first-party EF Core persistence under accepted
[ADR 0073](../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
This package does not own domain entities or a mandated `DbContext` base.

## Replacement / composition

There is no replacement contract in this package. Modules own derived `DbContext` types.
Hosts register the derived context that matches the selected relational provider and call
`EfDatabaseMigrator.ApplyAsync` with that provider's expected `Database.ProviderName`.

## Persistence resource enrollment and host composition

`EfPersistenceResourceParticipantAttribute` is the explicit enrollment marker for a shell feature
whose EF target may come from a named resource. Place it on the owning feature class alongside its
existing `ShellFeature` and `UsesEfModule` metadata. The EF participant catalog combines those
declarations with `EfModule` context ownership; it does not infer enrollment from setting names or
construct feature instances. The first-slice enrolled identities and exclusions are listed in the
[shared-persistence contract](../../../../specs/173-shared-persistence/contracts/persistence-configuration.md#enrollment-and-existing-metadata).

A host that enables resource preparation supplies one assembly-level
`EfToolingShellDefaultsAttribute` naming a type that implements `IEfToolingShellDefaults`. Its
`Configure(ShellBuilder, IConfiguration)` method declares that host's defaults without constructing
features or activating a shell. The same declared composer is used by runtime enrollment and EF tooling,
so the two paths share host-default composition. The Workbench integration is registered through
`AddEfPersistenceResources` in `Elsa.Modularity.EntityFramework`; legacy-only hosts need not opt in.

The verified first layout uses one target for enabled enrolled Runtime, Workflows Design, Activities
Design, and Publishing consumers. Runtime participants share `RuntimeDbContext` and must agree; they
are not independent stores. The separate Structured Logs/OpenTelemetry layout is modeled and enrolled,
but remains unverified until its dedicated two-target host/database proof is complete. Host-owned
OpenIddict, IAM/provider configuration, Secrets, distributed/private stores, Dashboard readers, and
unknown/custom consumers are not automatically redirected. A selected resource contributes only
Provider and ConnectionName; schema, pooling, and migration policy retain their existing owners.

## Candidate inspection capability

`EfCandidateInspectionContract.Version` and
`EfToolingHost.RunCandidateInspectionAsync(Stream, Stream, CancellationToken)` expose the independently
versioned candidate operation. `EfCandidateInspectionOperation` accepts captured post-edit file bytes
and a separate compiled-host identity, uses the declared composer and real feature descriptors, and
reconciles requested/expanded features against the accepted exact set before shared EF preparation.
Configured-value affinity checks are enabled here; existing v2 offline tooling keeps its weaker,
unchecked-value behavior. The streamed response contains safe logical targets or fixed refusals, with
legacy targets unprojected and no database, activation or migration-readiness claim.

A host must supply the complete matching capability; the worker refuses old or partial APIs rather than
using the live configuration reader. The CLI remains EF-free and invokes the capability inside the
installed host closure. The Elsa-owned inspection path does not activate a shell or execute migration
actions; the declared composer remains arbitrary trusted code, without a sandbox. The private protocol, bounds and evidence limits are defined in
[the candidate contract](../../../../specs/187-effective-persistence-preview/contracts/candidate-inspection-v1.md);
operators use [`composition inspect`](../../Cli/README.md#inspecting-an-accepted-runtime-candidate).

## Module descriptor

An assembly-level `[EfModule(name, contextType, ...)]` (`AllowMultiple`) is a module's single,
discoverable declaration — first-party or third-party — of its canonical name, base context, per-provider
derived contexts, frozen history name, dependencies and post-migration actions (accepted
[ADR 0076](../../../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md) D2). A third-party
module author declares one on their own assembly with no Elsa PR required.
`EfModuleCatalog.Discover(assemblies)` is the one place that reads it, and a `null` provider property means
that provider is unsupported for the module rather than a missing case. See the package README's table for
both types.

## Schema family declaration

An assembly-level `[EfSchemaFamily(name, module, currentVersion, Upcasters = ...)]` (`AllowMultiple`) is a schema
family's one declaration ([spec 180](../../../../specs/180-schema-upcaster-chain/spec.md), FR-001): the family its
checks name, the `[EfModule]` of the same assembly that owns it, the version this build stamps, and its upcaster chain,
oldest first. A module that stamps rows declares one per family, passing the family's `SchemaFamily` and version
constants, and its module class holds the family's one chain handle,
`public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(<class>).Assembly, SchemaFamily)`, which every
store and every reader outside the module checks and upcasts through (FR-002, FR-010).
`EfSchemaFamilyCatalog.Discover(assemblies)` is the one place that reads it, as metadata matched by type name, so a
package loaded with its own copy of this assembly is still read. The family's readable set is its current version and
every predecessor the chain reaches without a gap (FR-004), computed by one function for both the host's readability
report ([spec 183](../../../../specs/183-cluster-membership/spec.md), FR-020) and every read, so a host never reports a
version its reads refuse. `EfSchemaFamilyChainDeclarationGuardTests` fails the build when a family the stores check is
undeclared, a check names its family by anything but its chain handle, a chain is unsound, or a write stamps anything
but the declared current version; `EfSchemaFamilyRestampGuardTests` fails a write that rewrites content without
restamping, or a restamp that does not assign every declared content column of its row from upcast values (#2144).

A family whose loaded declarations disagree on the owning module is isolated rather than taking the rest of the host's
readability report down with it: its own entry credits no readable version - a host is counted for it and never for a
version it cannot read, the conservative direction (FR-020) - and `EfSchemaReadabilitySource` logs an error naming the
family and every conflicting declaration. Every other family is still reported normally.

### Upcasters

An upcaster is the extension point a module author adds when a family's stored content changes: a concrete
`IEfSchemaUpcaster` with a public parameterless constructor and no injected services (FR-003), in the owning module's
assembly beside the family's store code, carrying `[EfSchemaUpcaster(from, to)]` and listed at the end of the family's
`Upcasters`. It works on a whole row, not a column (#2144): it receives an `EfSchemaRowContent` - the table, named by
the type the family's `[EfSchemaContent]` declaration maps to it, and every declared content column's decoded value,
nulls included - and returns the same table's same columns, so one step can move or split data across columns. It is a
pure, total function of that row and returns every column it has no change for as it received it, usually by changing
columns with `EfSchemaRowContent.With` (FR-019, FR-012). A store reads a row with
`<Module>.Chain.Upcast<TEntity>(row.SchemaVersion, (nameof(row.A), row.A), (nameof(row.B), row.B))`, naming every
declared content column of the table, and takes each column from the result. `EfSchemaChain.Upcast` runs the steps
from a row's stamp to the current version, runs none at the current version (FR-021), refuses an unreadable stamp as
skew before anything else, refuses a read whose columns are not exactly its table's declared content columns at every
version, the current one included, and reports a failing upcaster, or one that returns another table or adds or drops
a column, as corruption (FR-009): a row is either at its stamp or wholly upcast. A chain with a gap, a duplicate, a
branch or a cycle, or one that does not end at the current version, fails the build and is refused when the module
registers (`AddEfModuleMigrations`), and a read never bridges a gap (FR-005). A family whose context EF materializes
directly (`IEfSchemaVersionedContext`) declares no upcasters: its value converters deserialize the content before any
upcaster could run, so `EfSchemaVersionMaterializationInterceptor` accepts its current version alone. Every upcaster
ships a committed fixture pair under `Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/` in its module's test project,
one row per pair, `<table>.source.json` and `<table>.expected.json`, each a JSON object of the row's content columns,
frozen by `tests/essentials/Architecture/Baselines/schema-upcaster-fixtures.sha256`, and a test class deriving directly
from `EfSchemaUpcasterProof<TUpcaster, TValue>(family, store)`, which runs FR-022's three proofs over every pair of its
step, comparing whole rows; the build fails without either (FR-022).

`EfSchemaWriteRefusedException` is the write refusal (FR-016a): a write whose value needs a version later than the
version the host may write. It carries a stable code, the family and both versions, and spec 182 derives from it for
dormant-feature writes. It derives from `Elsa.Primitives`' `SchemaWriteRefusedException`, which is what every domain
API answers with a 409 in its own envelope (`Elsa.Api.AspNetCore`'s `SchemaWriteRefusalProblem`), since an API
resolves no EF Core. A store lets it leave as itself, as it does the skew exception: a store that wraps it in a
failure of its own turns the 409 into that failure's status.

### Content and integrity columns

Beside its `[EfSchemaFamily]`, a module declares which columns of the family's tables are content: one
`[EfSchemaContent(family, typeof(Entity), columns...)]` per table, naming the type the context maps to the table and
its content columns with `nameof`. Content is what a read upcasts through the family's chain before it deserializes it,
and what a write that changes it restamps the row for (spec 180, FR-009 and FR-014). A document column that is instead
compared as the bytes it was stored with - a projection or integrity datum, read before any upcast (FR-008) and never
deserialized - is declared with `[EfSchemaIntegrity(family, typeof(Entity), column, reason)]`, and the reason is
required, since a column deserialized into a current type, or compared with anything this build serializes, is
content. `EfSchemaFamilyCatalog` reads both onto the family's descriptor (`ContentColumns`, `IntegrityColumns`) and
refuses discovery for a declaration naming a family its assembly does not declare, a blank column, an integrity column
without a reason, or a column declared twice.

The declaration is what the guards read, so nothing is inferred from call sites. `EfSchemaContentDeclarationTests`
builds every first-party module's context on every provider and fails when a document column of a stamped table - a
string column named `...Json`, `Content` or `Payload`, a payload column, or a domain value a converter stores as a
string - is declared by neither attribute, or when a declared column is not in the model.
`EfSchemaFamilyContentIntegrityGuardTests` fails when a column a store upcasts is not declared content by its family,
when a read upcasts some of a row's declared content columns rather than all of them, names a value as another
column's or upcasts a row as another table's; `EfSchemaFamilyContentReadGuardTests` fails when a read of a declared
content column in a source that can see its declaration - a presence check included - neither goes through the chain
nor deserializes nothing; and `EfSchemaFamilyRestampGuardTests` fails when a write that changes one, an
`ExecuteUpdate` included, does not restamp the row, or restamps a row without assigning every declared content
column it did not otherwise write, from upcast values (#2144). A
family EF materializes directly declares its content too; its reads meet the rule through the materialization
interceptor, which accepts its current version alone, and its tracked writes through the context's stamping.

### Shared families (no single owning module)

Shared mapping code - code that is not itself an `[EfModule]` but writes rows into several EF modules' contexts, such
as `modelBuilder.MapXyz(...)` called from every module's `OnModelCreating` - has no single module to name as the
family's owner. `[EfSchemaFamily(name, currentVersion)]`, the two-argument constructor, declares exactly that: its
`Module` reads `null` rather than a module name, on both the attribute and the descriptor `EfSchemaFamilyCatalog`
reads off it, and on the `ReadabilityEntry` the readability report carries it into.
`EfSchemaFamilyCatalog.Discover` accepts it only in an assembly that declares no `[EfModule]` of its own, so a module
that owns its family still names its module explicitly rather than reaching for the shared form to avoid the "exactly
one EF module" rule. Several loaded copies of one shared declaration - two generations of a package, or the same
mapping assembly loaded through two load contexts - are still read and intersected exactly like several copies of an
owned family. `EfSchemaFamilyChainDeclarationGuardTests` reads a declaration's version from its *last* positional
argument, so it resolves both the two-argument shared form and the three-argument owned form the same way. A module's
registration checks the chains of the families it owns and of the shared families this package declares, since every
module's context maps them.

## Apply policy

`EfMigratePolicy.AutoMigrate` runs `Database.MigrateAsync` (EF 9+ lock). On SQLite it goes through
`EfSqliteMigrationLock.MigrateAsync` instead (#2196): EF's SQLite lock is a row in `__EFMigrationsLock` that a killed process
leaves behind, and EF waits for it for ever, even when nothing is pending. So nothing pending applies nothing and takes no lock;
with migrations pending, a lock younger than `EfMigrateOptions.SqliteMigrationLockStaleAfter` (default 10 minutes, key
`Elsa:Persistence:EntityFramework:Migrate:SqliteMigrationLockStaleAfter`) is waited for as before, and an older one fails the
start with `EfMigrationLockStaleException` naming the `DELETE` that clears it. Elsa never removes the row itself: the row is the
only evidence of a holder, so nothing proves it dead. A host that migrates a SQLite store outside `EfDatabaseMigrator` calls
`EfSqliteMigrationLock.MigrateAsync(context, EfMigrateOptions)` rather than `Database.MigrateAsync`; the options come from
`EfMigrateOptions.FromConfiguration` where there is no options pipeline (`dotnet elsa persistence apply` reads the host's
configuration that way, and Workbench's OpenIddict store does the same). `dotnet elsa persistence apply` sends the bound in its
version-1 request (`sqliteMigrationLockStaleAfter`, read from the host's `appsettings.json`), so a host whose persistence build
predates that field gets the default bound for `apply`: the tool probes for the field, leaves it out, and prints a warning.
Caveats: skipping when nothing is pending leans on EF writing a migration's history row after its operations, which holds for a
migration that suppresses its transaction (SQLite table rebuilds do) as well, but there the row is not committed atomically
with them, so a process killed between the two leaves a migration pending whose operations have run, and the next start
migrates it again under the lock. And a peer that takes the lock between the wait returning and EF's own acquire, and is killed
there, still leaves EF waiting; nothing cancels a running migration, so no watchdog can stop a legitimate one.
`EfMigratePolicy.Validate` refuses to start when pending migrations exist.
`EfModuleMigrator<TContext>` registers that apply on both `IHostedService` and CShells
`IShellInitializer` — one instance under both — so a feature enable or reload uses the same policy as
a cold start. The same instance stops the same way: a plain host's `IHostedService.StopAsync` stops the gate's and the
backfill's loops, and a shell stops them in its drain, through an `IShellTerminator` the migrator registers in the `Start`
phase, while the shell's services are still usable. The shell container's disposal then finds nothing left to stop. Disposal
alone cannot do it: the container marks its provider disposed before it disposes the migrator, so a backfill round still
running then fails on a provider that refuses its scope (#2236). A round that does fail that way is logged at `Debug`, not as
a warning, and does not try to release its claim, which expires on its own.

Which one a deployment runs is an operator setting, not a code seam: `EfModuleMigrator<TContext>`
reads `EfMigrateOptions`, bound from `Elsa:Persistence:EntityFramework:Migrate:Policy`
(`EfMigrateOptions.SectionName`). A host that needs the policy decided in code can still
`services.Configure<EfMigrateOptions>(…)` after composing the module.

## Post-migration actions

`IEfPostMigrationAction` is the seam for work a module needs *after* its migrations apply (ADR 0076
D8). A module declares the types on its `[EfModule(... PostMigration = ...)]`; nothing is resolved
from DI, because the `dotnet elsa persistence` worker has no container. `EfPostMigrationActions` is
the one place a declaration becomes instances and the one place a set of them is audited, so the
running host and the out-of-process tooling can never disagree about what a module owes.

`AuditAsync` must be read-only and must throw rather than report "not required" for a database it
could not read. `RunAsync` has exactly one caller, `dotnet elsa persistence post-migrate`: after
applying migrations, `EfModuleMigrator<TContext>` audits under both policies and fails closed naming
that command, and `apply`/`validate` do the same — no command ever runs an action as a side effect.

## Fleet source for `persistence status`

`IEfToolingFleetSource` (`Elsa.Persistence.EntityFramework.Tooling`) is how `dotnet elsa persistence status` reads the
cluster's members without this package knowing what membership is ([spec 181](../../../../specs/181-schema-finalization-gate/spec.md),
FR-022). A provider that keeps its members in an EF module declares one public class with a parameterless constructor in the
assembly that declares that module, naming the module (`ModuleName`); the tool finds it beside the module, hands it a
context of the module on the command's connection once the module has no pending migration, with the skew allowance to judge
liveness with (or none, for the provider's default), and prints what it returns: the members, each with what it reads of the
families listed and only the entries that speak for each family's database, and for a family, version and database, the
counted members that cannot read it. The provider owns what "counted" and "can read" mean and how a member is judged live
(`EfClusterMembershipToolingSource` judges each member with the provider's own `StoredMember.ToFleetMember` and asks the same
`ReadsSchemaVersion` query as the gate), so the tool and the printer repeat none of it. The source must not write. A closure
with no source, or a module with no context for the provider, or a database whose module tables are not migrated, leaves the
fleet unread, which the status says by an explicit marker (`cluster.availability`) rather than by reporting that nobody
blocks a version.

## Schema finalization record

Not an extension point: every first-party EF module maps the finalization tables with
`modelBuilder.MapSchemaFinalization(<its history module name>)` after its own tables and before its provider
configuration, so its own baseline migration creates them beside its history table
([spec 181](../../../../specs/181-schema-finalization-gate/spec.md), FR-002), and a third-party module does the
same. Secrets is the exception: its migration chain already shipped before spec 181, so it keeps that chain and
gets the finalization tables from an incremental `SchemaFinalization` migration instead of its baseline. The
tables belong to their own schema family, `SchemaFinalization`, whatever module maps them;
`EfSchemaVersionMaterializationInterceptor` leaves them to `EfSchemaFinalizationStore`, which checks their stamp
before it reads anything else. The store works on any context that maps them and holds no unsaved changes of its own.

## Schema finalization gate

The gate itself is not an extension point: `EfModuleMigrator<TContext>` admits every module through an
`EfSchemaModuleGate` once its migrations are current and its post-migration actions audited, under both migrate
policies, and `EfModuleBinding.Apply` adds `EfSchemaWriteGateInterceptor.Instance` to every module context, so no
module opts in and none can opt out ([spec 181](../../../../specs/181-schema-finalization-gate/spec.md)). What a
module and a host decide:

- **A module that owns several families** names, in each `[EfSchemaFamily]`, the entity types whose rows stamp it
  (`Entities = [typeof(...)]`). A module that owns one family names none: every stamped table of its context is that
  family's. `ModuleFinalizationGateTests` fails the build when a stamped table belongs to no family, or to several.
- **A store writes its family's write version.** Today every chain has one version, so that is the family's current
  version whenever writes are allowed. The write check refuses a row stamped with any other version
  (`EfSchemaWriteRefusedException`), and every write to a family whose finalized version this host cannot read
  (`EfSchemaFamilyWritesRefusedException`, spec 181 FR-012). A store that one day writes an older version's format
  reads the write version from its module's gate (`EfSchemaFinalizationGates.FindModuleGate(...).StateOf(family)`).
- **`IEfSchemaFleet`** is the gate's view of cluster membership. `Elsa.Cluster.Readability` implements it over the
  host's one membership provider (`AddEfSchemaReadability`). A host that composes none has gates that never finalize
  a version past the one each record was created at: the conservative direction, which only ever delays.
- **`EfSchemaFinalizationObservations`**, registered once by instance on the host container, is what the gates read
  and what the readability report names (spec 183, FR-019). It also says whether each family's module is active in the
  host, per database, from a gate's admission until it stops or its activation fails (`Activate` and `Deactivate`); the
  rules are in spec 186, "When a module is active in the report". Only the backfill's settle condition reads it (spec 186,
  FR-012).
- **Where these live.** `IEfSchemaFleet` and its answer types, `EfSchemaFinalizationObservations`, the finalization
  record's model and status, `EfSchemaFinalizationGates` with the `IEfSchemaModuleGate` view the dormancy check's
  source reads, `EfSchemaFamilyCatalog` with its descriptors, and `IEfModuleRefusal`, which every EF module's refusal to
  activate implements so a host can name the module and its command without the exception's type, are in `Elsa.Persistence.Schema`, not in this
  assembly, and declared in that assembly's own namespaces: the catalog and its descriptors in `Elsa.Persistence.Schema`,
  everything else in `Elsa.Persistence.Schema.SchemaFinalization`, the name of the folder they sit in (a consumer adds
  `using Elsa.Persistence.Schema;` or `using Elsa.Persistence.Schema.SchemaFinalization;`). That assembly references no
  EF Core, and every host shares it with every package it loads (ADR 0067, amended 2026-09-29): an EF module Nuplane loads with its own copy of this assembly still finds
  the host's fleet, observations and registry, because their types come from the host's copy of that one (#2143).
- **Timings** come from `Elsa:Persistence:EntityFramework:Finalization` (`EvaluationInterval`, default 30 seconds;
  `RefreshInterval`, 15 seconds; `IntentWaitBound`, 2 minutes; `IntentPollInterval`, 1 second).

## Post-finalization backfill

The backfill itself is not an extension point: beside each module's gate, `EfModuleMigrator<TContext>` runs an
`EfSchemaBackfill` in the shell, with the shell's services, which upgrades a family's rows below the version this host
has adopted as finalized, waits until every counted member reports observing it and the settle margin has passed,
proves in a verification pass that no row below it remains, and records the family complete in its finish record; while
a completion stands it audits the family on its interval, and withdraws the completion, before rewriting it, when a row
below it turns up ([spec 186](../../../../specs/186-post-finalization-backfill/spec.md)). It is not an
`IEfPostMigrationAction`, whose audit at Prepare would refuse the module finalization needs active. The gate's status
carries the backfill's through an internal seam; neither exposes the other.

Several hosts sharing a database each run the backfill, and a claim in the family's finalization record keeps all but
one from repeating its reads (FR-008). It is taken before any pass reads a row, the survey, upgrade, settle,
verification and audit alike, and is held on the completion that stands or, while none stands, on the withdrawal that
ended it, so after a withdrawal one worker upgrades instead of every one. A worker that finds the family claimed
elsewhere reads nothing of it until the claim expires. The claimant keeps it only while its run goes on next round and
releases it on every other way out, a failed or cancelled round included. The claim only narrows who works: a worker
whose claim was taken over stops at its next renewal or before its next row, and neither withdraws nor records a
completion while another worker's claim holds; one whose cluster member has lapsed claims nothing, stops and releases
what it holds. Nothing correct depends on the claim; every row write, completion and withdrawal stays a
compare-and-set. See the spec's 2026-10-01 note.

**Breaking change for direct callers of the finalization store.** `EfSchemaFinalizationStore.RecordCompletionAsync` and
`WithdrawCompletionAsync` now take the acting worker, null for a writer that keeps no claim, and refuse with
`SchemaFinalizationRefusal.BackfillClaimed` while another worker's live claim holds, by the same rule a claim is held to
(`SchemaFinalizationRecord.ClaimKeepingOff`). Code outside this repository that records or withdraws a completion no
longer compiles until it names its worker, and is refused, rather than overriding a running backfill, while one holds.

`EfSchemaBackfill`, `EfSchemaBackfillScope` and the `EfSchemaBackfillScopeRunner` delegate a round takes are public, but
they are not an extension point either: only `EfModuleMigrator<TContext>` builds a backfill and gives it a scope runner.
They are public because two test suites this assembly grants no internals drive rounds directly, with scopes of their
own contexts: the dormancy scenario in `Elsa.Cluster.EntityFrameworkCore.Tests` and the synthetic family's run on the
three server engines in `Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests`. Making them internal would need
an `InternalsVisibleTo` for each. A runner must run the action it is given once, in a fresh service scope of the shell
with a fresh context of the module, and throw when it cannot; the backfill fails a round whose runner returned without
running its work, since a count it never read would otherwise read as "nothing found".

What a module declares:

- **A rewriter** (FR-004): `[EfSchemaFamily(..., Rewriter = typeof(...))]` names a class of the module's own assembly,
  beside its store code, implementing `IEfSchemaRowRewriter`. The backfill constructs it for every row in a fresh
  scope of the shell, so it may take the stores, serializers and codec the family's write path uses. It reads the row
  by key through the family's read path, leaves it alone when it is at the target or later, and otherwise writes it
  whole through the write path: every declared content column, the projections a newer version introduced, the stamp
  at the host's write version, by compare-and-set on the row's revision. A family that has only ever had one version
  needs none; `EfSchemaBackfillGuardTests` fails the build when a family with upcasters names no rewriter of its own
  module, and the backfill reports such a family blocked rather than complete.
- **Content-addressed tables** (FR-010b): `ContentAddressed = [typeof(...)]` names the family's tables whose rows'
  identity is their content, such as executables (ADR 0038), and each such entity type is marked
  `[EfSchemaContentAddressed(reason)]`, the reason saying how its key follows from its content. The backfill never
  rewrites them, and a family with any row in them below a version is never recorded complete at it.
  `EfSchemaContentAddressedDeclarationTests` fails the build when a first-party stamped table is marked but not named,
  or named but not marked, and when a table it pins as holding executables or executable activity templates is not
  both; `EfSchemaBackfillGuardTests` fails it when a feature needs completeness of a family that names any (FR-011c).

What a host decides:

- **`IEfSchemaFleet.CountObservingAsync` and `SettleMargin`** answer the settle condition (FR-012):
  `Elsa.Cluster.Readability`'s fleet reads each counted member's readability entry, which carries the finalized version
  it observed (spec 186, MR-001), and its margin is the membership expiry period plus the skew allowance. A host with no
  fleet never records completion.
- **Settings** come from `Elsa:Persistence:EntityFramework:Backfill` (`BatchSize`, default 500; `BatchPause`,
  100 ms; `CheckInterval`, 15 seconds; `AuditInterval`, one hour, which is also how often a family blocked by
  content-addressed rows, a missing rewriter or no fleet is surveyed again; `RepairableBlockerInterval`, 5 minutes, how
  often a family blocked by rows an operator repairs in place, with an unreadable stamp or that fail to upcast, is
  surveyed again, so a repair is seen well before the next audit; `SettleMargin`, the fleet's by default;
  `ClaimDuration`, 2 minutes, zero for none; `VerificationPasses`, 3).

**Breaking change for custom fleets.** `IEfSchemaFleet` gained `CountObservingAsync` and `SettleMargin` with spec 186.
An implementation outside this repository no longer compiles against this version until it adds both: answer the
settle condition from a fresh, complete read of the fleet, listing each counted member that has not observed one of
the versions and throwing rather than answering from part of it, and return the margin a write begun before a member
observed a version can still be in flight. Returning a margin shorter than that, or answering from a partial read, lets
a verification pass start while such a write can still land below the version, which is exactly what the settle
condition prevents.

## Schema and pooling

Neither is an extension point: a module opts in by passing its `Schema` and `Pooling` options through
`EfModuleBinding.Apply` and `EfModuleBinding.AddContext`, and the shared layer decides what that means per
provider. `EfSchema.Resolve` reads the module setting, then `Elsa:Persistence:EntityFramework:Schema`;
SQLite ignores a schema and MySQL refuses one. A module context applies what it was bound to with
`modelBuilder.HasElsaDefaultSchema(this)` as the first line of `OnModelCreating`, and
`EfSchemaMigrationsAssembly` puts its scaffolded migrations in the same schema. See the package README for
the operator-facing description of both settings.

## Provider binding validation

`EfRelationalProviderBinding` reaches each engine's `Use*` extension by type and method name, so nothing in a
compile notices a missing or mismatched provider package. `AddEfModuleMigrations<TContext>` therefore records the
provider each module context is configured for, and `EfProviderBindingValidator` probes all of them in the CShells
`Prepare` phase ahead of every migrator (and as the first `IHostedService` on a plain host). There is no extension
point here: a module opts in by registering its migrations, and the validator reads what that registration recorded.

A module composed once on the host container rather than by a shell feature, such as cluster membership, registers
with `AddEfModuleHostMigrations<TContext>` instead: the same migrator and validation, run only as a plain-host hosted
service. CShells copies every root registration into each shell, so the shell hook would otherwise migrate the module
again on every shell activation, from the shell's configuration rather than the host's.

## SQLite connection opens

Not an extension point. Every context `EfRelationalProviderBinding` binds to SQLite opens its connections one at a time
per connection string, through `EfSqliteSerialOpenInterceptor` (#2209). Microsoft.Data.Sqlite 10.0.10 can lend one pooled
connection to two opens of the same connection string that check out at once (dotnet/efcore#39008, fixed upstream in
10.0.13). The two then share one handle, and a host start whose module migrators, finalization gates and backfills open
connections to `elsa.db` together fails with "unable to delete/modify user-function due to active statements". Only the
checkout waits, not the connection's use, and the other providers do not carry the interceptor. A context bound to
SQLite outside the binding does not take the gate. Until Elsa pins 10.0.13, such a context must not share a module's
connection string. The gate is removed once that version is pinned (#2220); a test fails as soon as it is.

## Shared transactions

`EfSharedTransaction` is the owner a cross-module write uses when several module contexts must commit
as one unit. It constructs fresh instances of the configured contexts, shares one connection and one
transaction between them, commits or rolls back once, and refuses contexts that name different providers
or connection strings. Module atomic writers join it through their transaction-factory seam
(`EfSharedTransaction.BeginOperationAsync`); a writer that rolls back makes the owner rollback-only.

## Expand-only migration guard

Not an extension point in the usual sense: `ExpandOnlyMigrationGuard.Classify`/`Evaluate` read a
migration's `Up` operations against a closed allowed list, and `ExpandOnlyMigrationOptOutAttribute` is
the reviewed, per-migration permission for exactly the destructive operations it lists
([spec 185](../../../../specs/185-expand-only-migration-guard/spec.md), #2104). Both types are public
so a third-party module can run the same check from its own tests; the guard's own enforcement runs
only over first-party modules. See the package README's table for both types. A contracting opt-out
also names its schema family and version (FR-023, #2136); a third-party caller passes
`ExpandOnlyMigrationFamilies.Before(...)` so the guard can tell one. Applying such a migration is
checked for every module, first-party or not, by `EfDatabaseMigrator` (FR-024), which on a database
no host has admitted the module in creates the family's finalization record before the migration runs.

## Ordinal string collation

`EfOrdinalCollation` is a pinned decision, not an extension point: one binary collation per provider
(`Latin1_General_100_BIN2`, `C`, `utf8mb4_0900_bin`, and nothing on SQLite, whose default already is
`BINARY`). A module declares which of its columns are compared or ordered ordinally and calls
`EfOrdinalCollation.Apply` from each provider-derived context; a provider nobody has decided for is
refused rather than left on the server's linguistic default.

On MySQL it also sets the provider's own `MySQL:Collation` annotation, because Oracle's provider reads that
out of a migration's target model and ignores the relational column collation.

It is applied **per column**, never through `modelBuilder.UseCollation`, for two reasons: modules can
share one database, so a database-wide collation is one module setting its neighbours' comparison
semantics, and a model-level declaration did not survive migration generation at all
([#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837)).
