# Data Model: Executable Artifact Reconciliation (spec 151)

**Date**: 2026-08-14. Existing entities are referenced, not redefined; new/relocated types are marked.

## New: `WorkflowArtifactClosure` (portable export unit) — `Elsa.Workflows.Runtime.Core`

The single self-describing JSON closure envelope (clarified Q2). Not a persisted store document — a wire/file format with its own versioning discipline.

| Field | Type | Notes |
|---|---|---|
| `FormatVersion` | `int` | Starts at 1. Unknown/newer → loud rejection, no partial import (mirrors `ElsaRuntimeDocumentVersions.Parse` fail-loud discipline). |
| `RootArtifactId` | `string` | The exported artifact. Must appear in `Artifacts`. |
| `Artifacts` | `IReadOnlyList<WorkflowExecutable>` | Root + transitive dependency closure (children by `Dependencies` walk). Serialized without recomputed projections (`Nodes`/`NodesById`), matching the Groundwork serializer's converter discipline. |
| `SourceReferences` | `IReadOnlyList<WorkflowExecutableSourceReference>` | The exporting engine's **Published-scope** references for the closure members. **Provenance/expectations only** — the importer never persists these rows (D4). |
| `TriggerBindings` | `IReadOnlyList<WorkflowTriggerBinding>` | The exporting engine's active bindings for the closure members. **Expectations only** — activation recomputes bindings via `WorkflowTriggerIndexer`; a node/stimulus-set mismatch vs the recomputed surface is a broken-source diagnostic. |

**Validation rules**: `RootArtifactId` present in `Artifacts`; every `Dependencies` edge of every member resolves **inside `Artifacts` alone** — wire-format validity is environment-independent, so an envelope that is only complete because the target store happens to contain a child is a broken export and fails everywhere identically (the store is consulted only afterward, for idempotent skip-persistence of already-present members); declared `ArtifactHash` on edges matches the referenced member's identity hash; no cycles; **each member's canonical content hash recomputes to its declared `Identity.ArtifactHash`** (runtime-owned hasher, before persistence — corruption/invariant guard, not tamper-proofing); references restricted to `Scope == Published` (export-side enforcement, FR-B-011).

## New: activation authority — `Elsa.Workflows.Runtime.Core` *(rev 2026-08-15: neutral names supersede the unrenamed relocation)*

`IWorkflowActivationAuthority` over `WorkflowActivationSlot(SlotId, WorkflowDefinitionId, SlotName, ActiveActivationId, Source: WorkflowActivationSource, Revision, UpdatedAt)` with a CAS transition result carrying the replaced activation id. `WorkflowActivationSource` is the explicit ownership descriptor (e.g. publishing vs artifact-reconciliation + source id). Publishing's `IPublicationSlotStore`/`PublicationSlot` are superseded and deleted; the shared `WorkflowActivationCoordinator` is the only writer (both paths request activation through it). Persisted/wire-adjacent names elsewhere (`PublicationId`/`SlotId` fields on references and bindings, `PreparePublicationAsync`/`ActivatePublicationAsync` store members) are grandfathered per §E6 — the coordinator writes activation ids into those existing fields.

- One ledger per engine, **physically as well as contractually**: a single durable implementation backed by a new slot document kind in the **runtime Groundwork store family** (registered with the other runtime stores); the publishing-family Groundwork slot store is deleted (no consumers yet → nothing to migrate; removes the dual-ledger composition-transition hole). In-memory default via `AddWorkflowRuntime()` (`TryAdd`) is the non-Groundwork fallback.
- **Ownership + conflict rules** *(rev 2026-08-15 — supersedes the id-namespace convention)*: ownership lives in the slot's explicit `Source` field, never inferred from id prefixes (prefixes like `import:{sourceId}:…` may remain for diagnostics only). Rules: same artifact from any source → idempotent no-op; concurrent change → CAS on `Revision`; different artifact from a non-owning source → rejected with a diagnostic naming the owning source (ownership transfer = explicit operator action, post-v1).
- **State transitions**: CAS-guarded `TryActivate(definitionId, slotName, candidateActivationId, source, expectedRevision)` — returns the replaced activation id; conflict rules above enforced at the transition.
- **Supersession ordering** (FR-B-007): "newer" = SemVer sort key over `ArtifactVersion` (`SemVer.ToSortKey`, ordinal compare — the design-side comparator), evaluated against the active publication's minted source reference. Candidate ≤ active → skip; unparseable → reject at import.

