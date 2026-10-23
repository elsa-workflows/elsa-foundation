# Pinning packages and their closures

**Part of:** [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md).

A **root** is a package you ask for by name. Its **closure** is the root plus everything it depends on, directly or indirectly. This page explains why you should list the whole closure explicitly at exact versions, how to generate that list, how the EF Core database engine is chosen, and how to lock packages by hash.

> **The short version:** generate the full closure from a `dotnet restore` of your roots, pin each package with a single-point range like `[4.0.0-preview.793]`, select the EF engine with `Nuplane:Capabilities:ef-provider`, and turn on the lock file. Relying on automatic dependency resolution can silently leave packages out.

## Declare the full pinned closure, not just roots

Transitive dependencies are mostly resolved for you, and a feed with no `IncludePatterns` still serves
as a resolution source. Pinning one root against two feeds pulls its closure across both:

```
Elsa.Tasks.Schedules  4.0.0-preview.793  feed=elsa-4  feed-rule:elsa-4
Elsa.Primitives       4.0.0-preview.793  feed=elsa-4  dependency-of:Elsa.Tasks.Schedules
Elsa.Tasks.Core       4.0.0-preview.793  feed=elsa-4  dependency-of:Elsa.Tasks.Schedules
Cronos                0.13.0             feed=nuget   dependency-of:Elsa.Tasks.Schedules
```

**But do not rely on it.** Some declared dependencies are silently never acquired, and the cycle still
reports `IsDegraded=False, FailedCount=0`. List every package you need, each as an explicit root with
an exact single-point range. Generate that list from a `dotnet restore` of your roots.

### Why a declared dependency can go missing

The dependency walk skips anything `IsHostProvidedDependency` believes the host already supplies:

