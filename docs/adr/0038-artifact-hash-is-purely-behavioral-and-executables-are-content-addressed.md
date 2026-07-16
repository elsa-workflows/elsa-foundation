# Artifact Hash Is Purely Behavioral and Executables Are Content-Addressed

Status: accepted (2026-07-10; ratified in a grilling session on read-only executable inspection.
Plan of record: `docs/plans/content-addressed-executables-and-inspector.md`.)
Amended: 2026-07-16 by spec 097 to include declared workflow inputs and exact executable
dependencies in Execution Material.

The `ArtifactHash` is computed over Execution Material only, making "same hash = same behavior" a
true invariant in both directions. Executables become content-addressed objects (container-image
semantics); per-publish facts move to a Source Reference that points at the artifact.

## Context

The `ArtifactHash` payload currently mixes source identity (SourceKind, SourceId, SourceVersion,
DefinitionId, DefinitionVersionId, ArtifactVersion) with the canonical executable node tree, so every
publish mints a new hash even when behavior is unchanged. The hash answers "which publish?" but never
"same workflow?", which forecloses behavioral dedup, no-op-publish detection, and cross-environment
artifact equivalence — the properties that make a hash worth displaying, deduplicating on, or
promoting. We are pre-GA, so a wire break is still cheap.

## Decision

Compute the `ArtifactHash` over Execution Material only — the canonical node tree (activity
types/versions, construction descriptor payloads, input bindings, structure, child slots), the
versioned declared workflow-input contract, and the canonical direct executable-dependency set.
Each dependency contributes its full child artifact ID/hash identity plus the sorted executable node
bindings that authorize that edge. Because every child hash covers the same execution material, a
behavioral change anywhere in the reachable dependency graph changes the parent's hash inductively.
Source identity leaves the payload.

Canonicalization is structural rather than delimiter-based: declared inputs, default-presence facts,
dependency identities, and node bindings are encoded as distinct fields and deterministically ordered.
Shared or repeated references to the same exact child artifact are de-duplicated into one dependency
whose node bindings are sorted. Publication facts such as tenant, source-reference identity, liveness,
and timestamps remain excluded.

Executables become content-addressed: publishing a behaviorally identical workflow resolves to the
existing artifact instead of creating a new one, and per-publish facts (definition/version
provenance, artifact version label, published timestamp) move to a Source Reference that points at
the artifact. The wire break (all artifact hashes and ids change; W30b characterization goldens
re-pinned) is accepted.

## Considered Options

- Keep source identity in the hash (publish identity). Rejected because it forecloses behavioral
  dedup, no-op-publish detection, and cross-environment artifact equivalence.
- Dual hash (publish-identity `ArtifactHash` plus an additive `BehaviorHash`). Rejected because it is
  non-breaking but permanently carries two identities and their explanation burden; pre-GA is the
  moment to take the clean model instead of stepping toward it.
- Purely behavioral hash with content-addressed storage. Accepted because it makes the hash mean what
  users assume it means and shapes the model for promotion and dedup.

## Consequences

Storage changes from one-row-per-publish to artifact + Source References (one artifact, many
references) — mirrors image-and-tags in a container registry.

Publish becomes idempotent per behavior; Studio can surface "this publish produced no behavioral
change".

A parent that pins a different child behavior, changes its declared workflow-input contract, or binds
that dependency to different executable nodes has different Execution Material and therefore a
different `ArtifactHash`. Equivalent dependency sets and input contracts hash identically regardless
of discovery order.

`ArtifactId` (derived from the hash) is stable across cosmetic republishes; UI surfaces listing
executables must present source provenance as one-to-many.

Layout Sidecar placement is per Source Reference — see ADR 0039.