## New: `RuntimeRequirementCheckResult` — `Elsa.Workflows.Runtime.Core`

Returned by `IRuntimeRequirementChecker` (extracted from `RuntimeRequirementPreflight`, FR-B-005). Runtime-layer result — no Publishing views, no Design `ActivityDiagnostic`.

| Field | Type | Notes |
|---|---|---|
| `ArtifactId` | `string` | Subject. |
| `Requirements` | `IReadOnlyList<RuntimeRequirementStatusEntry>` | Per consumer-capability requirement: `(ConsumerKey, SchemaVersion, Status)`. Exact ordinal semantics, unchanged. |
| `StorageDrivers` | `IReadOnlyList<...>` | Per driver key: `(DriverKey, Status)` — `Available`/`Missing` only (unversioned). |
| `ActivityTypes` | `IReadOnlyList<...>` | **Second axis** (clarified Q1): per distinct node `TypeAlias`: `(TypeAlias, NodeIds, Status)` via `IWellKnownTypeRegistry.TryGetTypeOrDefault`. |
| `IsSatisfied` | `bool` | True iff every entry across all three collections is `Available`. |

Status enum gains `MissingActivityType` (or the type axis reuses `Missing` with its own collection — settle at task time; the gate verdict is identical either way).

## New: reconciliation contracts — `Elsa.Workflows.Runtime.Reconciliation.Core`

- **`IWorkflowArtifactReconciliationSource`**: `SourceId` (string, required), `SourceKind` (string), `ReadAsync(ct) → IAsyncEnumerable<WorkflowArtifactClosureFile>` where `WorkflowArtifactClosureFile(FilePath|Origin, WorkflowArtifactClosure)`. Mirrors `IWorkflowReconciliationSource` (SourceId/SourceKind self-identification).
- **`JsonWorkflowArtifactReconciliationOptions`**: `FilePath?` | `Files: [{Order, FilePath}]` | `FolderPath?` (exactly one shape; top-level `*.json` scan, ordinal filename order — mirrors `JsonWorkflowReconciliationOptions` including the non-recursive ConfigMap rationale), `SourceId` (required), `TenantId?` (**new**, clarified Q6 — stamped on minted references, default null).
- **Domain exceptions** (§2.23.5): `InvalidWorkflowArtifactClosureException(path, reason, inner)` — file-level parse/format/version failures; `WorkflowArtifactReconciliationException` family for pipeline failures that must abort a pass (missing folder). Per-artifact rejections are **diagnostics on the pass result**, not exceptions (batch isolation, US2-3).
- **Pass result**: `WorkflowArtifactReconciliationResult` — per-artifact outcomes (`Imported | AlreadyCurrent | Skipped(olderVersion) | Rejected(diagnostic)`), used by tests and logged.

## Imported rows (existing runtime entities, written by the importer)

- **`WorkflowExecutable`** — written via `IWorkflowExecutableStore.SaveAsync` verbatim from the envelope (content-addressed, create-only; already-exists = idempotent no-op). The importer never mints identities (edge case pinned in spec).
- **`WorkflowExecutableSourceReference`** — **minted** by the importer per activated artifact: `SourceKind`/`SourceId` from the source, `Scope = Published`, `PublicationId` = the opaque activation id minted via the shared coordinator (any prefix is diagnostics-only), `SlotId` = importer-derived (default slot per definition), `TenantId` from option, `DefinitionId`/`DefinitionVersionId`/`ArtifactVersion` copied from the artifact identity. Predecessor's minted reference retired with reason `"publication-replaced"` on supersession.
- **`WorkflowTriggerBinding`** — recomputed via `WorkflowTriggerIndexer.PreparePublicationAsync(executable, publicationId, slotId)`; activated/deactivated through the existing publication projection semantics. Never copied from the envelope.

## Relationships

```
WorkflowArtifactClosure ──contains──> WorkflowExecutable (1..n, closure)
WorkflowExecutable.Dependencies ──(ArtifactId+ArtifactHash)──> WorkflowExecutable (validated at import)
WorkflowActivationSlot (DefinitionId, SlotName) ──ActiveActivationId + Source──> minted activation id
minted WorkflowExecutableSourceReference ──PublicationId/SlotId (grandfathered field names)──> WorkflowActivationSlot entry
WorkflowTriggerBinding ──PublicationId──> publication projection state (IsActive flip)
```

Nothing on any existing entity changes shape. The only new persisted record kind is the runtime-family slot document (registered in the runtime Groundwork store family, replacing the deleted publishing-family slot store); everything else reuses existing stores.
