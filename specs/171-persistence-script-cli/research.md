# Research: `dotnet elsa persistence` execution model, module descriptor, and the Nuplane gap

**Spec**: [spec.md](./spec.md)

**Verified against**: `origin/main` at `43d88cef4`, 2026-09-19 (re-verified after a rebase onto this commit, 2026-09-20), plus a local checkout of the `valence-works/nuplane` repository (`git describe`: `0.0.10-10-g5c0e04e`, branch `pr-67`), 10 commits ahead of the `0.0.9-preview.61` tag Elsa's `Directory.Packages.props` pins. Every Nuplane fact below carries that version caveat once, here: **the cited behavior is what `pr-67` does today, not what Elsa's pinned `0.0.9-preview.61` does**; each upstream item (U1–U5) needs a Nuplane release and an Elsa pin bump before the Elsa slice that depends on it can start.

---

## How this was verified

Every Elsa-side citation below was re-opened in this worktree at the commit above and the line still matches. Every Nuplane citation was read from the Nuplane checkout, read-only, and is cited as plain text (`nuplane: <path>:<line>`), never as a relative markdown link, because that repository is not part of this tree and a link would 404. Two of those citations are worth stating precisely because they matter later in this document: the lock-file gap lives at `nuplane: src/Nuplane/Reconciliation/LockFileCoordinator.cs:23` (the check is `if (_options.Mode == LockFileMode.Generate)`), and the hardcoded host-provided allowlist is `IsSharedHostContractPackage` at `nuplane: src/Nuplane/Reconciliation/PackageDependencyGraphResolver.cs:514-533`, called from `IsHostProvidedDependency` at `:498`.

---

## Inventory of existing building blocks

[`tools/ef/module-migrate.sh`](../../tools/ef/module-migrate.sh) already does most of the mechanical work this issue asks for, for the 13 module contexts the tooling catalog enumerates — 12 with four provider-derived contexts each, plus Secrets with its single MySQL context, i.e. the 49 `ModuleDesignTimeFactory<TContext>` lines in `ModuleDesignTimeFactories.cs`:

- `script` and `script-check` produce and verify the exact `<Module>/<Provider>.sql` idempotent layout the issue proposes ([`tools/ef/module-migrate.sh:116-130`](../../tools/ef/module-migrate.sh)), one file per module context, each recording into its own `__EFMigrationsHistory_*` table.
- SQLite is already refused for scripting, with the same reasoning this spec keeps: `SqliteHistoryRepository.GetEndIfScript` throws `NotSupportedException` ([`tools/ef/module-migrate.sh:51-58`](../../tools/ef/module-migrate.sh)).
- `apply` and `validate` take the connection string as **positional argument 3** ([`tools/ef/module-migrate.sh:46`](../../tools/ef/module-migrate.sh), the `apply|validate)` case), which is why the issue's "credentials never in process arguments" acceptance line does not describe the tooling as it exists today — see *Corrections to the issue text* in [spec.md](./spec.md).
- Scripting always passes `--idempotent` ([`tools/ef/module-migrate.sh:120`](../../tools/ef/module-migrate.sh)).
- [`tools/ef/README.md:41-102`](../../tools/ef/README.md) documents the layout, the `script`/`script-check` contract, and the SQLite refusal in prose (`:69-74`); `:76-77` records that Secrets ships only a MySQL context in this shared catalog, because Secrets keeps a separate historical migration chain under `tools/ef/dual-migrate.sh`. (`tools/ef/README.md` is unmoved by the recent changes on `main` described below.)

**Why it cannot serve package consumers.** The script drives `dotnet ef`, which needs a design-time startup project and source projects to build:

