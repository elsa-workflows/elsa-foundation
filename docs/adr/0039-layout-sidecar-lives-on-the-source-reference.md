# Layout Sidecar Lives on the Source Reference

Status: accepted (2026-07-10; ratified in the same grilling session as ADR 0038.
Plan of record: `docs/plans/content-addressed-executables-and-inspector.md`.)

The Layout Sidecar — the publish-time copy of the graph geometry that makes an Executable renderable
without its source definition — is embedded on the Source Reference, never on the artifact and never
in the Artifact Hash.

## Context

With content-addressed executables (ADR 0038), two publishes can be behaviorally identical but
visually different, so "the artifact's layout" is not well-defined. At the same time, a workflow
Executable must be inspectable (rendered read-only) in environments where its source Workflow
Definition does not exist, so geometry has to travel with the artifact rather than be resolved
through the definition.

## Decision

The publish step copies the graph layout into a Layout Sidecar embedded in the Source Reference
record, alongside the other per-publish facts (version label, published time). The sidecar never
contributes to the Artifact Hash: visual arrangement is not behavior. The artifact stays fully
immutable — everything behavioral lives on the artifact, everything per-publish lives on the
reference, no exceptions.

## Considered Options

- Layout on the artifact, last-publish-wins. Rejected because a republish would mutate an existing
  artifact's sidecar, reintroducing mutability into the immutable object, and readers of the same
  artifact would see different geometry over time.
- No stored layout; resolve the source Definition Version to render. Rejected because it breaks when
  the Executable outlives or travels without its definition (retention, deletion, cross-environment
  promotion).
- Layout embedded on the Source Reference. Accepted because layout is publish provenance, exactly
  like the version label and timestamp.

## Consequences

The promotion/export unit is an artifact plus at least one Source Reference. The reference's
definition-version pointer may dangle in the target environment (shown as provenance text, never
followed); its embedded layout still renders.

Inspecting an artifact without a reference in hand borrows layout from a default reference (newest),
with the inspector stating which; with no reference at all, automatic layout is used. Any layout of a
behaviorally identical graph is a valid rendering, so the choice affects aesthetics, never truth.
