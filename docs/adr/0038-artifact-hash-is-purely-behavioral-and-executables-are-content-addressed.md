# Artifact hash is purely behavioral and executables are content-addressed

The `ArtifactHash` payload currently mixes source identity (SourceKind, SourceId, SourceVersion, DefinitionId, DefinitionVersionId, ArtifactVersion) with the canonical executable node tree, so every publish mints a new hash even when behavior is unchanged. We decided the hash must cover Execution Material only — the canonical node tree (activity types/versions, construction descriptor payloads, input bindings, structure, child slots) — making "same hash = same behavior" a true invariant, in both directions. Executables become content-addressed objects (container-image semantics): publishing a behaviorally identical workflow resolves to the existing artifact instead of creating a new one, and per-publish facts (definition/version provenance, artifact version label, published timestamp) move to a Source Reference that points at the artifact. Pre-GA, so the wire break (all artifact hashes and ids change; W30b characterization goldens re-pinned) is accepted.

## Considered Options

- **Keep source identity in the hash (publish identity)** — rejected: forecloses behavioral dedup, no-op-publish detection, and cross-environment artifact equivalence; the hash answers "which publish?" but never "same workflow?".
- **Dual hash (publish-identity ArtifactHash + additive BehaviorHash)** — rejected: non-breaking, but permanently carries two identities and their explanation burden; pre-GA is the moment to take the clean model instead of stepping toward it.

## Consequences

- Storage model changes from one-row-per-publish to artifact + Source References (one artifact, many references) — mirrors image-and-tags in a container registry.
- Publish becomes idempotent per behavior; Studio can surface "this publish produced no behavioral change".
- `ArtifactId` (derived from the hash) is stable across cosmetic republishes; UI surfaces listing executables must present source provenance as one-to-many.
- Layout Sidecar placement is per Source Reference — see ADR 0039.
