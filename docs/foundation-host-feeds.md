# Elsa.Foundation.Host package feeds

**Who this is for:** anyone deploying `Elsa.Foundation.Host` (or `Elsa.Workbench`) who needs to choose which Elsa features it loads.

`Elsa.Foundation.Host` compiles in no Elsa feature. Every feature (activities, HTTP, persistence, the Tasks feature) arrives as a NuGet package through a Nuplane **feed** and is discovered by CShells through the optional `CShells.Nuplane` integration. A feed can be a **directory** of `.nupkg` files or a **remote NuGet V3 service index**. Both are declared the same way, under `Nuplane:Setup:Feeds`, and both work in the shipped host, so a deployment that wants a feed URL and a pinned version does not need a mounted folder of package binaries.

This page used to hold the whole reference. It is now split into the pages below; the old section links on this page still work and point to the new location.

## Pages

| Page | Read it when you want to… |
|---|---|
| [Point the host at a feed](host-feeds/feeds.md) | declare a directory or remote feed, write include patterns that match, give a private feed credentials |
| [Pinning packages and their closures](host-feeds/pinning-and-closures.md) | list the full set of packages, pin exact versions, pick the EF Core engine, find a feature's root package, lock packages by hash |
| [Updating packages on a running host](host-feeds/updating-packages.md) | understand hot reload after a package change and reconcile on demand |
| [When feeds and shells fail](host-feeds/failure-modes.md) | diagnose a failure from its symptom |
| [Running several hosts as a cluster](host-feeds/clustering.md) | point several hosts at one database and roll out module versions safely |

## Glossary

| Term | Meaning |
|---|---|
| **Feed** | Where packages come from: a directory of `.nupkg` files or a remote NuGet V3 service index. |
| **Include pattern** | Which package ids (and versions) to take from a feed. Read differently by the two feed shapes. |
| **Root** | A package you ask for by name. |
| **Closure** | A root plus everything it depends on. |
| **Reconcile** | Nuplane's cycle that downloads and loads the desired package set. |
| **Shell** | A CShells unit of configured features; shells reload to pick up new packages. |
| **Cluster membership** | How hosts sharing one database learn about each other. |

> **Tested with:** Nuplane 0.0.11 and CShells 0.0.30 (as referenced on `main`). The include-pattern behaviour table was measured against Nuplane 0.0.9-preview.61 and has not been re-measured since.

## Old section links

These headings keep the anchors other documents used.

### The two feed shapes
Moved to [Point the host at a feed](host-feeds/feeds.md#the-two-feed-shapes).

### Include patterns behave differently on the two shapes
Moved to [Point the host at a feed](host-feeds/feeds.md#include-patterns-behave-differently-on-the-two-shapes).

### Declare the full pinned closure, not just roots
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#declare-the-full-pinned-closure-not-just-roots).

### Why a declared dependency can go missing
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#why-a-declared-dependency-can-go-missing).

### Cluster membership and EF module packages
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#cluster-membership-and-ef-module-packages).

### Generating the closure
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#generating-the-closure).

### Selecting the EF provider engine
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#selecting-the-ef-provider-engine).

### Finding the root package for a feature
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#finding-the-root-package-for-a-feature).

### Pinning and integrity
Moved to [Pinning and closures](host-feeds/pinning-and-closures.md#pinning-and-integrity).

### Feed credentials
Moved to [Point the host at a feed](host-feeds/feeds.md#feed-credentials).

### What fails loudly
Moved to [When feeds and shells fail](host-feeds/failure-modes.md#what-fails-loudly).

### Hot reload after a package change
Moved to [Updating packages](host-feeds/updating-packages.md#hot-reload-after-a-package-change).

### Reconciling on demand
Moved to [Updating packages](host-feeds/updating-packages.md#reconciling-on-demand).

### Running several hosts as a cluster
Moved to [Running several hosts as a cluster](host-feeds/clustering.md).

## Where this lives in the code

- `Essentials/apps/Elsa.Foundation.Host/` — the host, its `appsettings.json` and the module-management endpoints.
- `Nuplane:*` configuration — feeds, capabilities, lock file (Nuplane package).
- `Elsa:Shells:*` configuration — shell reload behaviour (CShells).

## Related

- [Docker & compose](docker.md) — `/app/packages` as the mounted directory feed.
- [Docker Hub quickstart](docker-hub-quickstart.md) — running the prebuilt images.
