# Research: optional Studio administration

## Reuse boundary

Decision: use Elsa.Modularity.Core/Nuplane, not Modularity.Api. The existing reuse/hosting reports settle the
package direction. IFeatureManagementService already owns catalog assembly, complete desired-state validation,
secret masking/restoration and save/refresh/reload sequencing. Implement existing IShellFeatureConfigurationStore,
IShellReloader and IFeatureActivationGuard privately. Delete local snapshots/conflict/manifest/settings algorithms.
PackageManifestCatalogMapper.ReadManifestBytes/Apply are public and preserve current browser field/dependency
semantics, including authoritative runtime-empty dependencies and same-package prefix handling.

## Configuration ownership

Decision: selected Hosting options supply shell/file; null means read-only. Reject mixed root provider ownership
of selected Features, including equal-valued/lower entries. Preserve the actual core provider/registry registration
identity in the existing options snapshot so custom replacements cannot produce false successful file writes.
Recheck within the mutation gate. Shared catalog reads use composed blueprint state even when it is read-only.

CShells0.0.31's ConfigurationShellBlueprint privately retains its source section; ProvidedBlueprint.Manager=null
and type/name/value equality alone do not prove ownership. Do not use reflection. Its builder permits one external
provider; extra AddBlueprintProvider/WithConfigurationProvider contributions fail startup. The core-owned default
factory is rooted in StudioBuilder.Configuration. Supported public provider/registry replacement is detected from
final DI descriptor identity, not reconstructed private framework metadata.

## Directory feeds and optional composition

Decision: choose from effective registered directory feed options after the Nuplane callback, not management JSON.
Resolve relative configuration/feed paths through ContentRoot/UseBasePath. A missing-feed packages fallback must
be registered so uploads are consumed. Freeze selection; JSON feed edits are pending restart. Keep shared
ChangedOrPending/AutoReload/reporting in the addon and distribution assembly selection in Web.

## Maintenance

Decision: reuse PruneAsync with explicit registration, preview and exact supplied confirmation. Preserve full
result facts; unknown protection/incomplete inventory are refusals even with Status=Preview. Null retention keeps
all; N counts inactive unprotected NuGet-version groups across feeds. Legacy count/age policy is incompatible and
requires deliberate selection. Execute replans under ownership; confirmation binds root/epoch/retention rather than
a fixed candidate digest. Enrollment remains operator-controlled and never inferred by a browser request.

Sources: qualified [package-store report](../../docs/reports/studio-package-store-qualification.md) and upstream
[maintenance API/semantics](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/docs/package-store-maintenance.md).

## Bounded protected content

Decision: qualify a narrow stock extracted/immutable install profile under exclusive native admission. Preflight
fresh canonical-path length inside admission, then use only the borrowed guarded PackageContent reader. Limit
asset/metadata sizes and active allocated responses; hold the response permit through actual execution. All
supported size-changing actors require the same root lock, so the size cannot change within this protocol window.
Arbitrary external/in-place mutation is unsupported, as are archive/unenrolled/custom mutable installs.

The public reader lacks a maximumBytes parameter and internally uses Array.MaxLength. Its native handles,
filesystem interfaces and traversal helpers are internal/private; XML documentation is not visibility evidence.
A post-allocation limit alone is insufficient. Exposing internal handles or cloning their identity/traversal code
would break the reuse boundary. An additive upstream bounded reader would be useful independently but is not a
prerequisite for this precisely qualified immutable profile. Keep the limitation explicit and prove actual overlap.

Sources: [PackageContent](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/src/Nuplane/PackageContent.cs),
[PhysicalStoreLock](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/src/Nuplane/Store/Coordination/PhysicalStoreLock.cs),
[native publication](https://github.com/valence-works/nuplane/blob/80f74d02bef70a0410f9dd21064692bc088bb87c/src/Nuplane/Feeds/NativePackageInstallSession.cs).

Three independent read-only research lenses examined shared feature contracts, composition/frontend migration,
and native content/maintenance APIs. No implementation was delegated. No unresolved owner decision remains.

## Implemented composition details

The directory module exposes effective resolver feeds through IOptions<FeedResolutionOptions>, with file URIs,
but does not expose a complete public directory-registration inventory at builder time. Do not construct a
temporary service provider or inspect private marker fields. Register a reserved fallback directory through
AddDirectoryFeed itself, wrap only its desired-source descriptor with a private conditional scoped delegator,
and remove its resolver entry when other directory feeds exist. Both ordinary and borrowed source calls
delegate to the original native-aware source only when fallback is selected. The fallback watcher is disabled: uploads
already reconcile; direct fallback-folder copies require manual reconciliation. Configured feeds keep their watcher
settings. Source-only/cache-only roles still need explicit qualification/refusal; do not imply uploads activate from
a resolver-only directory. Feed selection is frozen from final options. Public callback composition remains the
supported customization path; do not re-register the reserved fallback name afterward.

The root settings, private content reader and existing store interface are explicitly borrowed into shells.
Do not share arbitrary general HTTP factories/options or scoped contributor collections. The reader keeps its
own catalog lookup/read operations serialized so simultaneous HTTP assets do not race one another for native
non-waiting root ownership. Four allocated asset responses are allowed, with32 active/queued requests; ownership
of the response permit continues until result execution finishes. Metadata has a per-file1MiB cap, not an
unbounded-catalog aggregate-memory guarantee.

A standard debug event PackageContentPreflight records package ID, relative path and admitted length just before
guarded reading. It provides useful content diagnostics and a public ILoggerProvider observation seam for the
preflight/maintenance-overlap test without a production friend or a test-only callback API.
