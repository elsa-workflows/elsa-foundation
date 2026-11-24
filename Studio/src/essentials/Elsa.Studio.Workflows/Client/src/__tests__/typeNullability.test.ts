import { describe, expect, it } from "vitest";
import { typeAllowsNull } from "../typeNullability";
import { parseActivityTestRunInputs } from "../activityDefinitionTestRuns";
import { canonicalizeAuthoredContract } from "../activityContractAuthoring";
import type { ActivityInputContract } from "../activityDefinitionTypes";

describe("type-derived nullability", () => {
  it.each([
    ["String", "Single", true], ["Elsa.Any", "Single", true], ["Int32", "Single", false],
    ["Boolean", "Single", false], ["DateTime", "Single", false], ["Guid", "Single", false],
    ["Int32", "Array", true], ["System.Text.Json.JsonElement", "Single", true],
    ["System.Text.Json.Nodes.JsonNode", "Single", true]
  ])("derives %s %s without an authored flag", (alias, collectionKind, expected) => {
    expect(typeAllowsNull({ alias: alias as string, collectionKind: collectionKind as string })).toBe(expected);
  });

  it("uses activated contributed types and preserves pinned CLR annotations", () => {
    const types = [{ alias: "ConnectionReference", supportsNull: true }, { alias: "String", supportsNull: true }];
    expect(typeAllowsNull({ alias: "ConnectionReference", collectionKind: "Single" }, types)).toBe(true);
    expect(typeAllowsNull({ alias: "String", collectionKind: "Single", supportsNull: false }, types)).toBe(false);
  });

  it("accepts explicit null for a required nullable member while refusing omission before defaults", () => {
    const input: ActivityInputContract = { referenceKey: "value", name: "value", type: { alias: "String", collectionKind: "Single" },
      isRequired: true, default: { syntax: "Literal", value: "fallback" }, storageDriverKey: "elsa.json", durability: "Required" };
    expect(parseActivityTestRunInputs([input], { value: { presence: "Null", draft: "" } }).inputs.value).toEqual({ state: "Present", value: null });
    expect(parseActivityTestRunInputs([input], {}).errors.value).toContain("required");
  });

  it("drops retired authoring fields and read metadata while preserving unknown content", () => {
    const contract = canonicalizeAuthoredContract({ inputs: [{ isNullable: true, IsNullable: true,
      type: { alias: "String", collectionKind: "Single", supportsNull: false, future: "kept" }, future: { kept: true } }], outputs: [] });
    expect(contract.inputs[0]).toEqual({ type: { alias: "String", collectionKind: "Single", future: "kept" }, future: { kept: true } });
  });
});
