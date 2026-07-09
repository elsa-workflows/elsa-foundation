# Plan: Content-addressed workflow executables and the Executable Inspector

**Status:** Agreed (design grilled 2026-07-09)
**Owner:** Workflows workstream
**Consumers:** `elsa-foundation` (publishing/runtime model), `elsa-foundation-studio` (Workflows UI, Weaver)
**Decisions:** ADR 0038, ADR 0039, ADR 0040 (this repo); studio ADR 0010; studio `CONTEXT.md` glossary

## 1. Problem & goal

A workflow Executable is immutable and hash-addressed, yet Studio offers no way to see what an artifact
contains without dispatching it — and dispatch has side effects. Users also lack navigation from an
executable back to its (editable) source definition.

Designing the read-only view exposed a deeper issue: the `ArtifactHash` payload mixes source identity
(definition/version ids, artifact version) with the compiled node tree, so **every publish mints a new hash
even when behavior is unchanged**. "Same hash = same behavior" — the property that makes a hash worth
displaying, deduplicating on, or promoting across environments — does not hold.

Goal: make executables true content-addressed objects (container-image semantics), then build read-only
inspection and source navigation on top of that model.

## 2. The model (decided)

| Concept | Decision | Where |
|---|---|---|
| **Execution Material** | The behavior-defining content: canonical node tree, activity type/version refs, construction descriptor payloads, input bindings, structure, child slots. | ADR 0038 |
| **Artifact Hash** | Computed over Execution Material **only**. Same hash ⇔ same behavior, both directions. Source identity leaves the payload. | ADR 0038 |
| **Executable (artifact)** | Content-addressed, fully immutable, one row per distinct behavior. `ArtifactId` derives from the hash and is stable across cosmetic republishes. | ADR 0038 |
| **Source Reference** | Self-contained per-publish record pointing at an artifact: source identity (may dangle across environments), artifact version label, published time, scope, optional expiry, `deletedAt`, and the embedded **Layout Sidecar**. Behaviorally identical publishes ⇒ distinct references to the same artifact. | ADR 0038/0039/0040 |
| **Layout Sidecar** | Publish-time copy of the graph geometry, embedded on the reference, never hashed. Renders the artifact in any environment; auto-layout is the fallback when no reference is at hand. | ADR 0039, studio ADR 0010 |
| **Test runs** | The transient executable store is retired. A Test Run creates an **expiring Source Reference** from the draft snapshot into the single artifact store. | ADR 0040 |
| **Lifetime & deletion** | Artifact lifetime is derived: retained while any live reference points at it; GC is a two-query sweep (drop expired/retired references, then unreferenced artifacts). Deleting an executable = retiring references. | ADR 0040 |
| **Equivalence signal** | A draft test run resolving to the same artifact id as a published version proves the draft is behaviorally identical to it — no diffing machinery. | ADR 0040 |

Pre-GA break accepted: all artifact hashes/ids change; W30b characterization goldens are re-pinned.

## 3. Studio surfaces (decided)

- **Executables page**: rows are **artifacts** (distinct behaviors); the source column shows the newest
  reference with an expandable list of all references. Scope filter `Published | Test runs | All`,
  defaulting to Published; retired references visible only via filter.
- **Executable Inspector**: routed read-only page `/workflows/executables/{artifactId}?ref={sourceReferenceId}`
  (default: newest reference). Shows the canvas (structure from Execution Material, geometry from the
  reference's Layout Sidecar, honest ghost nodes for activity-catalog misses), an identity panel (artifact id,
  hash, node/resume-target counts), the reference list, and actions Run / Explain / Open source definition.
  Reuses the Runs-page read-only canvas (`buildCanvas`).
- **Open source definition**: navigates to the definition editor (open-not-edit naming). Drift caption when
  the inspected reference's version is behind the definition's latest; upgraded to "current draft is
  behaviorally identical to this artifact" when the equivalence signal applies. Disabled with a reason when
  the definition is absent in the environment (promotion case).
- **Weaver**: Explain keeps sending the summary; a new `get-executable-detail` Weaver Tool (backed by the
  same detail endpoint, same permission) lets Weaver pull structure on demand.
- **Authorization**: the detail endpoint reuses the existing workflow read permission behind the standard
  management-bridge gating (ADR 0037). No new permission until environment promotion ships real operator roles.

## 4. Phases

### P0 — Source link (studio, no backend dependency)
Make the source cell in the executables table a link to `/workflows/definitions?definition={definitionId}`.

### P1 — Model redesign (elsa-foundation)
- Hash payload = canonical node tree only; re-pin goldens; artifact id derivation unchanged in form.
- Storage split: artifact table + source-reference table (scope, expiry, `deletedAt`, layout sidecar,
  source identity, artifact version label, timestamps). Publish resolves-or-creates the artifact and
  always appends a reference. Retire `InMemoryTransientWorkflowExecutableStore` and artifact-level scope.
- GC sweep for expired/retired references and unreferenced artifacts.
- Bridge contract: executables list (artifact rows + nested references, scope filter) and
  `GET …/executables/{artifactId}` detail (Execution Material + chosen reference's layout + references).

### P2 — Studio surfaces
Executables table rework (artifact rows, reference expansion, scope filter) and the Executable Inspector
page as specified in §3.

### P3 — Intelligence
`get-executable-detail` Weaver Tool + Explain prompt update; draft-equivalence signal in the test-run flow
and Inspector drift caption.

## 5. Deferred (recorded, out of scope)

- Restore-version-as-draft (Definitions feature; needs draft-conflict semantics).
- Distinct executable-read permission (when environment promotion ships operator roles).
- Artifact export/promotion across environments (the model is shaped for it: promotion unit = artifact +
  at least one reference).

## 6. Risks & notes

- The hash-payload change invalidates any persisted artifact ids/hashes in existing dev databases; pre-GA,
  no migration is provided (recompile on publish).
- Dispatch and the test-run handler currently read scope from the executable; they move to reference-driven
  dispatch in P1.
- The Weaver tool must enforce the endpoint's permission, not bypass it via the bridge key.
- Input-binding literals are part of Execution Material and visible to anyone with workflow read; secrets
  must remain references, never literals.
