# Research: independent module witness

Reviewed current source at c986f9dd. Independent read-only investigations covered owning-resource authority and existing package/native/provider infrastructure. No new common public API is justified before execution.

## Observed startup correction

The [two bounded 30 October diagnostics](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6522243290) identified same-process exclusive native lock contention. Early `MapShells` registry construction starts readability's detached publication loop; its real package-catalog read acquires `root.lock` before initial reconciliation. Managed/native thread correlation establishes the owning call, beyond the earlier before/after snapshots. A diagnostic pre-start gate only hid this ordering and is not a correction.

The scoped correction queues background publication until the public host `ApplicationStarted` boundary, preserving installed subscriptions, conservative pins, direct schema reads and standalone service-provider behavior. It changes neither native lock policy nor the owning resource. Causal regression and normal ungated package-consumer verification precede the final distribution run. It does not promise serialization of later native operations; add/remove/restore and the remaining witness controls still test those boundaries.

## Owning resource

**Decision**: A separately authored widget's visibility in the explicit personal Home layout.
**Rationale**: Preferences.Core already supplies the public service, native validation/quota and revisioned CAS. Dashboard reads the public widget registry and stored visibility. Native API readback plus Home reload proves an actual effect without a new store.
**Alternatives**: ETL's store lives in an implementation package, so using it imports forbidden cross-domain coupling or expands scope. Repeating the candidate's private note store weakens existing-domain proof.

Use `IAuthSessionService.GetAsync(currentPrincipal)`: authenticated nonempty subject/tenant, reject reserved admin-default subject. Compare invocation actor/tenant; host ID comes from fixed trusted configuration. Canonical permission strings are `studio/preferences:read` and `studio/preferences:write`; constants misleadingly have a Core namespace in the Api assembly, so do not reference that implementation. Authorize against the actual key and recheck both permissions before write. The preference service itself does not authorize.

Seed via normal authenticated HTTP with `If-None-Match: *`; require a personal document from `/_elsa/studio/preferences/home-layout`, not a default/built-in revision. Change only own entry's `visible`, preserve all other JSON, use `Matches(reviewedRevision)`, then reread. Once committed, confirmation failure is uncertainty and never permission for another write.

## Browser boundary

**Decision**: Public `registerHomeWidget`, `api.agent` and module-owned settings route/renderers.
**Rationale**: No Dashboard private imports or host knowledge required. Settings remain reachable when widget is hidden. Only widget identity/visibility/revision enter context and results.
**Alternatives**: Immediate private Home-store invalidation requires domain behavior unnecessary for this witness. Reload Home and require successful native server reads; appearance/local-storage fallback is insufficient evidence.

## Distributions and hosts

**Decision**: Sibling `tools/studio/Weaver.Conformance.Probe` using existing composition qualification patterns.
**Rationale**: Existing tooling already audits isolated locked package restores, denies frontend tools during consumption, compares archive/cache/publish/runtime bytes, hides producer sources at runtime and retains genuine failing controls.
**Alternatives**: Candidate source fixture has explicit `independentPackageWitness: false`; successful historical runs cannot establish this proof. Copying the reference host implementation or shared engine into a consumer is disallowed.

Build the actual SDK library; prepare its published manifest only in the owned producer and pack a tgz. External module producer installs that tarball, typechecks and bundles its own assets with shared SDK/React externalized. Backend references public NuGet contracts. Each host's published dependency graph must be source-free. Include workflow/common support before baseline; trusted configuration may enable installed modules without rebuilding common code.

Before baseline, explicitly share the actual host copies of Agent.Core, Identity.Core and Preferences.Core plus CShells contracts through native loading configuration and declare their actual versions as host-provided. Record full assembly identity/location and scoped resolution after installation; matching names without shared type identity are insufficient. Studio's normal contract sharing covers its own Core/API interfaces, not these backend contracts.

## Package lifecycle and compatibility

**Decision**: Enrolled coordinated native store, actual add/remove/restore and exact serving observations.
**Rationale**: Upload/delete/reconcile acknowledgements do not establish active composition. Require intended package change/archive hash, serving-generation replacement and browser contributions/diagnostics.
**Control**: Same valid Studio module, deliberately unsupported `RequiredSdkVersion`. Require loader's precise incompatible outcome, absent contributions, zero effects and remaining Studio usability; unrelated restore/network/native failures do not qualify. Restore original compatible bytes and repeat positives. This control proves SDK incompatibility specifically.

## Provider separation

**Decision**: Separate fixture implements public Copilot client/session factory and Anthropic chat-client factory. Production adapters and engine remain unchanged, one provider active per run.
**Rationale**: Factories admit generic installed-tool name/arguments and opaque returned envelopes. Provider fixture has no resource-package reference or schema branches, and survives resource removal.
**Evidence**: Deterministic provider-protocol only; no provider account, purchase, credential inspection or live-provider claim.

Install both adapter artifacts before baseline, but enable exactly one provider feature (`GitHubCopilotAgent` or `AnthropicAgent`) plus its protocol-factory replacement in each run. Setting an adapter's `Enabled=false` does not remove its provider registration and cannot satisfy the one-provider rule.

## Generic corrections and contract decision

**Decision**: Demonstration first; causal regression and final complete distribution rerun after any correction.
**Rationale**: App currently retains incoming intent while WeaverSurface deduplicates locally. Close/reopen or registry remount may replay consumed intent. Prove through the actual extension sequence before changing the generic lifecycle. Broader context admission/queue/recovery remains #3439–#3441.
**Acceptance**: Common payload semantics stay opaque; source/artifact comparison must rule out a prearranged resource branch. Incorporate the explicit preview migration and supported workflow subset before freeze; #3437 accepted integration remains mandatory.
