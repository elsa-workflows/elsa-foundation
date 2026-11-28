# Nested activity structures

Ordinary nested activities should declare their child shape in the activity catalog. The Workflows adapter uses that declaration for authored navigation, generic collection editing, run-canvas slot badges, and executable inspection; activity implementations should not add their own navigation code.

## Declare fixed slots

Use `fixedStructureSlot` for a named child property:

```ts
import { fixedStructureSlot } from "./structureDesign";

const slots = [
  fixedStructureSlot({
    name: "If.Then",
    property: "then",
    displayName: "Then",
    cardinality: "single"
  }),
  fixedStructureSlot({
    name: "If.Else",
    property: "else",
    displayName: "Else",
    cardinality: "single"
  })
];
```

The descriptor `name` is the executable wire-slot name, `property` is the authored payload property, and `cardinality` describes whether that property contains one child or an array. A `many` slot is still one named slot; the activities in it retain their order.

## Declare collection-backed slots

Use `collectionStructureSlot` when each child is stored inside an item of a collection. The slot-name template must contain exactly one label placeholder:

```ts
import { collectionStructureSlot } from "./structureDesign";

const branch = collectionStructureSlot({
  name: "Parallel.Branch",
  property: "branches",
  displayName: "Branches",
  cardinality: "single",
  collectionProperty: "branches",
  childProperty: "activity",
  labelProperty: "name",
  slotNameTemplate: "Parallel.Branch[{name}]"
});
```

For `Parallel.Branch[Review]`, the adapter reads `Review` as the item label and maps the pinned child to `branches[].activity`. Additional item properties such as conditions or IDs may be declared when authored editing needs them; executable reconstruction never fabricates those values. The shared collection editor can then handle ordinary collection items without a custom designer.

## Validation and executable behavior

Structure descriptors are validated as one contract when read. Incomplete collection metadata, repeated/missing placeholders, duplicate names or physical addresses, and templates that can match the same wire name invalidate the declaration rather than silently dropping individual slots. Keep templates disjoint and labels stable enough for the executable wire contract.

When adapting pinned executable data, child properties are initialized empty before wire slots are applied, so template defaults cannot add children or branches that were not in the executable. If a pinned slot is unmatched, ambiguous, or contains too many children for a `single` descriptor, navigation uses an in-memory read-only projection of the exact wire slots and all their children. This projection is not serialized into the workflow or execution material. The Executable Inspector reports the affected wire slot and the reason the live catalog declaration could not be used.

Add or extend `executableGraph.test.ts` coverage when introducing a structure declaration. The shared conformance assertion in `executableStructureConformance.ts` verifies slot entry through both the Run canvas and Executable Inspector. Cover at least one populated slot and an empty slot; collection structures should also cover unusual labels and nested containers. Compensation scopes retain their specialized paired-step projection and should use the existing compensation tests rather than replacing that behavior with a generic designer.

Use a custom designer only when an activity has genuinely custom interaction or rendering semantics that the generic fixed/collection editor cannot express. It should still expose the shared child-slot contract where navigation is needed.