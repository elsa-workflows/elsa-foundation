import { describe, expect, it } from "vitest";
import {
  collectionStructureSlot,
  fixedStructureSlot,
  matchStructureDesignSlots,
  parseStructureDesignFacetPayload,
  validateStructureDesignSlotDescriptors
} from "../structureDesign";

const branch = collectionStructureSlot({
  name: "branches",
  property: "branches",
  displayName: "Branches",
  cardinality: "single",
  collectionProperty: "branches",
  childProperty: "activity",
  labelProperty: "name",
  slotNameTemplate: "Parallel.Branch[{name}]"
});

const fixed = fixedStructureSlot({
  name: "If.Then",
  property: "then",
  displayName: "Then",
  cardinality: "single"
});

describe("structure design slot declarations", () => {
  it("accepts typed fixed and collection declarations", () => {
    expect(validateStructureDesignSlotDescriptors([fixed, branch])).toEqual([]);
    expect(parseStructureDesignFacetPayload({
      mode: "sequence",
      supportsScopedVariables: false,
      slots: [fixed, branch],
      initialPayload: {}
    }).payload?.slots).toEqual([fixed, branch]);
  });

  it("rejects incomplete collection metadata and malformed placeholders as a whole", () => {
    const incomplete = rawBranch({ childProperty: undefined });
    const missingPlaceholder = rawBranch({ slotNameTemplate: "Parallel.Branch[name]" });
    const repeatedPlaceholder = rawBranch({ slotNameTemplate: "Parallel[{name}].Branch[{name}]" });
    const extraPlaceholder = rawBranch({ slotNameTemplate: "Parallel[{name}].Branch[{other}]" });
    const blankAddress = rawBranch({ collectionProperty: " " });

    for (const slot of [incomplete, missingPlaceholder, repeatedPlaceholder, extraPlaceholder, blankAddress]) {
      expect(parsePayload([slot]).payload).toBeNull();
    }
  });

  it("rejects duplicate names, physical addresses, and ambiguous collection templates", () => {
    const duplicateAddress = { ...fixed, name: "If.Else", displayName: "Else" };
    const secondBranch = collectionStructureSlot({
      ...branch,
      name: "cases",
      property: "cases",
      displayName: "Cases",
      collectionProperty: "cases",
      childProperty: "activity",
      labelProperty: "caseName",
      slotNameTemplate: "Parallel.Branch[{caseName}]"
    });

    expect(validateStructureDesignSlotDescriptors([fixed, duplicateAddress])).toContainEqual(
      expect.stringContaining("duplicates physical slot address")
    );
    expect(parsePayload([fixed, { ...fixed }]).payload).toBeNull();
    expect(validateStructureDesignSlotDescriptors([branch, secondBranch])).toContainEqual(
      expect.stringContaining("are ambiguous")
    );
  });

  it("matches collection labels literally and preserves empty branches", () => {
    const label = "Branch [2]. nested {label}";
    const slots = [
      { name: `Parallel.Branch[${label}]`, activities: [{}] },
      { name: "Parallel.Branch[]", activities: [] }
    ];
    const result = matchStructureDesignSlots(slots, [branch]);

    expect(result.issues).toEqual([]);
    expect(result.matches.map(item => item.match.itemLabel)).toEqual([label, ""]);
    expect(result.matches.map(item => item.slotIndex)).toEqual([0, 1]);
  });

  it("reports duplicate fixed slots and single-cardinality overflow", () => {
    const result = matchStructureDesignSlots([
      { name: "If.Then", activities: [{}] },
      { name: "If.Then", activities: [{}] },
      { name: "If.Else", activities: [{}, {}] }
    ], [fixed, { ...fixed, name: "If.Else", property: "else", displayName: "Else" }]);

    expect(result.issues).toEqual([
      { slotName: "If.Then", code: "duplicate-fixed-slot" },
      { slotName: "If.Else", code: "single-cardinality-overflow" }
    ]);
  });
});

function parsePayload(slots: unknown[]) {
  return parseStructureDesignFacetPayload({
    mode: "sequence",
    supportsScopedVariables: false,
    slots,
    initialPayload: {}
  });
}

function rawBranch(overrides: Record<string, unknown> = {}) {
  return {
    name: "branches",
    property: "branches",
    displayName: "Branches",
    cardinality: "single",
    collectionProperty: "branches",
    childProperty: "activity",
    labelProperty: "name",
    slotNameTemplate: "Parallel.Branch[{name}]",
    ...overrides
  };
}