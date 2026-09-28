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
version its reads refuse. `EfSchemaFamilyDeclarationGuardTests` fails the build when a family the stores check is
undeclared, a check names its family by anything but its chain handle, a chain is unsound, or a write stamps anything
but the declared current version or rewrites content without restamping.

A family whose loaded declarations disagree on the owning module is isolated rather than taking the rest of the host's
readability report down with it: its own entry credits no readable version - a host is counted for it and never for a
version it cannot read, the conservative direction (FR-020) - and `EfSchemaReadabilitySource` logs an error naming the
family and every conflicting declaration. Every other family is still reported normally.

### Upcasters

An upcaster is the extension point a module author adds when a family's stored content changes: a concrete
`IEfSchemaUpcaster` with a public parameterless constructor and no injected services (FR-003), in the owning module's
assembly beside the family's store code, carrying `[EfSchemaUpcaster(from, to)]` and listed at the end of the family's
`Upcasters`. It is a pure, total function of the decoded content it is given (`EfSchemaContent`: the table, the
column, the value) and returns content it has no change for as it received it (FR-019, FR-012). `EfSchemaChain.Upcast`
runs the steps from a row's stamp to the current version, runs none at the current version (FR-021), refuses an
unreadable stamp as skew, and reports a failing upcaster as corruption (FR-009). A chain with a gap, a duplicate, a
branch or a cycle, or one that does not end at the current version, fails the build and is refused when the module
registers (`AddEfModuleMigrations`), and a read never bridges a gap (FR-005). A family whose context EF materializes
directly (`IEfSchemaVersionedContext`) declares no upcasters: its value converters deserialize the content before any
upcaster could run, so `EfSchemaVersionMaterializationInterceptor` accepts its current version alone. Every upcaster
ships a committed fixture pair under `Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/` in its module's test project,
frozen by `tests/essentials/Architecture/Baselines/schema-upcaster-fixtures.sha256`, and a test class deriving directly
from `EfSchemaUpcasterProof<TUpcaster, TValue>(family, store)`, which runs FR-022's three proofs over every pair of its
step; the build fails without either (FR-022).

`EfSchemaWriteRefusedException` is the write refusal (FR-016a): a write whose value needs a version later than the
version the host may write. It carries a stable code, the family and both versions, and spec 182 derives from it for
dormant-feature writes. Nothing raises it until spec 181's gate lets a host write an older finalized version.

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
owned family. `EfSchemaFamilyDeclarationGuardTests` reads a declaration's version from its *last* positional
argument, so it resolves both the two-argument shared form and the three-argument owned form the same way. A module's
registration checks the chains of the families it owns and of the shared families this package declares, since every
module's context maps them.

## Apply policy

`EfMigratePolicy.AutoMigrate` runs `Database.MigrateAsync` (EF 9+ lock).
`EfMigratePolicy.Validate` refuses to start when pending migrations exist.
`EfModuleMigrator<TContext>` registers that apply on both `IHostedService` and CShells
`IShellInitializer` — one instance under both — so a feature enable or reload uses the same policy as
a cold start.

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
  reads the write version from its module's gate (`EfSchemaFinalizationGates.FindForContext(...).StateOf(family)`).
- **`IEfSchemaFleet`** is the gate's view of cluster membership. `Elsa.Cluster.Readability` implements it over the
  host's one membership provider (`AddEfSchemaReadability`). A host that composes none has gates that never finalize
  a version past the one each record was created at: the conservative direction, which only ever delays.
- **`EfSchemaFinalizationObservations`**, registered once by instance on the host container, is what the gates read
  and what the readability report names (spec 183, FR-019).
- **Timings** come from `Elsa:Persistence:EntityFramework:Finalization` (`EvaluationInterval`, default 30 seconds;
  `RefreshInterval`, 15 seconds; `IntentWaitBound`, 2 minutes; `IntentPollInterval`, 1 second).

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
only over first-party modules. See the package README's table for both types.

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
