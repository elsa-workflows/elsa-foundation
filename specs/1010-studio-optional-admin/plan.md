# Implementation Plan: Optional Studio administration

**Branch**: `agent/codex/3359-studio-optional-admin` · **Date**: 2026-10-26

**Spec**: [spec.md](spec.md) · **Issue**: [#3359](https://github.com/elsa-workflows/elsa-foundation/issues/3359)

## Summary and technical context

Create Elsa.Studio.Hosting.Nuplane as the accepted optional .NET10 library. Move Studio's HTTP/store adapters
out of Web, replacing duplicate feature catalog/validation/secret logic with Elsa.Modularity.Core/Nuplane.
Reuse released CShells0.0.31/Nuplane0.0.12 discovery, root sharing, admission and maintenance. Preserve stable
HTTP routes/auth/JSON where compatible; deliberately replace unsafe retention/prune semantics and controls.
No new loader, identity, persistence framework, production friend or release train.

Tests use existing whole Studio and Modularity projects, TestServer/Kestrel, real owned temporary files and
real native package stores. Frontend uses the existing React/TypeScript/Vitest workspace. Full candidate
consumer/browser combinations remain #3363–#3365; this leaf proves its own public production composition.

## Constitution check

Pre/post-design checks against framework §§2.1/2.2/2.6/2.8/2.21/2.23/2.25 and Elsa §E5 pass:

- Base Hosting has no optional references; addon references Hosting plus lightweight Modularity.Core/Nuplane
  and qualified infrastructure, never Modularity.Api or backend identity/Attention/cluster implementations.
- Only composition/options are public. DTOs, HTTP/store/asset adapters, validation guard and reloader stay private.
- Existing contracts own catalog, masking, validation, snapshots, revisions and maintenance. No copied catalog
  mapper, local prune algorithm, duplicate revision exception or generic settings framework.
- Subtract copied Web implementations/wiring and obsolete helper-focused tests only when public behavior tests
  and the affected whole projects demonstrate their replacement. Retain theme's private writer until #3360.
- Apply risk-based HTTP/configuration/native-store controls and independent exact-head review. No mandatory
  test-first cadence is declared by the derived constitution. No provisional pattern is adopted.

## Design and research decisions

See [research.md](research.md) for the source-backed decisions and limitations.

### Composition and installed identities

Public StudioNuplaneOptions contains ManagementFile (default nuplane-management.json) and DirectoryFeedName
(default unset). Bind Studio:Hosting:Nuplane first, then callback; resolve trusted file paths against ContentRoot.
Load the optional management JSON before binding Nuplane. Invoke Nuplane.UseBasePath(ContentRoot), configured
directory feeds/loading, then the caller's existing NuplaneBuilder callback. Register admin/maintenance and shared
feature catalog services. Select the actual effective registered directory feeds after callbacks: exactly one by
default, an explicit matching directory feed when named, or a registered packages fallback if none exist. Reject
ambiguous/missing/non-directory selection. Freeze selected feed/path for this process; feed edits require restart.

Move ChangedOrPending/AutoReload and existing failure reporting into the addon. Web's remaining assembly roster
uses public ConfigureShells/WithAssemblies; it does not reconstruct discovery/observer logic. The root owns
Nuplane/admin/catalog/admission/maintenance. Use supported CShells borrowing only for required shell services.

Installed Hosting IOptions<StudioHostingOptions> supplies selected shell/file. Preserve its null-file read-only
meaning when the host replaces the core blueprint-provider or registry registration: defer its snapshot factory
until Build and compare final service descriptors with the original core registrations. This exposes the applied
managed-file boundary through the existing option contract, avoiding a new public ownership abstraction. Root
configuration providers are checked independently for overlays and rechecked inside every mutation. A custom
registry/provider replacement is read-only even when its current values equal the managed file. Multiple external
providers contributed through CShells' builder are already rejected by its one-provider startup contract.

### Shared feature management and atomic writes

Map the existing /modularity/features GET and /apply POST to IFeatureManagementService. Existing shared response
models match the browser shape; preserve additional availability/runtime metadata. Reject null/duplicate/blank
request items before calling the shared service. Register a private mandatory-StudioApi activation guard.

Implement IShellFeatureConfigurationStore privately for the selected file. Load the authoritative blueprint's
composed enabled/configured state for catalog reads, including host-owned/read-only sources; persist only when
source ownership is proved. The file's selected Features section is the sole writable source: any other root
provider with selected-feature entries, whether above/below/equal-valued, makes it read-only. Preserve other
shells/properties. Acquire the shared mutation lease across reread, ownership/revision check, full desired-state
replacement, atomic sibling move and IConfigurationRoot.Reload. Use a process-stable private HMAC key for raw
feature-state revisions (including secrets); reuse shared snapshot/conflict types. Explicit false entries prevent
lower configuration from silently re-enabling disabled features. Unsupported imperative feature configurators
must not be falsely reported as persisted.

The shared service retains masking/restoration/full-state validation and save → refresh → reload ordering. A
private IShellReloader targets only the selected shell; reload failures are explicit and never described as rollback
of an already committed file. Acquire tracked scopes across async access to shell-owned manifest providers.

### Maintenance and retention

Remove Directory.Delete pruning and all modification-time/count-age decisions. Explicitly register
AddNuplanePackageStoreMaintenance and invoke INuplanePackageStoreMaintenanceOperations.PruneAsync.
The Studio retention DTO stores nullable KeepNewestInactiveVersionsPerPackage: null retains everything; zero
is an explicit supported selection. Persist a separate Studio-supported retention section; legacy CleanupPolicy
is reported unsupported until an operator explicitly chooses the new policy. Do not claim automatic cleanup.

Preview is the default. Return the upstream result's protection/inventory/diagnostic/candidate/attempt/outcome
facts; Status=Preview alone does not permit Execute. Execute accepts the exact observed root/epoch/retention
confirmation, never creates a fresh confirmation itself, and uses the selected persisted policy. Changed
identity/epoch/policy refuses; upstream replans protection/candidates under ownership. Enrollment remains an
explicit trusted operator cutover through Nuplane's existing protocol/callback; no HTTP automatic enrollment.

Update the frontend supported-policy model, preserving older Server registry data read-only. Remove synthetic
3-version/30-day defaults and age/mode/protection controls. Show Preview → candidate/protection details → explicit
Execute confirmation. Clear plans on host/policy changes, honor cancel, and report refused/partial/cancelled facts
instead of unconditional success after HTTP200.

### Protected bounded package content

Nuplane's public guarded reader buffers a full file without a byte-budget parameter; native handle helpers are
internal. Do not duplicate those guards or use a physical-path response. Support the stock extracted, immutable
published-install profile only, with all writers participating in native admission. Ordinary unenrolled/archive/
custom mutable profiles refuse protected package reads with a diagnostic; no ordinary-reader fallback.

Resolve package descriptors before admission. Under live exclusive install-path admission and its borrow,
validate canonical relative components, stat fresh length and reject over16MiB (metadata over1MiB per file), then get bytes
only through PackageContent.TryReadFile(borrow). The same normalized relative path is used for stat and read.
Check the result length as a violated-profile guard. Native root ownership excludes every supported publisher,
reconciler, maintenance/recovery writer between stat and read; arbitrary out-of-protocol filesystem modification
is outside this bound. This is coordinated immutability, not an identity-bound limit supplied by Nuplane's API.
Cancellation cannot interrupt its synchronous read; honor it before/after and always release ownership.

Keep at most4 asset payload responses allocated per host, with32 total admitted/queued asset requests; reject further saturation with a retryable response; keep the
permit until response execution completes, including slow/range responses. Release package admission after bytes
are detached. Reuse guarded content for package manifest/nuspec HTTP reads too; replace only the unprotected
package contributor with a private admitted adapter that delegates parsing/catalog projection to the shared mapper.
Retain no-cache/conditional/range/MIME behavior. Existing static-web-asset providers remain their own trusted
source; do not accidentally serve a mutable installed path through a bypass.

Prove the bound under the supported profile with a sparse oversized file and causal preflight control; pause after
preflight and before read, attempt real stock reconcile/confirmed prune and require busy refusal/unchanged bytes.
Prove symlink/hard-link/traversal refusal, no archive/unenrolled fallback, cancellation, range/revalidation and
permit ownership through actual response execution. No garbage-collection or cross-process transaction promise.

## Structure

- Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/: csproj, README/public API files, options/composition,
  private module/feature endpoints, selected-file store/reloader/guard, atomic writer and bounded content adapter.
- Studio/src/essentials/Elsa.Studio.Hosting/: preserve applied managed-file option semantics for replaced sources.
- Studio/src/apps/Elsa.Studio.Web/: remove moved/replaced files/wiring; keep distribution roster and theme writer.
- Web/Client/src/app/modules/: supported retention and explicit prune preview/confirmation UI/models/tests.
- Studio/tests/Elsa.Studio.Tests/: replace moved-internal helper access with public HTTP/composition/native fixtures.
- Relevant solution/lock/project inventory inputs and canonical hosting contract/docs updated with actual ownership.

## Validation and delivery

Run whole affected Studio/Modularity suites and reconcile discovery/execution counts. Run frontend typecheck,
lint/unit/build plus actual targeted browser behavior for changed retention interactions. Run native production
addon tests with real enrollment/install/active+retained-reader protection/physical obsolete deletion. Use causal
controls for coordinated revisions, mandatory guard, asset admission/bounds and preview confirmation.

Run affected locked restores,590+ architecture guards/maps/filter checks, actual candidate pack/inventory and
isolated Web smoke. Public docs state supported profiles/refusals and changed retention semantics. Complete
independent author review on a draft, exact-head required CI and external review. Verify resulting main before
Done; keep full isolated consumer combinations and publisher activation out of this leaf.
