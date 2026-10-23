# Research: Bounded Durable-Value Page Reuse

**Status**: Planning research against source commit `a6684744daa7df444a2b2944a6984172ab3470eb`.

The code graph was used first to locate symbols. The graph is discovery evidence only; behavior statements below were checked in the pinned source tree. T07's accepted report at `7bb1aaecb3567b7ff973be8ec1daf35258395f64` supplies an empty-state CLR mechanism signal. It does not establish a primary HTTP or REST page-call reduction.

## Decisions

### Keep the memo beneath the existing coalescing overlay

**Decision**: Place reuse in the existing `CoalescingDurableValueStateStore`, retaining its provider as the raw-page source and its merger as the caller-visible result path.

**Rationale**: `CoalescingRuntimeCheckpointPersistenceExtensions` decorates `IDurableValueStateStore` after provider registration. The durable decorator pages the inner store and merges the current session's additions, replacements, and tombstones. Reusing the merged result would make later staged changes invisible. Reusing raw pages and running the merger each time keeps the existing overlay semantics.

**Source**: `src/essentials/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs:21-55,66-88`; `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs:134-175` (pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: Memoizing merged rows or projected activity values was rejected because the overlay can change between reads and projected values are outside this feature's scope. A global cache or point-`FindAsync` miss cache would exceed the existing owner and freshness boundary.

### Use trusted backend ownership metadata for conservative eligibility

**Decision**: Automatically enable only when the existing `RuntimeOperationalStateStoreBackend` is the unique `EntityFramework` marker and owns the effective `IDurableValueStateStore` registration and relevant concrete registration. Capture eligibility before decoration. If the marker is absent, ambiguous, custom, in-memory, or an effective descriptor has been overridden, bypass reuse and preserve the existing provider path.

**Rationale**: The repository already uses service-descriptor ownership metadata to describe the selected runtime operational backend. EF registration records the durable store descriptors and its marker. This supports a narrow composition decision without probing provider implementations or adding a new public abstraction. The marker is trusted host-registration metadata, not an authorization or spoof-resistance boundary. The existing `Find` method uses `SingleOrDefault`; eligibility discovery must treat duplicate markers as ineligible rather than introducing a new startup failure.

**Source**: `src/essentials/Workflows/Runtime/Core/Contracts/RuntimeOperationalStateStoreBackend.cs:5-28`; `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeOperationalStateEntityFrameworkCoreRegistration.cs:24-40,71-78,86-135` (pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: EF provider-type detection, provider reflection, a new cache capability interface, and throwing on custom composition were rejected. They broaden the contract or alter existing behavior; inability to prove this narrow eligibility should simply disable reuse.

### Treat cursor validation as part of page eligibility

**Decision**: Cache only successful inner pages and include continuation-codec object identity in each memo key. Reuse is eligible only with the built-in sealed `HmacRuntimeRecoveryContinuationCodec`; custom codec compositions bypass it. Never parse or normalize provider cursor tokens in Runtime Services.

**Rationale**: `EfDurableValueStateStore.ListPageAsync` checks cancellation, validates the workflow identity, requires the selected scope, decodes a non-null token, and validates its scope/workflow/last-value binding before querying. The first page load therefore performs the provider's full validation. The built-in codec captures its key at construction and deterministically verifies the purpose and signature; the same request and same codec instance cannot silently switch keys. A different instance cannot hit an older entry. An application codec may revoke, expire, or otherwise change token acceptance, so matching token text alone is insufficient. Access-context and cancellation checks stay live on each call. A failed validation is an exception and never becomes a cache entry.

**Source**: `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfDurableValueStateStore.cs:10-15,105-127`; `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeOperationalStoreSupport.cs:168-178`; `src/essentials/Workflows/Runtime/Services/Recovery/HmacRuntimeRecoveryContinuationCodec.cs:10-38,48-77`; `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeRecoveryContinuationCodec.cs:3-15`; EF codec registration `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeOperationalStateEntityFrameworkCoreRegistration.cs:71-78` (all pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: Duplicating EF's cursor purpose, payload, or binding validation in Services was rejected because it creates a second provider contract. Caching custom codec results was rejected because the interface does not promise deterministic or non-expiring decode behavior.

### Keep the diagnostic control within Coalesced mode

**Decision**: Add `CoalesceDurableValueReads = true` to the existing options and shell feature. A false value disables page reuse while leaving Coalesced cadence intact. Immediate mode remains unchanged. Copy the new flag and `CoalesceInspectionReads` when the authored per-session checkpoint cap creates a new options object.

**Rationale**: The shell feature returns before coalescing registration for Immediate mode. The current drain factory creates a new options object when an authored cap overrides the host cap and copies only `MaxSegmentCheckpoints`; defaults could otherwise silently re-enable controls. Same-cadence A/B is needed to attribute a page-call difference to this memo.

**Source**: `src/essentials/Workflows/Runtime/Api/Coalescing/WorkflowsRuntimeCheckpointPersistenceFeature.cs:21-63`; `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistenceOptions.cs:7-29`; `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs:14-34` (pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: Changing checkpoint mode or cadence to disable reuse was rejected because it changes other persistence behavior and confounds the comparison.

### Fence writes without turning the memo into a lock over provider work

**Decision**: Advance a cache-local generation and clear before each actual inner checkpoint commit and direct durable-value save/delete; clear/fence in `finally` as well. Count overlapping writes and bypass both hits and fills while any is active. A failed or cancelled write disables admissions for the rest of that session. For a healthy memo, a successful boundary clears the old generation and resets the fresh segment's capacity counters. Do not invalidate for deferred in-memory buffering. Reject late fill admission when generation, owner, context, or session status changed.

**Rationale**: `CoalescingRuntimeCheckpointCommitStore` can buffer deferred commits without calling the provider, while boundary and flush paths call the inner commit. Direct durable-value store writes also reach the inner provider. Cache invalidation needs to track those actual freshness boundaries. It need not serialize database reads with writes: a concurrent bypass read follows current provider consistency, and a completion from an old generation cannot populate reusable state.

**Source**: `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs:33-124`; `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs:134-175`; `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs:15-83` (pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: Invalidating on `BufferDeferred` would discard valid baseline reads before any provider write. Serializing provider work or promising stronger consistency than the store provides was rejected.

### Detach full model values and use fixed per-generation limits

**Decision**: Clone every cached row into detached memo-owned data and clone again on return. Bound each active generation to 32 pages, 1,024 rows, and 4 MiB of conservatively accounted retained content. Estimate content before cloning with checked arithmetic. On overflow, return the full provider result, clear the memo, and stop admissions until a successful boundary/reset.

**Rationale**: `DurableValueState` carries mutable `IReadOnlyDictionary` metadata and JSON-backed values; the nested external reference also carries metadata. Readonly interfaces alone do not guarantee deep immutability. The fixed limits put a reviewable ceiling on memo entries, row count, and counted content without making the page response partial. These are guardrails, not a measured optimum or exact heap-size promise.

**Source**: `src/essentials/Workflows/Runtime/Core/Models/DurableValueState.cs:9-49,97-157`; `src/essentials/Workflows/Runtime/Core/Models/RuntimeStorePage.cs:6-25,57-88`; `src/essentials/Workflows/Runtime/Core/Models/PersistenceAccessContext.cs:6-50` (pinned at `a6684744daa7df444a2b2944a6984172ab3470eb`).

**Alternatives considered**: Eviction, truncation, and cloning only on insertion were rejected. Eviction adds ordering and accounting behavior; truncation breaks page semantics; insertion-only cloning lets a caller mutate the cached object through a returned reference.

## Evidence limits and implementation proof

- T07's accepted empty-state, five-CLR-activity probe observed a possible repeat-read mechanism using an in-memory provider. It did not establish a non-empty row count or a reduction for the primary HTTP or REST path.
- The accepted HTTP/REST workload captures and current accounting identify different workflow families. They are correctness regressions for this change, not by themselves proof that page memoization reduces calls.
- A normal non-empty typed-start → deferred `ActivityStarted` → invoke test must compare enabled and disabled options under identical Coalesced cadence using the actual first-party EF composition. A narrow test-only EF command counter can confirm backing page selects.
- Existing overlay, checkpoint, recovery, scope, and provider tests remain required. A successful unit/fake-store result alone does not prove first-party EF eligibility or primary-workload savings.
- No implementation, test, SQL capture, or latency result is claimed by this planning artifact.
