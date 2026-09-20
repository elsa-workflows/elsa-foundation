# Feature Specification: `dotnet elsa persistence` — Package-Aware Migration SQL for Selected EF Modules

**Feature Branch**: `171-persistence-script-cli`

**Created**: 2026-09-19

**Status**: Draft

**Input**: GitHub issue [#1861](https://github.com/elsa-workflows/elsa-foundation/issues/1861), the owner's answers during design (execution model, provider agreement, and the Nuplane migrate-before-activation path), and [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md). Verified facts are in [research.md](./research.md).

---

## Problem Statement

An Elsa 4 host should be able to select its persistence modules, choose one EF provider, and get reviewable, idempotent SQL plus a deployment manifest, without knowing each module's project, DbContext, startup project, or migration assembly.

Most of the mechanics already exist in [`tools/ef/module-migrate.sh script`](../../tools/ef/module-migrate.sh): per-module idempotent SQL, the SQLite refusal, and `script-check`. It cannot serve package consumers, though. It drives `dotnet ef` against module **source projects** and a compile-time catalog ([`tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs)), and an Elsa 4 host built from packages has neither: it has compiled module assemblies loaded by Nuplane, and nothing else.

Four gaps sit between that script and the issue's ask:

1. **No `dotnet elsa` tool exists.** There is no supported entry point a packaged host, or a DBA pipeline that only has that host's published output, can run.
2. **There is no `--modules` name vocabulary.** The only selector today is a regex over context class names, which is an implementation detail, not something an operator can be handed.
3. **There is no general post-migration seam.** The Secrets projection reindex is hand-wired into three separate places rather than represented as a declared, auditable action any module can have.
4. **The Nuplane enable pipeline has no gate before activation.** A module can be enabled through the feature-management API while its schema is still behind, with nothing to stop it.

This spec closes all four gaps with design documents that this repository's process requires before code: a CLI surface, an execution model that runs inside the host's own dependency closure rather than beside it, a module descriptor that collapses four independently-maintained declarations into one, a post-migration contract, and an activation guard. It also corrects several claims in the issue text itself against what the current tooling actually does (see *Corrections to the issue text* below).

### Program goal

Program-goal state for this unit is **`none/free-flow`**. `ef-core-persistence` is a completed program goal, and `feature-composition-readiness` does not own operations tooling, so this spec does not reopen either bucket; it is scoped, free-standing work against the completed EF persistence foundation. As with every spec in this repository since the retired-measurement policy ([#1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668), [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)), **this spec makes no performance or timing claim** anywhere below; every requirement is about correctness, determinism, and failure behavior.

---

## Settled Decisions

Settled with the owner during design on #1861. These are inputs, not open questions; planning must not re-open them.

| # | Decision | Rationale |
|---|---|---|
| **D1** | The tool is an **out-of-process worker running inside the host's own dependency closure**, with its entry point in the host's pinned policy package (`Elsa.Persistence.EntityFramework`). | Rejected: an in-process isolated `AssemblyLoadContext` built by the tool itself. The tool's own runtime config would decide which frameworks are shared, and `EfRelationalProviderBinding`'s reflection-based provider binding (`Type.GetType`, `AppDomain.GetAssemblies()`, `Assembly.Load`) would then resolve types through a different loading path than the real host uses, so the tool could bind a provider the host would not, or vice versa. |
| **D2** | The **assembly-level `[EfModule]` attribute is the single module declaration**, discovered by `EfModuleCatalog.Discover(assemblies)`. An attribute is readable as metadata (`CustomAttributeData`) without running module code, `AllowMultiple` covers an assembly with two contexts, and constant-only arguments keep it declarative. | Rejected: a public static descriptor or an interface. Both require instantiating module code to enumerate, and neither has a natural anchor for two contexts in one assembly. A single declaration is needed at all because a module's identity is maintained today across four independent places: the `*EfModule` constants, a hand-written `new EfModuleBinding(...)` in each registration class, one line per provider context in `ModuleDesignTimeFactories.cs`, and the anchors in the test `ModuleContextCatalog`. That status quo already produced a latent mismatch — see *Four places, one declaration* in [research.md](./research.md) — which a single descriptor, read by all four consumers, removes by construction rather than by discipline. |
| **D3** | The tool publishes a **fixed 13-name `--modules` vocabulary**; `HistoryModuleName` values stay frozen. | Rejected: derive the name from a class, feature, or package name. Those vocabularies already disagree for one module, four different ways: its context is named `Runtime*DbContext`, its frozen history name is `ElsaRuntime`, its CShells feature key is `WorkflowsRuntimeEntityFrameworkCore` ([`src/Apps/Elsa.Workbench/shells.json:73`](../../src/Apps/Elsa.Workbench/shells.json)), and its package id is `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore` — four different strings for one module — so no single existing vocabulary can stand in for the name and it is declared explicitly instead. Secondarily, rejected: keep selecting modules by a regex over context class names, as `module-migrate.sh` does today; a regex is an implementation detail a host operator should never have to know, and it gives no stable name to put in a deployment manifest or a support ticket. |
| **D4** | **`--provider` is authoritative.** A selected module whose configured provider disagrees exits 3 and lists every offender; there is no fallback. | Rejected: silently generate SQL for whichever provider a module happens to be configured for, or fall back to a "supported" provider when the requested one does not bind. Either produces SQL for a database the operator did not ask for, which the issue explicitly asks this tool to avoid ("avoid silently falling back to a different provider or migration set"). |
| **D5** | **`script --provider Sqlite` stays refused**, exit 2. | Rejected: emit a plain (non-idempotent) SQLite script anyway. The constraint is not a property of `dotnet ef migrations script` or of dropping `dotnet-ef`; it is EF Core's own `SqliteHistoryRepository.GetEndIfScript` throwing `NotSupportedException`, because SQLite has no conditional statement to wrap a migration in. `IMigrator.GenerateScript` with `MigrationsSqlGenerationOptions.Idempotent` goes through that same history repository, so calling `IMigrator` directly instead of shelling out to `dotnet-ef` does not lift the constraint; a plain script would sit in the same tree looking identical to every idempotent file while being unsafe to re-run. |
| **D6** | The artifact is **flat, ordered `NN-<slug>.sql` files plus one `migration-plan.json`**, exactly as the issue proposes, with fixed determinism rules (LF, UTF-8 no BOM, fixed JSON key order, no timestamps/absolute paths/tool version/connection string). | Rejected: nest output under `<Module>/<Provider>.sql`, mirroring the compiled-migrations tree the way `module-migrate.sh script` does today. The issue's own example is flat and numbered; a DBA pipeline applies files in the order they are named, and a nested tree has no inherent order without also reading the manifest first. |
| **D7** | **Connection by environment variable (`--connection-env`, default `ELSA_EF_CONNECTION`) or stdin (`--connection-stdin`) only.** There is no `--connection` flag. | Rejected: a `--connection` flag taking the connection string directly, which is what `module-migrate.sh apply`/`validate` do today (positional argument 3). The issue explicitly asks that credentials never appear in process arguments; the current tooling does not meet that bar, which is one of the *Corrections to the issue text* below. |
| **D8** | **Post-migration actions are declared on the module descriptor, take only the `DbContext` (no DI), are audited automatically after every apply under both migrate policies, and never auto-run.** A required, unaudited action fails closed and names `dotnet elsa persistence post-migrate`. This keeps today's rule for Secrets and generalizes it: `SecretsEfMigrationHostedService` does not auto-run a repair today either — its `EnsureCurrentAsync` call is an *audit* that throws when legacy rows remain, and the repair (`ReindexAsync`) only ever runs out of process, via `tools/ef/dual-migrate.sh` ([`tools/ef/README.md:194-195`](../../tools/ef/README.md), "Neither runtime path rewrites legacy rows"). | Rejected: (a) actions resolved from the feature's DI container. The CLI worker has no shell container to resolve from, and the one real instance, `SecretsProjectionReindex`, needs only the context. (b) Auto-running an action at startup or as part of `apply`. That would take a bounded-batch data rewrite out of the operator's hands, which is not what today's runtime path does either — declaring and auditing, but never auto-running, keeps that decision with the operator while still failing closed if it is skipped. |
| **D9** | A new **`IFeatureActivationGuard` contract in `src/Elsa/Modularity/Core/Contracts/`**, with the EF-aware implementation, `EfPendingMigrationActivationGuard`, in a new adapter project, `src/Elsa/Modularity/EntityFramework/` (proposed), so `src/Elsa/Modularity/Core/` and `src/Elsa/Modularity/Nuplane/` stay EF-free. That new adapter project references EF Core and Relational only and therefore needs a **new admission record of its own** in [`EfCoreDependencyGuardTests`](../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) — a `SurfacePathPrefixes` entry for its path plus an `ExpectedEfPackagesByProject` entry naming its exact expected EF package set, the same shape `Adr0072SecretsEfPilot` (`:718`) and the `Adr0073…` records already use, `IsAdmittedEfSource` (`:675`) ORing them together — **not** an entry in `AllowedEfConsumers` (`:20`), which licenses a *host* to carry full provider engines and would wrongly license that here. | Rejected: (a) register the guard from the EF features themselves. The first EF feature enabled on a shell that has none registered yet would find no guard registered, so activation order would decide whether the check ran at all. (b) Put it in `Elsa.Modularity.Nuplane` directly. That project must stay free of EF, the same reason `Elsa.Modularity.Core` must. |
| **D10** | **The tool never downloads packages by default.** `--packages <dir>` is a path to an existing, already-populated directory; there is no implicit restore or reconcile. | Rejected: an implicit restore when the install root is empty or the state file is stale. That would make the tool's output depend on what it happened to download at the moment it ran, which is the same "silently changes what it operates on" failure the issue asks this tool to avoid for provider fallback. |
| **D11** | **`dotnet-ef` remains only for migration generation** (`generate-module-migrations.sh`), never for runtime `apply`/`script`/`validate` against a packaged host. | Rejected: have the worker shell out to `dotnet-ef` at runtime. `dotnet-ef` needs a design-time startup project and source projects to build against; a packaged host has neither, which is the exact gap this spec closes. |
| **D12** | **Gaps in Nuplane are fixed in Nuplane (U1–U4).** Elsa does not reimplement Nuplane's loader or its state-file format. | Rejected: parse `store-state.json` by hand and build a bespoke `AssemblyLoadContext` in Elsa. Nuplane's `HostIntegrated` load mode already puts assemblies through a `Default.Resolving` hook that `EfRelationalProviderBinding`'s reflection depends on (see [research.md](./research.md), R2); a parallel implementation would drift from it and bind providers differently from the real host. |
| **D13** | **Two gates, not one**: the Elsa `IFeatureActivationGuard` at feature-enable time is required; the Nuplane `IPackageActivationGate` (U3) at package-load time is the defense. | Rejected: rely on only one of the two. The Elsa guard alone does not stop a process restart from reloading a module whose schema went stale after the guard last ran; U3 alone does not know which shell enables which feature, only that a package is about to load — it cannot express "this feature needs this schema state." |

### Note on D5 and D6 — SQLite produces no artifact

D5 and D6 interact once: `script --provider Sqlite` is refused entirely (D5), so a SQLite deployment never gets a `migration-plan.json` from `script` — the operator instead uses `apply Sqlite` directly, or the host's own `AutoMigrate` default. The determinism rules in D6 apply only to artifacts that exist, which for SQLite means none from `script`.

### Why the tool runs inside the host's closure

`Elsa.Foundation.Host` compiles in **no EF Core reference at all** — verified in its [`.csproj`](../../src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj), which references only CShells and Nuplane packages plus one shared endpoint-metadata project. Its EF Core version, its modules, and its provider engine all arrive as Nuplane packages at run time, and [`docs/foundation-host-feeds.md:151`](../../docs/foundation-host-feeds.md) already documents that the provider engine "must be named by hand" when composing such a host's package closure, because nothing in the graph declares it — `EfRelationalProviderBinding` binds it reflectively.

A tool that ships its own EF Core reference would therefore almost certainly bind a **different** EF Core (and provider) version than the host it is scripting for. The design instead makes `dotnet-elsa` a front end with no EF or Elsa reference of its own. It launches a worker with `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Elsa.Cli.Worker.dll`, so the worker resolves assemblies exactly the way the host's own process would: on the host's exact EF Core version, the host's exact provider engine version, and the host's exact module assembly versions. The worker then reflectively calls one frozen entry point already inside that closure, `Tooling.EfToolingHost.RunAsync(Stream request, Stream response)` in the host's own `Elsa.Persistence.EntityFramework`, exchanging versioned JSON over stdin/stdout. Because the tool carries no EF reference, it cannot skew from the host's EF version, and [`EfCoreDependencyGuardTests`](../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) — whose `AllowedEfConsumers` list is `["Elsa.Workbench"]` today — needs no allowlist change for this feature.

For a Nuplane host specifically, the worker does not reimplement Nuplane's assembly resolution either. It boots Nuplane's own loader, already present in the host's closure, and loads the active package set recorded in `store-state.json`, offline and without reconciling. [research.md](./research.md) (R1, R2) verifies why: `HostIntegrated` packages load into a custom, non-collectible `HostIntegratedPackageGraphLoadContext` and become visible only through an `AssemblyLoadContext.Default.Resolving` hook Nuplane installs — the same path `EfRelationalProviderBinding` depends on. A hand-rolled resolver would not go through that hook and could bind a provider differently from the real host. This part of the execution model depends on upstream item U2 (below); `--packages <dir>` remains as an override for a package directory that has no state file, resolving through the same upstream API and honoring the `.nuplane-ready` completion marker. A host with no Nuplane at all — every module already in its own `.deps.json` — needs none of this; the worker resolves everything from the deps file alone.

### Two gates, and what each one knows

This spec keeps two separate gates rather than one, because they know different things and neither can stand in for the other:

- **The Elsa `IFeatureActivationGuard`** runs at feature-enable time, inside `FeatureManagementService.ApplyAsync`. It knows the provider, the connection, and — through `[UsesEfModule]` on the feature class — exactly which EF module a feature depends on. This is the gate the acceptance criterion in #1861 ("a later Nuplane module installation has an explicit migration-before-activation path") is about, and it is **required**. When this guard is not composed into a host at all, the fallback is the `Validate`-policy `InvalidOperationException` thrown at CShells' Prepare phase (`EfDatabaseMigrator.ApplyAsync`, [`EfDatabaseMigrator.cs:28-35`](../../src/Elsa/Persistence/EntityFramework/EfDatabaseMigrator.cs)), which refuses the module only **after** `shells.json` has already been saved — this is FR-065, not the guard itself.
- **The Nuplane `IPackageActivationGate`** (upstream item U3) runs at package-load time, inside `PackageLoader.EnsureGraphLoadedAsync`, before a load context is created for the graph. It knows the package being loaded and its declared `[EfModule]` metadata (read without loading the module), but not which shell or feature is about to enable it. This is the **defense**: it stops a process restart from ever loading a module whose schema is behind, which is the gap between "the feature was correctly refused once" and "the package quietly loads again on the next restart anyway." Today's backstop for that same gap is the same `Validate`-policy exception at Prepare — but there it fails the shell's activation only **after** the stale-schema package's assemblies have already loaded, one step later than the guard-not-composed fallback above, because loading precedes CShells' Prepare phase.
- **How the Elsa implementation of the U3 gate contract decides**, given that Nuplane itself knows neither the provider nor the connection a module would use: it reads `[EfModule]` metadata off the package **without loading it**, then looks in the host's shell configuration for any enabled feature mapped to that module through `[UsesEfModule]`. When no enabled feature uses the module, it returns `Allow` — an installed-but-unused module is not this gate's concern. When one does, it resolves that feature's provider and connection the same way the Elsa guard does, and under `Migrate:Policy=AutoMigrate` it also returns `Allow`, exactly as the Elsa guard does, because auto-migration is expected to bring the schema current itself.

### Corrections to the issue text

The issue's proposed experience and acceptance criteria are the starting point for this spec, but several of its specific claims do not match the current tooling or the module topology, and are corrected here rather than carried forward silently:

- **`Workflows.Management` does not exist as a module.** The issue's example command passes `--modules Secrets,Workflows.Runtime,Workflows.Management`. The vocabulary this spec defines has `Workflows.Design` and `Workflows.Publishing` instead; there is no single module that corresponds to "Workflows.Management".
- **"Resolve each module's provider-specific migration artifact"** does not mean a per-provider package. The migration artifact is a **provider-derived DbContext inside the one module assembly** (for example `SecretsPostgreSqlDbContext` inside `Elsa.Secrets.Persistence.EntityFrameworkCore.dll`), selected by provider name, not a separately shipped package per provider.
- **The SQLite refusal is not mentioned in the issue**, but it is a real, load-bearing constraint of EF Core's `SqliteHistoryRepository.GetEndIfScript` today (D5) — not of `dotnet ef migrations script` specifically, and not lifted by this tool calling `IMigrator` directly instead of shelling out to `dotnet-ef` — and this spec keeps it.
- **The issue's "migration range"** is **`0 → head`** offline, always. A real `from` (a database's actual current migration) exists only when `plan` is given a live connection; `script` never opens one.
- **The issue's "dependency/order information"** is empty today, because no cross-module foreign key exists in the first-party module set (verified in [research.md](./research.md)). The manifest records that explicitly as `dependsOn: []` per module and `ordering: "dependsOn-then-name"` at the top level, which tells a reader that any order is safe today and that the ordinal-name order the manifest lists is the canonical one — not that dependency information was omitted.
- **"Keep credentials out of process arguments"** is not true of the tooling that exists today: `module-migrate.sh apply`/`validate` take the connection string as **positional argument 3** ([`tools/ef/module-migrate.sh:46`](../../tools/ef/module-migrate.sh)). This spec's D7 (`--connection-env`/`--connection-stdin` only, no `--connection` flag) is what actually satisfies the issue's own requirement; the existing script does not, yet.
- **The issue's acceptance test — "starting Elsa with migration validation succeeds after the artifact is applied" — cannot honestly pass for Secrets** until the post-migration slice (Elsa slice 7 below) lands, because Secrets' projection reindex is not yet expressed as a migration-adjacent, auditable step; today it is a hand-wired call inside `SecretsEfMigrationHostedService` that this tool's `apply`/`validate` commands do not know about.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A DBA-pipeline operator scripts PostgreSQL for an explicit module selection, then starts the host under Validate (Priority: P1)

An operator running a deployment pipeline that reviews SQL before it runs picks an explicit module list and PostgreSQL, gets a reviewable artifact with no database connection needed to produce it, has a DBA apply the SQL, and then starts the host with `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate`.

**Why this priority**: This is the feature as the issue names it, and it is the scenario every other requirement in this spec ultimately serves. If this does not work end to end, nothing else about the tool matters.

**Independent Test**: Run `dotnet elsa persistence script --host <path> --provider PostgreSql --modules Secrets,Workflows.Runtime --output artifacts/` against a built host with no database reachable; assert the artifact and manifest are produced; apply the SQL with a raw ADO client against a real PostgreSQL database; start a host configured with `Migrate:Policy=Validate` against that database and assert it starts without throwing.

**Acceptance Scenarios**:

1. **Given** a built Elsa host and no database connection, **When** the operator runs `script --provider PostgreSql --modules Secrets,Workflows.Runtime --output artifacts/`, **Then** the command succeeds (exit 0), writes `01-<slug>.sql`, `02-<slug>.sql`, and `migration-plan.json` to `artifacts/`, and never opens a connection.
2. **Given** that artifact, **When** a DBA applies each `.sql` file in numeric order against a fresh PostgreSQL database with a plain ADO client, **Then** every migration in the manifest's `migrations.ids` is recorded in its module's own `__EFMigrationsHistory_*` table.
3. **Given** that database, **When** the host starts with `Migrate:Policy=Validate`, **Then** it starts successfully, because no module reports a pending migration.
4. **Given** a module list containing a name not in the 13-name vocabulary, **When** `script` runs, **Then** it exits 3 before writing any file and names the unresolved module.
5. **Given** `Workflows.Runtime` is selected and two of its enabled shell features disagree with each other on `Provider` (`RuntimeEntityFrameworkCoreFeature` set to `PostgreSql`, `RuntimeBookmarksEntityFrameworkCoreFeature` left at its `Sqlite` default), **When** `script --provider PostgreSql --modules Workflows.Runtime --from-host --shell <name>` runs, **Then** it exits 3 and lists `RuntimeBookmarksEntityFrameworkCoreFeature` as an offender, naming its module and its configured `Sqlite` value — the other feature already agreeing with `--provider` does not mask it.

---

### User Story 2 - An operator on a Nuplane-only host generates SQL with no source tree at all (Priority: P1)

`Elsa.Foundation.Host` ships no EF Core reference and no source code; every module, and the provider engine itself, arrives as a Nuplane package. An operator with only that host's published output and its `packages/` directory (or a configured feed already reconciled once) runs the same `dotnet elsa` command and gets the same kind of artifact.

**Why this priority**: This is the host shape the issue is actually motivated by — a "clean Elsa host" in the issue's acceptance language is, in practice, a packaged host with no `dotnet-ef` and no source. If the tool only works against a source checkout, it has not closed the gap the issue opened.

**Independent Test**: Build `Elsa.Foundation.Host` with its packages already installed under its `packages/` directory and a `store-state.json` recording the active set; run `dotnet elsa persistence list --host <published output>` and `script` against it with no source tree present anywhere on the machine; assert both succeed and name every installed EF module.

**Acceptance Scenarios**:

1. **Given** a published `Elsa.Foundation.Host` output directory with its Nuplane `packages/` populated and a `store-state.json`, **When** the operator runs `dotnet elsa persistence list --host <path>`, **Then** every EF module in the active package set is listed by its canonical `--modules` name, with no `dotnet-ef` invocation and no source project resolution.
2. **Given** the same host, **When** the operator runs `script --provider SqlServer --all --output artifacts/`, **Then** the worker boots Nuplane's own loader to resolve the module and provider-engine assemblies, and the resulting artifact matches what a source-tree run against an equivalent host would produce for the same modules.
3. **Given** a `--packages <dir>` override pointing at a directory with an in-progress extraction (a package directory present but missing `.nuplane-ready`), **When** the command runs, **Then** that package is treated as not installed, not as a corrupt one.
4. **Given** the host's install root is empty (never reconciled), **When** any command runs, **Then** it fails (exit 3) rather than silently downloading packages to populate it.

---

### User Story 3 - A module installed later through Nuplane cannot activate ahead of its schema (Priority: P1)

An operator enables a feature backed by an EF module that was just installed through Nuplane, on a host running `Migrate:Policy=Validate`. The feature is refused, naming the exact command to run; after the operator scripts, applies, and runs any required post-migration action, the same enable request succeeds.

**Why this priority**: This is the issue's other named acceptance criterion — "a later Nuplane module installation has an explicit migration-before-activation path" — and the one gap that has no partial existing coverage at all today (`FeatureManagementService.ApplyAsync` has no guard between validating a request and saving it).

**Independent Test**: Compose a test host with the guard registered and `Migrate:Policy=Validate`; enable a feature whose `[UsesEfModule]` module has a pending migration; assert the apply call is refused and nothing was saved; run the module's migrations; retry the same apply call and assert it now succeeds.

**Acceptance Scenarios**:

1. **Given** a feature whose module has a pending migration and `Migrate:Policy=Validate`, **When** the operator calls `FeatureManagementService.ApplyAsync` to enable it, **Then** the call fails with a 409-shaped result naming the exact `dotnet elsa persistence script` (or `post-migrate`) command to run, and `shellStore.SaveAsync` is never called.
2. **Given** the same feature under `Migrate:Policy=AutoMigrate`, **When** the operator enables it, **Then** the guard passes and the feature activates normally.
3. **Given** the operator has since applied the pending migration out of process, **When** the same enable request is retried, **Then** it succeeds.
4. **Given** a host where the `IFeatureActivationGuard` composition is absent entirely (an older host, or one that opted out), **When** a module with pending migrations is enabled, **Then** `shells.json` is saved first and the existing `Validate`-policy exception at CShells' Prepare phase is what ultimately refuses the module — later, and after the save, which is why this spec still asks for the Nuplane package-activation gate (U3) as a second line of defense (see *Two gates* above).

---

### User Story 4 - `script-check` in CI catches edited or stale SQL before it merges (Priority: P2)

A CI job regenerates every module's SQL from the current model into a temporary location and byte-compares it against the committed artifact. A hand-edited statement, a model change with no regenerated script, and a script for a module that no longer exists in the selection all fail the job with a diff or a clear message — not a passing green check that hides drift.

**Why this priority**: `script-check` already exists for the source-tree tool ([`tools/ef/README.md`](../../tools/ef/README.md), "Keeping the committed SQL honest") and is the only thing standing between "SQL was reviewed once" and "SQL silently drifted from the model it claims to represent." Carrying it forward, and making it as available to a packaged host as `script` itself, is what keeps the artifact trustworthy over time rather than only at the moment it was first generated.

**Independent Test**: Commit a generated artifact; hand-edit one statement in one `.sql` file; run `script-check` against the same host, provider, and module selection; assert it fails and reports the specific file and diff. Separately, add a migration to one module without regenerating; assert `script-check` also fails for that module, distinctly.

**Acceptance Scenarios**:

1. **Given** a committed artifact that exactly matches what `script` would produce today, **When** `script-check` runs with the same `--host`, `--provider`, and module selection, **Then** it exits 0.
2. **Given** the same artifact with one statement hand-edited, **When** `script-check` runs, **Then** it exits 1, names the file, and shows the diff.
3. **Given** a module's model changed since the artifact was generated, with no regenerated `.sql`, **When** `script-check` runs, **Then** it exits 1 and reports that file as stale — before comparing against a stale in-memory model, because the worker is built from the host's current deps, not from whatever produced the committed files.
4. **Given** a `.sql` file physically present in the output directory that the directory's own `migration-plan.json` does not name — for example, a module that was removed from the selection after the artifact was first generated, **When** `script-check <dir>` runs, **Then** it is reported as a stale/orphaned file, distinct from an out-of-date one.

---

### User Story 5 - Post-migration reindex for Secrets is explicit, audited, and never silent (Priority: P2)

After Secrets' migrations are applied, its projection columns are audited against the persisted Unicode algorithm they must use. If any row still carries a legacy projection, the host refuses to start (or, for the tool, refuses to report `apply` as fully done) and names `dotnet elsa persistence post-migrate` rather than silently leaving stale rows in place or silently reindexing them without the operator's awareness.

**Why this priority**: This is the first real instance of the general post-migration seam (D8), and it retires three hand-wired call sites (`SecretsEfMigrationHostedService`, the Secrets-only `MigratePolicy` setting, and the entire Secrets `Tooling/` project) in favor of one declared, reusable mechanism every other module can use the same way.

**Independent Test**: Seed a Secrets database with rows written under the legacy projection algorithm; apply migrations; run `dotnet elsa persistence post-migrate --modules Secrets`; assert it reports the required action, and that a plain `apply` without `post-migrate` leaves the host refusing to consider the module fully migrated.

**Acceptance Scenarios**:

1. **Given** a Secrets database with legacy projection rows, **When** migrations are applied and the post-migration audit runs, **Then** `SecretsProjectionReindex.AuditAsync` reports the action as required, and the tool reports it rather than exiting 0 as if nothing were pending.
2. **Given** that required action, **When** the operator runs `dotnet elsa persistence post-migrate --modules Secrets --provider <provider>`, **Then** `SecretsProjectionReindex.RunAsync` reindexes the legacy rows and a subsequent audit reports the action as no longer required.
3. **Given** a Secrets database with no legacy rows, **When** the audit runs, **Then** it reports nothing required, and no action ever runs automatically as a side effect of `apply`.
4. **Given** the retirement of `SecretsEfMigrationHostedService` and the Secrets-only `MigratePolicy` setting, **When** a host that used to depend on the old hosted service starts, **Then** the same audit-and-refuse behavior is provided by `EfModuleMigrator`'s post-migration audit instead, with no behavior silently dropped.

---

### User Story 6 - A third-party module author adds their own EF module to the vocabulary (Priority: P3)

A module author outside the 13 first-party modules declares `[EfModule("Acme.Widgets", typeof(WidgetsDbContext), ...)]` on their own assembly. `EfModuleCatalog.Discover` picks it up from the host's closure the same way it picks up first-party modules, with no Elsa PR required.

**Why this priority**: The descriptor (D2) only earns its design if it is genuinely extensible past the 13 names this spec ships. This story is lower priority than the others because no third-party module exists yet to exercise it, but the acceptance scenario is cheap to test with a fixture and protects the design from being accidentally first-party-only.

**Independent Test**: Build a fixture assembly under `tests/Elsa/Cli/Fixtures/` carrying one `[EfModule]` declaration with a name outside the 13-name table; load it into a test host closure; assert `list` and `script` both recognize and process it identically to a first-party module.

**Acceptance Scenarios**:

1. **Given** a third-party assembly in the host's closure carrying a valid `[EfModule]` declaration, **When** `dotnet elsa persistence list` runs, **Then** the third-party module appears by its declared name, indistinguishable in shape from a first-party entry.
2. **Given** that module and a supported provider, **When** `script` runs with it selected, **Then** it produces a numbered `.sql` file and a `migration-plan.json` entry exactly like a first-party module's.
3. **Given** a third-party module whose declared name collides case-insensitively with an existing name in the host's closure, **When** discovery runs, **Then** it fails closed (exit 3) naming both colliding assemblies, rather than silently picking one.

---

## Edge Cases

- **A module's `[EfModule]` declares `null` for the requested provider.** That provider is unsupported for that module; the tool exits 3 naming the module and the provider, not a stack trace from a failed reflection call.
- **`--modules`, `--all`, and `--from-host` are all omitted, or more than one is given.** Exit 2 (usage error). There is no default selection.
- **Two versions of the same module assembly are visible in the closure with nothing to choose between them** (for example, both present in a Nuplane package graph with no shared-assembly policy resolving it). This is a refusal (exit 3), not a "pick the first" or "pick the newest" heuristic.
- **`script --provider Sqlite`.** Refused, exit 2, with the same message shape `module-migrate.sh` already gives, naming `apply Sqlite` as the alternative (D5).
- **An enabled feature backing a selected module has a configured provider that disagrees with `--provider`.** Exit 3, and every disagreeing feature is listed in one error, by feature name, module, and configured value — not just the first one found. This runs whenever shells configuration is found beside the host, under any selector, not only `--from-host`.
- **Two enabled features backing the same module disagree with each other**, one matching `--provider` and one not (for example `Workflows.Runtime`'s `RuntimeEntityFrameworkCoreFeature` set to `PostgreSql` and `RuntimeBookmarksEntityFrameworkCoreFeature` left at its `Sqlite` default). Because the comparison is per feature, the disagreeing one is still reported: agreement by one feature of a module never masks disagreement by another.
- **`--connection-env NAME` names a variable that is unset, and `--connection-stdin` was not given either**, for a command that needs a connection (`apply`, `validate`, `plan` with a live check). Exit 2.
- **A required post-migration action has not been run.** `post-migrate --audit-only` (or the audit step folded into `validate`) reports it, and any command that would otherwise report success instead fails closed, naming `post-migrate`.
- **`--packages <dir>` points at a directory with no `store-state.json` and no `.nuplane-ready` markers anywhere** (an empty or freshly created directory). Exit 3: nothing is installed there, and the tool does not attempt to populate it (D10).
- **A module declares a `DependsOn` cycle.** Exit 3 at discovery time, before any ordering is attempted, naming the cycle. (Not observed in the first-party set today, per SC-008 and [research.md](./research.md) — but the ordering algorithm must still detect it rather than infinite-loop or silently drop a module.)
- **A module's `DependsOn` names a module that is not itself selected.** Exit 3, naming the missing module, before any ordering is attempted.
- **`script-check <dir>` regenerates from that directory's own `migration-plan.json` and byte-compares.** Any `.sql` file physically present in the directory that the plan does not name is reported as stale; there is no separate filtered/unfiltered mode, because the plan itself, not the command-line selection, is what `script-check` checks against.
- **The requested provider's engine is present neither in the host's deps file nor in the resolved package set.** Exit 3 before any file is written, naming where the tool looked and ending "No other provider was tried."; for a Nuplane-only host this points the operator at [`docs/foundation-host-feeds.md`](../../docs/foundation-host-feeds.md) (FR-009).
- **The host pins an `Elsa.Persistence.EntityFramework` older than the release that adds `Tooling.EfToolingHost`.** Exit 3 before any file is written, naming both the pinned and the required version (FR-010).
- **The host directory has no `.runtimeconfig.json` or `.deps.json`** — a single-file or self-contained publish that does not produce them. Exit 3 before any file is written, naming both required files (FR-011).
- **An artifact scripted for one schema is applied against a host configured for a different schema** (artifact scripted for schema A, host's `Elsa:Persistence:EntityFramework:Schema` set to schema B). The tool cannot catch this itself — it does not read the host's runtime configuration key (FR-029) — so the SQL applies cleanly into schema A, and the host then starts under `Migrate:Policy=Validate` looking for its migrations-history table in schema B, where nothing was applied; every migration reports pending. This is a correct refusal, not a tool defect, and the manifest's top-level `schema` field is how a reviewer catches the mismatch before applying, by checking it against the host's actual configured schema.

---

## Requirements *(mandatory)*

### Functional Requirements

**Execution model**

- **FR-001**: `dotnet-elsa` MUST be a front-end process with no `PackageReference` to EF Core, any EF provider, or any Elsa persistence assembly.
- **FR-002**: The tool MUST launch a worker via `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Elsa.Cli.Worker.dll`, so the worker resolves assemblies on the host's own EF Core, provider engine, and module versions.
- **FR-003**: The worker MUST communicate with the host's `Tooling.EfToolingHost.RunAsync(Stream request, Stream response)` entry point in `Elsa.Persistence.EntityFramework` using versioned JSON over stdin/stdout, and MUST NOT depend on any other entry point.
- **FR-004**: Adding `Tooling.EfToolingHost` to `Elsa.Persistence.EntityFramework` MUST NOT grow that project's expected EF package set. The project is already covered by the `Adr0072SecretsEfPilot` admission record in [`EfCoreDependencyGuardTests`](../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) (`:718`) — `src/Elsa/Persistence/EntityFramework/` is one of its `SurfacePathPrefixes`, and its `ExpectedEfPackagesByProject` entry for this project's `.csproj` is exactly `Microsoft.EntityFrameworkCore`, `.Abstractions`, `.Analyzers`, and `.Relational` (`CorePackages()`), with no `.Design` package and no provider engine. `EfToolingHost` MUST be implemented using only EF Core and Relational types already reachable from that package set — `IMigrator`, `IMigrationsAssembly`, `IHistoryRepository` — so the existing admission record's expected-package list does not need to change.
- **FR-005**: For a host with an active Nuplane package set, the worker MUST resolve module and provider-engine assemblies by loading that active set (from `store-state.json`, offline, without reconciling) through Nuplane's own loading API, and MUST NOT implement its own assembly-loading or `store-state.json`-parsing logic.
- **FR-006**: `--packages <dir>` MUST override the package root used for that resolution, MUST honor the `.nuplane-ready` completion marker, and MUST skip any `.tmp/` staging directory.
- **FR-007**: For a host with every module in its own `.deps.json` (no Nuplane), the worker MUST resolve directly from the deps file with no additional package resolution step.
- **FR-008**: The tool MUST NOT require `dotnet-ef` or a source project at run time for `list`, `plan`, `script`, `script-check`, `apply`, `validate`, or `post-migrate`.
- **FR-009**: When the requested provider's engine is present neither in the host's `.deps.json` nor in the resolved package set, the tool MUST exit 3 before writing any file, with a message built from `EfRelationalProviderBinding.DescribeBindingFailure(provider)` prefixed with where the tool looked (deps file, then package set), and ending "No other provider was tried." — for a `Elsa.Foundation.Host`-shaped host, the message MUST point the operator at [`docs/foundation-host-feeds.md`](../../docs/foundation-host-feeds.md), which already documents that the provider engine "must be named by hand" in such a host's package closure.
- **FR-010**: When the host pins a version of `Elsa.Persistence.EntityFramework` older than the release that adds `Tooling.EfToolingHost`, the tool MUST exit 3 before writing any file, with a message naming both the pinned version and the minimum required version.
- **FR-011**: When the host directory is missing `<host>.runtimeconfig.json` or `<host>.deps.json` (for example, a single-file or self-contained publish that does not produce them), the tool MUST exit 3 before writing any file, with a message naming both required files.
- **FR-012**: Shipping the `dotnet-elsa` front end and its worker MUST require no change to [`EfCoreDependencyGuardTests`](../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) at all: neither project resolves any EF Core package, so the guard's project scan — which covers `src/` and `extensions/` via `ModuleRoots.Production`, not `src/` alone — finds nothing to flag for either of them, whether or not they are named in `AllowedEfConsumers` or covered by an admission record. This is a different, narrower guarantee than the one the activation-gate adapter project needs (FR-061, in the *Activation gate* group below), which does reference EF Core and Relational and therefore does need a new admission record of its own.

**Discovery and the module descriptor**

- **FR-013**: An assembly-level `[EfModule(name, contextType, ...)]` attribute MUST exist in `src/Elsa/Persistence/EntityFramework/` (proposed), carrying at minimum: the canonical name, the base `DbContext` type, the `HistoryModule` name, per-provider derived-context types (`Sqlite`, `SqlServer`, `PostgreSql`, `MySql`), `DependsOn`, and `PostMigration` actions.
- **FR-014**: `EfModuleCatalog.Discover(assemblies)` (proposed) MUST enumerate every `[EfModule]` declaration across the assemblies handed to it, first-party and third-party alike, with no first-party-only special case.
- **FR-015**: The attribute MUST support `AllowMultiple`, for assemblies that declare two contexts (Identity, Runtime.Distributed).
- **FR-016**: A `null` value for a provider property on the descriptor MUST mean that provider is unsupported for that module, producing a clear error rather than a null-reference failure when that provider is requested.
- **FR-017**: `EfModuleBinding.For(contextType)` (proposed) MUST replace the private `new EfModuleBinding(...)` construction in each module's registration class, so the binding is derived from the descriptor rather than duplicated by hand.
- **FR-018**: `ModuleContextCatalog`'s history-table derivation MUST move to reading the descriptor's declared `HistoryModule` name, removing the latent mismatch where it currently re-derives `__EFMigrationsHistory_Runtime` from the context class name while the host uses `__EFMigrationsHistory_ElsaRuntime`.
- **FR-019**: The design-time factory lines in `ModuleDesignTimeFactories.cs` MUST remain, because migration generation still needs them, and a guard test MUST tie each factory line to a corresponding descriptor entry so the two cannot silently diverge.

**Module names**

- **FR-020**: The tool MUST publish exactly the 13-name `--modules` vocabulary in the *Module names* table below.
- **FR-021**: `--modules` matching MUST be case-insensitive.
- **FR-022**: Every module name visible in one host's closure MUST be unique (case-insensitively); a collision is a discovery-time refusal naming both sources.
- **FR-023**: `HistoryModuleName` values MUST stay exactly as declared today (frozen); this feature MUST NOT rename any history table.

**CLI surface and exit codes**

- **FR-024**: The tool MUST expose exactly these commands: `list`, `plan`, `script`, `script-check`, `apply`, `validate`, `post-migrate`.
- **FR-025**: `--host` MUST be required on every command.
- **FR-026**: `--packages` MUST be repeatable and MUST point at a Nuplane package root.
- **FR-027**: `--provider` MUST be required wherever a command needs one, and MUST be authoritative (D4): no command may silently substitute a different provider.
- **FR-028**: Exactly one of `--modules`, `--all`, `--from-host` MUST be given for any command that selects modules; there is no default, and `--from-host` MUST accept `--shell`.
- **FR-029**: `--schema` and `--output` MUST be accepted as named. `--schema` and the `ELSA_EF_SCHEMA` environment variable MUST both resolve through [`EfSchema.Normalize(owner, provider, schema)`](../../src/Elsa/Persistence/EntityFramework/EfSchema.cs), exactly as [`ModuleDesignTimeFactory<TContext>.CreateDbContext`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactory.cs) (`:34`) already does for design-time tooling — refusing a schema on MySQL, ignoring one on SQLite. The tool MUST keep honoring `ELSA_EF_SCHEMA` for parity with `module-migrate.sh`, which already documents and reads it ([`tools/ef/module-migrate.sh:11`](../../tools/ef/module-migrate.sh), [`src/Elsa/Persistence/EntityFramework/README.md:245`](../../src/Elsa/Persistence/EntityFramework/README.md); cleared only by [`tools/ef/generate-module-migrations.sh:22`](../../tools/ef/generate-module-migrations.sh), which scaffolds migrations and must not bake a schema into the model snapshot). An explicit `--schema` MUST override `ELSA_EF_SCHEMA` when both are given. The tool MUST NOT read the host's runtime configuration key `Elsa:Persistence:EntityFramework:Schema` — the same reason it cannot see an environment-variable override of shells configuration (FR-039): both are facts about a *running* host's process environment, which the tool, working from the host's built output and package set, has no way to observe. The manifest's top-level `schema` field MUST record the schema the artifact actually targets (`null` when none was given). The operator is responsible for passing the same schema the host is configured with: a host started under `Migrate:Policy=Validate` looks for its migrations-history table inside its own configured schema, not the tool's.
- **FR-030**: `--connection-env NAME` MUST default to `ELSA_EF_CONNECTION`; `--connection-stdin` MUST be the only alternative; there MUST NOT be a `--connection` flag (D7).
- **FR-031**: `--idempotent` MUST be accepted (for compatibility with the issue's proposed surface) and MUST be a no-op in the sense that scripting is always idempotent regardless of the flag's presence.
- **FR-032**: Every input MUST be validated — module names resolve, provider agrees, the provider binds, dependencies are acyclic — before the first byte of output is written.
- **FR-033**: The tool MUST NOT download packages under any command by default (D10), and MUST NOT fall back to a different provider or a different migration set under any circumstance.
- **FR-034**: The exit codes MUST be exactly: `0` success; `1` a negative result (pending migrations, drift, or a required post-migration action); `2` a usage error or refusal (including `script --provider Sqlite`); `3` a resolution failure (host, module, provider artifact, engine, provider disagreement, or dependency); `4` a database failure.

**Provider agreement**

- **FR-035**: The tool MUST check enabled features' configured providers against `--provider`, and exit 3 listing every offender on any disagreement, whenever `--from-host` was given **or** the host's shells configuration is found regardless of which selector was used — not only under `--from-host`. The tool looks for that configuration beside the host's built output: `shells.json` in the `--host` directory, plus — only when `--environment NAME` is given — its overlay `shells.<NAME>.json` layered on top, the same base-file-plus-overlay layering [`Elsa.Workbench`'s host applies at startup](../../src/Apps/Elsa.Workbench/Program.cs) (`:85`, `:91`). With no `--environment`, only the base file is read.
- **FR-036**: The comparison MUST run **per feature, not per module**: one module can be backed by several shell features that each declare their own `Provider` setting while registering against the same base context — for example `Workflows.Runtime` is backed by `RuntimeEntityFrameworkCoreFeature`, `RuntimeBookmarksEntityFrameworkCoreFeature`, `RuntimeActivityExecutionEntityFrameworkCoreFeature`, `RuntimeOperationalStateEntityFrameworkCoreFeature`, `RuntimeWorkflowAlterationEntityFrameworkCoreFeature`, `RuntimeWorkflowExecutionEntityFrameworkCoreFeature`, and `RuntimeWorkflowTestScopeEntityFrameworkCoreFeature`, each with its own `Provider` setting, all calling `AddEfModuleMigrations<RuntimeDbContext>(Provider)`. For every **enabled** feature mapped, through `[UsesEfModule]`, to a selected module, the tool MUST compare that feature's own effective provider against `--provider`; a feature that is not enabled in the shell MUST be ignored. Every disagreeing feature MUST be listed as an offender by its feature name, together with the module it backs and its configured provider value, so a disagreement between two features of the same module is always caught — at most one of them can equal `--provider`.
- **FR-037**: An unset `Provider` setting on a feature MUST be treated as `Sqlite`, matching that feature's own configuration default.
- **FR-038**: `--environment NAME` MUST be optional. When given, the tool MUST read `shells.<NAME>.json` beside `shells.json` in the `--host` directory and layer it on top, the same two-file layering [`Elsa.Workbench`'s host applies](../../src/Apps/Elsa.Workbench/Program.cs) (`:85`, `:91`). The default, with no `--environment`, is to read only `shells.json`, with no overlay.
- **FR-039**: The manifest MUST record `providerAgreement` as `"checked"` whenever shells configuration was found and compared, under any selector, and as `"not-checked"` only when no shells configuration was found at all. The manifest MUST also state that a running host's own environment-variable configuration overrides (distinct from the `--environment` flag's file selection) are invisible to the tool: the check reads `shells.json` plus its `--environment` overlay when given, not the process environment a running host would additionally consult, so a live host's actual effective provider can differ from what `providerAgreement: checked` verified.

**Artifact and determinism**

- **FR-040**: The artifact MUST be flat `NN-<slug>.sql` files plus one `migration-plan.json` in the output directory, never a nested per-module directory tree.
- **FR-041**: Ordering MUST be topological by each module's `DependsOn`, then by ordinal name — never by the order modules were passed on the command line.
- **FR-042**: Because no cross-module foreign key exists today (verified in [research.md](./research.md)), every module's `dependsOn` MUST be `[]` until that changes, and the manifest MUST record that explicitly rather than omitting the field.
- **FR-043**: Output MUST use LF line endings and UTF-8 without a byte-order mark, MUST use a fixed JSON key order in the manifest, and MUST NOT contain a timestamp, an absolute path, the tool's own version, or a connection string.
- **FR-044**: `script-check <dir>` MUST regenerate the artifact from that directory's own `migration-plan.json` — not from a separate command-line module selection — into a temporary location, and byte-compare it against the committed directory, reporting any difference as a diff.
- **FR-045**: `script-check` MUST report as stale any `.sql` file physically present in `<dir>` that the directory's own `migration-plan.json` does not name; there is no separate filtered/unfiltered mode, because the plan, not a command-line selection, is the check's source of truth for what should be present.

**Manifest schema**

- **FR-046**: `migration-plan.json` MUST declare `schemaVersion: 1` and, at the top level: `provider`, `engine` (`package`, `version`, `source`), `efCoreVersion`, `schema`, `idempotent`, `ordering` (`"dependsOn-then-name"`), and `host` (`name`, `providerAgreement`, `shell`).
- **FR-047**: Each module entry MUST carry: `order`, `module`, `file`, `sha256`, `package` (`id`, `version`, `source`), `assembly`, `context`, `historyTable`, `migrations` (`from`, `to`, `count`, `ids`), `dependsOn`, and `postMigration` (an array of `{id, kind, requiredWhen, audit, run}`).
- **FR-048**: `package.source` and `engine.source` MUST record where the version was determined from — the host's `.deps.json` or the resolved `.nupkg` — so a reader can tell a package-derived fact from an assembly-metadata-derived one.

**Apply, validate, and connection hygiene**

- **FR-049**: `apply` and `validate` MUST take their connection exclusively via `--connection-env`/`--connection-stdin` (FR-030); neither MUST accept a connection string as a positional or named argument value directly on the command line.
- **FR-050**: As an interim fix inside `module-migrate.sh` itself (Elsa slice 6, not a new command), `apply`/`validate` in that script MUST stop taking the connection as a positional argument, converging on the same `--connection-env`/`--connection-stdin` convention as the new tool. ([PR #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867) already made `module-migrate.sh` search `extensions/` as well, before this unit's design concluded, so this requirement no longer needs to touch that: the script's project resolution previously used a `src`-only `find`, which left the `Elsa3Import*` contexts without a project; it now searches `src` plus `extensions` when present ([`tools/ef/module-migrate.sh:34-35, 96`](../../tools/ef/module-migrate.sh)).)
- **FR-051**: `validate` MUST fail (exit 1) if any selected module reports a pending migration, and MUST NOT apply anything.
- **FR-052**: `apply` MUST run each selected module's compiled migrations through `EfDatabaseMigrator`/`DbContext.Database.MigrateAsync`, which already takes EF's own migration lock (`IHistoryRepository.AcquireDatabaseLockAsync`, EF 9+); the tool MUST NOT wrap that call in a second lock of its own.

**Post-migration**

- **FR-053**: A proposed `IEfPostMigrationAction` contract MUST expose `Id`, `AuditAsync`, and `RunAsync`, taking only the `DbContext` and using no dependency injection.
- **FR-054**: A module declares its post-migration actions on its `[EfModule]` descriptor (`PostMigration = ...`).
- **FR-055**: After applying migrations, `EfModuleMigrator<T>` MUST audit every declared action under both migrate policies (`AutoMigrate` and `Validate`) and, if any action reports itself required, MUST fail closed and name `dotnet elsa persistence post-migrate` — it MUST NOT run the action itself.
- **FR-056**: The first post-migration action instance MUST be `SecretsProjectionReindex`, wrapping the existing `SecretsProjectionContract` audit/reindex pair.
- **FR-057**: `SecretsEfMigrationHostedService` and the Secrets-only `MigratePolicy` setting on `SecretsEntityFrameworkCoreFeature` MUST be retired once the post-migration seam lands; their behavior MUST be fully covered by `EfModuleMigrator`'s post-migration audit, with no behavior silently dropped.

**Activation gate**

- **FR-058**: A proposed `IFeatureActivationGuard` contract MUST live in `src/Elsa/Modularity/Core/Contracts/`, alongside `IFeatureCatalogContributor` and the other modularity contracts.
- **FR-059**: `FeatureManagementService.ApplyAsync` MUST call every registered guard after `ValidateRequest` and before `shellStore.SaveAsync`; any guard refusal MUST prevent the save.
- **FR-060**: `EfPendingMigrationActivationGuard` (proposed) MUST live in a new adapter project, `src/Elsa/Modularity/EntityFramework/` (proposed), so `src/Elsa/Modularity/Core/` stays free of an EF dependency.
- **FR-061**: Because `src/Elsa/Modularity/EntityFramework/` (proposed) references EF Core and Relational, it MUST be admitted to [`EfCoreDependencyGuardTests`](../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) through a **new admission record of its own** — a `SurfacePathPrefixes` entry for that path plus an `ExpectedEfPackagesByProject` entry naming exactly `Microsoft.EntityFrameworkCore`, `.Abstractions`, `.Analyzers`, and `.Relational` (no provider engine), in the same shape as `Adr0072SecretsEfPilot` (`:718`) and the `Adr0073…` records, joined into `IsAdmittedEfSource` (`:675`). It MUST NOT be added to `AllowedEfConsumers` (`:20`): that list licenses a *host* project to carry a full set of provider engines, which is not what this adapter project needs or should be granted. This is a distinct, additional admission from FR-012's guarantee that the tool and worker projects need none at all.
- **FR-062**: A `[UsesEfModule("...")]` attribute (proposed) on a feature class MUST map that feature to the EF module(s) it depends on, for the guard to check.
- **FR-063**: Under `Migrate:Policy=Validate`, a feature whose module has a pending migration MUST be refused with a result naming the exact `script` (or `post-migrate`) command to run, and `shellStore.SaveAsync` MUST NOT be called for that request. The refusal MUST map to an HTTP 409 problem response, and MUST NOT trigger a save or a shell reload.
- **FR-064**: Under `Migrate:Policy=AutoMigrate`, the guard MUST pass without blocking activation.
- **FR-065**: When the guard is not composed into a host at all, the existing `Validate`-policy exception thrown at CShells' Prepare phase MUST remain the fallback refusal — occurring, as today, only after `shells.json` has already been saved. This spec does not change that ordering for hosts that omit the guard; it only adds the guard as the preferred, earlier refusal point.
- **FR-066**: The Elsa implementation of the Nuplane `IPackageActivationGate` contract (U3) MUST read `[EfModule]` metadata off the package without loading it, then look in the host's shell configuration for an enabled feature mapped to that module through `[UsesEfModule]`; it MUST return `Allow` when no enabled feature uses the module, and MUST return `Allow` under `Migrate:Policy=AutoMigrate`, matching the Elsa guard's own `AutoMigrate` behavior (FR-064).

**Upstream Nuplane requirements**

- **FR-067 (U1)**: Nuplane MUST expose a named, DI-free, offline reader for the installed active-package set (for example `NuplaneStore.ReadActivePackages(stateFilePath)`), built from types already public today (`StoreRegistry`, `StoreStateSerializer`, `StoreStateRecord`, `ActivePackageDescriptor`).
- **FR-068 (U2)**: Nuplane MUST expose a host-free "load this active set" entry point — either a public `PackageAssetResolver(installPath, targetFramework, rid)` or a supported minimal composition of `AddNuplane` plus loading from state, offline, without reconciling — so the worker can reuse the real loader instead of copying its private TFM/main-assembly/native-probing logic.
- **FR-069 (U3)**: Nuplane MUST expose a pre-activation gate contract, `IPackageActivationGate.EvaluateAsync(PackageActivationContext) → Allow | Block(reason)`, invoked inside `PackageLoader.EnsureGraphLoadedAsync` between load-mode selection and load-context creation, with a `Block` surfacing as an ordinary load failure.
- **FR-070 (U4)**: Nuplane MUST register `DesiredManifestPackageSource` as an `IDesiredPackageSource` when `Convergence:Manifest:Enabled` is set, since the type is already implemented, bound, and tested but never wired up.
- **FR-071 (U5, follow-up, not required for #1861)**: A schema-v2 `nuplane.json` capability declaration, letting a module declare "I need one of these engines, chosen by configuration," is recorded as a follow-up and is explicitly out of scope for this feature.

**Fate of existing tooling**

- **FR-072**: `module-migrate.sh`'s `apply`, `validate`, `script`, and `script-check` subcommands MUST become thin shims over the new CLI once it exists, rather than a second implementation of the same behavior.
- **FR-073**: `dual-migrate.sh` and the Secrets `Tooling/` project MUST be retired once the new CLI and the post-migration seam cover their behavior.
- **FR-074**: `ModuleMigrateScriptToolTests` MUST be rewritten against the new CLI's behavior.
- **FR-075**: `tools/ef/README.md` and every error message that currently names `tools/ef/module-migrate.sh` (for example the `Validate`-policy exception in `EfDatabaseMigrator.cs`) MUST be updated to name the CLI once it ships.

### Key Entities

- **Module descriptor (`[EfModule]`)**: the single, discoverable declaration of a module's canonical name, base context type, per-provider derived contexts, frozen history name, dependencies, and post-migration actions.
- **Migration plan (`migration-plan.json`)**: the deterministic, versioned manifest describing exactly what a `script` run produced — provider, engine, per-module migration range and file, and post-migration obligations — for a DBA or `script-check` to read. `apply` and `validate` do not read it; they run the host's compiled migrations for the selected modules directly, through the same `EfDatabaseMigrator`/`MigrateAsync` path a running host uses (FR-052).
- **Post-migration action (`IEfPostMigrationAction`)**: a named, audited, never-auto-run unit of work a module declares for after its migrations apply; `SecretsProjectionReindex` is the first instance.
- **Feature activation guard (`IFeatureActivationGuard`)**: the Elsa-side, feature-enable-time check that a module's schema is current before a feature using it is allowed to activate.
- **Package activation gate (Nuplane `IPackageActivationGate`, U3)**: the Nuplane-side, package-load-time defense against a stale-schema module ever loading again, independent of which feature is enabling it.

---

## Success Criteria *(mandatory)*

- **SC-001**: A clean Elsa host — including a packaged, Nuplane-only host with no source tree and no `dotnet-ef` — can generate a provider-specific artifact for an explicit module selection with one command.
- **SC-002**: The output identifies every included module/context and its migration history table, both in the console output of `script`/`list` and in `migration-plan.json`.
- **SC-003**: The command is deterministic — byte-identical output for byte-identical inputs — and fails (exit 2, 3, or 4 as appropriate) on unresolved modules, unsupported providers (including `script --provider Sqlite`), missing artifacts, or provider disagreement; it never silently falls back to a different provider or migration set.
- **SC-004**: A generated artifact can be reviewed and applied by a serialized deployment job using only a plain SQL/ADO client — no `dotnet-ef`, no source project, and no live connection needed to produce the artifact.
- **SC-005**: Starting Elsa with `Migrate:Policy=Validate` succeeds after the artifact is applied, for every module including Secrets — the Secrets case specifically requires the post-migration seam (Elsa slice 7) to have landed first, per *Corrections to the issue text*.
- **SC-006**: A later Nuplane module installation has an explicit migration-before-activation path: enabling a feature whose module has a pending migration under `Validate` policy is refused, names the exact command to run, and saves nothing.
- **SC-007**: PostgreSQL, SQL Server, MySQL, and SQLite provider coverage is documented per command — including that `script`/`script-check` refuse SQLite outright, and that this is a documented refusal, not a silent gap.
- **SC-008**: No cross-module foreign key exists in the first-party module set today (verified in [research.md](./research.md)), so every module's `dependsOn` ordering is `[]` and topological sort never encounters a cycle in the shipped modules.
- **SC-009**: `script-check` catches both a hand-edited `.sql` file and a model change with no regenerated file, reporting each distinctly, on every server provider, in CI.
- **SC-010**: A required post-migration action that has not been run blocks the tool from reporting success, and no post-migration action ever runs as a side effect of any other command.

---

## Module names

The 13-name `--modules` vocabulary. Canonical names come from the design session; base `DbContext` names and `HistoryModuleName` values are read from the code (frozen — FR-023). The file slug is the canonical name lower-cased with dots replaced by hyphens.

| Canonical `--modules` name | Base DbContext | `HistoryModuleName` (frozen) | File slug |
|---|---|---|---|
| `Secrets` | [`SecretsDbContext`](../../src/Elsa/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs) | `ElsaSecrets` | `secrets` |
| `Workflows.Runtime` | [`RuntimeDbContext`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeDbContext.cs) | `ElsaRuntime` | `workflows-runtime` |
| `Workflows.Design` | [`WorkflowsDesignDbContext`](../../src/Elsa/Workflows/Design/Persistence/EntityFrameworkCore/WorkflowsDesignDbContext.cs) | `ElsaWorkflowsDesign` | `workflows-design` |
| `Workflows.Publishing` | [`PublishingSnapshotReviewDbContext`](../../src/Elsa/Workflows/Publishing/Persistence/EntityFrameworkCore/PublishingSnapshotReviewDbContext.cs) | `ElsaPublishingSnapshotReview` | `workflows-publishing` |
| `Workflows.Runtime.Distributed.Placement` | [`ExecutionPlacementDbContext`](../../src/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ExecutionPlacementDbContext.cs) | `ElsaDistributedExecutionPlacement` | `workflows-runtime-distributed-placement` |
| `Workflows.Runtime.Distributed.CommandTransport` | [`ExecutionCommandTransportDbContext`](../../src/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ExecutionCommandTransportDbContext.cs) | `ElsaDistributedCommandTransport` | `workflows-runtime-distributed-commandtransport` |
| `Activities.Design` | [`ActivitiesDesignDbContext`](../../src/Elsa/Activities/Design/Persistence/EntityFrameworkCore/ActivitiesDesignDbContext.cs) | `ElsaActivitiesDesign` | `activities-design` |
| `Identity.Iam` | [`IdentityIamDbContext`](../../src/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/IdentityIamDbContext.cs) | `ElsaIdentityIam` | `identity-iam` |
| `Identity.ProviderConfiguration` | [`IdentityProviderConfigurationDbContext`](../../src/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/IdentityProviderConfigurationDbContext.cs) | `ElsaIdentityProviderConfiguration` | `identity-providerconfiguration` |
| `Diagnostics.OpenTelemetry` | [`EfOpenTelemetryDbContext`](../../src/Elsa/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/EfOpenTelemetryDbContext.cs) | `ElsaOpenTelemetry` | `diagnostics-opentelemetry` |
| `Diagnostics.StructuredLogs` | [`StructuredLogsDbContext`](../../src/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/StructuredLogsDbContext.cs) | `ElsaStructuredLogs` | `diagnostics-structuredlogs` |
| `Studio.Preferences` | [`StudioPreferencesDbContext`](../../src/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/StudioPreferencesDbContext.cs) | `ElsaStudioPreferences` | `studio-preferences` |
| `Elsa3.Activities.Design.Import` | [`Elsa3ImportDbContext`](../../extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Elsa3ImportDbContext.cs) | `Elsa3ReusableActivityImport` | `elsa3-activities-design-import` |

Elsa3's module is under `extensions/`, not `src/`; it is included in the vocabulary on the same terms as every other module (D2, D3). `module-migrate.sh` already resolves its project correctly: [PR #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867) widened its project lookup from a `src`-only `find` to `src` plus `extensions` ([`tools/ef/module-migrate.sh:34-35, 96`](../../tools/ef/module-migrate.sh)).

## CLI flags

| Flag | Rule |
|---|---|
| `--host` | Required. |
| `--packages` | Repeatable; points at the Nuplane package root. |
| `--provider` | Required and authoritative. |
| `--modules`, `--all`, `--from-host` | Exactly one is required. `--from-host` takes `--shell`. There is no default. |
| `--environment NAME` | Optional. Names the `shells.<NAME>.json` overlay layered on top of `shells.json` when reading shell configuration for the provider-agreement check. Default: no overlay. |
| `--schema`, `--output` | As named. |
| `--connection-env NAME` | Defaults to `ELSA_EF_CONNECTION`. `--connection-stdin` is the alternative. |
| `--connection` | Does not exist. |
| `--idempotent` | Accepted and implied. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Negative result: pending migrations, drift, or a required post-migration action. |
| 2 | Usage error or refusal, including `script --provider Sqlite`. |
| 3 | Resolution failure: host, module, provider artifact, engine, provider disagreement, or dependency. |
| 4 | Database failure. |

## `migration-plan.json` example (schemaVersion 1)

Illustrative only: `sha256`, `version`, and `efCoreVersion` values below are placeholders (`"…"`), not real hashes or pins. The Secrets migration ids are real, read from [`src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/`](../../src/Elsa/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/): `20260910210216_Initial`, `20260911011058_WidenLookupKeys`, `20260918221655_OrdinalCollation`.

```json
{
  "schemaVersion": 1,
  "provider": "PostgreSql",
  "engine": {
    "package": "Npgsql.EntityFrameworkCore.PostgreSQL",
    "version": "…",
    "source": "host-deps-file"
  },
  "efCoreVersion": "…",
  "schema": null,
  "idempotent": true,
  "ordering": "dependsOn-then-name",
  "host": {
    "name": "…",
    "providerAgreement": "checked",
    "shell": null
  },
  "modules": [
    {
      "order": 1,
      "module": "Secrets",
      "file": "01-secrets.sql",
      "sha256": "…",
      "package": {
        "id": "Elsa.Secrets.Persistence.EntityFrameworkCore",
        "version": "…",
        "source": "host-deps-file"
      },
      "assembly": "Elsa.Secrets.Persistence.EntityFrameworkCore",
      "context": "SecretsPostgreSqlDbContext",
      "historyTable": "__EFMigrationsHistory_ElsaSecrets",
      "migrations": {
        "from": "0",
        "to": "20260918221655_OrdinalCollation",
        "count": 3,
        "ids": [
          "20260910210216_Initial",
          "20260911011058_WidenLookupKeys",
          "20260918221655_OrdinalCollation"
        ]
      },
      "dependsOn": [],
      "postMigration": [
        {
          "id": "SecretsProjectionReindex",
          "kind": "projection-reindex",
          "requiredWhen": "legacy-projection-detected",
          "audit": "SecretsProjectionContract.EnsureCurrentAsync",
          "run": "dotnet elsa persistence post-migrate --modules Secrets --provider PostgreSql"
        }
      ]
    }
  ]
}
```

---

## Assumptions

- Every first-party module ships its own already-compiled per-provider migrations inside its own assembly; no source project is needed at run time for `list`, `plan`, `script`, `script-check`, `apply`, `validate`, or `post-migrate`.
- Nuplane's `HostIntegrated` load mode and `store-state.json` format are a stable enough contract for the worker to depend on, subject to the version caveat recorded in [research.md](./research.md): the Nuplane facts here are verified against branch `pr-67`, ahead of the `0.0.9-preview.61` version Elsa currently pins, and each upstream item needs a release and a pin bump before the Elsa slice depending on it can start.
- No cross-module foreign key exists in the first-party module set today (verified); every module's `dependsOn` is `[]` until that changes.
- A host's shell feature configuration is visible to the tool whenever `shells.json` (plus its `--environment` overlay, if given) is found beside the host's built output, under `--from-host` or any other selector — not only `--from-host`. The per-feature provider-agreement check (FR-035–FR-039) runs in every case where that configuration is found, and only skips when none is found at all.
- Pre-release policy applies: no back-compat shim is owed for `migration-plan.json` schema version 1 itself; a later schema version is additive, not a break the tool has to bridge.
- `EfModuleBinding.AddContext` remains the single registration seam for every module context; the descriptor changes what feeds it, not the seam itself.

## Out of Scope

- Any change to EF Core's own migration mechanics. This spec is a discovery, orchestration, and packaging layer over `dotnet ef`/`IMigrator`, never a replacement for either.
- U5 (the schema-v2 `nuplane.json` capability declaration) — recorded as a Nuplane follow-up, not required for #1861.
- Any Nuplane repository change. This spec only proposes Nuplane work (U1–U5) in an issue comment; no Nuplane issue is filed and no Nuplane code changes as part of this unit.
- Changing the scope, labels, or acceptance criteria of the already-filed child issues (Elsa slices 1–11, Nuplane N1–N5) beyond what each slice states below. A separate go-ahead is required before any of that.
- Any performance measurement, benchmark, or threshold derived from one (#1668, ADR 0073).
- A general-purpose dependency-graph or capability system beyond what `DependsOn` and the module descriptor need for ordering and post-migration declaration.
- Fixing either of the two adjacent Nuplane defects recorded in [research.md](./research.md) (the dead lock-file writer, the hardcoded host-provided allowlist) — both are out of scope for #1861 and are mentioned in the issue comment only.

---

## Delivery slices

Each slice below carries one scope paragraph and one **Acceptance:** line. The slices were filed on 2026-09-19: the Elsa slices as sub-issues of #1861, and the Nuplane slices as issues in `valence-works/nuplane`.

### Elsa slices

**Slice 1 ([#1871](https://github.com/elsa-workflows/elsa-foundation/issues/1871)) — `[EfModule]` and `EfModuleCatalog` on all 13 modules, with guard tests.** Add the descriptor attribute and discovery API in `src/Elsa/Persistence/EntityFramework/` (proposed), and annotate all 13 first-party modules (including `Elsa3.Activities.Design.Import` under `extensions/`) with it. No runtime behavior changes; every existing registration class keeps its own hand-written binding for now. **Acceptance:** `EfModuleCatalog.Discover` returns all 13 modules with correct names, base context types, and frozen `HistoryModule` values, and a guard test fails if any module's descriptor and its `EfModule.cs` constant disagree.

**Slice 2 ([#1872](https://github.com/elsa-workflows/elsa-foundation/issues/1872)) — `EfModuleBinding.For(...)` collapses the registration classes onto the descriptor.** Replace each module's private `new EfModuleBinding(...)` with `EfModuleBinding.For(contextType)`, reading from the descriptor added in slice 1. **Acceptance:** every module's DI registration is unchanged in behavior (existing module tests stay green), and the private `EfModuleBinding` construction is gone from every registration class.

**Slice 3 ([#1873](https://github.com/elsa-workflows/elsa-foundation/issues/1873)) — `EfToolingHost` entry point: `list`, `plan`, and `script`.** Implement `Tooling.EfToolingHost.RunAsync` in `Elsa.Persistence.EntityFramework`, backing `list`, `plan`, and `script` with deterministic output, the SQLite refusal, and `DependsOn`-then-name ordering, driven by the descriptor from slice 1. **Acceptance:** in-process tests prove the output is byte-stable for the three server providers: `script` run twice against the same inputs produces byte-identical files; `script` run with `--modules` given in reversed order produces byte-identical files to forward order; and no output file contains a CR line ending, an absolute path, or a timestamp. This does not promise equality with `module-migrate.sh script`'s own output — the new layout is flat and numbered rather than nested under `<Module>/<Provider>.sql`, and line endings are LF-normalized where the old tool's were not guaranteed to be.

**Slice 4 ([#1874](https://github.com/elsa-workflows/elsa-foundation/issues/1874)) — `src/Elsa/Cli/Elsa.Cli.csproj` (proposed), the worker, `--packages` probing, `script-check`.** Add the `PackAsTool` project (`dotnet-elsa`), using the already-pinned `System.CommandLine` 2.0.8; the worker process launch (FR-002); `--packages` resolution against a directory with no state file; `script-check`; and a third-party fixture under `tests/Elsa/Cli/Fixtures/` (User Story 6). It lives under `src/` because `.github/workflows/packages.yml` packs `find src extensions`, and a project under `tools/` would never ship. **Acceptance:** `dotnet elsa persistence script` run against a packaged (non-source) host produces an artifact matching slice 3's source-tree output for the same modules and provider — for hosts with every module in their own `.deps.json`; the Nuplane-loader path depends on N1/N2 (below) and can ship after them for Nuplane hosts specifically.

**Slice 5 ([#1875](https://github.com/elsa-workflows/elsa-foundation/issues/1875)) — Acceptance leg in the `ci.yml` EF container matrix.** Add a leg to `ef-container-suites`: `script`, then apply the result twice with a raw ADO client (proving idempotency), then start a host under `Migrate:Policy=Validate`. The raw client differs per engine — Npgsql for PostgreSQL, `SqlConnection` for SQL Server (splitting the script on `GO` batch separators itself, since `SqlConnection` does not understand them), and MySQL's client handling `DELIMITER` — because the artifact is plain SQL for each engine's own tooling, not a client-agnostic format. Reuse `tests/.../Migrations/ProviderTests/ProviderDatabase.cs` for container lifecycle, and the existing [`WorkbenchProcess`](../../tests/Elsa/Workbench/Tests/WorkbenchProcess.cs) test helper to start Workbench itself with `Migrate:Policy=Validate` for the last step. **Acceptance:** the leg is green on PostgreSQL, SQL Server, and MySQL, and fails if any of the three steps regresses.

**Slice 6 ([#1876](https://github.com/elsa-workflows/elsa-foundation/issues/1876)) — `apply` and `validate`, plus connection hygiene.** Implement the two commands against the host's compiled migrations for the selected modules — not against `migration-plan.json`, which `script` produces for a DBA to review, not for `apply`/`validate` to read back — via `EfDatabaseMigrator`/`MigrateAsync` (FR-052), with `--connection-env`/`--connection-stdin` only (FR-030, FR-049); make the interim `module-migrate.sh` fix (FR-050): no positional connection argument. ([PR #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867) already made `module-migrate.sh` search `extensions/` as well, so its other interim defect — a `src`-only project lookup — no longer applies.) **Acceptance:** capturing the worker's `ProcessStartInfo` shows no credential in any process argument; a `--connection` flag is rejected as a usage error; and `apply` followed by `validate` against the same database exits 0.

**Slice 7 ([#1877](https://github.com/elsa-workflows/elsa-foundation/issues/1877)) — Post-migration seam.** Add `IEfPostMigrationAction`, wire `EfModuleMigrator<T>`'s post-apply audit, and move Secrets onto it as `SecretsProjectionReindex`; add the three missing Secrets design-time factories (SQLite, SQL Server, PostgreSQL) alongside the existing MySQL one so Secrets is fully covered by the same catalog every other module uses. **Acceptance:** User Story 5's four scenarios pass, and `SecretsEfMigrationHostedService`/the Secrets-only `MigratePolicy` setting are removed with no behavior silently dropped (FR-057).

**Slice 8 ([#1878](https://github.com/elsa-workflows/elsa-foundation/issues/1878)) — `module-migrate.sh` becomes a shim; retire `dual-migrate.sh` and the entire Secrets `Tooling/` project.** `apply`, `validate`, `script`, and `script-check` in the shell script call the new CLI instead of `dotnet ef` directly; `dual-migrate.sh` and the whole `src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/` project (not only its `Program.cs`) are deleted; `ModuleMigrateScriptToolTests` is rewritten; `tools/ef/README.md` and every error message naming the old script (including `EfDatabaseMigrator.cs`'s `Validate`-policy exception) are updated to name the CLI. **Acceptance:** `script-check` continues to work when called through the shim; `ModuleMigrateScriptToolTests` is rewritten to pin the shim's contract — that it calls `dotnet elsa` with the right arguments — with the SQL-layout assertions themselves moved to the CLI's own tests, since the layout is exactly what changed (flat and numbered rather than nested). This does not promise the shim's byte-for-byte output is unchanged for every existing caller; the layout change means it is not.

**Slice 9 ([#1879](https://github.com/elsa-workflows/elsa-foundation/issues/1879)) — `[UsesEfModule]`, `--from-host`, `--environment`, and the per-feature provider-agreement check.** Add the attribute mapping features to modules; implement `--from-host --shell <name>` and `--environment NAME` reading a real host's shell feature configuration, base file plus overlay; implement the provider-agreement comparison so it runs per enabled feature whenever shells configuration is found under any selector, not only `--from-host` (FR-035–FR-039). **Acceptance:** User Story 1's scenarios 4 and 5, and both provider-disagreement edge cases (a single offending feature, and two features of the same module disagreeing with each other), all produce the documented exit-3 refusal, listing every offending feature by name, module, and configured value.

**Slice 10 ([#1880](https://github.com/elsa-workflows/elsa-foundation/issues/1880)) — The Nuplane activation gate.** Add `IFeatureActivationGuard` (`src/Elsa/Modularity/Core/Contracts/`, proposed) and `EfPendingMigrationActivationGuard` (`src/Elsa/Modularity/EntityFramework/`, proposed, which needs its own new admission record — `SurfacePathPrefixes` plus `ExpectedEfPackagesByProject`, not an `AllowedEfConsumers` entry — in `EfCoreDependencyGuardTests` per FR-061); wire `FeatureManagementService.ApplyAsync` to call registered guards between `ValidateRequest` and `shellStore.SaveAsync`; add the contributor-interface entry to [`src/Elsa/Modularity/Api/EXTENSION_POINTS.md`](../../src/Elsa/Modularity/Api/EXTENSION_POINTS.md)'s "Implementable contributor interfaces" group, beside `IFeatureCatalogContributor`, and the matching row to the root [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index. **Acceptance:** User Story 3's four scenarios pass; a refusal from the guard maps to an HTTP 409 problem response, with neither `shellStore.SaveAsync` nor a shell reload occurring for that request; the Elsa half (the required guard) does not depend on Nuplane's own gate (U3/N3), which is the Nuplane half of this same slice and blocks only that half (see dependency notes below).

**Slice 11 ([#1881](https://github.com/elsa-workflows/elsa-foundation/issues/1881), optional) — Mirror the descriptor into `elsa-package.json` under `extensions.efModules`.** Needs an upstream generator change to `Elsa.Platform.PackageManifest.Generator`; deferred until R3 (research.md) is settled. **Acceptance:** a module's `elsa-package.json` lists its EF module name(s) without hand-editing, once the generator change lands.

### Nuplane slices

Each Nuplane slice lives in the `valence-works/nuplane` repository, needs its own release, and needs an Elsa `Directory.Packages.props` pin bump before the Elsa slice that depends on it can start.

**N1 (U1, [valence-works/nuplane#73](https://github.com/valence-works/nuplane/issues/73)) — Offline reader.** A named, DI-free reader for `store-state.json`'s active package set, built from the already-public types identified in research.md (R1). **Acceptance:** `NuplaneStore.ReadActivePackages(stateFilePath)` (or equivalent) returns every active package's id, version, and install path with no DI container and no network access.

**N2 (U2, [valence-works/nuplane#74](https://github.com/valence-works/nuplane/issues/74)) — Host-free load.** A public entry point that loads an already-resolved active set into assemblies the same way `HostIntegrated` mode does today, without a running host or reconciliation loop. **Acceptance:** the worker (Elsa slice 4) can resolve a module and its provider engine from a `--packages` directory using only this entry point, with no copy of Nuplane's private TFM/native-probing logic in Elsa.

**N3 (U3, [valence-works/nuplane#75](https://github.com/valence-works/nuplane/issues/75)) — Activation gate.** `IPackageActivationGate.EvaluateAsync`, invoked inside `PackageLoader.EnsureGraphLoadedAsync` between load-mode selection and load-context creation. **Acceptance:** a `Block` result from a registered gate prevents that package graph from loading, surfaced as an ordinary load failure.

**N4 (U4, [valence-works/nuplane#76](https://github.com/valence-works/nuplane/issues/76)) — Manifest source registration.** `DesiredManifestPackageSource` registered as an `IDesiredPackageSource` when `Convergence:Manifest:Enabled` is set. **Acceptance:** setting that configuration key causes the existing, already-tested type to actually participate in reconciliation.

**N5 (U5, follow-up, [valence-works/nuplane#77](https://github.com/valence-works/nuplane/issues/77)) — Capability declaration.** A schema-v2 `nuplane.json` capability declaration letting a module ask for "one of these engines, chosen by configuration." **Acceptance:** deferred; no acceptance criterion is set for #1861.

### Dependency notes

- **N1 ([valence-works/nuplane#73](https://github.com/valence-works/nuplane/issues/73)) and N2 ([valence-works/nuplane#74](https://github.com/valence-works/nuplane/issues/74)) block Elsa slice 4 ([#1874](https://github.com/elsa-workflows/elsa-foundation/issues/1874)) for Nuplane hosts.** Slice 4 can ship first for hosts that carry every module in their own `.deps.json`; the Nuplane-loader path within it waits on both.
- **N3 ([valence-works/nuplane#75](https://github.com/valence-works/nuplane/issues/75)) blocks only the Nuplane half of Elsa slice 10 ([#1880](https://github.com/elsa-workflows/elsa-foundation/issues/1880)).** The Elsa `IFeatureActivationGuard` (the required gate) does not depend on N3 at all; only the Nuplane `IPackageActivationGate` defense does.
- **N4 ([valence-works/nuplane#76](https://github.com/valence-works/nuplane/issues/76)) is independent** of every Elsa slice; it can ship on its own schedule.
- **Every Nuplane slice needs a Nuplane release and an Elsa `Directory.Packages.props` pin bump** (currently `0.0.9-preview.61`) before the Elsa slice that depends on it can start, per the version caveat in [research.md](./research.md).
