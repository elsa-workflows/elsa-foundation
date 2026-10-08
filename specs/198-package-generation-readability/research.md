# Research: Protect Package Generations

## Decisions

### Candidate accounting uses the upstream build lease

**Decision**: Begin an unresolved pin before CShells reads its feature catalog; replace it with the exact selected feature snapshot before feature construction; release it only after provider teardown is confirmed, or after a pre-provider build unwind is confirmed.

**Rationale**: The existing initializer begins after catalog selection and can miss an in-flight candidate that already selected an older snapshot. The CShells participant and exact-snapshot boundary are the accepted upstream seam. Unknown selection is conservatively pinned; known selection counts only feature assemblies and their non-default load contexts, including disabled features and sibling assemblies sharing a context.

**Alternatives considered**: Keep the initializer-only model (leaves the preselection gap); pin every assembly found in the process (overstates FR-021 and can block retirement for unrelated assemblies); release on activation or early lifecycle state (does not prove provider teardown).

### Keep the shared report source separate from the participant adapter

**Decision**: Preserve the `NuplanePackageGenerations`/`ISupersededAssemblySource` instance as nondisposable and nonparticipant. Register a separate root-only, public sealed build-lease adapter; let it own root catalog notification cleanup and stop/cancel the optional custom-catalog watch.

**Rationale**: CShells removes participant descriptors from child shell providers. Implementing the participant interface on the shared source could therefore remove the source from the very providers that need it. A separate adapter keeps root lifecycle ownership distinct from copied query-service identity and meets the repository's §2.23.3 visibility/testing rule.

**Alternatives considered**: Put the participant on the shared source (descriptor filtering breaks child copies); make source and adapter both disposable (double ownership); add a new Foundation public contract (unneeded because the upstream participant/commit contracts already exist).

### Preserve lifecycle compatibility only as an unleased fallback

**Decision**: Keep the public `BindTo`/`IShellLifecycleSubscriber` facade. When a lifecycle notification identifies a shell descriptor that has never been build-owned, retain a conservative pin from that first notification through confirmed drain. A build-owned descriptor remains marked as lease-owned through its terminal lifecycle notification, even after its lease leaves the live-pin set, so a late callback cannot create a zombie fallback pin. Lifecycle callbacks never release a lease; only its completion does.

Normal lifecycle callbacks may observe a drain failure for diagnostics, preserving the existing warning objective. Observation does not grant release authority: published leases are disposed by CShells only after full provider teardown and a successful terminal notification. An unpublished candidate can unwind without that notification.

**Rationale**: An existing test verifies tracking a shell whose host-local initializer was removed. The refactor may change test wiring, but §2.21.1 requires preserving that test's subject and objective. The descriptor-scoped fallback preserves the boundary without creating two release authorities for normal generations.

**Alternatives considered**: Drop lifecycle tracking and remove the existing test (not approved); keep lifecycle notifications as a second release path for leased generations (can release before confirmed provider teardown); delete lease-owned identity when its live pin is released (a late notification can recreate a fallback pin); use shell-object identity alone (does not match the immutable generation identity used by build callbacks).

### The host container explicitly owns one canonical adapter

**Decision**: Register one public sealed adapter singleton and map CShells' participant service to that same instance. The root `IShellLifecycleSubscriber` factory first resolves the canonical adapter, then returns the existing generation source's `BindTo(root)` facade. The adapter singleton owns and stops its commit subscription and custom-catalog watch when the root provider is disposed. Factory aliases can cause Microsoft DI to dispose the same instance more than once, so stop/unsubscription must be idempotent. `BindTo` does not resolve the adapter; direct legacy `BindTo` use cannot start a root subscription.

**Rationale**: CShells resolves root lifecycle subscribers and build participants through distinct contracts; existing `Bound(host)` tests resolve the lifecycle facade directly. Resolving the same singleton from the lifecycle factory establishes one owner for both real host discovery and tests without constructing the adapter recursively through `BindTo` or adding a second disposable source owner.

**Alternatives considered**: Assume participant discovery alone starts the tracker (the existing test binder would miss root cleanup); create the adapter from `BindTo` (constructor/factory cycle); register separate participant instances (duplicated subscription and ownership); let direct source binding own a watcher (its lifetime is not bounded by registered-root disposal).

### Use a short pin gate and asynchronous coalesced evaluation