- Its startup project is [`tools/ef/Elsa.EntityFrameworkCore.Tooling`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj), a compile-time catalog: [`ModuleDesignTimeFactories.cs`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs) holds 49 `ModuleDesignTimeFactory<TContext>` lines, one per provider-derived context, and its `Program.cs` reflects over them to answer `list`. All four `Elsa3Import*` factory lines are already among them ([`:9-12`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs)).
- Each context's project is located by `find "${module_roots[@]}" -name "$assembly.csproj"` ([`tools/ef/module-migrate.sh:96`](../../tools/ef/module-migrate.sh)), where `module_roots` is `src` plus `extensions` when the latter exists ([`tools/ef/module-migrate.sh:34-35`](../../tools/ef/module-migrate.sh)). This was a `src`-only `find` as recently as this spec's first draft; [PR #1867](https://github.com/elsa-workflows/elsa-foundation/pull/1867) widened it before this unit's design work concluded, so the `Elsa3Import*` contexts (under `extensions/Elsa3/src/...`) resolve a project today. This closes what an earlier draft of this document called an open defect; it is no longer part of this work's scope (see *Corrections to the issue text* in [spec.md](./spec.md)).
- An Elsa 4 host built from packages has neither a source tree nor `dotnet-ef` available to it: it has compiled module assemblies loaded by Nuplane, and nothing else. There is no `dotnet elsa` tool, and no supported way for such a host to script SQL for the modules it actually has installed. This is independent of the `find` fix above: even with every context's project resolvable, `dotnet ef` still needs the design-time startup project and source projects a packaged host does not have.

## Four places, one declaration

Adding a module, or checking that one module's identity is consistent, means touching (or auditing) four independent places today, each of which encodes the same handful of facts about that module on its own:

| # | Place | What it declares | Example |
|---|---|---|---|
| 1 | `<Module>EfModule.cs` | `HistoryModuleName` (frozen) and table/column name constants | [`RuntimeEfModule.cs:11`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEfModule.cs) |
| 2 | `<Module>EntityFrameworkCoreRegistration.cs` | a private `static readonly EfModuleBinding` built by hand, naming the owner, history table, and migrations assembly | [`SecretsEntityFrameworkCoreRegistration.cs:15`](../../src/Elsa/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs) |
| 3 | `tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs` | one `ModuleDesignTimeFactory<TContext>` line per provider-derived context, needed for migration generation | [`ModuleDesignTimeFactories.cs:37-40`](../../tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs) (the four `Runtime*DbContextFactory` lines) |
| 4 | `tests/.../Migrations/Tests/ModuleContextCatalog.cs` | the history table name, **re-derived from the context class name** rather than read from the module | [`ModuleContextCatalog.cs:91-92`](../../tests/Elsa/Persistence/EntityFrameworkCore/Migrations/Tests/ModuleContextCatalog.cs) |

Place 4 does not read place 1's constant; it computes `EfMigrationsHistory.TableName(context.Name[..^("SqlServerDbContext".Length)])`, which for Runtime yields `__EFMigrationsHistory_Runtime`, while the host actually uses `RuntimeEfModule.HistoryTableName` = `__EFMigrationsHistory_ElsaRuntime` ([`RuntimeEfModule.cs:19`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEfModule.cs)). Nothing currently notices the mismatch, because place 4's `HistoryTable` helper is used only to build placeholder options for model-building, not to assert equality against place 1. The `[EfModule]` descriptor this spec proposes is read by all four consumers, which is what actually closes the gap rather than just moving it.

## R1 — RESOLVED: the pinned install layout is `store-state.json`, not the lock file

- Install layout: `nuplane: src/Nuplane/Feeds/PackageInstallStore.cs:36-40` (`GetInstallDirectory`) — `<installRoot>/<feedName>/<packageId>/<version>/`, a raw extracted nupkg, casing preserved by `SanitizePathSegment`; completion marker `.nuplane-ready` (`:20`, `IsInstalled` at `:48`); default root `AppContext.BaseDirectory/.nuplane/packages` when `PackageInstallRoot` is unset (`:26-29`).
- The active set persists in the state file, not the lock file: `nuplane: src/Nuplane/Store/State/StoreStateRecord.cs:16-23` carries `ActivePackageDescriptorsById`, each entry a public `nuplane: src/Nuplane.Abstractions/ActivePackageDescriptor.cs:19-32` record carrying `InstallPath`. `StoreStateSerializer` (public, camelCase JSON) and `StoreRegistry`'s public constructor `(IStoreStateSerializer, string?)` (`nuplane: src/Nuplane/Store/State/StoreRegistry.cs:28`) need no DI container to read it back. `EffectiveStorePersistenceSettings.Resolve(StoreRegistryOptions)` (`nuplane: src/Nuplane/Store/State/EffectiveStorePersistenceSettings.cs:70`) resolves the effective state-file path the same way the host does.
- The lock file is not that set: `LockFileMode.Generate` never writes it (`nuplane: src/Nuplane/Reconciliation/LockFileCoordinator.cs:23`), and nothing under `src/` calls `LockFileStore.WriteAsync` — every call site found is a test.
- Only `nuplane: src/Nuplane/Operational/ActivePackageCatalogMapper.cs:7` (`internal static class ActivePackageCatalogMapper`) is internal among these types; everything else U1 needs is already public.

**Conclusion**: an offline reader of `store-state.json` (U1) can be built entirely from types already public in the `Nuplane` package, without DI and without touching the lock file.

## R2 — RESOLVED: `HostIntegrated` loads into a private, non-collectible ALC, not the default context

- `PackageLoadMode` has exactly two values, `Collectible` and `HostIntegrated` (`nuplane: src/Nuplane.Loading.Abstractions/PackageLoadMode.cs:6-16`); "dependency closure" is a load-planning scope, not a third mode.
- `HostIntegrated` packages load into `HostIntegratedPackageGraphLoadContext`, a `sealed internal` non-collectible subclass of `PackageGraphLoadContext` (`nuplane: src/Nuplane.Loading/HostIntegratedPackageGraphLoadContext.cs:6-11`), constructed in `PackageLoader.EnsureGraphLoaded` when `graphLoadMode == PackageLoadMode.HostIntegrated` (`nuplane: src/Nuplane.Loading/PackageLoader.cs:265-268`).
- Assemblies in that context become visible to ordinary by-name resolution (`AppDomain.GetAssemblies()`, `Assembly.Load`, `Type.GetType`) through a `Default.Resolving` hook: `nuplane: src/Nuplane.Loading/HostIntegratedAssemblyResolver.cs:26` (`AssemblyLoadContext.Default.Resolving += ResolveFromHostIntegratedPackages;`). That is exactly the resolution path [`EfRelationalProviderBinding`](../../src/Elsa/Persistence/EntityFramework/EfRelationalProviderBinding.cs) uses (`Type.GetType` at `:178`, `AppDomain.CurrentDomain.GetAssemblies()` at `:240`, `Assembly.Load(engine.PackageId)` at `:250`), so a worker that boots the real Nuplane loader binds providers exactly as the host does; a hand-rolled resolver would not go through this hook and could disagree.
- TFM selection, main-assembly choice, and native/RID probing are private statics on `PackageLoader` (`nuplane: src/Nuplane.Loading/PackageLoader.cs:716-894` roughly, e.g. `ResolveMainAssemblyPath`, `ResolveHostFramework`) and on `PackageGraphLoadContext`. A `hostTargetFrameworkOverride` parameter exists on the private resolution path (`nuplane: src/Nuplane.Loading/PackageLoader.cs:69, 718-719, 890-894`) but every public call site passes `null` — no supported caller can reach it today.

**Conclusion**: the worker cannot reimplement this loader cheaply or safely; it must reuse Nuplane's own composition (U2) to get provider binding that matches the real host.

## R3 — OPEN: does the package-manifest generator skip a tool project?

`.github/workflows/packages.yml:99` builds its pack set with `find src extensions -name '*.csproj'` (excluding `src/Apps/*` and `*/tests/*`), so a project physically under `src/` is packed. It is not yet verified whether the separate manifest-generation step (`Elsa.Platform.PackageManifest.Generator`, referenced by the `[ManifestFeatureCategory]` attributes seen on `SecretsEntityFrameworkCoreFeature`) has its own project-shape assumptions that would exclude a CLI/tool project such as the proposed `src/Elsa/Cli/Elsa.Cli.csproj` (a `PackAsTool`, no `[ShellFeature]`) from the manifest it emits. This would settle whether slice 4 needs a manifest-generator change alongside the new project, or whether the generator already skips non-feature projects cleanly.

**What would settle it**: reading the manifest generator's project-selection logic, or building `Elsa.Cli` against it once slice 4 exists and checking whether it appears (or correctly does not appear) in the generated manifest.

## R4 — OPEN: does Nuplane's existing offline mode already do what U2 needs?

The worker needs to load the active package set "from state, offline, without reconciling" for `--packages` and any non-Nuplane-managed root. It is not yet verified whether composing Nuplane with an existing `OfflineMode`-shaped option, without registering the hosted reconciliation service, already produces that composition, or whether U2's new entry point has to assemble the pieces (state read, package resolution, ALC construction) itself because today's composition helpers assume a running host with a background reconciler.

**What would settle it**: tracing what `AddNuplane(...)` registers when reconciliation is disabled, and whether `IPackageLoader.EnsureGraphLoadedAsync` can be called directly against packages resolved from `store-state.json` alone, with no `IDesiredPackageSource` and no network access. If yes, U2 is a thin composition helper (closer to size S than M); if the pieces are not separable without the hosted service, U2 stays a distinct public entry point that the S/M estimate in the issue-comment slice list already assumes.

## R5 — OPEN: should the tool ever restore packages itself?

A host whose install root has never been populated (a fresh remote-feed host that has not yet run its first reconcile) has an empty package directory, so the worker would find nothing to script against. It is open whether an opt-in `--restore` flag should run a Nuplane reconcile (network access, no loading) before reading the state, or whether the tool's "never downloads by default" rule (D10) should extend to "never downloads, period," pushing the operator to run the host (or an explicit Nuplane reconcile command) first.

**What would settle it**: an owner decision on whether `dotnet elsa` is ever allowed to touch the network, even opt-in. Absent that decision, the default in this spec is the safe one: never downloads, and `--restore` is not part of this spec's scope.

---

## U1–U5 evidence table

| # | Change | Evidence it is needed | Evidence it is buildable |
|---|---|---|---|
| U1 | Named offline reader for the installed set | Nothing today reads `store-state.json` without pulling in reconciliation plumbing; a hand implementation would mean parsing JSON Nuplane already owns the schema for, or referencing all of `Nuplane` plus its `NuGet.Protocol` dependency just to read a state file. | `StoreRegistry`, `StoreStateSerializer`, `StoreStateRecord`, `ActivePackageDescriptor` are already public, camelCase-serializable, and DI-free (R1). Only `ActivePackageCatalogMapper` is internal. |
| U2 | Host-free "load this active set" entry point | `EfRelationalProviderBinding` resolves types by `Type.GetType`, `AppDomain.GetAssemblies()`, and `Assembly.Load` (`:178, 240, 250`); those only see `HostIntegrated` assemblies through the `Default.Resolving` hook Nuplane installs (R2). A worker that loads assemblies any other way binds providers differently from the real host. | TFM/main-assembly/native resolution already exists as private statics on `PackageLoader`/`PackageGraphLoadContext`; a `hostTargetFrameworkOverride` seam already exists, unreachable only because no public caller passes it (R2). |
| U3 | Pre-activation gate contract | `INuplaneObserver` exceptions are swallowed (`nuplane: src/Nuplane/Events/ObserverEventDispatcher.cs:16-45`, every observer call wrapped in `try`/`catch` that only logs); `IPackageLoadModeAdvisor` is advice-only (`nuplane: src/Nuplane.Loading/PackageLoader.cs:37` as a constructor parameter, consulted for mode selection, never for a block/allow decision); `PackageTransactionRequest.StageExecutor` (`nuplane: src/Nuplane/Store/Transactions/PackageTransactionRequest.cs:24`) is declared but never set by any caller. Nothing in Nuplane today can stop a package from loading. | The natural seam is inside `PackageLoader.EnsureGraphLoadedAsync` (`nuplane: src/Nuplane.Loading/PackageLoader.cs:107`), between the load-mode decision (`:122`, `SelectGraphAsync`) and load-context construction (`:265-268`, the `HostIntegratedPackageGraphLoadContext`/`PackageGraphLoadContext` choice) — a natural `Allow`/`Block` insertion point already bracketed by two existing calls. |
| U4 | Register `DesiredManifestPackageSource` | `nuplane: src/Nuplane/Sources/DesiredManifestPackageSource.cs:12` is implemented and (per the plan) tested, but grepping Nuplane's composition (`AddNuplane` and friends) finds no registration of it as an `IDesiredPackageSource`; setting `Convergence:Manifest:Enabled` currently does nothing. | The type already exists and needs only a registration line plus a supported way to enable it; no new design work. |
| U5 | Schema-v2 `nuplane.json` capability declaration | [`docs/foundation-host-feeds.md:151`](../../docs/foundation-host-feeds.md) documents today's workaround in prose: the provider engine "must be named by hand" when generating a host's package closure, because no package declares "I need one of these engines, chosen by configuration." | Not required for #1861; recorded as a follow-up (N5) because nothing currently in Nuplane's manifest schema expresses a capability choice — this is new design, not a composition change, which is why it is sized L and deferred. |

## Two adjacent Nuplane defects (out of scope for #1861)

Found while reading the areas above; neither blocks this issue, and neither is proposed as Nuplane work here — they are recorded so the issue comment can mention them without conflating them with U1–U5:

- **The lock file is dead code for writing.** `LockFileMode.Generate` never writes (`nuplane: src/Nuplane/Reconciliation/LockFileCoordinator.cs:23`), and only test code calls `LockFileStore.WriteAsync`. Nuplane's own roadmap already has a line item close to this: `nuplane: specs/028-operational-stability-roadmap/roadmap.md:366`, "Track 7 - Lock File as Offline Execution Contract".
- **A hardcoded, Elsa-specific host-provided allowlist.** `PackageDependencyGraphResolver.IsSharedHostContractPackage` (`nuplane: src/Nuplane/Reconciliation/PackageDependencyGraphResolver.cs:514-533`) lists thirteen `Elsa.*` package ids, three `CShells.*.Abstractions` ids, two `Nuplane.*.Abstractions` ids, and an `Microsoft.Extensions.` prefix, by name, in a general-purpose package manager. This is adjacent to, but distinct from, U4/U5: it is a dependency-resolution concern, not a load-mode or capability concern.

Nuplane's own spec for host-integrated loading already names migrations assemblies as the motivating case for that mode: `nuplane: specs/018-host-integrated-loading/spec.md:147` ("SC-003: A package containing database migrations can be loaded in host-integrated mode and its migrations assembly can be resolved by framework code by name"), which is independent confirmation that this design direction (reuse Nuplane's loader rather than re-implement it) is the one Nuplane's own authors had in mind for exactly this scenario.
