# Contract: Closure envelope wire format (spec 151, FormatVersion 1)

The portable export/import unit — one JSON document per closure (clarified Q2). Produced by `IWorkflowArtifactClosureFactory`, consumed by the artifact reconciler and by studio#493's download.

```jsonc
{
  "formatVersion": 1,                    // int; unknown/newer → reject loudly, no partial import
  "rootArtifactId": "artifact-6f1c2a9b3d4e",
  "artifacts": [
    { /* WorkflowExecutable — full runtime shape: identity (artifactId, definitionId,
         definitionVersionId, artifactVersion, artifactHash), node snapshot, input contract,
         runtimeRequirements, storageDriverRequirements, dependencies (childArtifactId +
         childArtifactHash + dispatchNodeIds), incident strategy, checkpoint cadence, variables.
         Serialized WITHOUT recomputed projections (nodes/nodesById are rebuilt by the ctor). */ }
    // ... transitive dependency closure members, topologically complete
  ],
  "sourceReferences": [
    { /* WorkflowExecutableSourceReference of the EXPORTING engine, Scope == "Published" only.
         Provenance/expectations — the importer mints its own references and never persists
         these rows. TestRun-scope references are never exported (FR-B-011). */ }
  ],
  "triggerBindings": [
    { /* WorkflowTriggerBinding rows active on the EXPORTING engine for closure members.
         Expectations — the importer recomputes bindings via WorkflowTriggerIndexer with its
         own minted activationId/slotId; a node/stimulus-set mismatch between recomputed and
         carried bindings is a broken-source diagnostic. */ }
  ]
}
```

## Invariants (validated at import; violation = per-file or per-artifact rejection)

1. `rootArtifactId` ∈ `artifacts[].identity.artifactId`.
2. Every `dependencies[]` edge of every member resolves **within `artifacts` alone**, with declared `childArtifactHash` equal to the resolved member's `identity.artifactHash` (`MissingArtifact` / `HashMismatch` / `ConflictingIdentity` / `Cycle` → reject the whole closure unit, other units in the batch continue — per D5 step 6, which supersedes the per-artifact granularity this line originally described). This invariant is environment-independent: the closure is self-contained by contract (FR-B-010), so a file must never validate on one runtime and fail on another because of what happens to be in the store. Deduplication against already-imported store content happens **after** envelope validation, at persistence time only.
3. Each member's canonical content hash, **recomputed by the runtime-owned executable hasher from the received payload**, equals its declared `identity.artifactHash` (and the id-embedded hash prefix) — mismatch rejects the member and its dependents before persistence (corruption/invariant guard; signing is the tamper-proofing follow-up).
4. Every `sourceReferences[].scope == "Published"`; version ids never `draft:`-prefixed.
5. Artifact ids are content-addressed and stable — the importer MUST NOT mint or rewrite identities.
6. Tenant is absent from artifacts by design; `tenantId` on carried references is ignored (the importer stamps its source-configured tenant option).

## Versioning

`formatVersion` follows the fail-loud discipline of the runtime document codec: readers accept exactly the versions they know; there is no silent upcast in v1. Envelope evolution adds upcasters behind a version bump, never in-place shape drift. Serialization goes through `IPayloadSerializer` with the runtime document serializer's converter set so store-round-tripped and exported artifacts are byte-consistent.
