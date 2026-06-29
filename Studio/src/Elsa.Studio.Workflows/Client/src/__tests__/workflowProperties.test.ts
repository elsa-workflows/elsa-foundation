import { describe, expect, it } from "vitest";
import {
  createInput,
  createOutput,
  createVariable,
  defaultStorageDriverTypeName,
  defaultVariableTypeName,
  friendlyTypeLabel,
  generateUniqueName,
  readBooleanField,
  readStringField,
  shortenTypeName,
  updateInput,
  updateOutput,
  updateVariable
} from "../workflowProperties";
import type { WorkflowVariable } from "../workflowTypes";

describe("variable shape construction", () => {
  it("creates a canonical variable with a generated id and the expected keys", () => {
    const variable = createVariable({ name: "IsValid" });

    expect(Object.keys(variable).sort()).toEqual(
      ["id", "isArray", "name", "storageDriverTypeName", "typeName", "value"].sort()
    );
    expect(variable.id).toBeTypeOf("string");
    expect(variable.id.length).toBeGreaterThan(0);
    expect(variable.name).toBe("IsValid");
    expect(variable.typeName).toBe(defaultVariableTypeName);
    expect(variable.isArray).toBe(false);
    expect(variable.value).toBeNull();
    expect(variable.storageDriverTypeName).toBe(defaultStorageDriverTypeName);
  });

  it("honours an explicit type and storage driver", () => {
    const variable = createVariable({ name: "Count", typeName: "Int32", storageDriverTypeName: "MemoryStorageDriver" });
    expect(variable.typeName).toBe("Int32");
    expect(variable.storageDriverTypeName).toBe("MemoryStorageDriver");
  });

  it("gives each created variable a unique id", () => {
    expect(createVariable({ name: "a" }).id).not.toBe(createVariable({ name: "b" }).id);
  });

  it("matches the canonical Code-tab shape once fully edited", () => {
    let variable = createVariable({ name: "IsValid", typeName: "Boolean" });
    variable = updateVariable(variable, { value: "False", storageDriverTypeName: "WorkflowInstanceStorageDriver" });

    expect(variable).toMatchObject({
      name: "IsValid",
      typeName: "Boolean",
      isArray: false,
      value: "False",
      storageDriverTypeName: "WorkflowInstanceStorageDriver"
    });
    expect(typeof variable.id).toBe("string");
  });

  it("preserves unknown fields when editing an existing variable", () => {
    const existing = {
      id: "abc",
      name: "Old",
      typeName: "Boolean",
      isArray: false,
      value: "False",
      storageDriverTypeName: "WorkflowInstanceStorageDriver",
      // Field the editor does not know about — must survive the round-trip.
      customMetadata: { tag: "keep-me" }
    } as unknown as WorkflowVariable;

    const updated = updateVariable(existing, { name: "New" });

    expect(updated.name).toBe("New");
    expect(updated.id).toBe("abc");
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
