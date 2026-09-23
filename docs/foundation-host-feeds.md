# Elsa.Foundation.Host package feeds

`Elsa.Foundation.Host` compiles in no Elsa feature. Every feature — activities, HTTP, persistence,
the Tasks feature — arrives as a NuGet package through a Nuplane feed and is discovered by CShells
via `NuplaneAssemblyProvider`. This page is the worked reference for pointing the host at a feed.

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
the host's copy, whatever version the feed package was built against, so each host's
`appsettings.json` also declares, under `Nuplane:HostProvidedPackages`, the package of every assembly it
lists in `Nuplane:Loading:SharedAssemblies` — restating Nuplane's two defaults, because a configured list
replaces them. `HostProvidedPackagesGuardTests` (`tests/essentials/Architecture`) fails the build when a
shared assembly's package is not declared. The declaration is what makes the version check apply: a feed
package that needs a newer version of a declared package than the host's `deps.json` carries is refused
at reconciliation (see [What fails loudly](#what-fails-loudly)) instead of being bound to the older copy
and failing later at a missing member.

Neither case is caught by the version range or the lock file, because nothing was resolved to check.

The checks above apply only to **dependencies**, never to roots — which is why naming a package
explicitly always acquires it, and why the full-closure list is the reliable shape. It also makes a
deployment reproducible.

Note that `Microsoft.Extensions.*` is no longer skipped by a fixed prefix rule either: since
`0.0.11-preview.91` it is acquired from the feed like any other dependency unless it is also present in
the host's own `deps.json` (rule 2) or the host explicitly adds the `Microsoft.Extensions.` prefix to
`Nuplane:HostProvidedPackages` (rule 1). `Microsoft.EntityFrameworkCore.*` and `Npgsql.*` were never
covered by the old prefix rule either way, so EF and its provider have always arrived through the feed
normally.

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
   `NuGet.*` families plus `FastEndpoints`, `Newtonsoft.Json` and `JetBrains.Annotations` — but check,
   do not assume.

   **Then put every `Elsa.*` id back.** `Elsa.Api.AspNetCore` is in the host's `deps.json`, so a drop-list
   derived mechanically would prune the one Elsa package that is in there — the rule 2 shape again, and it
   surfaces as a `FeatureNotFoundException` naming a *feature* rather than the missing package.

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

## Related

- [Docker & compose](docker.md) — `/app/packages` as the mounted directory feed.
- [Docker Hub quickstart](docker-hub-quickstart.md) — running the prebuilt images.
