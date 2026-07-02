import { describe, expect, it } from "vitest";
import {
  createInput,
  createOutput,
  createVariable,
  defaultEditorFor,
  descriptorAlias,
  friendlyTypeLabel,
  generateUniqueName,
  literalDefault,
  makeArgumentType,
  readArgumentType,
  readStringField,
  readVariableDefault,
  shortenTypeName,
  updateInput,
  updateOutput,
  updateVariable
} from "../workflowProperties";
import type { VariableDefinition, VariableTypeDescriptor } from "../workflowTypes";

describe("variable shape construction", () => {
  it("creates a canonical VariableDefinition with a generated reference key and the expected keys", () => {
    const variable = createVariable({ name: "IsValid" });

    expect(Object.keys(variable).sort()).toEqual(
      ["default", "name", "referenceKey", "storageDriverType", "type"].sort()
    );
    expect(variable.referenceKey).toBeTypeOf("string");
    expect(variable.referenceKey.length).toBeGreaterThan(0);
    expect(variable.name).toBe("IsValid");
    expect(variable.type).toEqual({ alias: "String", collectionKind: "Single" });
    expect(variable.storageDriverType).toBeNull();
    expect(variable.default).toBeNull();
  });

  it("uses the supplied alias for an explicit type", () => {
    const variable = createVariable({ name: "Count", alias: "Int32" });
    expect(variable.type).toEqual({ alias: "Int32", collectionKind: "Single" });
  });

  it("builds an argument type, defaulting the collection kind to Single", () => {
    expect(makeArgumentType("Boolean")).toEqual({ alias: "Boolean", collectionKind: "Single" });
    expect(makeArgumentType("String", "List")).toEqual({ alias: "String", collectionKind: "List" });
    expect(makeArgumentType("")).toEqual({ alias: "String", collectionKind: "Single" });
  });

  it("reads a stored argument type tolerantly", () => {
    expect(readArgumentType({ type: { alias: "Guid", collectionKind: "HashSet" } })).toEqual({ alias: "Guid", collectionKind: "HashSet" });
    expect(readArgumentType({ type: "String" })).toEqual({ alias: "String", collectionKind: "Single" });
    expect(readArgumentType({ type: { alias: "X", collectionKind: "Nope" } })).toEqual({ alias: "X", collectionKind: "Single" });
    expect(readArgumentType({})).toEqual({ alias: "", collectionKind: "Single" });
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

  it("stores a storage driver as a bare alias string", () => {
    expect(createVariable({ name: "V", storageDriver: "Elsa.Memory.MemoryStorageDriver" }).storageDriverType).toBe(
      "Elsa.Memory.MemoryStorageDriver"
    );
    expect(createVariable({ name: "V", storageDriver: "" }).storageDriverType).toBeNull();
  });

  it("matches the canonical backend shape once fully edited", () => {
    let variable = createVariable({ name: "IsValid", alias: "Boolean" });
    variable = updateVariable(variable, { default: literalDefault("False") });

    expect(variable).toMatchObject({
      name: "IsValid",
      type: { alias: "Boolean", collectionKind: "Single" },
      default: { value: "False", expressionType: "Literal" }
    });
    expect(typeof variable.referenceKey).toBe("string");
  });

  it("preserves unknown fields when editing an existing variable", () => {
    const existing = {
      referenceKey: "abc",
      name: "Old",
      type: makeArgumentType("Boolean"),
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
  it("creates a canonical input with a reference key and no default/read-only leftovers", () => {
    const input = createInput({ name: "OrderId" });
    expect(input).toMatchObject({
      name: "OrderId",
      type: { alias: "String", collectionKind: "Single" },
      displayName: "OrderId",
      description: "",
      category: "",
      uiHint: "singleline",
      storageDriverType: null,
      isRequired: false
    });
    expect(input.referenceKey).toBeTypeOf("string");
    expect(input.referenceKey.length).toBeGreaterThan(0);
    // The backend InputDefinition has no default member; the Elsa-3 leftovers must not be emitted.
    expect("defaultValue" in input).toBe(false);
    expect("defaultSyntax" in input).toBe(false);
    expect("isReadOnly" in input).toBe(false);
    expect("isArray" in input).toBe(false);
  });

  it("preserves unknown fields when editing an input", () => {
    const existing = { ...createInput({ name: "OrderId" }), legacyFlag: true } as never;
    const updated = updateInput(existing, { isRequired: true });
    expect(updated.isRequired).toBe(true);
    expect((updated as Record<string, unknown>).legacyFlag).toBe(true);
  });
});

describe("output shape construction", () => {
  it("creates a canonical output with a reference key and the smaller field set", () => {
    const output = createOutput({ name: "Result", alias: "Boolean" });
    expect(output).toMatchObject({
      name: "Result",
      type: { alias: "Boolean", collectionKind: "Single" },
      displayName: "Result",
      description: "",
      category: ""
    });
    expect(output.referenceKey).toBeTypeOf("string");
    expect(output.referenceKey.length).toBeGreaterThan(0);
  });

  it("preserves unknown fields when editing an output", () => {
    const existing = { ...createOutput({ name: "Result" }), extra: 1 } as never;
    const updated = updateOutput(existing, { name: "Renamed" });
    expect(updated.name).toBe("Renamed");
    expect((updated as Record<string, unknown>).extra).toBe(1);
  });
});

describe("descriptor helpers", () => {
  const descriptors: VariableTypeDescriptor[] = [
    { alias: "Boolean", displayName: "Boolean", category: "Primitives", defaultEditor: "checkbox" },
    { alias: "DateTime", displayName: "Date/Time", category: "Primitives", defaultEditor: "date" },
    { alias: "Int32", displayName: "Integer", category: "Primitives", defaultEditor: "" },
    { typeName: "Legacy.Type", displayName: "Legacy" }
  ];

  it("resolves the default editor hint for an alias, falling back to text", () => {
    expect(defaultEditorFor("Boolean", descriptors)).toBe("checkbox");
    expect(defaultEditorFor("DateTime", descriptors)).toBe("date");
    expect(defaultEditorFor("Int32", descriptors)).toBe("text");
    expect(defaultEditorFor("Unknown", descriptors)).toBe("text");
    expect(defaultEditorFor("Boolean", null)).toBe("text");
  });

  it("reads a descriptor's alias, tolerating legacy typeName", () => {
    expect(descriptorAlias(descriptors[0])).toBe("Boolean");
    expect(descriptorAlias(descriptors[3])).toBe("Legacy.Type");
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
});
