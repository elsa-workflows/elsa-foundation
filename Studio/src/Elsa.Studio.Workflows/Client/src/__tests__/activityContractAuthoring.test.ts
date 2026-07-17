import { describe, expect, it } from "vitest";
import {
  authorableContractTypes,
  createInputContract,
  createOutputContract,
  defaultMode,
  generateReferenceKey,
  parseLiteralDefault,
  selectTypeForMember
} from "../activityContractAuthoring";
import { classifyBreakingContractTransition } from "../ActivityDefinitionContractEditor";
import type { ActivityAuthoringCapabilities, ActivityContract } from "../activityDefinitionTypes";

describe("Activity contract authoring model", () => {
  it("offers only capability-catalog types with an activated durable driver", () => {
    const capabilities = authoringCapabilities();
    capabilities.types.push(
      { ...capabilities.types[0], alias: "Transient", displayName: "Transient", supportsDurability: false },
      { ...capabilities.types[0], alias: "Driverless", displayName: "Driverless", compatibleStorageDriverKeys: [] },
      { ...capabilities.types[0], alias: "InactiveDriver", displayName: "Inactive driver", compatibleStorageDriverKeys: ["elsa.inactive"] }
    );

    expect(authorableContractTypes(capabilities).map(type => type.alias)).toEqual(["String"]);
    expect(createInputContract("Customer note", emptyContract(), capabilities)).toMatchObject({
      referenceKey: "customer-note",
      name: "Customer note",
      displayName: "Customer note",
      type: { alias: "String", collectionKind: "Single" },
      isNullable: false,
      storageDriverKey: "elsa.json",
      durability: "Required"
    });
  });

  it("generates deterministic collision-safe reference keys without requiring unique names", () => {
    expect(generateReferenceKey("Résumé status", ["resume-status"])).toBe("resume-status-2");
    expect(generateReferenceKey("Résumé status", ["resume-status", "resume-status-2"])).toBe("resume-status-3");
  });

  it("preserves unknown member and type fields while changing a catalog-backed selection", () => {
    const member = {
      ...createInputContract("Value", emptyContract(), authoringCapabilities())!,
      futureMemberField: { retained: true },
      type: {
        alias: "String",
        collectionKind: "Single",
        futureTypeField: "retained"
      }
    };
    const capabilities = authoringCapabilities();
    capabilities.storageDriverKeys.push("elsa.number");
    const selected = selectTypeForMember(member, {
      alias: "Number",
      displayName: "Number",
      category: "Primitive",
      defaultEditor: "number",
      supportedCollectionKinds: ["List"],
      supportsNull: false,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.number"],
      futureCapabilityField: true
    }, capabilities);

    expect(selected).toMatchObject({
      futureMemberField: { retained: true },
      type: {
        alias: "Number",
        collectionKind: "List",
        futureTypeField: "retained"
      },
      storageDriverKey: "elsa.number"
    });
  });

  it("keeps no default, literal null, present literal values, and expressions distinct", () => {
    expect(defaultMode(null)).toBe("none");
    expect(defaultMode({ syntax: "Literal", value: null })).toBe("literal");
    expect(defaultMode({ syntax: "Literal", value: "present" })).toBe("literal");
    expect(defaultMode({ syntax: "JavaScript", value: "input + 1" })).toBe("expression");
    expect(parseLiteralDefault("null")).toEqual({ ok: true, value: null });
    expect(parseLiteralDefault("\"present\"")).toEqual({ ok: true, value: "present" });
    expect(parseLiteralDefault("not-json")).toEqual({ ok: false });
  });

  it("classifies backend-breaking transitions while leaving presentation changes frictionless", () => {
    const input = createInputContract("Value", emptyContract(), authoringCapabilities())!;
    const output = createOutputContract("Value", emptyContract(), authoringCapabilities())!;

    expect(classifyBreakingContractTransition("input", input, { ...input, displayName: "Friendly value" })).toEqual([]);
    expect(classifyBreakingContractTransition("input", input, { ...input, name: "Renamed" })).not.toEqual([]);
    expect(classifyBreakingContractTransition("input", input, { ...input, type: { alias: "Number", collectionKind: "Single" } })).not.toEqual([]);
    expect(classifyBreakingContractTransition("input", { ...input, isNullable: true }, { ...input, isNullable: false })).not.toEqual([]);
    expect(classifyBreakingContractTransition("input", { ...input, default: { syntax: "Literal", value: 1 } }, { ...input, default: null })).not.toEqual([]);
    expect(classifyBreakingContractTransition("input", input, { ...input, isRequired: true })).not.toEqual([]);
    expect(classifyBreakingContractTransition("output", { ...output, isRequired: true }, { ...output, isRequired: false })).not.toEqual([]);
    expect(classifyBreakingContractTransition("outcome",
      { referenceKey: "done", name: "Done", isEmitted: false },
      { referenceKey: "done", name: "Done", isEmitted: true }
    )).not.toEqual([]);
  });
});

function emptyContract(): ActivityContract {
  return {
    contractSchemaVersion: "1",
    inputs: [],
    outputs: [],
    outcomes: []
  };
}

function authoringCapabilities(): ActivityAuthoringCapabilities {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: {
      serverGenerated: true,
      allowsPreCreationOverride: true,
      immutable: true,
      prefix: "elsa.user",
      pattern: "^elsa\\.user\\.",
      maximumLength: 160,
      collisionScope: "tenantId + activityTypeKey"
    },
    providers: [],
    types: [{
      alias: "String",
      displayName: "Text",
      category: "Primitive",
      defaultEditor: "text",
      supportedCollectionKinds: ["Single"],
      supportsNull: true,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.json"]
    }],
    storageDriverKeys: ["elsa.json"],
    snapshotFingerprint: "sha256:test"
  };
}
