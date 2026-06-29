import { describe, expect, it } from "vitest";
import {
  createInput,
  createOutput,
  createVariable,
  defaultVariableType,
  friendlyTypeLabel,
  generateUniqueName,
  literalDefault,
  readBooleanField,
  readStringField,
  readVariableDefault,
  resolveStorageDriverType,
  resolveTypeInformation,
  shortenTypeName,
  typeInformationKey,
  updateInput,
  updateOutput,
  updateVariable,
  wellKnownVariableTypes
} from "../workflowProperties";
import type { VariableDefinition } from "../workflowTypes";

describe("variable shape construction", () => {
  it("creates a canonical VariableDefinition with a generated reference key and the expected keys", () => {
    const variable = createVariable({ name: "IsValid" });

    expect(Object.keys(variable).sort()).toEqual(
      ["default", "name", "referenceKey", "storageDriverType", "typeInformation"].sort()
    );
    expect(variable.referenceKey).toBeTypeOf("string");
    expect(variable.referenceKey.length).toBeGreaterThan(0);
    expect(variable.name).toBe("IsValid");
    expect(variable.typeInformation).toEqual(defaultVariableType);
    expect(variable.storageDriverType).toBeNull();
    expect(variable.default).toBeNull();
  });

  it("resolves an explicit type key to its well-known TypeInformation", () => {
    const variable = createVariable({ name: "Count", typeKey: "Int32" });
    expect(variable.typeInformation).toEqual(wellKnownVariableTypes.find(type => type.typeName === "Int32"));
    expect(typeInformationKey(variable.typeInformation)).toBe("System.Int32");
  });

  it("splits an unknown free-text type into namespace + type name", () => {
    expect(resolveTypeInformation("My.Custom.Order")).toMatchObject({ typeName: "Order", namespace: "My.Custom" });
    expect(resolveTypeInformation("Bare")).toMatchObject({ typeName: "Bare", namespace: "" });
  });

  it("gives each created variable a unique reference key", () => {
    expect(createVariable({ name: "a" }).referenceKey).not.toBe(createVariable({ name: "b" }).referenceKey);
  });

  it("wraps and reads a literal default value", () => {
    expect(literalDefault("")).toBeNull();
    expect(literalDefault("False")).toEqual({ value: "False", expressionType: "Literal" });
    expect(readVariableDefault({ value: "False", expressionType: "Literal" })).toBe("False");
    expect(readVariableDefault(null)).toBe("");
  });

  it("resolves a storage driver key, or null when cleared", () => {
    expect(resolveStorageDriverType("")).toBeNull();
    expect(resolveStorageDriverType("Elsa.Storage.MemoryStorageDriver")).toMatchObject({
      typeName: "MemoryStorageDriver",
      namespace: "Elsa.Storage"
    });
  });

  it("matches the canonical backend shape once fully edited", () => {
    let variable = createVariable({ name: "IsValid", typeKey: "Boolean" });
    variable = updateVariable(variable, { default: literalDefault("False") });

    expect(variable).toMatchObject({
      name: "IsValid",
      typeInformation: { typeName: "Boolean", namespace: "System" },
      default: { value: "False", expressionType: "Literal" }
    });
    expect(typeof variable.referenceKey).toBe("string");
  });

  it("preserves unknown fields when editing an existing variable", () => {
    const existing = {
      referenceKey: "abc",
      name: "Old",
      typeInformation: resolveTypeInformation("Boolean"),
      storageDriverType: null,
      default: null,
      // Field the editor does not know about — must survive the round-trip.
      customMetadata: { tag: "keep-me" }
    } as unknown as VariableDefinition;

    const updated = updateVariable(existing, { name: "New" });

    expect(updated.name).toBe("New");
    expect(updated.referenceKey).toBe("abc");
    expect(updated.customMetadata).toEqual({ tag: "keep-me" });
  });
});

describe("input shape construction", () => {
  it("creates a canonical input with the full argument field set", () => {
    const input = createInput({ name: "OrderId" });
    expect(input).toMatchObject({
      name: "OrderId",
      type: "String",
      displayName: "OrderId",
      description: "",
      category: "",
      isArray: false,
      uiHint: "singleline",
      storageDriverType: null,
      defaultValue: null,
      defaultSyntax: null,
      isReadOnly: null
    });
  });

  it("preserves unknown fields when editing an input", () => {
    const existing = { ...createInput({ name: "OrderId" }), legacyFlag: true } as never;
    const updated = updateInput(existing, { isArray: true });
    expect(updated.isArray).toBe(true);
    expect((updated as Record<string, unknown>).legacyFlag).toBe(true);
  });
});

describe("output shape construction", () => {
  it("creates a canonical output with the smaller field set", () => {
    const output = createOutput({ name: "Result", type: "Boolean" });
    expect(output).toEqual({
      name: "Result",
      type: "Boolean",
      displayName: "Result",
      description: "",
      category: "",
      isArray: false
    });
  });

  it("preserves unknown fields when editing an output", () => {
    const existing = { ...createOutput({ name: "Result" }), extra: 1 } as never;
    const updated = updateOutput(existing, { name: "Renamed" });
    expect(updated.name).toBe("Renamed");
    expect((updated as Record<string, unknown>).extra).toBe(1);
  });
});

describe("name generation", () => {
  it("returns the first free indexed name", () => {
    expect(generateUniqueName("Variable", [])).toBe("Variable1");
    expect(generateUniqueName("Variable", ["Variable1", "Variable2"])).toBe("Variable3");
    expect(generateUniqueName("Variable", ["Variable1", "Variable3"])).toBe("Variable2");
  });
});

describe("display + field helpers", () => {
  it("shortens .NET type names", () => {
    expect(shortenTypeName("System.Collections.Generic.List`1, mscorlib")).toBe("List");
    expect(shortenTypeName("System.String")).toBe("String");
  });

  it("prefers an already-friendly display name", () => {
    expect(friendlyTypeLabel("Boolean", "System.Boolean")).toBe("Boolean");
    expect(friendlyTypeLabel(undefined, "System.Boolean")).toBe("Boolean");
    expect(friendlyTypeLabel("System.Int32", "System.Int32")).toBe("Int32");
  });

  it("reads string fields across casings", () => {
    expect(readStringField({ TypeName: "Boolean" }, ["typeName", "TypeName"])).toBe("Boolean");
    expect(readStringField({ value: 12 }, ["value"])).toBe("12");
    expect(readStringField({}, ["missing"])).toBe("");
  });

  it("reads boolean fields across casings and string forms", () => {
    expect(readBooleanField({ isArray: true }, ["isArray", "IsArray"])).toBe(true);
    expect(readBooleanField({ IsArray: "true" }, ["isArray", "IsArray"])).toBe(true);
    expect(readBooleanField({ isArray: false }, ["isArray"])).toBe(false);
    expect(readBooleanField({}, ["isArray"])).toBe(false);
  });
});
