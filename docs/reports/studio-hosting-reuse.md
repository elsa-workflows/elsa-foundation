# Studio hosting reuse decision

Decision and bounded compatibility evidence for [spike #3351](https://github.com/elsa-workflows/elsa-foundation/issues/3351),
under [Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340). Source baseline:
`cf0e81fec384012088013808c12e6775379a3282`. This report selects the reuse route; it does not claim extraction is delivered.

## Selected route

Adopt the existing **CShells.Nuplane** adapter and the lightweight **Elsa.Modularity.Core / Elsa.Modularity.Nuplane**
catalog/application services in the optional Studio administration integration. Keep Studio HTTP projection,
browser authorization and file-storage policy as adapters. Do not pull in **Elsa.Modularity.Api**, whose backend
identity, Attention and cluster dependencies do not belong in the Studio host. No new generic modularity framework
or contracts package is needed.

The standard host remains independent of Nuplane administration. Its own authentication, runtime configuration,
manifest routing and browser assets remain Studio responsibilities. Package names/asset ownership are settled
by [#3352](https://github.com/elsa-workflows/elsa-foundation/issues/3352) and
[#3355](https://github.com/elsa-workflows/elsa-foundation/issues/3355), not by this adapter probe.

## Reuse matrix

Removed wrapper links below are pinned to the pre-adoption source snapshot; #3356 replaces them with the shared adapter.

| Current responsibility | Disposition | Reason and implementation requirement |
|---|---|---|
| [StudioNuplaneAssemblyProvider](https://github.com/elsa-workflows/elsa-foundation/blob/d55f5f5229ab4aaeaa728a85f6d41d9050af0a71/Studio/src/apps/Elsa.Studio.Web/StudioNuplaneAssemblyProvider.cs) | Replace with `CShells.Nuplane` | The released provider reads Nuplane's package-aware assembly catalog. Register after Nuplane autoload; also register the host's explicitly selected feature assemblies. |
| [StudioNuplaneShellReloadObserver](https://github.com/elsa-workflows/elsa-foundation/blob/d55f5f5229ab4aaeaa728a85f6d41d9050af0a71/Studio/src/apps/Elsa.Studio.Web/StudioNuplaneShellReloadObserver.cs) | Replace with the adapter coordinator | Use `ChangedOrPending`, automatic reload enabled, and `OnReloadResults` for Studio-specific reporting. Remove the local observer and its detached task/semaphore. |
| [StudioRuntimeFeatureCatalogRefresher](https://github.com/elsa-workflows/elsa-foundation/blob/d55f5f5229ab4aaeaa728a85f6d41d9050af0a71/Studio/src/apps/Elsa.Studio.Web/StudioRuntimeFeatureCatalogRefresher.cs) | Replace with existing catalog services | Use the shared refresher for apply and runtime contributor for catalog reads; avoid another public refresh abstraction. The newer catalog interface returns its snapshot abstraction on refresh, so update Studio's test doubles as part of dependency convergence. |
| [ElsaFeatureManagementApi](../../Studio/src/apps/Elsa.Studio.Web/ElsaFeatureManagementApi.cs) | Adapt HTTP; reuse application services | `AddNuplaneFeatureCatalog` already composes runtime/manifest contributors and `IFeatureManagementService`. Preserve Studio routes, authorization and browser DTOs through a small mapping adapter. |
| [StudioShellFeatureConfigurationStore](../../Studio/src/apps/Elsa.Studio.Web/StudioShellFeatureConfigurationStore.cs) | Adapt to the existing `IShellFeatureConfigurationStore` | Keep a Studio file provider with configured shell/path, atomic writes, one read-modify-write gate, revision conflicts and deterministic reload. Do not substitute the backend file store as-is. |
| [Backend JsonShellFeatureConfigurationStore](../../Essentials/src/Modularity/Api/Services/JsonShellFeatureConfigurationStore.cs) | Retain in its current owner | It supports a configured path and current `ShellSettings`, and keyed revisions, but writes directly and does not provide Studio's atomic-write/concurrency/reload behavior. Reuse its contracts and keyed revision policy, not the implementation wholesale. |
| [Elsa.Modularity.Core](../../Essentials/src/Modularity/Core/Elsa.Modularity.Core.csproj) | Reuse contracts/models | No backend project/package dependencies. Existing configuration-store, catalog, reload and apply contracts cover the required seams. |
| [Elsa.Modularity.Nuplane](../../Essentials/src/Modularity/Nuplane/Elsa.Modularity.Nuplane.csproj) | Reuse catalog/application implementations | Depends on Core, CShells, Nuplane admin and package-manifest contracts, not backend identity or persistence. Supply Studio's store and `IShellReloader` implementations. |
| [Elsa.Modularity.Api](../../Essentials/src/Modularity/Api/Elsa.Modularity.Api.csproj) | Keep out of Studio | Its dependency closure includes backend identity, cluster, Attention and backend API infrastructure. Studio permissions remain based on the browser's backend session and the existing Studio bridge seam. |
| [ElsaModuleManagementApi](../../Studio/src/apps/Elsa.Studio.Web/ElsaModuleManagementApi.cs), package assets | Retain Studio adapters; qualify framework operations | Their browser DTOs, uploads and file-backed feed administration are Studio-owned. Direct directory pruning must be replaced by a qualified supported operation or fail closed; #3353 owns that proof. |

## Behavioral differences to retain or adopt explicitly

- The shared adapter defers freshness when no shell is active, consumes it before a later shell build,
  refreshes before reload, and retains failed/deferred work. Its documented post-loading callback respects
  coordinated Nuplane root/store ownership. Studio's local observer only reacts to nonempty change sets and
  schedules a detached refresh/reload task. Select the shared behavior; do not retain a second observer.
- Automatic reload is **off** in the generic adapter default. Studio must explicitly enable it. Foundation.Host
  refreshes on every eligible completion; Studio should use `ChangedOrPending` to match its change-driven policy
  while gaining pending-work retries. Host-specific error reporting uses the existing options callback, as
  [Foundation.Host already demonstrates](../../Essentials/apps/Elsa.Foundation.Host/Shells/NuplaneIntegrationOptionsSetup.cs).
- The existing [feature application service](../../Essentials/src/Modularity/Nuplane/Services/FeatureManagementService.cs)
  masks secret values/defaults, restores unchanged secret placeholders and validates complete desired state before
  saving. Studio currently projects saved configuration directly. Adopt the shared secret handling and validation;
  add browser-contract regressions for full-state apply, unknown/omitted enabled features, placeholder round-trips
  and safe errors. Do not preserve silent feature loss or expose secrets merely for legacy DTO compatibility.
- A reusable file store must preserve Studio's concurrency and atomic-write guarantees while using keyed revisions
  when the revision covers secret values, as the existing backend store explains. Configuration overrides must not
  silently redirect management writes to a different shell/file than the selected hosting configuration.
- At qualification, Studio used CShells `0.0.29-preview.144`, Nuplane `0.0.8` and the older
  `Elsa.Platform.PackageManifests` contract. #3356 aligns it with Foundation's `0.0.31`, `0.0.12` and
  `Elsa.Specifications.PackageManifests` preview.8, including the source-only generator and consumed wire shape.
  The [implementation qualification](../../specs/1007-studio-shared-loader/quickstart.md) exercises the actual
  Studio adapter profile with real directory-feed packages and round-trips a generated manifest through the
  selected contract. Compatibility is verified through behavior, not package names or cache presence.

## Executed compatibility proof

The [reproducible probe](../../tools/studio/HostingReuse.Probe/README.md) references the actual released
`CShells.Nuplane` package and real CShells catalog/registry. Two fixture assemblies provide different services
under the same feature ID. Four groups passed:

1. A cold completion did no catalog read/reload; first activation discovered the package feature.
2. Updating the scripted package assembly refreshed discovery before reload completion, replaced the serving
   generation and resolved the second assembly's service; an outstanding first-generation scope remained usable.
3. Removal-to-empty removed discovery and the replacement shell's registration, preserving the retained scope.
4. An unchanged empty completion did no refresh/reload work.

The baseline exited 0. Disabling automatic reload exited 1 at the required generation-replacement assertion.
Restoring the normal profile exited 0. The adapter reports source commit `b7611aadb49789d3b282749df18ffe68d0eacec5`;
Nuplane abstractions report `80f74d02bef70a0410f9dd21064692bc088bb87c`. Assembly informational versions are not
package versions; the latter are pinned by central versions and the probe lock files.

This proof deliberately scripts the Nuplane catalog and completion events. It does not qualify real package
resolution/loading, collectible unload, physical pruning, protected-store enrollment or browser serving. Reuse
the existing [upstream qualification record](../program-goals/modular-hosting-upstream-delivery.md) as provenance,
not as current Studio acceptance. #3353 and #3363/#3364 own the remaining store and end-to-end consumer checks.

## Implementation handoff

[#3356](https://github.com/elsa-workflows/elsa-foundation/issues/3356) aligns dependencies and replaces local loader
glue with the selected adapter profile. [#3359](https://github.com/elsa-workflows/elsa-foundation/issues/3359) reuses
the existing modularity contracts/services behind Studio's HTTP/store adapters and implements the qualified
administration operations. [#3355](https://github.com/elsa-workflows/elsa-foundation/issues/3355) captures these
choices in the full class/package/public-API contract after the asset proof. Existing #2500/#1959 work remains
separately owned and parked; this spike creates no competing upstream backlog or new release.
