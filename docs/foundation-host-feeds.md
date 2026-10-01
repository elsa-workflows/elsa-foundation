# Elsa.Foundation.Host package feeds

`Elsa.Foundation.Host` compiles in no Elsa feature. Every feature — activities, HTTP, persistence,
the Tasks feature — arrives as a NuGet package through a Nuplane feed and is discovered by CShells
via `NuplaneAssemblyProvider`. This page is the worked reference for pointing the host at a feed, and,
in [Running several hosts as a cluster](#running-several-hosts-as-a-cluster), for pointing several of
them at one database.

A feed can be a **directory** of `.nupkg` files or a **remote NuGet V3 service index**. Both are
declared the same way, under `Nuplane:Setup:Feeds`, and both work in the shipped host. A deployment
that wants a feed URL and a pinned version does not need a mounted folder of package binaries.

## The two feed shapes

Exactly one of `DirectoryPath` or `ServiceIndex` per entry.

```json
{
  "Nuplane": {
    "Setup": {
      "AutomaticReconciliation": true,
      "PollInterval": "00:01:00",
      "Feeds": [
        {
          "Name": "local-packages",
          "DirectoryPath": "packages",
          "IncludePatterns": [ "*" ],
          "Directory": { "Watch": true, "DebounceWindow": "00:00:01" }
        },
        {
          "Name": "elsa-4",
          "ServiceIndex": "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json",
          "IncludePatterns": [
            "Elsa.Tasks [4.0.0-preview.793]",
            "Elsa.Tasks.Schedules [4.0.0-preview.793]"
          ]
        }
      ]
    }
  }
}
```

Both kinds are registered by `AddNuplane` itself, which reads `Nuplane:Setup:Feeds` and calls
`AddFeed(...).FromUri(...)` for every entry carrying a `ServiceIndex`. The host's extra
`AddDirectoryFeedsFromConfiguration` call exists only because the *directory* source ships in a
separate package (`Nuplane.Sources.Directory`) that `AddNuplane` cannot reach. There is no
`AddRemoteFeedsFromConfiguration`, and none is needed.

Add `Credentials` for a private feed — a *reference* to its secret, never the secret; see
[Feed credentials](#feed-credentials). `Nuplane:FeedResolution` controls multi-feed behavior —
`FeedPriorities`, `StopOnFirstSuccessfulFeed`, `OfflineMode`, `PackageInstallRoot`.

A relative `DirectoryPath` is resolved against the host's **content root** — the same directory this
`appsettings.json` was read from — because the host passes
`nuplane.UseBasePath(builder.Environment.ContentRootPath)` where it composes Nuplane
(`src/apps/Elsa.Foundation.Host/Program.cs`, and the same call in `Elsa.Workbench`). Without that call
Nuplane resolves such a path against the *process's current directory* instead, and the two are the same
place only when the host is started from its own directory. A deployment that sets the content root
independently of where the process is launched — `ASPNETCORE_CONTENTROOT`, a systemd unit, a container
`WORKDIR` — then reads this file out of one directory and looks for `packages` under another. That presents
as an empty feed, and per [What fails loudly](#what-fails-loudly) an empty feed is the quiet failure. An
absolute `DirectoryPath` is unaffected either way.

## Include patterns behave differently on the two shapes

This is the sharp edge, and in the silent direction it is silent.

An include pattern is `<package-id-glob> [<version-range>]`. The two feed kinds read it almost
inversely, because only a directory feed has a package list to match against, and only a remote feed
parses the version range. Measured against Nuplane 0.0.9-preview.61:

| Pattern | Directory feed | Remote feed |
|---|---|---|
| `*` | every id in the folder, highest version of each | **nothing, silently** |
| `Elsa.*` | every matching id, highest version | **nothing, silently** |
| `Elsa.Tasks` | that id, at the highest version in the folder | **fails the cycle** — no version to resolve |
| `Elsa.Tasks [4.0.0-preview.793]` | **nothing** — the range is not parsed | that package, at exactly that version |

On a **directory** feed the version always comes from the filename and the highest wins;
`PackagePatternMatcher` compares your raw pattern string against the package id, so a ` [range]`
suffix simply fails to match. Use bare globs or bare ids.

On a **remote** feed `FeedRuleDesiredSource` runs in *direct mode*: with no catalog to enumerate it
discards every pattern containing `*` or `?` and keeps only literal ids, and the version range is
required. Use exact ids with an exact range.

A remote feed declared with wildcards is registered and reachable and contributes **zero** desired
packages, so reconciliation completes with nothing to do:

```
Reconciliation cycle completed [IsDegraded=False, FailedCount=0]
Active package catalog read [PackageCount=0, IssueCount=0]
warn: Shell 'default' requested 6 feature(s) that are not available in the runtime feature catalog
```

`IsDegraded=False` with `PackageCount=0` and a shell missing its features is the signature of this
mistake. It looks identical to a healthy host that was asked for nothing, because that is what it is.

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
   [What fails loudly](#what-fails-loudly).
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
replaces them. `HostProvidedPackagesGuardTests` (`tests/essentials/Architecture`) fails the build when a
shared assembly's package is neither declared nor exempted by name. The declaration is what makes the
version check apply: a feed package that needs a newer version of a declared package than the host's
`deps.json` carries is refused at reconciliation (see [What fails loudly](#what-fails-loudly)) instead of
being bound to the older copy and failing later at a missing member.

An entry takes effect only when Nuplane's matcher takes it, on the assembly's name, its public key token
(`null` for an unsigned assembly, as every Elsa one is) and its major version, exactly. Every Elsa
assembly's `AssemblyVersion` is its line's major, `4.0.0.0` in a source build and a computed one alike
([ADR 0067](adr/0067-package-versioning-uses-two-lines-with-computed-patch.md),
[#2150](https://github.com/elsa-workflows/elsa-foundation/issues/2150)), so every Elsa entry declares
`"MajorVersion": 4`. `SharedAssemblyMajorVersionGuardTests` (`tests/essentials/Architecture`) binds each host's
entries with Nuplane's own code and fails the build when one would not take the reference a package built
against either build carries. A feed package that carries its own copy of a shared assembly, in its own files
or acquired into its graph, then binds the host's copy (since Nuplane `0.0.11-preview.94`,
[valence-works/nuplane#101](https://github.com/valence-works/nuplane/pull/101); before it no entry with a
`null` token bound at all, and such a package loaded its own copy). An entry the matcher does not take is
silent: the package loads its own copy, and its types stop being the host's.

Both hosts declare all their shares, the Elsa ones included, when built with computed package versions, as CI
builds their images
([ADR 0067](adr/0067-package-versioning-uses-two-lines-with-computed-patch.md),
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
`SharedAssemblyClosureGuardTests` (`tests/essentials/Architecture`) fails the build when a host shares an
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
see [Running several hosts as a cluster](#running-several-hosts-as-a-cluster)). What a module reaches membership through
is in `Elsa.Persistence.Schema`, which references no EF Core, and `Elsa.Cluster.Core`; both hosts share both (ADR 0067,
"Host-composed shares"). With them shared, a module's finalization gate counts the host's fleet, so a single host
finalizes a new schema version at activation (spec 181, FR-021), and the host's dormancy check reads the module's gate,
so a feature that needs that version serves once it is finalized (spec 182, FR-019). Without them nothing fails loudly:
the module admits, its gate never finalizes past the version its record was created at, and every feature waiting on it
is refused as not observed. `SharedAssemblyClosureGuardTests` fails the build when a host does not share both.

**`Elsa.Foundation.Host` carries EF Core for its membership provider, and its modules bind that copy.** It carries
`Elsa.Cluster.EntityFrameworkCore`, the Data Protection key store `Elsa.Foundation.DataProtection.EntityFrameworkCore`,
`Elsa.Persistence.EntityFramework`, EF Core and the four engines, and nothing else of EF
([ADR 0076](adr/0076-persistence-tooling-runs-inside-the-host-closure.md), amended 2026-09-29 and 2026-10-01). It shares
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
([ADR 0067](adr/0067-package-versioning-uses-two-lines-with-computed-patch.md)). A module that needs something newer
than the dev build carries fails late, at a missing member, instead of being refused at reconciliation.

**Naming a shared assembly as a feed root is harmless; naming a driver is not.** A root is always acquired. Since
Nuplane `0.0.11-preview.94` ([#2150](https://github.com/elsa-workflows/elsa-foundation/issues/2150)) the copy of
`Elsa.Persistence.EntityFramework` or EF Core (`Microsoft.EntityFrameworkCore`, `.Abstractions`, `.Relational`) a root
brings is skipped, and the module binds the host's, so those need not be kept out of your roots, though nothing is gained
by naming them. The engines' driver closures (`Microsoft.Data.Sqlite*`, `SQLitePCLRaw.*`, `Npgsql`, `MySql.Data`,
`Microsoft.Data.SqlClient*`) are carried by the host but not shared, so a root for one brings a second copy of it: leave
them out of your roots; see [Generating the closure](#generating-the-closure). The engine package itself may stay, since
the `ef-provider` selection injects it as a root anyway.

`FeedLoadedEfModuleTests` (`tests/essentials/Cluster/EntityFrameworkCore/Tests`) proves each direction on both hosts'
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

## Feed credentials

A private feed is configured with a **reference** to its secret, never with the secret:

```json
{
  "Nuplane": {
    "Setup": {
      "Feeds": [
        {
          "Name": "private-feed",
          "ServiceIndex": "https://packages.example.com/v3/index.json",
          "Credentials": "secrets://elsa/feed-token",
          "IncludePatterns": [ "Acme.Plugins.Widgets [1.4.2]" ]
        }
      ]
    }
  }
}
```

The reference is `secrets://<provider>/<name>`. The provider segment selects a registered provider and is
matched case-insensitively; everything after the first slash is the name. A `Credentials` value that is not
of that shape fails options validation at startup, and the rejected value is never echoed — a host that
pasted the token itself into `Credentials` is exactly the case that rule catches. Credentials are forbidden
on a `file://` feed, and a credentialed feed has to be an HTTPS service index.

**The two providers, and where each one works.**

| Provider | Registered by | Reads | Works in |
|---|---|---|---|
| `env` | `AddNuplane`, always | That name in the process's environment | Anywhere Nuplane runs: a started host, and the host-free `dotnet elsa persistence --restore` pass |
| `elsa` | `AddSecretsFeedCredentials()`, from `Elsa.Secrets.Nuplane` | That name in the Secrets module, through `ISecretValueResolver` | The container that composed both `AddNuplane` and the Secrets read path — and nowhere else |

That last column is the whole of it, and it is a container rule rather than a preference. Nuplane resolves
`IEnumerable<ISecretReferenceProvider>` out of the container `AddNuplane` was called on, once, and shares
the instance across every feed and every cycle. So a host that composes Nuplane on its own container — which
is what `Elsa.Foundation.Host` and `Elsa.Workbench` both do — has to register the provider *there*:

```csharp
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.UseBasePath(builder.Environment.ContentRootPath);
    nuplane.Services.AddSecretsFeedCredentials();   // and AddSecrets(...) on the same container
});
```

Enabling the `SecretsNuplane` **shell feature** registers the same provider, but into that shell's own
container, which is a different container: CShells builds each shell by copying the root's service
descriptors into a fresh collection and adding the shell's features to that. So the feature claims `elsa`
for a shell's own Nuplane composition, and does **not** claim it for the host's reconcile loop. Neither host
composes the provider on its own container today, so `secrets://elsa/…` in either of their
`appsettings.json` is refused by name — which is the reported outcome below, not a silent one.

There is also a bootstrapping limit worth knowing before reaching for `elsa` on a package-hosted host: the
Secrets module itself arrives as a package. A feed whose credential lives in Secrets cannot be the feed that
delivers Secrets. `secrets://env/NAME` has no such ordering problem, which is why it stays the right answer
for a host's first boot.

**Secret shape.** Whatever the provider returns is either `user:password`, split at the first colon so a
password may contain colons, or a bare token, which Nuplane sends as the password under a fixed placeholder
user name — the form Azure Artifacts, Feedz and other token-issuing feeds accept. A value whose colon leaves
either half empty is refused rather than sent half-formed. Both the version enumeration and the package
download authenticate, each with credentials attached to that one feed; no process-global NuGet credential
provider is installed, so one feed's secret is never offered to another.

**When a reference cannot be resolved** — no registered provider claims its segment, the provider holds no
value, or the referenced secret is expired, revoked or under another tenant — the feed is refused **by
name**. It is not contacted at all: not for version enumeration, not for a download, and not even to serve a
package an earlier authenticated run already installed from it. A running host fails the packages that could
only have come from that feed, naming the feed in the failure; the `--restore` pass reports the feed and
drops it before its first network call. With no provider able to resolve anything, behaviour is exactly what
it was before credentials could be resolved at all.

**What is never logged.** The reference is configuration and may appear in logs; the secret never does.
Resolved secrets are wrapped so that logging, interpolating or including one in an exception prints `***`,
and every refusal and failure message names the feed only — never the value, the reference, or the provider.
Nothing resolved is written to the state file, and a resolved secret is cached for the duration of one
reconciliation cycle and dropped with it. The `elsa` provider holds the same line on its own side: it
returns the raw string to Nuplane and does nothing else with it, and a resolution that failed contributes
its failure code at most.

## What fails loudly

A malformed feed entry fails at startup through options validation (`ValidateOnStart`), before the
app serves traffic:

```
OptionsValidationException: Nuplane setup feed 'x' at 'Nuplane:Setup:Feeds:0'
  must set exactly one of DirectoryPath or ServiceIndex.
OptionsValidationException: Nuplane setup feed 'x' at 'Nuplane:Setup:Feeds:0'
  has an invalid absolute ServiceIndex URI 'not-a-uri'.
```

A package that is declared but missing from every feed fails the *cycle*, not startup — the run
reports `IsDegraded=True` with the package in `FailedPackages`, and the state file records the
reason under `lastFailureById`. That is the loud failure to look for; a degraded cycle means the
feed was reached and the package was not there.

A package that needs a **newer host than the one running** is refused at reconciliation rather than
loaded (since Nuplane `0.0.11-preview.93`, [valence-works/nuplane#100](https://github.com/valence-works/nuplane/pull/100)).
It happens when the package, or a package in its closure, depends on a package the host declares under
`Nuplane:HostProvidedPackages`, and the host's own `*.deps.json` carries that package at a version
outside the range the dependency requires — a module built against `Elsa.Workflows.Core [4.1.0, 5.0.0)`
on a host whose deps file carries `4.0.0`, with `Elsa.` declared. A declared package is never acquired,
so there is no newer copy for Nuplane to fetch; loading the module anyway would bind it to the older
assembly, and the failure would surface much later as a missing member at the point of use. Instead the
module, and every root whose closure reaches it, is refused under stage `host-version-unsatisfied`, and
every other root still applies. The host's log carries one Warning per refused package (event 1034,
category `Nuplane.Observability.ReconciliationLogger`):

```
Host-provided dependency version refused [CorrelationId=…, PackageId=Acme.Widgets]:
  Package 'Acme.Widgets@1.4.2' requires 'Elsa.Workflows.Core [4.1.0, 5.0.0)', but the host provides
  'Elsa.Workflows.Core 4.0.0'. …
```

The cycle is degraded like any other failed package, the state file records the stage and message under
`lastFailureById`, and `Reconciliation:StartupFailurePolicy` decides what that means for startup.
`dotnet elsa persistence --restore` refuses the same case by name (exit 3,
`restore-host-version-unsatisfied`), carrying Nuplane's message verbatim; when the same cycle also refused
a module for an unselected provider engine, that refusal is listed beside it under its own `capability-*`
stage rather than left for the next run to find. The fix is one of two, and never a configuration key: run
a host at or above the version the module requires, or pin the module to a version whose requirement this
host satisfies.

The check needs a version to compare against. A declared package the host's deps file does not carry at
all — a prefix entry such as `Elsa.` on a host that compiles in no Elsa feature — is still trusted as
supplied, and each such dependency is logged as a Warning (event 1035) naming the dependent package, the
dependency and the range, so it is visible rather than silent. An *undeclared* package the host carries at
an unsatisfying version is not refused either: it is acquired from the feed like any other dependency.

A package that carries a **shared assembly the host cannot supply** is refused when it loads (since Nuplane
`0.0.11-preview.94`, [valence-works/nuplane#101](https://github.com/valence-works/nuplane/pull/101)). Nuplane
never loads a package's own copy of an assembly a `Nuplane:Loading:SharedAssemblies` entry matches, so when the
host has no copy of it at the entry's major version, the package's whole graph fails to load, and every package in
it is reported with the reason (category `Nuplane.Loading.PackageAutoLoadingObserver`):

```
Package Acme.Widgets failed to load: Package 'Acme.Contracts@1.0.0'
  carries shared assembly 'Acme.Contracts' (public key token: unsigned, major version: 1),
  which the shared-assembly policy leaves to the host, but the host has no copy of it with that major version.
```

The cycle is degraded, and at startup, under the default `Reconciliation:StartupFailurePolicy`, the host exits
with a `NuplaneStartupReconciliationException` naming every failed package. So an entry for an assembly a host
does not carry is not harmless: share only what the host itself references. `FoundationHostBootTests` pins the
refusal, and `SharedAssemblyMajorVersionGuardTests` fails the build when either host declares a share its build output
does not carry.

What is not loud is the wildcard case above, because a pattern that matches nothing is
indistinguishable from a feed that was asked for nothing.

## Hot reload is a Foundation.Host behavior, not a product one

`Elsa.Foundation.Host` picks up a newly reconciled package without a restart.
`ShellReloadOnPackagesChanged` (`src/apps/Elsa.Foundation.Host/Shells/ShellReloadOnPackagesChanged.cs`)
is registered as a Nuplane observer in `src/apps/Elsa.Foundation.Host/Program.cs`, and when a
reconciliation cycle completes — not when packages land on disk, which is before their assemblies are
loaded — it refreshes the CShells runtime feature catalog and reloads every active shell. It is on unless
`Elsa:Shells:ReloadOnPackageChange` is set to `false`, and it does nothing until a shell is active, so the
startup cycle is left to ordinary shell activation. A reload failure is logged and the new assemblies
apply at the next restart. The bridge performs no compatibility check of its own: it reloads whatever the
cycle applied, which is why the refusal above has to happen at reconciliation — a refused package is
never applied, so there is nothing for the reload to pick up.

CShells does not throw when a shell's reload fails: it keeps the previous generation active and reports the
failure in that shell's `ReloadResult.Error`. The bridge logs each such shell at `Warning` (an EF module's
refusal) or `Error` (anything else), and `POST /_module-management/reload` answers with a problem document instead
of `200`:

- `409 Conflict` when every shell that stayed on its old generation was refused by an EF module: pending migrations
  under `Migrate:Policy=Validate`, a contracting migration that may not be applied yet, or the schema finalization
  gate. The operator resolves that outside the request and repeats it.
- `500 Internal Server Error` when any failed shell was not such a refusal. Only the exception's type name is
  reported for it; its message stays in the host's log.

The body is `application/problem+json` in both cases, with the standard `type`, `title`, `status` and `detail`
(one sentence per shell) and these fields:

| Field | Meaning |
| --- | --- |
| `features` | The number of feature descriptors in the refreshed runtime catalog. |
| `reloaded` | The number of shells that reloaded. |
| `shells` | One entry per shell that stayed on its previous generation, in the order CShells reported them. |

Each entry of `shells`:

| Field | Meaning |
| --- | --- |
| `shell` | The shell's name. |
| `error` | What went wrong. For a refusal, the module's own message; otherwise the exception's type name and a pointer to the host log. |
| `code` | For a refusal, one of the codes below; `null` otherwise. |
| `module` | For a refusal, the EF module; `null` otherwise. |
| `pendingMigrations` | For a refusal, the ids of the migrations it concerns (empty for `schema-activation-refused`); `null` otherwise. |
| `command` | For a refusal, the exact command that resolves it, when there is one; `null` otherwise. |

`code` values:

- `pending-migrations`: the module's database has migrations that are not applied. `command` applies them.
- `contracting-migration-refused`: the pending batch holds a contracting migration that may not be applied yet. `error`
  says which and what it waits for; there is no `command`.
- `schema-activation-refused`: the schema finalization gate refuses to activate the module. `error` names the remedy;
  there is no `command`.

For `pending-migrations` the command is runnable as it stands, with `--host` the directory of the running host (the one
holding its build output and its `.nuplane` state, not its content root), which is where the tool reads the package set
the host last reconciled:

```
dotnet elsa persistence apply --host "<host directory>" --modules <module> --provider <p> --connection-env ELSA_EF_CONNECTION
```

The host recognises the refusal by `IEfModuleRefusal` (`Elsa.Persistence.Schema`, a shared assembly) and names no EF
type: it carries and shares `Elsa.Persistence.EntityFramework`, which the module binds, so the tool finds the module
through the host's own copy in the deployed directory. A module whose exception was built against a private copy of `Elsa.Persistence.Schema` too is
recognised by the full name of the interface it implements. Once the command has run, the same `POST` answers `200`
and the shell's generation advances, with no restart.

`Elsa.Workbench` does not do this. Its `Program.cs` composes Nuplane with no reconciliation observer, and
registers `NullShellReloader` (`src/apps/Elsa.Workbench/Modularity/NullShellReloader.cs`) as its
host-level `IShellReloader` — a no-op that reports zero shells reloaded. A package the directory watcher
or the `/_elsa/module-management` upload and reconcile endpoints bring in is reconciled and loaded, but
the running shells keep the feature set they were built with; those endpoints answer
`"RequiresReload": true` to say so. The new package takes effect at the next restart. The one exception
is a shell that enables the `ModularityApi` feature: it replaces `NullShellReloader` inside that shell
with a reloader that does reload it, so a feature change applied through that shell's module API also
refreshes the feature catalog and rebuilds that one shell. That is a side effect of applying feature
configuration, not a response to reconciliation.

## Reconciling on demand

`POST /_module-management/reconcile` (with `Elsa:ModuleManagement:Enabled` and the `X-Elsa-Module-Management-Key`
header, like `reload`) runs one reconcile cycle now, the same cycle the folder watcher and the poll interval run, and
answers once it has finished. A wrong or missing key is answered `401`.

Whether a package the cycle added is live in the running shells when the request returns depends on the hot-reload
observer, described in the section "Hot reload is a Foundation.Host behavior, not a product one": it reloads the active
shells at the end of the cycle only when `Elsa:Shells:ReloadOnPackageChange` is on (it is by default) and a shell is
already active. With it off, or before any shell has activated, the package is reconciled and its assemblies are loaded,
but the shells keep the feature set they were built with until `POST /_module-management/reload` (or a restart). A shell
whose reload was refused after the cycle is not reported by this request: the host logs it, and `reload` answers it
with the `409` documented in that same section.

The answer carries Nuplane's `ManualReconcileOutcome`, with its code by name. The status says whether the cycle ran:

| Outcome | Status | Meaning |
| --- | --- | --- |
| `Completed` | `200` | The cycle ran to its end. `runResult.skipped` is `false`. |
| `Accepted` | `200` | Nuplane's code for a cycle still in progress. This request waits for the cycle, so the pinned Nuplane does not produce it here. |
| `Rejected` | `409` | The cycle did not run, because another cycle is already running in this host (`single-flight-active`) or another process owns the store (`store-lock-unavailable`). Repeat the request once that has cleared. |
| `Unavailable` | `503` | The reconcile service failed to run the cycle. `reasonCode` is `reconcile-service-failed`; the failure's message is logged at `Error` with the `correlationId`, never returned, because it can hold paths, feed addresses or connection details. |
| any other code | `500` | A Nuplane newer than this host's reported an outcome code it does not map, so it cannot say whether the cycle ran. `outcomeCode` is the number, `reasonCode` is `reconcile-outcome-unrecognized`, and the host logs it at `Error` with the `correlationId`. |

A `200` is the outcome itself, in camelCase JSON:

```json
{
  "outcomeCode": "Completed",
  "correlationId": "6a1d09e5dc654ded863555321a055673",
  "reasonCode": null,
  "runResult": {
    "skipped": false,
    "changeSet": {
      "added": [ { "id": "Acme.Widgets", "version": "2.0.0", "feedName": "local-packages", "sourceName": "local-packages", "installedAt": "2026-09-30T10:15:59.101+00:00" } ],
      "updated": [],
      "removed": [],
      "correlationId": "6a1d09e5dc654ded863555321a055673",
      "timestamp": "2026-09-30T10:15:59.419047+00:00"
    },
    "failedPackages": [],
    "isDegraded": false,
    "skipReason": 0
  }
}
```

| Field | Meaning |
| --- | --- |
| `outcomeCode` | The outcome's name: `Completed`, `Accepted`, `Rejected` or `Unavailable`; the number for a code this host does not know. |
| `correlationId` | Ties the request to the cycle's log lines. |
| `reasonCode` | For a rejected outcome, `single-flight-active` or `store-lock-unavailable`; for an unavailable one, `reconcile-service-failed`; `null` for the others. |
| `runResult` | The cycle's result. `skipped` is `true` when the cycle did nothing, and `skipReason` says why: `0` not skipped, `1` another cycle was already running in this host, `2` another process owns the store. |
| `runResult.changeSet` | The packages the cycle `added` and `updated` (id, version, feed, source and when it was installed; not where on the host's disk), and the ids it `removed`. An empty feed, or one that changed nothing, has all three empty. Packages acquired as dependencies or by capability selection are listed with the ones asked for; `sourceName` says which. |
| `runResult.failedPackages` / `isDegraded` | The ids of packages that failed to load, and whether the cycle finished degraded. |

A `409`, `503` or `500` is an `application/problem+json` document, as for a refused `reload`, with the standard `type`, `title`,
`status` and `detail` and these fields: `outcomeCode` (`Rejected`, `Unavailable` or the unknown code's number), `reasonCode` (as above) and
`correlationId`.

The request runs through the host's own Nuplane operations, not the ones its shell holds. CShells copies every host
registration into every shell, and a registration made by instance is shared by all of them, but one made by type or by
factory is copied and instantiated again, so a shell has its own instance of Nuplane's trigger queue, and a reconcile
queued on that copy is one no dispatcher reads (#2159). The host takes the operations from its root provider for that
reason, and its composition shares the operations, the coordinator and the trigger ingress with every shell, so a shell
that resolves them gets the host's instance. `HostOwnedServicesAreSharedWithShellsTests` holds that for every Elsa and Nuplane
singleton the host registers: it builds both hosts, activates a shell, and fails on any that the shell holds a second
instance of and that the test does not list with a reason a shell's own copy is right or unreachable. `FoundationHostReconcileTests` pins the answers on the built host, and
`FoundationHostReconcileEndpointTests` the statuses of each outcome.

## Running several hosts as a cluster

With nothing configured each `Elsa.Foundation.Host` is a cluster of one: the in-process membership default, which writes
nothing and counts only itself (spec 183, FR-017, FR-018). Several hosts that share one database must be configured as a
cluster, or each will finalize a feed-loaded EF module's new schema version as soon as it alone can read it, however many
hosts on the older release still write the same rows (spec 183, FR-018a). Nothing detects that misconfiguration.

A cluster is declared by enabling the durable EF membership provider on every host, with the same membership store and a
host id of each host's own ([#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151); ADR 0078; spec 183,
FR-024):

```bash
Elsa__Cluster__Membership__HostId=foundation-host-a            # distinct per host, stable across its restarts
Elsa__Cluster__Membership__EntityFrameworkCore__Enabled=true
Elsa__Cluster__Membership__EntityFrameworkCore__Provider=PostgreSql
Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionString="Host=db;Database=elsa;Username=elsa;Password=…"
```

- **The keys.** Under `Elsa:Cluster:Membership`: `HostId`, `HeartbeatInterval` (default 10 s), `ExpiryPeriod` (30 s) and
  `SkewAllowance` (5 s); under its `EntityFrameworkCore` subsection: `Enabled`, `Provider` (`Sqlite`, `SqlServer`,
  `PostgreSql` or `MySql`; default `Sqlite`), `ConnectionString`, or `ConnectionName` naming an entry under
  `ConnectionStrings` (with `ConnectionStrings:Elsa` as the fallback), `Schema`, `Pooling` and `CleanupPeriod` (10 min;
  it must exceed the expiry period plus the skew allowance). `ClusterMembershipConfigurationExtensions` in
  `src/essentials/Cluster/EntityFrameworkCore` is the reference.
- **The host id.** It is required once the provider is enabled: a host without one refuses to start, naming
  `Elsa:Cluster:Membership:HostId` (FR-003a). Give each host its own and keep it across restarts; two live processes
  under one id are refused rather than allowed to displace each other (FR-004b). A host restarted after a crash under
  the same id waits until its earlier incarnation's entry expires, at most the expiry period plus the skew allowance,
  before it joins and becomes ready.
- **The shared database.** Every host's membership connection must reach the same primary, never a read replica
  (FR-032). It is normally the database the EF modules' own features use, so each finalization record and the fleet that
  decides it live side by side. SQLite serves several processes on one machine only, which suits development and tests
  (FR-033); use PostgreSQL, SQL Server or MySQL across machines.
- **Migrations.** The membership table is an EF module of its own (`Cluster.Membership`). Under the default policy the
  first host to start creates it; under `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate` a host refuses to start
  until `dotnet elsa persistence apply` has created it, like any other module.
- **Half a configuration is refused.** Settings under `EntityFrameworkCore` without `Enabled` stop the host at startup,
  naming the key, because a host that meant to join a cluster and silently stayed a cluster of one is exactly the failure
  that looks like success. `Enabled=false` is the explicit way to stay alone.
- **The engine selection is separate.** `Nuplane:Capabilities:ef-provider` still has to be set for the feed-loaded EF
  modules, which Nuplane refuses without it ([Selecting the EF provider engine](#selecting-the-ef-provider-engine)); each
  module's own `Provider` feature setting chooses the engine it binds, and membership's `Provider` the one its table
  uses. They are normally all the same.
- **Share the Data Protection key ring too.** Membership does not share it. A feed-loaded feature that signs users in
  or checks antiforgery tokens protects them with the host's key ring, so hosts behind one load balancer refuse each
  other's cookies unless every host sets `Elsa:DataProtection:EntityFrameworkCore:Enabled=true` (with its `Provider`,
  and the same database), and a certificate under `Elsa:DataProtection:Certificate` to encrypt the keys at rest. A
  clustered host without it logs a warning as it starts and still starts; a cluster whose features sign nobody in has
  nothing to share. See [Data Protection key ring](reference/identity-configuration.md#data-protection-key-ring).

**Rolling a new module version out.** Install the new release of an EF module on one host and it reads the new schema
version, but the version is finalized only once every live host can read it (spec 181). Until then that host writes the
old version, and a feature that needs the new one answers `409` with code `schema-version-not-finalized`, saying it
becomes available once every host can read the version (spec 182); the gate's status names each host that cannot yet
(spec 181, FR-022; `dotnet elsa persistence status` prints it, below). Upgrade the other hosts to the new release, in place or by restarting each on it. In place, drop the
new package into each host's feed: the host reconciles, reloads its shells onto it, and stops counting the previous
release once nothing on it can run that release any more, although the release stays loaded in the load context
Nuplane gave it, which is never unloaded (spec 183, FR-021, amended 2026-09-29; see above). A host reports only the
versions every declaration it has not retired reads, so until then it goes on counting as unable to read the new
version. Once the last host is on the new release, the version finalizes on its own and the feature serves on every
host, with no restart of the hosts that already had it. A host that is stopped leaves the fleet; one that crashes is still counted until its
entry expires.

**Seeing which host holds a version back.** One command, from any machine that can reach the shared database, shows the
family's finalization state, names each host that cannot read a pending version, and lists the cluster's members:

```bash
dotnet elsa persistence status --host /app --provider PostgreSql --modules SamplesNotes,Cluster.Membership \
  --family SamplesNotes --connection-env ELSA_EF_CONNECTION
```

```text
SamplesNotes (SamplesNotes): finalized at 1.0.0; this host reads [1.0.0, 2.0.0]
  2.0.0: pending, held by nothing; waits for every counted member to read it
    waits for: foundation-host-a (reads 1.0.0)

members: 2 in Cluster.Membership, judged at 2026-09-29T12:00:00Z with a skew allowance of 00:00:05
  foundation-host-a: Active, live, last heartbeat 2026-09-29T11:59:58Z
    SamplesNotes: reads 1.0.0
  foundation-host-b: Active, live, last heartbeat 2026-09-29T11:59:58Z
    SamplesNotes: reads 1.0.0, 2.0.0
```

Each `waits for` line is a live host that counts toward the version and does not report reading it, with the versions it
does read; a host whose report the tool cannot interpret is named as reading nothing, which blocks the version just as it
blocks the gate. When no member counted here blocks a pending version the line says exactly that, and no more: it is what
this reading of the table shows, not a promise about the next evaluation. The members list every row of the membership
table, so a host that left (`Left`, not counted) or stopped heartbeating (`expired`, not counted) is visible until the
cleanup period removes it. A live member that reports nothing about a family says `reports nothing, so it is not counted
for it` under that family, and one whose row this build cannot interpret shows the status `unknown`, since it did not
state one this tool reads. `--modules` need not name `Cluster.Membership`: the members are read through the host's own
closure whenever it carries the membership module, on the connection the command uses. Three things to know:

- **Liveness is judged on the machine running the command, with a skew allowance the command prints** (`with a skew
  allowance of 00:00:05` above). It is the `--skew-allowance` you give, else the host's `Elsa:Cluster:Membership:SkewAllowance`
  from its `appsettings.json` and the `appsettings.<environment>.json` overlay of `--environment`, else 5 s. Hosts started with
  `--fast-membership` (2 s) or the setting in their process environment (`Elsa__Cluster__Membership__SkewAllowance`) are not
  visible to the command, so pass `--skew-allowance 00:00:02`: with the default, a host killed a moment ago still reads as
  live and blocks the version for three seconds after the hosts themselves count it expired.
- A host with no membership provider in its closure prints `members: this host only (this host's closure carries no cluster
  membership provider)`; one whose database has not had the `Cluster.Membership` migrations applied prints that the table is
  missing, a cluster of one. Pending versions then keep the plain "waits for every counted member" line, since nothing was
  read to name anyone. A host whose tooling predates this reading prints `members: cluster membership not reported by this
  host's tooling`, which says nothing about whether it runs alone.
- With `--skew-allowance` the command needs a host whose `Elsa.Persistence.EntityFramework` is the release beside it; an
  older host's tooling lists no members and judges no liveness, so the option is simply not sent to it.

The running host reports the same blockers itself: a feature that needs the pending version answers `409` with
`schema-version-not-finalized`, and the Modularity API's Attention items carry the reason (spec 182). There is no separate
status endpoint on `Elsa.Foundation.Host`; the CLI is the operator's view before and beside a running host.

`FoundationHostClusterBootTests` (`tests/essentials/Cluster/EntityFrameworkCore/Tests`) runs this whole sequence on two
built hosts over one SQLite database, and `PostgreSqlFoundationHostClusterTests` (`.../ProviderTests`, in a container) on
one PostgreSQL database.

## Related

- [Docker & compose](docker.md) — `/app/packages` as the mounted directory feed.
- [Docker Hub quickstart](docker-hub-quickstart.md) — running the prebuilt images.
