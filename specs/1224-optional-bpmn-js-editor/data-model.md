# Historical prototype model and production candidate

The model below describes the retained schema 1 prototype. The production candidate is complete canonical XML plus disjoint Elsa configuration under authored schema 2, as specified in [production-design.md](production-design.md). Both are unmerged; neither is a second persisted process or a substitute for current-source qualification. Follow the dependency order in [delivery-slices.md](delivery-slices.md).

The existing Elsa draft owns the complete activity tree, bound activity versions, inputs/outputs, variables, intrinsic settings and design metadata. A BPMN process is an authored structure on that tree, with elements/flows keyed by their existing identities. A bpmn-js document is an ephemeral canvas projection; it is never persisted as an independent process.

Projection-only process and DI identifiers must be valid, deterministic and collision-free across all authored identifiers. They do not replace `ActivityNode.nodeId`. A projected element maps to its exact authored element, which can separately reference child and listener ActivityNodes. Full nested configurations remain canonical and are edited through existing Elsa authoring surfaces.

Diagram bounds, sizes, edge waypoints and label positions belong to existing design metadata/diagram data. Position changes from either editor must be reconciled without treating stale edge geometry as proof of routing fidelity.

State transitions are load canonical draft → admit/project → ready → supported modeling command → validate/merge atomically → ordinary host draft edit. Property edits and host Undo/Redo refresh the projection from the same draft. Failed/refused import/modeling preserves canonical data and displays recovery; a stale asynchronous completion never mutates a newer scope. Source-owned/pinned/read-only documents stay read-only regardless of preference.

This is a prototype model, subject to real-host feasibility and post-design review. Native child-bound and direct human/service publication contracts remain separate existing contracts.

The expanded full-toolbox scope requires a complete syntax authority; see the schema-2 XML-plus-disjoint-bindings proposal and bounded proofs in [production-design.md](production-design.md). The production proposal stores no independently editable V1 graph or second diagram copy. The existing prototype representation remains supported during qualification and is not migrated merely by viewing or switching editors.
