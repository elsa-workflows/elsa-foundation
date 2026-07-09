# Layout sidecar lives on the Source Reference

With content-addressed executables (ADR 0038), two publishes can be behaviorally identical but visually different, so "the artifact's layout" is not well-defined. We put the Layout Sidecar on the Source Reference — embedded in the record, not resolved through the definition — alongside the other per-publish facts (version label, published time). The artifact stays fully immutable; everything behavioral lives on the artifact, everything per-publish lives on the reference, no exceptions.

## Consequences

- The promotion/export unit is an artifact plus at least one Source Reference. The reference's definition-version pointer may dangle in the target environment (shown as provenance text, never followed); its embedded layout still renders.
- Inspecting an artifact without a reference in hand borrows layout from a default reference (newest), with the inspector stating which; with no reference at all, automatic layout is used. Any layout of a behaviorally identical graph is a valid rendering, so the choice affects aesthetics, never truth.
- Putting layout on the artifact (last-publish-wins) was rejected: a republish would mutate an existing artifact's sidecar, reintroducing mutability into the immutable object.
