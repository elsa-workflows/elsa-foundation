# Artifact Hash Is Purely Behavioral and Executables Are Content-Addressed

Status: accepted (2026-07-10; ratified in a grilling session on read-only executable inspection.
Plan of record: `docs/plans/content-addressed-executables-and-inspector.md`.)

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
types/versions, construction descriptor payloads, input bindings, structure, child slots). Source
identity leaves the payload.

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

`ArtifactId` (derived from the hash) is stable across cosmetic republishes; UI surfaces listing
executables must present source provenance as one-to-many.

Layout Sidecar placement is per Source Reference — see ADR 0039.
