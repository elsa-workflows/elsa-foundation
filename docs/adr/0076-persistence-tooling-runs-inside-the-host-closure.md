---
status: proposed
date: 2026-09-19
decision_context: Design of issue #1861, owner decisions taken during the design session on 2026-09-19.
---

# Persistence tooling runs inside the host's closure

Status: proposed (2026-09-19). Sipke Schoorstra approved the design direction for issue #1861 in
session: assembly source (host output plus the Nuplane package root, driven through EF's `IMigrator`
directly, with no `dotnet-ef`), the SQLite script refusal, the deliverable shape (spec, ADR, and a
slice breakdown), and fixing Nuplane gaps in Nuplane when that is cleaner. Implementation has not
started; this ADR and spec 171 are the design session's only artifacts.

Program goal: none/free-flow. [EF Core Persistence](../program-goals/ef-core-persistence.md) is
complete as of 2026-09-16, and [Feature Composition Readiness](../program-goals/feature-composition-readiness.md)
scopes Feature Composition Explorer readiness and dependency/settings classification, not operations
tooling. This decision does not reopen either bucket.

Tracking:

- [#1861](https://github.com/elsa-workflows/elsa-foundation/issues/1861)
- [Spec 171](../../specs/171-persistence-script-cli/spec.md)
- related: [#1667](https://github.com/elsa-workflows/elsa-foundation/issues/1667), the epic that
  delivered the shared EF foundation and independent module migrations; [#1629](https://github.com/elsa-workflows/elsa-foundation/issues/1629),
  the Secrets EF pilot phase that delivered runtime and CI/CD migration modes
  (`SecretsEfMigrationHostedService`, `dual-migrate.sh`); [PR #1755](https://github.com/elsa-workflows/elsa-foundation/pull/1755),
  which gave every EF module a four-provider migration lifecycle (`module-migrate.sh` and the
  design-time tooling catalog); and [#1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669),
  the spike that proved the four-provider lifecycle and isolation.

[ADR 0072](0072-ef-first-relational-persistence-with-provider-derived-contexts.md) D6 established
that AutoMigrate, Validate, and out-of-process apply share one compiled migration artifact set; this
decision keeps that invariant for the new tool instead of adding a second artifact format. [ADR
0073](0073-ef-core-is-the-only-first-party-persistence-family.md) D6 named out-of-process apply and
validation, concurrent locking, and fail-before-activation behavior as unresolved, gated on issue
#1657; this decision answers that migration-lifecycle question for tooling, while #1657's
cross-module transaction half stays a separate, open question this ADR leaves untouched. [ADR
0075](0075-oracle-is-not-a-supported-ef-core-engine.md) settled the engine set at four; this
decision's `--provider` flag accepts the same four and adds none. None of the three is reopened.

## Context

Inventory snapshot: `main` at `43d88cef44844e9f0153f222656189bc929abeb1`.

**`tools/ef/module-migrate.sh script` already does most of the mechanical work, for a source
checkout.** It enumerates every module context from the shared design-time tooling project, builds it
once, and for each context runs `dotnet ef migrations script --idempotent`
(`module-migrate.sh:120`) into `<output-dir>/<Module>/<Provider>.sql`. `script Sqlite` is refused
with exit code 2, because `SqliteHistoryRepository.GetEndIfScript` throws `NotSupportedException`
(`module-migrate.sh:51-59`; `tools/ef/README.md:69-74`). `script-check` regenerates into a temporary
directory and diffs it against the committed tree. None of this reaches a host built from packages:
`apply` and `validate` still take the connection string as positional argument 3
(`module-migrate.sh:46`), so it is not true today that "credentials never appear in process
arguments." Module resolution used to search `src` alone, which left the `Elsa3ImportMySqlDbContext`
and its three sibling contexts — now under `extensions/Elsa3/src` since [PR
#1850](https://github.com/elsa-workflows/elsa-foundation/pull/1850) moved them, so lookup found no
matching project there; [PR #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867)
already made `module-migrate.sh` search `extensions/` as well, by setting `module_roots=(src)` plus
`extensions` when present (`module-migrate.sh:31-35,96`). That change does not reach the deeper
limitation: the whole approach
still depends on `dotnet ef` against a **source** project and a **compile-time** design-time factory
(`tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs`, 49 factory lines, one per
provider-derived context across 13 modules). A host assembled from Nuplane packages has neither a
source checkout nor a startup project that references every module by `ProjectReference`.

**`Elsa.Foundation.Host` is the host this tool has to serve, and it carries no EF at all.** Its
`.csproj` references `CShells.AspNetCore`, `CShells.FastEndpoints`, `Nuplane`, `Nuplane.Loading`,
`Nuplane.Sources.Directory`, `Nuplane.Admin`, and exactly one project reference
(`Elsa.Api.AspNetCore`, for typed endpoint metadata only); no EF package, no provider engine, and no
Elsa feature implementation are compiled in
(`src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj`). Every module, its EF context, and its
provider engine arrive at run time as Nuplane packages, discovered by CShells through
`NuplaneAssemblyProvider`. A design-time factory approach cannot run against this host, because there
is nothing to point `dotnet ef --startup-project` at.

**A module's identity is scattered across at least four independent strings today**, none of which
is a stable, host-usable name. The registration class hardcodes an `Owner` string passed into
`EfModuleBinding` (for example `"Secrets"` in
`src/Elsa/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs:15-16`).
The module's own `*EfModule` class carries a separately frozen `HistoryModuleName`
(`SecretsEfModule.cs:7`, `"ElsaSecrets"`; `RuntimeEfModule.cs:11`, `"ElsaRuntime"`). The CShells
`[ShellFeature(name: ...)]` attribute on the feature class is a third string, and it is the one that
actually enables or disables the module (`SecretsEntityFrameworkCoreFeature.cs:12-13`,
`"SecretsEntityFrameworkCore"`). And design-time tooling and the migrations test suite each derive a
module name from the provider-suffixed `DbContext` class name independently — which is not a merely
theoretical divergence: the test's own `ModuleContextCatalog.HistoryTable` derives
`__EFMigrationsHistory_Runtime` by trimming the provider and `DbContext` suffix off the class name
(`ModuleContextCatalog.cs:91-92`), while every real host uses `__EFMigrationsHistory_ElsaRuntime`
(`RuntimeEfModule.cs:11,19`). Nothing today would catch a module whose four identities quietly
disagreed like this one already does.

**The only post-migration action anywhere is the Secrets projection reindex, and it is hand-wired
three separate ways.** The runtime path calls `SecretsProjectionContract.EnsureCurrentAsync` directly
after `EfDatabaseMigrator.ApplyAsync` inside `SecretsEfMigrationHostedService.ApplyAsync`
(`SecretsEfMigrationHostedService.cs:35-36`) — and `EnsureCurrentAsync` only audits: it throws when
legacy rows remain (`SecretsProjectionContract.cs:22,40-44`) rather than rewriting anything. The
out-of-process tooling's own `Program.cs` calls `SecretsProjectionContract.ReindexAsync` for a
`reindex` verb (`src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Program.cs:27`), and that is
the one path that actually rewrites rows (`SecretsProjectionContract.cs:54-58`). `tools/ef/dual-migrate.sh`
drives that verb as part of its `apply` command (`dual-migrate.sh:2-6,24-26`). `tools/ef/README.md`'s
own "What the checks mean" section states the same rule in general terms: "Neither runtime path
rewrites legacy rows" (`tools/ef/README.md:192-195`). Nothing generalizes this rule to a module
declaring its own action, and no other module has anywhere to put one.

**The Nuplane enable pipeline has no gate at all.** `FeatureManagementService.ApplyAsync` calls
`ValidateRequest` (`FeatureManagementService.cs:24`), then `shellStore.SaveAsync`
(`FeatureManagementService.cs:33`), then refreshes the runtime feature catalog and reloads shells
(`FeatureManagementService.cs:40-41`). Nothing in that sequence checks whether the module being
enabled has an unapplied migration; a module can be enabled against a database whose schema it has
never migrated, and the only backstop is `EfDatabaseMigrator`'s `Validate` exception
(`EfDatabaseMigrator.cs:28-35`), raised from `EfModuleMigrator<T>` at `LifecyclePhase.Prepare`
(`EfModuleMigrator.cs:18,51,70`) — which runs after CShells has already saved the enable request.

**Nuplane facts driving D1, D12, and D13.** Checked against a local checkout of the
`valence-works/nuplane` repository, branch `pr-67`, `git describe` = `0.0.10-10-g5c0e04e` — ten
commits ahead of tag `0.0.10`, itself ahead of the pinned `Nuplane` package version,
`0.0.9-preview.61` (`Directory.Packages.props:105`). **Every Nuplane change this ADR names therefore
needs a Nuplane release and an Elsa pin bump before the Elsa slice depending on it can start**; that
caveat applies to every `nuplane:`-cited fact below and is not repeated per fact.

A `HostIntegrated` package loads into a custom, non-collectible load context,
`HostIntegratedPackageGraphLoadContext` (`nuplane: src/Nuplane.Loading/HostIntegratedPackageGraphLoadContext.cs:6-12`),
made visible through an `AssemblyLoadContext.Default.Resolving` hook
(`nuplane: src/Nuplane.Loading/HostIntegratedAssemblyResolver.cs:26`) rather than loaded into the
default context. `EfRelationalProviderBinding` resolves a provider's `Use*` extension method by
`Type.GetType` (`EfRelationalProviderBinding.cs:178`), a scan of
`AppDomain.CurrentDomain.GetAssemblies()` (`:240`), and `Assembly.Load(engine.PackageId)` (`:250`).
A worker that resolved assemblies its own way could therefore bind a provider through a path the real
host never uses. The install layout is `<installRoot>/<feed>/<id>/<version>/`, marked complete by a
`.nuplane-ready` file (`nuplane: src/Nuplane/Feeds/PackageInstallStore.cs:20,42-47`); the pinned
active set is `store-state.json`, read and written through `StoreRegistry`, whose state types
(`StoreStateRecord`, `ActivePackageDescriptor`, carrying `InstallPath`) are already public and
DI-free (`nuplane: src/Nuplane/Store/State/StoreRegistry.cs:28-33`;
`src/Nuplane.Abstractions/ActivePackageDescriptor.cs:19-32`) — only `ActivePackageCatalogMapper` is
internal (`nuplane: src/Nuplane/Operational/ActivePackageCatalogMapper.cs:7`). TFM selection,
main-assembly choice, and native/RID probing are private statics inside `PackageLoader`; its
`hostTargetFrameworkOverride` parameter exists on `ResolveMainAssemblyPath` but every call site passes
`null` (`nuplane: src/Nuplane.Loading/PackageLoader.cs:69,716,890-894`). Nothing in Nuplane can refuse
to activate a package today: `ObserverEventDispatcher` catches and only logs an observer's exception
(`nuplane: src/Nuplane/Events/ObserverEventDispatcher.cs:16-45`), and `IPackageLoadModeAdvisor` is
consulted where a graph's load context is built, between the advisor pass and load-context creation
(`nuplane: src/Nuplane.Loading/PackageLoader.cs:123,271-273`), but it can only advise. Two adjacent
defects were found and are out of scope for #1861 (mentioned in the issue comment, not decided here):
`LockFileMode.Generate` never writes the lock file (`nuplane:
src/Nuplane/Reconciliation/LockFileCoordinator.cs:23-26`), and `PackageDependencyGraphResolver`
hardcodes an Elsa-specific allowlist of host-provided packages
(`nuplane: src/Nuplane/Reconciliation/PackageDependencyGraphResolver.cs:514-533`).

## Decision

### D1 — Persistence tooling runs as an out-of-process worker inside the host's closure

`dotnet-elsa` is a thin front end with no EF and no Elsa references. It launches a BCL-only worker
with `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json
Elsa.Cli.Worker.dll` (proposed), so the worker runs on the host's exact EF Core and provider engine
versions rather than versions the tool itself would pin. For a Nuplane host, the worker boots
Nuplane's own loader — already present in the host's closure — instead of reimplementing assembly
resolution; for a host that carries everything in its own deps file, it needs nothing extra. The
worker's one call into product code is a frozen entry point, reflectively invoked in the host's own
`Elsa.Persistence.EntityFramework`: `Tooling.EfToolingHost.RunAsync(Stream request, Stream response)`
(proposed), taking versioned JSON on stdin.

Reason: a worker that resolved assemblies its own way would bind a provider through a different
mechanism than the real host and could disagree with it silently, because
`EfRelationalProviderBinding` binds reflectively (Context, above) and a `HostIntegrated` package is
visible only through Nuplane's own `Default.Resolving` hook, not the default load context.
`dotnet-elsa` and its worker resolve no EF package at all, so `EfCoreDependencyGuardTests`'s consumer
scan finds nothing to flag for either project, and neither needs an admission record or a place in
`AllowedEfConsumers` — which licenses *hosts* to carry EF, provider engines included
(`EfCoreDependencyGuardTests.cs:20-38`), not how a module project is admitted. `EfToolingHost` itself
lives inside `Elsa.Persistence.EntityFramework`, a project the guard already admits through the
`Adr0072SecretsEfPilot` record (`EfCoreDependencyGuardTests.cs:718,731`); that record's expected
package set for the project — EF Core, Abstractions, Analyzers, and Relational only — does not need
to grow, because the entry point needs only `IMigrator`, `IMigrationsAssembly`, and
`IHistoryRepository`, all in `Microsoft.EntityFrameworkCore.Relational`, no `Design` package and no
provider engine.

Resolution failures exit 3 before any file is written, in four cases: the provider engine is in
neither the host's deps file nor the active package set, and the message reuses
`EfRelationalProviderBinding.DescribeBindingFailure(provider)` (`EfRelationalProviderBinding.cs:86`),
names where the tool looked, and ends "No other provider was tried."; the host pins an
`Elsa.Persistence.EntityFramework` older than the release that adds `EfToolingHost`, and the message
names both the pinned version and the version required; the host directory has neither a
`.runtimeconfig.json` nor a `.deps.json` — a single-file or self-contained publish — and the message
names both required files; and two versions of one assembly exist with nothing to choose between
them, which is refused rather than resolved by taking the highest.

Rejected: an in-process isolated `AssemblyLoadContext`. The tool's own runtime config would decide
which frameworks are shared, and `EfRelationalProviderBinding`'s reflection-based binding would then
resolve against the tool's own assemblies instead of the host's — the exact skew this decision exists
to avoid.

### D2 — One assembly-level attribute is a module's single declaration

An assembly-level `[EfModule("Workflows.Runtime", typeof(RuntimeDbContext), HistoryModule = ...,
Sqlite = ..., SqlServer = ..., PostgreSql = ..., MySql = ..., DependsOn = ..., PostMigration = ...)]`
(proposed) carries a module's name, context type, per-provider derived-context types, dependencies,
and post-migration actions. `EfModuleCatalog.Discover(assemblies)` (proposed) is the one place that
reads it. `EfModuleBinding.For(contextType)` (proposed) replaces the private `new EfModuleBinding(...)`
field each of the 13 registration classes constructs today, and the migrations test's own module
list, `ModuleContextCatalog`, moves onto the same descriptor — which fixes its latent
`__EFMigrationsHistory_Runtime` versus `__EFMigrationsHistory_ElsaRuntime` mismatch (Context, above)
by construction. A null provider property on the attribute means that provider is unsupported for
that module and produces a clear error rather than a missing case. `AllowMultiple` covers the two
assemblies that carry two contexts (Identity, Runtime.Distributed).

Reason: a module's identity is scattered across at least four independent strings today (Context,
above), with no guard keeping them in agreement. One descriptor collapses these onto one source of
truth. An attribute is also readable as metadata (`CustomAttributeData`) without running any module
code, which is what D13's Nuplane gate depends on: it reads the descriptor to decide whether a
package may activate before that package is loaded, not after. The 49 design-time factory lines in
`ModuleDesignTimeFactories.cs` stay as they are, because `dotnet ef migrations add` still needs a
factory per provider-derived context to generate a migration (D11); a guard test ties those lines to
the descriptor so the two cannot drift apart again.

Rejected: a public static descriptor or an interface each module implements by hand. Both require
instantiating module code merely to enumerate the module set, and neither gives a natural anchor for
the two assemblies that carry two contexts (Identity, Runtime.Distributed); an attribute reads
directly off metadata, `AllowMultiple` covers the two-context case, and constant-only attribute
arguments keep the declaration itself inert rather than executable.

### D3 — The module-name vocabulary is a fixed table; frozen history names are untouched

Spec 171 publishes the 13-name vocabulary that `--modules` matches case-insensitively: `Secrets`,
`Workflows.Runtime`, `Workflows.Design`, `Workflows.Publishing`,
`Workflows.Runtime.Distributed.Placement`, `Workflows.Runtime.Distributed.CommandTransport`,
`Activities.Design`, `Identity.Iam`, `Identity.ProviderConfiguration`, `Diagnostics.OpenTelemetry`,
`Diagnostics.StructuredLogs`, `Studio.Preferences`, `Elsa3.Activities.Design.Import`. Names must be
unique across the host's closure. Every module's frozen `HistoryModuleName` (for example
`RuntimeEfModule.HistoryModuleName = "ElsaRuntime"`) is untouched: the new vocabulary names what an
operator selects, not what a database records.

Reason: the only selector `module-migrate.sh` offers today is a regex over context class names
(`filter="${4:-.*}"`, `module-migrate.sh:46-47`), which requires reading .NET type names to use the
tool at all, and which a package consumer cannot supply because it has no source tree to read those
names from. The 13 names come from the D2 descriptors, not from class, feature, or package names, so
renaming a context or repackaging a module later does not change what an operator types.

Rejected: deriving names from class, feature, or package names directly. Any one of those three
already disagrees with at least one of the others for the same module today (D2's evidence), so
deriving the public vocabulary from one of them would promote its particular disagreement into the
interface operators depend on.

### D4 — `--provider` is authoritative; disagreement is a refusal, never a fallback

`--provider` is required on every command that selects a migration set (`plan`, `script`,
`script-check`, `apply`, `validate`, `post-migrate`); `list` needs none, since it only enumerates
what the host's closure declares. The provider-agreement check runs whenever the host's shells
configuration is found, whichever of `--modules`, `--all`, or `--from-host` selected the modules —
it is not limited to `--from-host`.

The check is per feature, not per module. One module can be backed by several shell features that
each carry their own `Provider` setting while mapping to the same context: `Workflows.Runtime` alone
is backed by eight such features — `RuntimeEntityFrameworkCoreFeature`,
`RuntimeBookmarksEntityFrameworkCoreFeature`, `RuntimeActivityExecutionEntityFrameworkCoreFeature`,
`RuntimeOperationalStateEntityFrameworkCoreFeature`, `RuntimeWorkflowAlterationEntityFrameworkCoreFeature`,
`RuntimeWorkflowTestScopeEntityFrameworkCoreFeature`, `RuntimeWorkflowExecutionEntityFrameworkCoreFeature`,
and `RuntimeArtifactsEntityFrameworkCoreFeature` — every one of them defaulting `Provider` to `Sqlite`
and each applying its own migrations against the one shared `RuntimeDbContext`
(`RuntimeEntityFrameworkCoreFeature.cs:30`; `RuntimeBookmarksEntityFrameworkCoreFeature.cs:24,58`;
`RuntimeActivityExecutionEntityFrameworkCoreFeature.cs:23`;
`RuntimeOperationalStateEntityFrameworkCoreFeature.cs:21`;
`RuntimeWorkflowAlterationEntityFrameworkCoreFeature.cs:17`;
`RuntimeWorkflowTestScopeEntityFrameworkCoreFeature.cs:17`;
`RuntimeWorkflowExecutionEntityFrameworkCoreFeature.cs:21`;
`RuntimeArtifactsEntityFrameworkCoreFeature.cs:69-73`). A mapped feature need not even live in the
module's own project: `AspNetCoreIdentityEntityFrameworkCoreFeature`, in the separate
`Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore` project, carries its own `Provider`
and registers migrations for `Identity.Iam`'s `IdentityIamDbContext` directly
(`AspNetCoreIdentityEntityFrameworkCoreFeature.cs:21,66`). A feature can also map to more than one
module: `WorkflowsDashboardEntityFrameworkCoreFeature` reads both `Workflows.Design`'s and
`Workflows.Runtime`'s contexts, through `WorkflowsDesignDbContext` and `RuntimeDbContext`
(`WorkflowsDashboardEntityFrameworkCoreFeature.cs:17-21`; `EfWorkflowPortfolioDataSource.cs:31-33`),
which is why `[UsesEfModule]` is `AllowMultiple`.

Every feature that is enabled in the shell, mapped through `[UsesEfModule]` to a selected module, and
that itself declares a `Provider` setting, must have an effective `Provider` equal to `--provider`;
the tool exits 3 and lists each one that differs, by feature name, together with its configured
value. A feature that is not enabled in the shell is ignored. Two features of one module that
disagree with each other therefore always surface, because at most one of them can equal
`--provider` — an unset `Provider` on a feature that declares one defaults to `Sqlite`
(`SecretsEntityFrameworkCoreFeature.cs:23`), so it disagrees with `--provider PostgreSql` exactly as
an explicit `Sqlite` setting would, and that disagreement is treated as the same error rather than
papered over as a gap. A mapped feature with no `Provider` property at all —
`WorkflowsDashboardEntityFrameworkCoreFeature` registers no migrations and only reads contexts other
features register — is skipped by this comparison entirely: the provider of the contexts it reads is
decided by the features that register them, and those are the ones compared. The manifest records
`providerAgreement: checked | not-checked`, and states plainly that an environment-variable override
of a shell's configured provider is invisible to the tool: the check reads shell configuration, not
what a host's environment would resolve to at runtime. There is no provider auto-detection and no
fallback to a different provider.

The check reads the same layered configuration a host would: `shells.json`, then
`shells.<environment>.json` when that file exists, the same overlay `Elsa.Workbench` applies
(`src/Apps/Elsa.Workbench/Program.cs:85,91`). `--environment` defaults to `Production`, because
ASP.NET Core's own default `EnvironmentName` is `Production` whenever no environment variable sets
it, and a host started with no configuration at all — the ordinary case — therefore reads
`shells.Production.json` when the host ships one, as `Elsa.Workbench` does
(`src/Apps/Elsa.Workbench/shells.Production.json`). The tool never reads `ASPNETCORE_ENVIRONMENT`
from its own process: the tool's environment is not the host's, and inferring one from the other
would be exactly the kind of silent cross-process assumption D10 refuses to make for packages. The
manifest records the environment the check actually used.

Reason: `EfProviderGuard.Ensure` already refuses a context opened against the wrong live provider at
runtime, throwing `"{owner} refuses provider '...'. Expected '...'"` (`EfProviderGuard.cs:17-27`), and
a feature's `Provider` setting silently defaults to `Sqlite` when a host leaves it unset
(`SecretsEntityFrameworkCoreFeature.cs:23`). A scripting tool that inferred the provider from that
kind of default, or fell back when the requested one lacked an artifact, could silently script the
wrong dialect against a real database — precisely what the issue asks the tool to avoid.

Rejected: a flag that silently overrides host configuration when the two disagree. That reintroduces,
at the CLI layer, the exact failure mode `EfProviderGuard` exists to catch at the runtime layer.

### D5 — `script --provider Sqlite` stays refused

The new tool keeps `module-migrate.sh`'s SQLite scripting refusal (exit code 2) rather than emitting
a script that is not safe to re-run.

Reason: `SqliteHistoryRepository.GetEndIfScript` throws `NotSupportedException`, because SQLite has no
conditional statement to wrap a migration in, so EF itself cannot produce an idempotent SQLite script
(`module-migrate.sh:51-54`; `tools/ef/README.md:69-74`). Dropping `dotnet-ef` (D11) does not lift this
constraint: `IMigrator.GenerateScript` with `MigrationsSqlGenerationOptions.Idempotent` goes through
that same `SqliteHistoryRepository`, so the new tool hits the identical `NotSupportedException`
however it drives EF. A plain script would sit in the same `NN-<slug>.sql` tree looking identical to
every idempotent file beside it while not being safe to re-run, which is a worse failure mode than a
refusal at the command line naming the alternative (`apply Sqlite`).

Rejected: a plain, non-idempotent SQLite script flagged `rerunnable: false` in the manifest. It keeps
the unsafe artifact in the same tree a reviewer already trusts to be safe, trading one clear refusal
for a manifest flag a downstream pipeline is free to ignore.

### D6 — A flat, ordered SQL tree plus one manifest, under fixed determinism rules

The output is flat `NN-<slug>.sql` files plus `migration-plan.json`, ordered topologically by each
module's declared `DependsOn` and then by ordinal name — never by the order modules were named on the
command line. The manifest carries top-level `efCoreVersion` and `engine` (`package`, `version`,
`source`), and per module: package id and version, context, history table, migration id range, a
sha256, `dependsOn`, and `postMigration[]`. Output is LF-terminated, UTF-8 without a byte-order mark,
with a fixed JSON key order, and carries no timestamps, absolute paths, tool version, or connection
string. `script-check` regenerates from the directory's own plan and byte-compares, reporting any
`.sql` file the plan does not list as stale.

A package upgrade with no migration change still moves `efCoreVersion`, `engine.version`, or a
module's `package.version`, without moving any SQL. `script-check` still exits 1 for that case: the
committed artifact no longer describes exactly what the host pins, and D6's determinism promise
covers the whole artifact, not only the `.sql` files. It reports that case distinctly from an actual
SQL difference — "manifest versions differ, SQL identical: regenerate and commit, nothing new to
run" — and lists which fields moved, so a reviewer does not mistake a version-only diff for a schema
change.

Reason: no cross-module foreign key exists anywhere in the codebase today, so `dependsOn: []` for
every module in the manifest is a true statement, not an aspiration; topological order still matters
once one is added. Determinism matters because `script-check`'s existing analogue
(`tools/ef/README.md:79-90`) exists specifically to catch a hand-edited or stale script in CI, and a
byte-for-byte comparison only works when nothing incidental — a path, a timestamp, a key order —
changes between two runs that changed nothing real.

Rejected: keeping the existing `<Module>/<Provider>.sql` tree (`module-migrate.sh:117-118`) and
ordering by argument order. A per-module subdirectory hides deployment order from a plain directory
listing, and ordering by argument order would let two invocations of the same `--modules` set produce
artifacts that differ only in file names — defeating `script-check`'s byte comparison.

### D7 — Connection by environment variable or stdin only

`--connection-env NAME` (default `ELSA_EF_CONNECTION`) or `--connection-stdin` are the only ways to
give `apply` and `validate` a connection string. `--connection` does not exist.

Reason: the issue requires that credentials stay out of process arguments, and today's tool does not
meet that bar: `module-migrate.sh apply` and `validate` take the connection string as positional
argument 3 (`module-migrate.sh:46`), so it lands in process arguments and shell history. The new front
end passes only the environment variable's NAME to the worker process; the value itself travels by
ordinary environment inheritance, and the worker's own request arrives as JSON on stdin, so nothing
sensitive appears in any process's argv. The design-time factories already read `ELSA_EF_CONNECTION`
for the same reason scripting never needs a real connection (`tools/ef/README.md:66-67`), so an
environment variable is already the pattern the rest of the tooling relies on.

Rejected: a `--connection` flag. It is exactly the positional-argument shape the issue calls out as
the thing to fix, carried forward under a new name.

### D8 — Post-migration actions are declared, audited at startup, and never auto-run

`IEfPostMigrationAction` (proposed) exposes `Id`, `AuditAsync`, and `RunAsync`, takes only the
context, and uses no DI. A module declares its actions on the D2 descriptor. After applying
migrations, `EfModuleMigrator<T>` audits every declared action under both `EfMigratePolicy` values; if
one reports as required, migration fails closed and the error names `dotnet elsa persistence
post-migrate`. The action itself never runs automatically. `SecretsProjectionReindex` (proposed),
wrapping `SecretsProjectionContract`, is the first instance; `SecretsEfMigrationHostedService` and the
Secrets-only `MigratePolicy` setting are retired, because the seam this decision adds is general
rather than Secrets-specific.

`SecretsEntityFrameworkCoreFeature.MigratePolicy` (`:54`) is a per-feature setting independent of the
host-wide `Elsa:Persistence:EntityFramework:Migrate:Policy` (`EfMigrateOptions.SectionName`).
Retiring it costs exactly that independence: an operator can no longer set Secrets' apply policy
separately from every other module enabled in the same shell. A host that configured it explicitly
must move the value to the host-wide key; a shell that genuinely needs a different policy from its
neighbors still can, because a shell's own `Configuration` node already layers over the host's for
this key — "one shell can run `Validate` while another does not"
(`src/Elsa/Persistence/EntityFramework/README.md:286-287`) — just at the shell-configuration layer
rather than the retired feature-setting layer.

Failing loudly needs a specific mechanism, not just an intention. CShells only binds a configuration
key onto a feature when the feature type still has a public settable property of that name
(`cshells: src/CShells/Features/FeatureConfigurationBinder.cs`, `AutoBindFeatureProperties`,
`:54-75`), so deleting the `MigratePolicy` property outright would make a host's still-configured
value bind to nothing and vanish silently. A throwing property setter is not sufficient either:
`BindProperty` (`:80-116`) catches any exception the setter raises and only logs a warning
(`:111-115`), so shell activation would proceed as if nothing were wrong. The property therefore
stays for one release, as an obsolete, nullable, ordinarily settable property that binds without
throwing, and the feature refuses to configure when it has a value — either throwing from its own
service-configuration path, or failing the feature validation the binder runs immediately after
binding (`ValidateFeature`, `:226-229`) — with a message naming
`Elsa:Persistence:EntityFramework:Migrate:Policy`. Everything else
`SecretsEfMigrationHostedService` did — the provider guard, the host and shell lifecycle hooks, and
the reindex audit — is covered by the general `EfModuleMigrator<T>` plus the post-migration audit
above; only the independent per-feature policy knob is lost.

Reason: today's rule — audit and fail closed at runtime, repair only out of process — is already the
right one; it is just hand-wired three separate ways for Secrets alone (Context, above), with no
declaration surface for any other module. This decision keeps that rule and generalizes it:
`RunAsync` is the same kind of explicit, operator-invoked repair that `EnsureCurrentAsync`'s audit
already refuses to perform itself, now declared once per module instead of wired by hand per module.

Rejected: resolving actions from the feature's DI container, and auto-running them at startup. DI
resolution is rejected because the CLI has no shell container to resolve from, and the one real
instance, `SecretsProjectionReindex`, needs only the context, so a DI dependency would buy nothing.
Auto-running at startup is rejected because it would take a bounded-batch data rewrite out of the
operator's hands, against the rule `tools/ef/README.md` already documents: neither runtime path
rewrites legacy rows.

### D9 — The activation-guard contract lives in Modularity Core; its EF implementation lives in its own adapter project

`IFeatureActivationGuard` (proposed) is added to `src/Elsa/Modularity/Core/Contracts/`, beside the
existing `IFeatureCatalogContributor`, `IFeatureManagementService`, `IRuntimeFeatureCatalogRefresher`,
and `IShellFeatureConfigurationStore`. `FeatureManagementService.ApplyAsync` calls the guards after
`ValidateRequest` and before `shellStore.SaveAsync`. The implementation,
`EfPendingMigrationActivationGuard` (proposed) — mapping features to modules via
`[UsesEfModule("...")]` (proposed) on feature classes — lives in a new
`src/Elsa/Modularity/EntityFramework/` adapter project, not inside the EF features themselves and not
inside `Elsa.Modularity.Nuplane`. Under `Validate`, a pending module refuses; under `AutoMigrate`, it
passes. A refusal surfaces as an HTTP 409 problem response from the Modularity API — the same status
`ModularityProblems` already returns for a revision conflict (`ModularityProblems.cs:48`) — naming the
exact `script` command, and nothing is saved or reloaded. When the guard is not composed, the
fallback is today's behavior: the `Validate` exception at `Prepare` still refuses, but only after
`shells.json` has already been saved. The documented operator path is: pin or upload the package,
reconcile so the feature becomes visible but stays disabled, run `script`, have the DBA apply the
SQL, run `post-migrate` if the plan lists a required action, then enable the feature. The new project
needs its own admission record in `EfCoreDependencyGuardTests` — a surface path prefix plus an exact
expected package set limited to EF Core and Relational, no provider engine (Consequences) — and must
not be added to `AllowedEfConsumers`, which licenses hosts to carry provider engines, not modules.

`FeatureManagementService.ApplyAsync` calls `RestoreSecrets` before `ValidateRequest`
(`FeatureManagementService.cs:23-24`), so by the time the guards run — after `ValidateRequest` — the
feature configuration they inspect already has its real connection settings restored, not the masked
placeholder a client submitted. A guard must never put a connection string in a refusal message or a
log line; only the feature name, the module name, and the policy may appear there. Under `Validate`,
a guard that cannot open the database or read its history table treats every migration as pending and
refuses — fail closed, naming the feature and the module and saying the database could not be
reached, never the connection itself. Under `AutoMigrate` the guard does not open the database at all
and passes, except that a provider engine that cannot bind is still a refusal regardless of policy,
exactly like D1's resolution failures (`EfRelationalProviderBinding.DescribeBindingFailure`).

A mapped feature with no `Provider` or connection of its own —
`WorkflowsDashboardEntityFrameworkCoreFeature` registers no migrations and only reads contexts other
features register — is evaluated through whichever enabled feature in the same shell actually
registers that module's migrations. If none is enabled, the guard passes and leaves the refusal to
that reading feature's own startup check, which already throws when its backends are not EF-owned
(`WorkflowRunHealthEntityFrameworkCoreRegistration.cs:16-21`;
`WorkflowPortfolioEntityFrameworkCoreRegistration.cs:19-22,30-33`).

Reason: `FeatureManagementService.ApplyAsync` runs `ValidateRequest`, then `shellStore.SaveAsync`,
then refreshes and reloads, with no migration check anywhere in that sequence (Context, above), so a
module can be enabled today against a database whose schema it has never migrated. The dependency
direction is what places the pieces where they are: `Elsa.Modularity.Core` stays free of EF, so the
contract lives there; `Elsa.Modularity.Nuplane` also stays free of EF, so the implementation cannot
live there either; the new adapter project references `Elsa.Modularity.Core` plus
`Elsa.Persistence.EntityFramework` — EF Core and Relational only, no provider engine — which is
exactly the reference set a guard that reads `[EfModule]` descriptors and asks EF whether migrations
are pending needs. Composition order is the other half: the guard has to already be registered before
any EF feature enables, which an EF-feature-owned registration cannot guarantee (Rejected, below).

Rejected: registering the guard from the EF features themselves. The first EF feature enabled on a
shell that has none yet would find no guard registered at all — the guard has to already be composed
before any EF feature is, not brought in by one. Putting the implementation inside
`Elsa.Modularity.Nuplane` is rejected on the opposite ground: that project has to stay free of EF, the
same way `Elsa.Modularity.Core` does.

### D10 — The tool never downloads packages by default

Every command resolves the active package set already on disk: `store-state.json` for a Nuplane
host, or `--packages <dir>` for an override directory carrying no state file, honoring the
`.nuplane-ready` marker and skipping `.tmp/` either way. No command reconciles or fetches from a feed;
an opt-in `--restore` remains an open research question (spec 171 research item R5) and nothing in
this ADR adds one.

Reason: the issue asks the tool to avoid silently falling back to a different provider or migration
set, and an implicit restore is the same category of surprise applied to packages instead of
providers — a script generated today should reflect exactly the packages an operator already pinned,
not whatever a feed would resolve to right now.

Rejected: the tool restoring packages implicitly when the install root is empty or incomplete. A
never-started remote-feed host has an empty install root by construction; silently populating it
before scripting would make the artifact depend on feed state at the moment the tool happened to run,
which is what the D6 determinism rules exist to rule out.

### D11 — `dotnet-ef` remains, but only for migration generation

`dotnet ef migrations add` against the factories in `ModuleDesignTimeFactories.cs` stays the only way
a migration is authored. The new CLI never shells out to `dotnet-ef` and never wraps it for `list`,
`plan`, `script`, `script-check`, `apply`, `validate`, or `post-migrate`; it drives EF's `IMigrator`
directly against the host's own compiled assemblies (D1).

Reason: migration generation needs a source project and a model to diff against, which a
package-based host does not have; operating against already-compiled artifacts is a different problem
with a different solution, and conflating the two is what makes `module-migrate.sh` unable to serve a
packaged host at all (Context, above).

Rejected: wrapping `dotnet-ef` inside the new tool for scripting, the way `module-migrate.sh` does
today. That reintroduces the requirement the new tool exists to remove: a source project per module
and a `dotnet-ef` invocation per context, neither of which a Nuplane-packaged host has.

### D12 — Gaps in Nuplane are fixed in Nuplane; Elsa does not reimplement its loader or state format

U1 (a named offline reader for `store-state.json`) and U2 (a host-free "load this active set" entry
point) are filed as Nuplane changes rather than built as Elsa-side workarounds. U3 (a pre-activation
gate contract, D13) and U4 (registering `DesiredManifestPackageSource`) are filed the same way. This
matches the owner's earlier stance on Groundwork gaps: fix them upstream when that is the cleaner
design, rather than duplicating the fix inside Elsa. Elsa's worker does not parse `store-state.json`
by hand, and does not reimplement TFM or asset resolution.

Reason: `StoreRegistry`, `StoreStateSerializer`, `StoreStateRecord`, and `ActivePackageDescriptor`
(carrying `InstallPath`) are already public and DI-free — only `ActivePackageCatalogMapper` is
internal — so a named reader is a thin addition, not new architecture. TFM selection, main-assembly
choice, and native/RID probing are private statics inside `PackageLoader`, and its
`hostTargetFrameworkOverride` parameter exists but no caller can reach it (Context, above). A
hand-rolled Elsa-side reader or resolver would either duplicate logic Nuplane can change without
notice, or misresolve an asset Nuplane's own loader would have picked correctly — the same skew
concern D1 raises about assembly resolution, applied to package state instead.

Rejected: Elsa parsing `store-state.json` directly and reimplementing TFM/asset resolution. Both are
private, versioned Nuplane internals; matching them by hand ties Elsa to Nuplane's current file format
and resolution algorithm instead of to a contract Nuplane commits to keep stable.

### D13 — Two gates, distinguished by when they run rather than by what either can know

D9's `IFeatureActivationGuard` is the gate the acceptance criterion depends on, and it ships
regardless of Nuplane's release schedule. U3, Nuplane's own pre-activation gate contract
(`IPackageActivationGate.EvaluateAsync`, invoked inside `PackageLoader.EnsureGraphLoadedAsync` between
the advisor pass and load-context creation), is kept as a second, independent gate rather than folded
into the first or dropped once the first exists. Nuplane's contract itself knows only the package
being loaded, but the Elsa implementation of that contract reads `[EfModule]` metadata from the
package without loading it, looks in shell configuration for any enabled feature mapped to that
module through `[UsesEfModule]`, and returns Allow when no enabled feature uses the module — or when
the policy is `AutoMigrate`.

Reason: the two gates differ mainly in *when* they run, not in what either one is theoretically able
to know. The Elsa guard runs when a feature is enabled on a package that is already loaded — Nuplane's
loader takes no part in that call at all. The Nuplane gate runs when a package graph is loaded, at a
restart or a reconcile, a point at which no feature-enable call happens, so there is nothing for the
Elsa guard to intercept. Today nothing can block package activation at that point: observer exceptions
are swallowed, and the load-mode advisor can only advise, never refuse (Context, above). Without U3,
the only backstop for that restart case is `EfDatabaseMigrator`'s `Validate` exception at `Prepare`;
the point there is not that `shells.json` was already saved — it was saved long before the restart —
but that the exception fires only after the package's assemblies have already been loaded, so the
schema check runs downstream of the thing it is supposed to guard.

Rejected: a single gate at only one of the two points. Enable-time only misses the restart and
reconcile case, where no feature is being enabled and the Elsa guard's call never fires. Load-time
only misses the ordinary case instead: it never runs when a feature is enabled on a package that is
already loaded, which is exactly how a module installed earlier and enabled later behaves, and
exactly the case the issue's acceptance criterion names. Depending on load-time alone would also make
that acceptance criterion wait on a Nuplane release and an Elsa pin bump before it could be met, while
the Elsa guard has no such dependency and can ship on its own schedule.

## Consequences

Positive:

- One documented path exists from a packaged Elsa 4 host to reviewable, deterministic SQL, closing
  the gap `module-migrate.sh` cannot close for a host with no source tree.
- A module's name, history table, and post-migration actions have one declared source instead of
  four independent strings that can silently disagree, as `ModuleContextCatalog` already does today.
- The Nuplane enable pipeline gains a required gate where none exists today, with a documented
  fallback for the case where it is not composed.
- No fifth engine, no new migration format, and no change to any of the 13 modules' schemas.

Costs and risks:

- A new packable tool, `Elsa.Cli` (proposed), is added under `src/`, not `tools/`, because
  `.github/workflows/packages.yml` packs `find src extensions -name '*.csproj' -not -path
  'src/Apps/*' -not -path '*/tests/*'` (`packages.yml:99-101`): a project under `tools/` is invisible
  to that pack and would never ship as `dotnet-elsa`.
- `Tooling.EfToolingHost.RunAsync` (proposed) becomes a frozen compatibility surface the moment it
  ships: a host built against a release older than the one that adds it has no such entry point, and
  the worker must fail with a clear, named error rather than an opaque reflection failure.
- `SecretsEfMigrationHostedService` and the Secrets-only `MigratePolicy` setting
  (`SecretsEntityFrameworkCoreFeature.cs:54`) are retired. A host that configured it loses the
  ability to set Secrets' apply policy independently of every other module enabled in the same
  shell, and must move the value to the host-wide key
  `Elsa:Persistence:EntityFramework:Migrate:Policy` (`EfMigrateOptions.SectionName`,
  `EfMigrateOptions.cs:18`), the same key every other module's `EfModuleMigrator<T>` already reads.
  A shell that still needs Secrets on a different policy from its neighbors keeps that ability, since
  a shell's own `Configuration` node already layers over the host's for this key
  (`src/Elsa/Persistence/EntityFramework/README.md:286-287`) — at the shell-configuration layer, not
  the retired feature-setting layer. Failing loudly for a host that still sets it needs a specific
  mechanism: CShells binds a configuration key onto a feature only when the feature type still has a
  public settable property of that name (`cshells:
  src/CShells/Features/FeatureConfigurationBinder.cs`, `AutoBindFeatureProperties`, `:54-75`), so
  deleting the property outright would make a still-configured value bind to nothing and vanish
  silently, and a throwing property setter is not sufficient either, because `BindProperty`
  (`:80-116`) catches any exception the setter raises and only logs a warning (`:111-115`). The
  property therefore stays for one release, obsolete and nullable, and the feature refuses to
  configure when it has a value — either from its own service-configuration path, or from the feature
  validation the binder runs after binding (`ValidateFeature`, `:226-229`) — naming
  `Elsa:Persistence:EntityFramework:Migrate:Policy`. This is a behavior change, not an additive one.
- `module-migrate.sh`'s `apply`, `validate`, `script`, and `script-check` become thin shims over the
  new CLI; its `pending` command, and `generate-module-migrations.sh` / `generate-ef-migrations.sh`,
  stay exactly as they are, because migration generation still needs a source project and `dotnet-ef`
  (D11). `tools/ef/dual-migrate.sh` and the Secrets `Tooling/` project are retired outright, not
  shimmed, and `ModuleMigrateScriptToolTests.cs` is rewritten against the new CLI surface. The three
  design-time factories Secrets is missing today — SQLite, SQL Server, and PostgreSQL, beside its
  existing MySQL-only line (`ModuleDesignTimeFactories.cs:41`) — are added, so Secrets stops being the
  one module the shared catalog treats differently.
- The proposed `Elsa.Modularity.EntityFramework` adapter needs a new admission record of its own in
  `EfCoreDependencyGuardTests` — a `SurfacePathPrefixes` entry plus an `ExpectedEfPackagesByProject`
  set restricted to EF Core and Relational, no provider engine — the same shape as the existing
  `Adr0072SecretsEfPilot` and `Adr0073*` records (`EfCoreDependencyGuardTests.cs:675-690,718-788`). It
  must not be added to `AllowedEfConsumers` (`EfCoreDependencyGuardTests.cs:20`), which licenses
  *hosts* to carry EF including every provider engine (`:21-38`) and would wrongly license the adapter
  to do the same. `dotnet-elsa` and its worker resolve no EF package at all, so neither needs a record
  or an allowlist entry. `Elsa.Persistence.EntityFramework`, which hosts `EfToolingHost`, is already
  admitted by `Adr0072SecretsEfPilot` (`:718,731`); its expected package set for that project — EF
  Core, Abstractions, Analyzers, and Relational only — must not grow, because the entry point needs
  only `IMigrator`, `IMigrationsAssembly`, and `IHistoryRepository`, all in
  `Microsoft.EntityFrameworkCore.Relational`, no `Design` package and no engine. [PR
  #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867) widened this guard's own project
  scan from `LoadSrcProjects()` over `src/**/*.csproj` to `LoadModuleProjects()` over
  `ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)` — `src` and `extensions`
  (`EfCoreDependencyGuardTests.cs:518,521-523`; `ModuleRoots.Production` at
  `tests/Elsa/Architecture/Support/ModuleRoots.cs:19`) — but the proposed adapter lives under `src/`
  regardless, so it was already inside the scan before that widening and stays inside it after.
- U1–U4 depend on a Nuplane release ahead of the pinned `0.0.9-preview.61`, and on an Elsa pin bump
  after that release ships; the Nuplane checkout this ADR verifies facts against is ten commits past
  a tag Elsa does not yet consume, so every dependent Elsa slice is blocked until that release and
  bump land (Context, above).
- Out of scope: U5 (a schema-v2 capability declaration, recorded as a Nuplane follow-up, not required
  for #1861); the two adjacent Nuplane defects found during this design (the lock file's missing
  writer, and `PackageDependencyGraphResolver`'s hardcoded allowlist), which are named in the issue
  comment only; and migration generation, which stays exactly where D11 leaves it, on `dotnet-ef`
  against source projects.

## Decision record

| Date | State | Record |
|---|---|---|
| 2026-09-19 | Design session held | Issue #1861 was designed against the current `tools/ef/module-migrate.sh`, `Elsa.Foundation.Host`, `FeatureManagementService`, and Nuplane's `pr-67` checkout. |
| 2026-09-19 | Four owner decisions taken | Assembly source is the host's own output plus the Nuplane package root, resolved through EF's `IMigrator` directly with no `dotnet-ef`; `script --provider Sqlite` stays refused; the deliverable is spec 171, this ADR, and an issue comment proposing the slice breakdown; Nuplane gaps (U1–U4) are fixed in Nuplane rather than worked around in Elsa, matching the owner's earlier stance on Groundwork gaps. |
| 2026-09-19 | ADR 0076 proposed | This document records D1–D13 from the design session. No implementation has started, and no child issue, Nuplane issue, or label change follows from this ADR alone. |