**Decision**: Await package and catalog reads outside the tracking gate. Under the short gate, copy or update the live pin set and exact selected contexts. Queue report evaluation after Begin, selection, release, and committed catalog change. Never await membership publication or invoke callback/user code while holding the gate.

**Rationale**: The report must see a consistent snapshot of all concurrent candidate and active pins, but catalog calls and publication are asynchronous external work. A short gate prevents a race between pin changes and snapshotting without holding locks across those operations. Coalescing handles reentrant changes while an evaluation is in flight.

**Alternatives considered**: Enumerate a concurrent dictionary without a gate (weak snapshot can miss a live pin); perform awaited reads under the gate (blocks unrelated builds and creates deadlock risk); publish inline from lease disposal (blocks provider teardown and runs membership work under lifecycle locks).

### Commit notifications drive both-direction retirement-set comparison

**Decision**: Subscribe to optional committed-snapshot notifications before initialization and reconcile after subscription. Notification handlers only enqueue. Compare the entire retired set: publish after teardown when declarations become newly retired; publish restored readability constraints after commit when a previously retired assembly is reintroduced; do nothing when the set is unchanged. Keep a conservative watcher for custom catalogs without commit notification support and stop it with the root adapter.

**Rationale**: A commit can remove or restore assemblies in the report. Comparing only newly retired assemblies misses rollback/reselection. Commit notification is stronger than polling for the stock catalog; the compatibility fallback must remain conservative.

**Alternatives considered**: Poll every catalog (adds routine delay and needless work); only publish on set growth (misses reintroduction); trust a custom catalog without commit evidence (can retire prematurely); synchronously publish inside the notification (violates callback and report timing constraints).

### Qualification uses preview privately, stable packages at final adoption

**Decision**: Use actual CShells `.166` and Nuplane `.99` packages only in a private preflight copy with private pins/locks. Final acceptance refreshes D7/release-plan versions and exercises the actual Foundation host with published upstream NuGet references.

**Rationale**: The committed Foundation pins (`.159`/`.94`) predate the required upstream contracts. A private preview compile can validate integration without prematurely changing pins. Foundation source-project references to its own projects remain valid; substituting upstream source projects for published packages does not satisfy final acceptance.

**Alternatives considered**: Claim a current-pin compile proves the new contract (the API is absent); commit preview pins or locks as adoption (not a final stable-package proof); freeze intended stable versions without checking D7 (release versions are refreshed before qualification).

## Evidence Sources

- [Spec 198](spec.md), especially FR-002–FR-010 and SC-001–SC-004.
- [Canonical Spec 183](../183-cluster-membership/spec.md), FR-021 readability and retirement meanings.
- `src/essentials/Cluster/Readability/NuplanePackageGenerations.cs` and `EfSchemaReadabilityServiceCollectionExtensions.cs`, current source and registration behavior.
- `src/essentials/Persistence/Schema/ISupersededAssemblySource.cs`, existing query contract.
- `tests/essentials/Cluster/Readability/Tests/SupersededPackageGenerationTests.cs` and `SupersededPackageGenerationShellTests.cs`, current subject/objective and exact report-count expectations.
- `.specify/memory/constitution-framework.md`, §§2.6, 2.21.1, 2.23.2–2.23.4; `.specify/memory/constitution.md`, dependency and Elsa packaging rules.
- [D7](../../docs/plans/modular-hosting-upstream/decisions.md) and the [release plan](../../docs/reports/modular-hosting-release-plan.md), preview/stable qualification boundary.
- [CShells API change at commit `2d81b210`](https://github.com/valence-works/cshells/commit/2d81b210), with canonical [build-participant issue #147](https://github.com/valence-works/cshells/issues/147), [catalog-commit issue #146](https://github.com/valence-works/cshells/issues/146), and [optional integration issue #156](https://github.com/valence-works/cshells/issues/156).
- The [modular-hosting upstream delivery program](../../docs/program-goals/modular-hosting-upstream-delivery.md) and Foundation [issue #2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164) are the durable ownership and acceptance record.

## Resolved Context and Remaining Gate

No design clarification remains for planning. Current Foundation pins cannot compile the required upstream API; private preview preflight and later stable actual-host qualification are explicit validation gates, not unresolved design choices. Synthetic provider tests use a test-owned `IServiceProvider`/`IAsyncDisposable` wrapper: dispose the provider first, then signal the lease's confirmed teardown callback. Fake drains hold that signal until their modeled completion or failure. This preserves the production release boundary without using an initializer as a disposal proxy. This plan stage does not claim either package gate has passed.
