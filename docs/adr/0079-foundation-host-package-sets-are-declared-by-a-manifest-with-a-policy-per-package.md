---
status: accepted
date: 2026-10-03
decision_context: Customer answers of 2026-10-01 on program #2262, settled by the owner on #2256 and #2262 on 2026-10-02 and 2026-10-03; decided by Sipke Schoorstra.
---

# Foundation.Host package sets are declared by a manifest, with a policy per package

Status: accepted (owner decisions 2026-10-02/03). Sipke Schoorstra decided every topic below on
[#2256](https://github.com/elsa-workflows/elsa-foundation/issues/2256) and
[#2262](https://github.com/elsa-workflows/elsa-foundation/issues/2262), after the customer's answers of
2026-10-01. This ADR records those decisions; it does not reopen them. It is the single reference every issue of
program #2262 cites, by decision number (for example "ADR 0079 D6").

## Context

A customer runs the prebuilt `Elsa.Foundation.Host` image, with Studio deployed separately, on Kubernetes with
several pods. Today they build their own ASP.NET Core project that references packages at build time on top of a
base image. That works, and it is not what they prefer. They want:

- their own authenticated feed (Azure Artifacts, with a PAT);
- every package present at start-up, with no download from a feed;
- a versioned package set, with a specific version per package or a default;
- a **policy** per package: **locked**, **hot-reload** or **restart**;
- locked items that Studio users and management-API callers cannot change. Operators change them through the
  manifest and a redeploy.

What the code does today, which is what this ADR has to work with:

- **The host compiles in no Elsa feature.** `Elsa.Foundation.Host` references Nuplane, CShells, Line A's ten
  contract packages and the cluster membership closure, and nothing else
  (`src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj`; its Dockerfile header: every feature "arrives at
  runtime as a .nupkg through the Nuplane directory feed at /app/packages"). So the locked essentials, Workflows
  Design, Workflows Runtime and the persistence packages, are **not in the image**. "Locked" cannot mean "already
  fixed by the image tag". It has to be enforced.
- **Nuplane is pinned at `0.0.11-preview.94`** (`Directory.Packages.props`). A feed is a directory of `.nupkg`
  files or a remote NuGet V3 index, both declared under `Nuplane:Setup:Feeds`
  ([Elsa.Foundation.Host package feeds](../foundation-host-feeds.md), "The two feed shapes"). The host's shipped
  `appsettings.json` declares one directory feed (`local-packages`, path `packages`), with `AutomaticReconciliation` on and a one-minute
  `PollInterval`. Nuplane also models a **desired manifest** (`Nuplane:Convergence:Manifest`, entries of
  `{ Id, Version, SourceHint, Sha512 }`). Its source is always registered but runs only when
  `Manifest:Enabled` is true and `Manifest:Path` is set, and it projects each entry to a request with
  `PackageUpdatePolicy.Exact` (`Nuplane.xml`, `DesiredManifestPackageSource`; the docs' "Pinning and integrity"
  section). The **lock file** (`Nuplane:LockFile`, default path `nuplane.lock.json`) has three modes, `Generate`,
  `Enforce` and `Strict`, and can fail on a hash mismatch (same section). Neither is used by the host today: the
  docs say exact-id include patterns plus the lock file already cover pinning, and call the manifest "available and
  opt-in" rather than a prerequisite.
- **Falling back to a feed is configuration, not code.** Nuplane's `FeedResolutionOptions.RemoteFallbackMode` is
  `Never`, `WhenLocalMisses` or `Always`. On #2262 the owner's session checked that directory feeds act as a local
  cache ahead of remote feeds under `WhenLocalMisses`, that the fallback applies only when the pinned ids are
  listed on the **remote** feed's include patterns, and that a request starting from the directory feed itself
  never falls back. Whether that holds for requests that come from the manifest source is not yet proven; #2253
  proves it.
- **There is no lock concept anywhere.** `FeatureManagementService.ApplyAsync`
  (`src/essentials/Modularity/Nuplane/Services/FeatureManagementService.cs`) accepts any feature id and any package
  state. The only veto is `IFeatureActivationGuard`, which refuses a whole request, and CShells has no such concept
  either.
- **Feature state is per pod.** `JsonShellFeatureConfigurationStore` writes `shells.json` under the pod's content
  root, and its revision is an HMAC keyed per process unless `FeatureManagementOptions.RevisionKey` is set. A
  change made through one pod never reaches the others. `Elsa.Foundation.Host` composes no Modularity API at all
  today; its only management surface is `/_module-management/reconcile` and `/reload`, behind a static API key
  (`ModuleManagementEndpoints`), and the host's own `shells.json` is a bare shell with no features.
- **Hot reload exists, without a policy.** `ShellReloadOnPackagesChanged` refreshes the CShells feature catalog and
  reloads every active shell when a reconciliation cycle completes (the docs' "Hot reload after a package change").
  Nothing decides, per package, whether that reload is wanted.
- **Retired package generations are never unloaded.** Nuplane loads each host-integrated package graph into a load
  context it never unloads; `NuplanePackageGenerations` only marks the old release replaced, then retired (the
  docs, "Cluster membership and EF module packages").
- **The artifact reconciler only activates.** `WorkflowArtifactReconcilerStartupTask`
  (`src/essentials/Workflows/Runtime/Reconciliation`, its README) activates the artifacts a host ships. When an
  artifact leaves the mounted set its slot stays active and its triggers keep serving.
- **Floating ranges misresolve.** Nuplane's `NuGetVersionRangeEvaluator` ignores the floating part of a range such
  as `1.4.*` and takes the highest version at or above `1.4.0`, 2.x included. This was read from the code at the
  pinned commit and is confirmed by a test in #2363.

## Decision

Every decision below is the owner's, with the issue that carries it out. Where a detail is left to an issue, it is
listed under "Left to the owning issues", never decided silently.

Index: D1 delivery (#2253, #2261); D2 customer input (#2257, #2361); D3 runtime manifest (#2257, #2258);
D4 policies (#2258, #2259); D5 locks (#2259, #2354, #2254); D6 features (#2360); D7 removal (#2359, #2323);
D8 memory (#2362); D9 floating versions (#2363); D10 test feed (#2255); D11 Studio (#2260).

### D1 — Delivery is a versioned package image, copied into an `emptyDir` per pod ([#2253](https://github.com/elsa-workflows/elsa-foundation/issues/2253), [#2261](https://github.com/elsa-workflows/elsa-foundation/issues/2261))

CI builds the manifest's packages into a small OCI image tagged with the manifest version, for example
`acme-elsa-packages:2026.10.1`. Each pod's init container copies the `.nupkg` files into its own `emptyDir`, and
Nuplane reads that folder as a directory feed. Start-up involves no feed and no shared storage; the version is the
image tag. A package missing from the image falls back to the customer's authenticated feed. The PAT is in
`nuget.config` at image-build time only; the host's own feed credential is a `secrets://` reference, never the
secret (#2255; the docs' "Feed credentials").

The fallback matters more than its name suggests. The init container runs once, when the pod starts, so a
**hot-reload version that arrives after the pod started cannot be in its `emptyDir`**. It comes from the feed
through that same fallback, which relies on #2253 proving it for manifest-sourced requests (see the Context and
"Left to the owning issues"). A restart-policy or locked change arrives with a new image tag and a rolled pod.

### D2 — The customer maintains a packages-only csproj ([#2257](https://github.com/elsa-workflows/elsa-foundation/issues/2257), [#2361](https://github.com/elsa-workflows/elsa-foundation/issues/2361))

The customer's input is a csproj of package references with **no `Program.cs`**. It uses **Central Package
Management** and imports an **Elsa release versions package** for default versions (#2361); a `VersionOverride`
pins a specific version. It is the manifest input: exact versions, a lock file, and Renovate or Dependabot bumps.

Each package's **policy** is metadata on its `PackageReference`:

```xml
<PackageReference Include="Elsa.Activities.Bpmn" ElsaPolicy="hot-reload" />
<PackageReference Include="Elsa.Workflows.Runtime" ElsaPolicy="locked" />
```

`ElsaPolicy` takes `locked`, `hot-reload` or `restart`. `ElsaAlwaysEnabledFeatures` names features the manifest
keeps on (D5); how several ids are written in it is #2257's to fix. The snippet shows the shape only: the metadata
names and the three policy values are the decision. An **Elsa-shipped MSBuild target** writes the runtime manifest
in CI (#2257).

### D3 — The runtime manifest is a ConfigMap, converged through Nuplane ([#2257](https://github.com/elsa-workflows/elsa-foundation/issues/2257), [#2258](https://github.com/elsa-workflows/elsa-foundation/issues/2258))

CI publishes the manifest as a **ConfigMap** holding exact pins and policies, using **Nuplane's convergence
manifest and lock file** rather than a format of Elsa's own, so pinning and integrity stay Nuplane's. Pods re-read
the manifest every cycle (the reconciliation cycle's poll interval), and what a change does depends on the package's
policy (D4). The manifest's name and version are logged at start-up and reported by the host's status endpoint
(#2257's acceptance criteria).

### D4 — Policy is per package, with three outcomes ([#2258](https://github.com/elsa-workflows/elsa-foundation/issues/2258), [#2259](https://github.com/elsa-workflows/elsa-foundation/issues/2259))

| Policy | A new version in the manifest | Through the APIs |
|---|---|---|
| `hot-reload` | Applied live from the feed, with no restart | Its features are managed through the API (D6) |
| `restart` | Staged and reported as **pending restart**; never applied live | Its features are managed through the API (D6) |
| `locked` | Pinned; only a manifest change and a redeploy move it | Refused on every API |

Hot-reload candidates for the first scope and the demo are BPMN (`Elsa.Activities.Bpmn`), Liquid
(`Elsa.Expressions.Liquid`) and Agents. Email is out of the first scope because it does not exist in Foundation
yet. The locked set the customer named is Workflows Design, Workflows Runtime and the persistence packages. Theme
Builder is not a candidate: it is a Studio page compiled into the Studio client, so it changes with a Studio
release and the manifest does not govern it.

### D5 — Locks are enforced by the host now ([#2259](https://github.com/elsa-workflows/elsa-foundation/issues/2259), [#2354](https://github.com/elsa-workflows/elsa-foundation/issues/2354), [#2254](https://github.com/elsa-workflows/elsa-foundation/issues/2254))

Because the locked essentials are not in the image (Context), "locked" is enforced as three things: the version is
**pinned by the manifest**, the package is **never upgraded or removed through the API or Studio**, and its
features are **always on**. The host refuses with a `409` that names the lock, on the Modularity API and on the host
management endpoints (#2259).

A **compiled-in essentials flavour**, for example `WorkflowEssentials.Host`, which compiles Workflows Design,
Workflows Runtime and the EF modules in as host-provided packages, is **decided after #2254 measures start-up**.
It would remove the API check for those packages and start as fast as the customer's current csproj, at the cost of
versions that follow the image tag instead of the manifest. Features inside them would still need the lock. Any such
flavour is built on the new packable host library (#2354), **never by copying `Elsa.Foundation.Host`'s
composition**.

### D6 — Features are a writable cluster-wide API, and the manifest wins ([#2360](https://github.com/elsa-workflows/elsa-foundation/issues/2360))

A **writable cluster-wide feature API** keeps the desired feature state in the **shared database**, and every pod
converges to it. Authority is split:

- **The manifest alone** owns packages, versions, policies and the always-enabled features of locked packages.
  The API refuses any change to them.
- **The API** owns enabling and disabling the features of **unlocked** packages, cluster-wide.
- **The manifest wins.** When a package leaves the manifest, its feature entries become **inert**: kept in the
  database, ignored, and applied again if the package returns.

### D7 — A package that leaves the manifest stops its workflows ([#2359](https://github.com/elsa-workflows/elsa-foundation/issues/2359), [#2323](https://github.com/elsa-workflows/elsa-foundation/issues/2323))

The mounted set is the desired state. The artifact reconciler deactivates a slot it owns whose artifact is no longer
mounted, on the next pass, and only after a complete, successful read of the mounted set (#2359). An operator command
covers a slot Publishing does not own (#2323). #2359 and #2323 land before wave 3 of the program, which depends on
removal behaving.

### D8 — Retired generations are unloaded upstream ([#2362](https://github.com/elsa-workflows/elsa-foundation/issues/2362))

Hot reload becomes a first-class policy, so memory must not grow with every update. The owner's decision is to fix
it in Nuplane, with collectible load contexts for package generations. If host-integrated graphs cannot be unloaded
reliably, the fallback is an Elsa **Attention item** raised when a pod holds more than N retired generations, with
documented behaviour and a recommendation of periodic rolling restarts.

### D9 — Floating versions are fixed upstream, and never relied on ([#2363](https://github.com/elsa-workflows/elsa-foundation/issues/2363))

Nuplane resolves floating ranges with NuGet's floating semantics (the highest version within the float). **Hot
reload never relies on floating versions:** the manifest carries exact pins (D3), so a floating range has no role in
what a pod applies. #2363 also corrects the bare-id comment in Foundation.Host's `appsettings.json` and the docs.

### D10 — One local authenticated test feed, used everywhere ([#2255](https://github.com/elsa-workflows/elsa-foundation/issues/2255))

Credentials are exercised against a **local authenticated NuGet test feed**, in tests, in the program's fixtures
and in the demo. Nuplane supports HTTP Basic auth only, so the local feed exercises the same path Azure Artifacts
uses with a PAT.

### D11 — Studio shows, and does not propose ([#2260](https://github.com/elsa-workflows/elsa-foundation/issues/2260))

Studio shows locked and pending packages and features read-only. Proposing manifest changes from Studio is out of
scope. The work lives in `elsa-foundation-studio`.

## Left to the owning issues

These are not decided here. Each is named so no issue assumes the ADR settled it.

| Open detail | Owner |
|---|---|
| Whether an omitted `ElsaPolicy` is a build error or has a default, and whether `ElsaAlwaysEnabledFeatures` is allowed on a non-locked package | #2257, #2259 |
| The manifest file's schema, name and version fields; whether Nuplane's manifest `SchemaVersion` check is used | #2257 |
| Lock-file mode (`Enforce` or `Strict`) and where the lock file is mounted; behaviour when the ConfigMap is briefly unreadable (Nuplane's `DesiredSourceSnapshotCache` keeps the last snapshot when a source is temporarily unavailable) | #2257 |
| Whether manifest-sourced requests fall back to the feed when the directory misses, and the include patterns that makes work | #2253 |
| How a `restart` package is withheld from a running host while a cycle applies the rest, so that "pending restart" is a fact rather than a label | #2258 |
| The role of the per-pod `shells.json` once the database holds desired features, and how a pod orders reading that state against activating its shells | #2360 |
| Whether `Elsa.Foundation.Host` may take the EF store and feature API that #2360 needs, given ADR 0076's closure rule (below) | #2360, #2354 |
| The measurement that decides the compiled-in flavour | #2254 |

## Considered options

- **One shared ReadWriteMany volume filled by an init container.** Rejected. Several pods each running an init
  container that writes into one shared volume would race each other, and many clusters offer no RWX storage class.
  A per-pod `emptyDir` fed from an image has no shared writer.
- **Policy in a side file next to the csproj.** Rejected. A package's version and its policy would live in two
  files kept in step by hand, while the csproj is already the file that Central Package Management, the lock file
  and the dependency bots work on. Metadata on the `PackageReference` keeps one package's facts in one place.
- **Rolling restart only, with no hot reload.** Rejected. The customer asked for new versions of custom packages and
  of Elsa packages such as Agents and Liquid to apply without a container restart. `restart` stays as a policy for
  packages that should not move live, and `locked` for those that should not move at all.
- **Floating version ranges in the manifest.** Rejected. They are not reproducible, and Nuplane resolves them wrongly
  today (#2363). Even once fixed upstream, the manifest carries exact pins.
- **A manifest-only, read-only feature API.** Rejected. Toggling a feature of an unlocked package would need a
  manifest change and a redeploy, and feature state would stay per pod, because `shells.json` is a file under each
  pod's content root. A writable API over the shared database is what lets one change reach every pod.
- **Testing against a real Azure Artifacts feed in CI.** Rejected. It needs a real PAT in CI and a network
  dependency. Nuplane speaks Basic auth only, so a local authenticated feed covers the same code path.
- **A compiled-in essentials image now.** Deferred, not rejected: it is decided after #2254 measures start-up. It
  trades the API lock check for versions that follow the image tag, and it still needs the feature lock.
- **A hand-written manifest file in the customer's repository.** The first planning answer (2026-10-01). Superseded
  by D2: the csproj is the customer's input and CI generates the runtime manifest from it, so there is one source
  of truth for versions.
- **Per-pod `shells.json` as the feature store.** Rejected for the reason above: a change on one pod never reaches
  the others.

## Consequences

- **Locked versions follow the manifest, not the image,** until a compiled-in flavour exists. Every locked package
  therefore depends on the package image or on the feed at pod start.
- **A hot-reload bump needs the feed to be reachable at runtime,** because the init container runs once (D1). The
  credential must stay valid for the pod's life. A PAT that expires, or one a rotated `secrets://env/…` value
  cannot reach without a restart, stops hot reload and nothing else (#2255).
- **The ConfigMap must be mounted so that updates reach the pod.** Kubernetes does not refresh a ConfigMap mounted
  through `subPath`, and the kubelet refreshes others on its own schedule. Pods that re-read every cycle see a
  change only after that (#2257, #2261).
- **Schema-changing packages keep ADR 0077's and ADR 0078's rules.** A locked persistence package moves with a
  redeploy; the migration is applied out of process (`dotnet elsa persistence apply`), and the cluster version gate
  of ADR 0078 covers pods activating at their own pace. The manifest does not change that.
- **ADR 0076's closure rule is under pressure from D6.** The host's csproj says its EF closure exists for cluster
  membership and the Data Protection key store, "nothing else", and `EfCoreDependencyGuardTests` holds the host to
  it. A database-backed feature store and API need either the host library (#2354) or an amendment of ADR 0076, and
  #2360 owns saying which.
- **Docs that later issues must update, none edited here.**
  [`docs/foundation-host-feeds.md`](../foundation-host-feeds.md): "Pinning and integrity", which still calls the
  manifest opt-in and not a prerequisite (#2257); the pre-seeded-directory and feed-fallback behaviour (#2253); "Feed
  credentials" for the local test feed (#2255); "Hot reload after a package change" for policy and pending restart
  (#2258) and for retired generations (#2362); the bare-id and "Include patterns" statements (#2363). The
  Reconciliation README and the Runtime `EXTENSION_POINTS.md` for slot deactivation (#2359, #2323), and the
  Modularity `EXTENSION_POINTS.md` for locks and the feature API (#2259, #2360).
- **Timing.** Code starts now. Kubernetes and image-build work (#2254 and #2261) waits until after the customer
  demo on 2026-10-05.

## Linked decisions

- [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) — what the host may carry, and D9's activation
  guards that lock enforcement extends
- [ADR 0077](0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) — runtime installation
  and the refusal of a schema-changing module until its migration is applied
- [ADR 0078](0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md) — the
  cluster membership and version gate several pods rely on
- [ADR 0067](0067-package-versioning-uses-two-lines-with-computed-patch.md) — the versioning the release versions
  package (#2361) publishes
- [ADR 0043](0043-publication-slots-define-start-authority.md) — the slots that D7 deactivates; its amendment is
  accepted by #2353, not here
- [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md) — the worked reference this ADR leaves to later
  issues to update
- Issues: program #2262; #2256 (this ADR); #2253 (delivery), #2254 (start-up measurement), #2255 (feed credentials and
  test feed), #2257 (manifest), #2258 (policy and pending restart), #2259 (lock enforcement), #2260 (Studio), #2261
  (demo), #2323 (operator command), #2354 (host library), #2359 (slot deactivation), #2360 (feature API), #2361
  (release versions package), #2362 (unloading), #2363 (floating versions)