1. An entry in `Nuplane:HostProvidedPackages` — a plain list of package ids and `Prefix.`-style
   prefixes (since Nuplane `0.0.11-preview.91`, [valence-works/nuplane#90](https://github.com/valence-works/nuplane/issues/90)).
   Unconfigured, it defaults to Nuplane's own two contract ids, `Nuplane.Abstractions` and
   `Nuplane.Loading.Abstractions` — nothing else. Earlier Nuplane versions hard-coded this list
   instead: the CShells and Nuplane `*.Abstractions` packages, thirteen `Elsa.*` ids (`Elsa.Api.Common`,
   `Elsa.Caching`, `Elsa.Common`, `Elsa.Expressions`, `Elsa.Features`, `Elsa.KeyValues`, `Elsa.Mediator`,
   `Elsa.Resilience`, `Elsa.Resilience.Core`, `Elsa.Tenants`, `Elsa.Workflows.Core`,
   `Elsa.Workflows.Management`, `Elsa.Workflows.Runtime`), and the whole `Microsoft.Extensions.` prefix.
   That fixed list is gone; a host that needs any of those ids treated as already-supplied now has to
   name them under `Nuplane:HostProvidedPackages`. A declared package is never acquired, but since
   `0.0.11-preview.93` it is no longer skipped blindly either: when the host's own `*.deps.json` carries
   it at a version outside the dependency's range, the dependent package is refused — see
   [What fails loudly](failure-modes.md#what-fails-loudly).
2. Anything present in the host's own `*.deps.json` at a version satisfying the range — unchanged by
   `#90`, and still independent of rule 1.

Rule 1 used to be the trap for this host: the hard-coded list assumed a host that compiles Elsa in, and
`Elsa.Foundation.Host` deliberately compiles in **no Elsa feature at all**, so it supplied none of
them. Pinning `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore` used to acquire 15 packages,
`IsDegraded=False`, with `Elsa.Workflows.Runtime` missing from them — while its sibling
`Elsa.Workflows.Runtime.Core`, which was never on the list, arrived. The shell then failed at load:

```
Failed to load types from assembly Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.
System.IO.FileNotFoundException: Could not load file or assembly 'Elsa.Workflows.Runtime'
Shell 'default' requested 1 feature(s) that are not available: WorkflowsRuntimeEntityFrameworkCore
```

Rule 2 has a subtler edge: a package genuinely in the host's `deps.json` is correctly not downloaded,
but it is loaded in the host's default context. A feed package only sees it if it is also listed in
`Nuplane:Loading:SharedAssemblies` — acquisition-skipping and assembly-sharing are two separate lists,
and Nuplane does not keep them in step. `NativeEndpoints` behaves this way, and a feature depending on it
fails with `FeatureNotFoundException` rather than a missing-file error.

The reverse direction is kept in step by this repository instead. A shared assembly always resolves to
the host's copy, whatever version the feed package was built against within the entry's major, so each host's
`appsettings.json` also declares, under `Nuplane:HostProvidedPackages`, the package of every assembly it
lists in `Nuplane:Loading:SharedAssemblies` — restating Nuplane's two defaults, because a configured list
replaces them. `HostProvidedPackagesGuardTests` (`Essentials/tests/Architecture`) fails the build when a
shared assembly's package is neither declared nor exempted by name. The declaration is what makes the
version check apply: a feed package that needs a newer version of a declared package than the host's
`deps.json` carries is refused at reconciliation (see [What fails loudly](failure-modes.md#what-fails-loudly)) instead of
being bound to the older copy and failing later at a missing member.

An entry takes effect only when Nuplane's matcher takes it, on the assembly's name, its public key token
(`null` for an unsigned assembly, as every Elsa one is) and its major version, exactly. Every Elsa
assembly's `AssemblyVersion` is its line's major, `4.0.0.0` in a source build and a computed one alike
([ADR 0067](../adr/0067-package-versioning-uses-two-lines-with-computed-patch.md),
[#2150](https://github.com/elsa-workflows/elsa-foundation/issues/2150)), so every Elsa entry declares
`"MajorVersion": 4`. `SharedAssemblyMajorVersionGuardTests` (`Essentials/tests/Architecture`) binds each host's
entries with Nuplane's own code and fails the build when one would not take the reference a package built
against either build carries. A feed package that carries its own copy of a shared assembly, in its own files
or acquired into its graph, then binds the host's copy (since Nuplane `0.0.11-preview.94`,
[valence-works/nuplane#101](https://github.com/valence-works/nuplane/pull/101); before it no entry with a
`null` token bound at all, and such a package loaded its own copy). An entry the matcher does not take is
silent: the package loads its own copy, and its types stop being the host's.

Both hosts declare all their shares, the Elsa ones included, when built with computed package versions, as CI
builds their images
([ADR 0067](../adr/0067-package-versioning-uses-two-lines-with-computed-patch.md),
[#2084](https://github.com/elsa-workflows/elsa-foundation/issues/2084),
[#2126](https://github.com/elsa-workflows/elsa-foundation/issues/2126)). Such a build records each Elsa package
in its `deps.json` at the version the feed carries, and ships `ComputedVersions/appsettings.Production.json` as
its `appsettings.Production.json`: the committed file plus a `Nuplane:HostProvidedPackages` list naming every
share. `Elsa.Foundation.Host` shares Line A's ten contracts (`Elsa.Primitives`, `Elsa.Events.Core` and the rest),
the two host-composed ones, `Elsa.Cluster.Core` and `Elsa.Persistence.Schema`, and what it carries for its EF cluster
membership provider, `Elsa.Persistence.EntityFramework` and EF Core's `Microsoft.EntityFrameworkCore`, `.Abstractions`
and `.Relational` (see [Cluster membership and EF module packages](#cluster-membership-and-ef-module-packages)),
beside its three `CShells.*.Abstractions` ones; `Elsa.Workbench` shares the ten and the two plus the domain `.Core`
packages its own features need, `Elsa.Workflows.Runtime.Core` among them. So each image refuses a feed
package that needs a newer shared Elsa package than it carries. A host built from source records its Elsa
packages at their line's dev version (`4.0.0-dev` today), which every Elsa feed package's range excludes, so
declaring them would refuse every Elsa feed package that depends on one. A source build therefore keeps the
committed `appsettings.Production.json`, declares only its CShells/Nuplane shares (and, on `Elsa.Foundation.Host`, EF
Core's, whose versions are real in any build), and carries a named exemption for the Elsa ones, which it leaves
unchecked: the late-failure shape this section describes. That covers
`dotnet run`, the Docker image built locally and the source-built compose stack alike. The exemption lives in the
guard with its reason beside each entry, and applies to source builds only; the guard fails if an exempted assembly
stops being shared or a source build declares it after all, if a computed build leaves any share undeclared, and if
the computed production file differs from the committed one in anything but that list.

Sharing is also closed under Elsa project dependencies. A shared assembly's dependencies are not shared
with it. A feed-loaded feature gets its own private copy of any Elsa dependency the host does not also
share, so the types it exchanges with the shared assembly do not match the host's, and it fails at runtime.
`SharedAssemblyClosureGuardTests` (`Essentials/tests/Architecture`) fails the build when a host shares an
assembly built from a project under `src/` without sharing every Elsa project it references, directly or
transitively. That is why `Elsa.Workbench` shares `Elsa.Events.Core`, `Elsa.Pipelines.Core` and
`Elsa.Workflows.Design.Validations.Core`, and it shares `Elsa.Attention.Core` because that assembly is a
Line A contract features exchange with one another. All four are declared and exempted like its other
Elsa shares. `Elsa.Foundation.Host`'s closure holds by construction: Line A depends only on Line A
(ADR 0067), the two host-composed shares reach only Line A, and `Elsa.Persistence.EntityFramework` reaches only
`Elsa.Primitives` and `Elsa.Persistence.Schema`, so sharing those thirteen leaves nothing unshared to flag.

Neither case is caught by the version range or the lock file, because nothing was resolved to check.

The checks above apply only to **dependencies**, never to roots — which is why naming a package
explicitly always acquires it, and why the full-closure list is the reliable shape. It also makes a
deployment reproducible.

Note that `Microsoft.Extensions.*` is no longer skipped by a fixed prefix rule either: since
`0.0.11-preview.91` it is acquired from the feed like any other dependency unless it is also present in
the host's own `deps.json` (rule 2) or the host explicitly adds the `Microsoft.Extensions.` prefix to
`Nuplane:HostProvidedPackages` (rule 1). `Microsoft.EntityFrameworkCore.*` and `Npgsql.*` were never
covered by the old prefix rule either way, so EF and its provider have always arrived through the feed
normally — until `Elsa.Foundation.Host` began carrying them for its cluster membership
([#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151)). On that host EF Core, Abstractions and
Relational are declared (rule 1) and the engines' closures are in its `deps.json` (rule 2), so a module's
dependency on any of them is never acquired, and binds the host's copy.

### Cluster membership and EF module packages

Both hosts compose cluster membership once, on the host container (`AddEfSchemaReadability`): the readability report,
the in-process default, the finalization gate's view of the fleet and the dormancy check's source (spec 183, FR-017;
[#2143](https://github.com/elsa-workflows/elsa-foundation/issues/2143)). Both also compose
`AddConfiguredClusterMembership`, which replaces that default with the durable EF provider when configuration enables it
(spec 183, FR-024; on `Elsa.Foundation.Host` since [#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151);
see [Running several hosts as a cluster](clustering.md#running-several-hosts-as-a-cluster)). What a module reaches membership through
is in `Elsa.Persistence.Schema`, which references no EF Core, and `Elsa.Cluster.Core`; both hosts share both (ADR 0067,
"Host-composed shares"). With them shared, a module's finalization gate counts the host's fleet, so a single host
finalizes a new schema version at activation (spec 181, FR-021), and the host's dormancy check reads the module's gate,
so a feature that needs that version serves once it is finalized (spec 182, FR-019). Without them nothing fails loudly:
the module admits, its gate never finalizes past the version its record was created at, and every feature waiting on it
is refused as not observed. `SharedAssemblyClosureGuardTests` fails the build when a host does not share both.

**`Elsa.Foundation.Host` carries EF Core for its membership provider, and its modules bind that copy.** It carries
`Elsa.Cluster.EntityFrameworkCore`, the Data Protection key store `Elsa.Foundation.DataProtection.EntityFrameworkCore`,
`Elsa.Persistence.EntityFramework`, EF Core and the four engines, and nothing else of EF
([ADR 0076](../adr/0076-persistence-tooling-runs-inside-the-host-closure.md), amended 2026-09-29 and 2026-10-01). It shares
`Elsa.Persistence.EntityFramework` and EF Core's three assemblies, and declares EF Core's host-provided in every build
and `Elsa.Persistence.EntityFramework` in a build with computed versions. So a feed-loaded EF module's dependency on any
of them is never acquired, and the module binds the host's copy: the one `dotnet elsa persistence` discovers modules
through, since it runs through the host's own `Elsa.Persistence.EntityFramework` and matches `[EfModule]` by type. A
module that needs a newer EF Core than the host carries is refused (`host-version-unsatisfied`); on a computed build
the same holds for `Elsa.Persistence.EntityFramework`. A source build leaves that one undeclared, so it is not refused
there for being newer: a feed module built against a computed-version `Elsa.Persistence.EntityFramework` binds the host's
copy, its own being skipped as a shared assembly whenever the major matches, and the host's is the dev build (see the
builds that work, below). `Elsa.Workbench` carries `Elsa.Persistence.EntityFramework`
too and does not share it, so an EF module loaded there from a feed keeps a copy of its own.

**Which builds work together.** Build the host and the EF module packages from one commit. Two combinations work:

- a source-built host with EF modules packed from the same tree (dev versions on both sides);
- a computed-version host with modules from the matching computed feed.

The mix is the one to avoid. A source-built host fed a computed-version EF module binds the host's copy of
`Elsa.Persistence.EntityFramework` (since Nuplane `0.0.11-preview.94` the module's own is skipped, the major being the
same in both builds, [#2150](https://github.com/elsa-workflows/elsa-foundation/issues/2150)), but that copy is the dev
build, and the host's undeclared Elsa shares are exempt from the version check
([ADR 0067](../adr/0067-package-versioning-uses-two-lines-with-computed-patch.md)). A module that needs something newer
than the dev build carries fails late, at a missing member, instead of being refused at reconciliation.

**Naming a shared assembly as a feed root is harmless; naming a driver is not.** A root is always acquired. Since
Nuplane `0.0.11-preview.94` ([#2150](https://github.com/elsa-workflows/elsa-foundation/issues/2150)) the copy of
`Elsa.Persistence.EntityFramework` or EF Core (`Microsoft.EntityFrameworkCore`, `.Abstractions`, `.Relational`) a root
brings is skipped, and the module binds the host's, so those need not be kept out of your roots, though nothing is gained
by naming them. The engines' driver closures (`Microsoft.Data.Sqlite*`, `SQLitePCLRaw.*`, `Npgsql`, `MySql.Data`,
`MySqlConnector`, `Microsoft.Data.SqlClient*`) are carried by the host but not shared, so a root for one brings a second copy of it: leave
them out of your roots; see [Generating the closure](#generating-the-closure). The engine package itself may stay, since
the `ef-provider` selection injects it as a root anyway.

`FeedLoadedEfModuleTests` (`Essentials/tests/Cluster/EntityFrameworkCore/Tests`) proves each direction on both hosts'
configured shares, as Nuplane binds and matches them, a module carrying its own copy of `Elsa.Persistence.EntityFramework`
included. `FoundationHostBootTests` (same project) boots the built `Elsa.Foundation.Host` as a child process over a
directory feed of the packed fixture, plus a second, resolve-only `closure` feed holding EF Core and the Sqlite engine's
closure, showing finalization at activation and dormancy ending once a hold is released. It also boots the host over the
fixture carrying its own copies of `Elsa.Persistence.Schema` and `Elsa.Cluster.Core`: with the shares as shipped the
module binds the host's copies and finalizes, and with the `Elsa.Cluster.Core` entry at another major it binds its
own, admits, and its feature cannot reach the host's dormancy check (#2150). And it boots the host with an added share for an assembly the host does not
carry, which refuses the package that carries it and stops the host. `FoundationHostClusterBootTests` boots
two hosts over one database with the EF provider enabled, and shows that neither acquired any of what it carries from
the closure feed that offers it. Its PostgreSQL twin also shows that the host maps one copy of Npgsql, EF Core and
`Elsa.Persistence.EntityFramework`, and two of the engine assembly (the host's and the one Nuplane injected).

An EF module upgraded in place leaves its previous release loaded: Nuplane loads each host-integrated package graph
into a load context it never unloads. `AddEfSchemaReadability` therefore also composes the host's superseded-generation
source, `NuplanePackageGenerations` (spec 183, FR-021, amended 2026-09-29), which the readability report and the EF
activation guard both subtract through. It calls a release *replaced* once Nuplane's catalog of the active package set
lists a newer release of the same assembly, and *retired* once nothing on the host could still run it: no shell
generation composes a feature from its load context - counted from before a generation's first initializer runs until
its drain, or otherwise its container's disposal, has completed - and CShells' runtime feature catalog, which the next
generation is built from, names nothing in it. A generation whose drain fails stops counting only once its container
has disposed what it created for it, since until then its provider may be only partly disposed. The guard stops
reading a release once it is replaced; the report, once it is retired, and the host publishes its report again then,
so the new version finalizes without a restart. That publish never runs inside CShells' disposal of a shell: the
generation is marked released there, and one loop of the host's own publishes after it, taking every release made
meanwhile into a single publish, and only when something retired. CShells raises nothing when it refreshes its feature
catalog, so while a replaced release is held back by the catalog alone the host reads the catalog's snapshot generation
once a second, and publishes again once a refresh has lifted the pin. Until then the report intersects both releases. That includes a host whose shells are not active, since it does not
refresh the feature catalog after a reconcile then: with `Elsa:Boot:EagerShellActivation:Enabled` set to `false` and no
request yet, the catalog is not initialized, and the previous release counts until the first request initializes it
from the upgrade; after an eager activation that failed once it had initialized the catalog, the first request builds
the previous release, which then counts until a reload - the next reconcile, or `/_module-management/reload` -
refreshes the catalog and drains that shell. `FoundationHostBootTests` upgrades the fixture in place on a running host
both with eager activation and without it.

### Generating the closure

Maintain the **roots** by hand — they map one-to-one onto the shell features in `shells.json`. Generate
the closure, and regenerate it on every pin bump rather than hand-editing it; a real composition runs to
tens of entries.

1. Write a throwaway project with one `PackageReference` per root at the pinned version. Include the EF
   provider (`Npgsql.EntityFrameworkCore.PostgreSQL` or equivalent) if you use EF persistence, so the
   restore below resolves its closure — but you do **not** have to list the engine itself as a feed
   root. Every EF persistence module package declares an `ef-provider` capability in its own
   `nuplane.json`, one option per engine it can bind, each pinned to the version Elsa built against; the
   host picks one option with `Nuplane:Capabilities:ef-provider` and Nuplane acquires that engine
   package as a root of its own, with the rest of its closure following as ordinary dependencies. Naming
   the engine package explicitly in a feed's `IncludePatterns` still works, and wins when both are
   present. See [Selecting the EF provider engine](#selecting-the-ef-provider-engine).

2. Give it a `NuGet.config` naming the same feeds the host will use, then let NuGet resolve the real
   closure — including the conditional and framework-specific edges a nuspec walk gets wrong:

   ```bash
   dotnet restore restore.csproj --packages ./pkgs
   ```

3. Turn each `./pkgs/<id>/<version>/` into one `"<Id> [<Version>]"` pattern, dropping the ids the host
   genuinely provides, then split the rest across your feeds by origin.

   Derive that drop-list from the host image's own `*.deps.json` rather than copying one from elsewhere —
   it is exactly the set rule 2 above already skips, and it changes as the host's own references change.
   For the host as it ships today that means the `Microsoft.Extensions.*`, `CShells.*`, `Nuplane*` and
   `NuGet.*` families plus `FastEndpoints`, `Newtonsoft.Json` and `JetBrains.Annotations`, and, since
   [#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151), EF Core and every engine's closure:
   `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite*`, `SQLitePCLRaw.*`, `Npgsql`, `MySql.*` and
   `Microsoft.Data.SqlClient*` with what they pull in — but check, do not assume.

   **Then put every `Elsa.*` id back, except `Elsa.Persistence.EntityFramework`.** `Elsa.Api.AspNetCore` is in
   the host's `deps.json`, so a drop-list derived mechanically would prune the one Elsa package that is in there
   — the rule 2 shape again, and it surfaces as a `FeatureNotFoundException` naming a *feature* rather than the
   missing package. `Elsa.Persistence.EntityFramework` is the opposite case: the host carries it for its cluster
   membership and shares it, and naming it as a root gives every EF module a copy of its own (see
   [Cluster membership and EF module packages](#cluster-membership-and-ef-module-packages)).

### Selecting the EF provider engine

No Elsa package depends on an EF Core provider engine: `EfRelationalProviderBinding` binds it
reflectively by assembly-qualified type name, so no nuspec edge names it and the dependency walk never
acquires it. Every EF persistence module package therefore declares the choice instead, as a capability
in its package-root `nuplane.json` — one option per engine it can bind, each carrying that engine's
package id and the exact version Elsa built against. The host makes the choice with one key:

```json
"Nuplane": {
  "Capabilities": { "ef-provider": "PostgreSql" }
}
```

The option names are the same four `--provider` uses: `Sqlite`, `SqlServer`, `PostgreSql`, `MySql`. The
environment-variable form is `Nuplane__Capabilities__ef-provider=PostgreSql`.

- **Several engines.** A host that runs two on purpose selects both: `"PostgreSql,Sqlite"`. Each selected
  option becomes its own root.
- **A different patch.** The object form replaces the version the module declared, and can steer the
  injected root at one feed: `{ "Option": "PostgreSql", "Version": "[10.0.1]", "Feed": "local-packages" }`.
  A feed is a host fact, so it is the only place one can be named — package metadata cannot.
- **Naming the engine by hand still works, and wins.** When one of the option packages is already an
  explicit root — an include pattern, a directory-feed drop, a convergence manifest — the capability is
  satisfied by that root, nothing is injected, and the only difference is one Information log line. If
  that explicit root's version cannot satisfy the option's declared range, the declaring module is
  refused (`capability-conflict`) naming both requests, rather than bound against a version it cannot
  use; the root the operator asked for is still applied.
- **No selection is a refusal, never a guess.** A module that declares the capability, with no selection
  and no option package as an explicit root, fails resolution with stage `capability-unselected`, naming
  the capability, every declared option and this key. The cycle is degraded and
  `Reconciliation:StartupFailurePolicy` decides what that means for startup. Nuplane never picks an
  engine, and the metadata has no default: a silently chosen engine surfaces much later as a reflection
  error at shell load.

In the cycle the injected engine is an ordinary root: acquired from a trusted feed under the same retry
policy, evaluated against the lock file, applied in the same transaction, and recorded in
`store-state.json` with `PackageRole = Root` and `SourceName = capability:ef-provider=PostgreSql`, which
is what says the selection is where it came from. Changing the selection changes the graph, so the next
cycle reconciles the previous engine out of the desired set and the new one in.

**On `Elsa.Foundation.Host` the engine is also the host's.** The host carries all four engines for its cluster
membership, and a module binds its engine by name through the host's `Elsa.Persistence.EntityFramework`, so it binds
the host's copy. The selection still has to be made, because a module that declares the capability is refused without
one. The package it injects is still acquired as a root: `Microsoft.EntityFrameworkCore.Sqlite` carries no assembly of
its own, and for the other three the engine assembly is loaded into the module's graph unused, while everything each
depends on is the host's.

**`--provider` stays authoritative** (ADR 0076 D4). `dotnet elsa persistence` reads this key out of the
host's own `appsettings.json` plus its `--environment` overlay, and when the selection does not contain
the engine `--provider` names, the command exits 3 and lists the selection beside any per-feature
offenders. It never takes the selection for the provider and never falls back. A multi-engine selection
that contains `--provider` agrees.

### Finding the root package for a feature

Roots do not need a hand-maintained feature-to-package table. Every package carries an
`elsa-package.json` manifest at its root, with `package: { id, version }` and a `features` array whose
entries carry settings, dependencies and required capabilities. A feature id is
`<PackageId>.<FeatureName>`, so the name you write in `shells.json` is the id with the package prefix
removed. `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore` declares eight, including the umbrella
`WorkflowsRuntimeEntityFrameworkCore`:

```
Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.WorkflowsRuntimeEntityFrameworkCore
Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence
…
```

So "which package provides feature X" is answerable from the feed itself, and a generator can keep
`shells.json` as the only hand-edited file: feature names resolve to root packages through the
manifests, and the closure follows from the roots. The same manifests back the module-management
surface (`Elsa.Modularity.Nuplane`).

**Keep the exclusions honest.** A declared dependency must be resolvable even when it does nothing at
runtime. Pruning `Microsoft.EntityFrameworkCore.Analyzers` from a *directory* feed — on the reasoning that
its types cannot load without `Microsoft.CodeAnalysis` and it contributes no features — failed startup
reconciliation for all 36 packages with `NoEligibleFeedException` and exited 139. Its type-load warning is
noise to be left alone. This bites on directory feeds, where the package must physically be present; a
remote feed resolves an unlisted dependency by itself.

## Pinning and integrity

A single-point range (`[4.0.0-preview.793]`) pins exactly. For integrity, turn on the lock file
rather than hand-maintaining hashes:

```json
"Nuplane": {
  "LockFile": { "Mode": "Enforce", "Path": "nuplane.lock.json", "FailOnHashMismatch": true }
}
```

`Generate` writes the lock on first reconcile; `Enforce` and `Strict` then refuse a package whose
hash does not match.

An engine injected by [`Nuplane:Capabilities:ef-provider`](#selecting-the-ef-provider-engine) is pinned
without appearing in any `IncludePatterns`: each module's declaration carries the exact single-point
version Elsa built against, so the injected root is a single-point request by construction, and the
`Version` override on the key replaces it with another one. That is what keeps
`dotnet elsa persistence --restore` — which refuses anything naming more than one version — able to
restore a host that never lists its engine. A contributed request's pinned-ness can only be judged
inside the cycle, because the module declaring it has to be acquired before its declaration can be read,
so an unpinned one is not the usual "nothing was resolved" skip: the cycle runs, refuses the
contribution before fetching it (`capability-unpinned`), and the CLI reports it as
`restore-capability-unresolved` naming the declaring module and the key. The injected root is subject to
the lock file like any other root, and is never subject to `Nuplane:HostProvidedPackages`, which is
consulted only for dependencies.

Nuplane also models a desired manifest (`Nuplane:Convergence:Manifest` — a file of
`{ Id, Version, SourceHint, Sha512 }` entries). Since `0.0.11-preview.83`
([valence-works/nuplane#76](https://github.com/valence-works/nuplane/issues/76)),
`DesiredManifestPackageSource` is always registered as an `IDesiredPackageSource`, but the source
itself is skipped unless `Nuplane:Convergence:Manifest:Enabled` is true AND
`Nuplane:Convergence:Manifest:Path` is set, so it is available and opt-in rather than unregistered.
It is not a prerequisite for feed-based deployment: exact-id include patterns plus the lock file
cover pinning and integrity.

---

All pages: [Overview](../foundation-host-feeds.md) · [Point the host at a feed](feeds.md) · [Pinning and closures](pinning-and-closures.md) · [Updating packages](updating-packages.md) · [When things fail](failure-modes.md) · [Clustering](clustering.md)
