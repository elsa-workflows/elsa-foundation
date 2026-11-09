import { describe, expect, it } from "vitest";
import { isArgumentTypeCompatible } from "../workflowReferenceAuthoring";

describe("shared activity JSON reference compatibility", () => {
  it.each(["System.Text.Json.JsonElement", "System.Text.Json.JsonElement?"])("accepts JSON-compatible values for %s on any activity", target => {
    for (const alias of ["String", "Boolean", "Int64", "Decimal", "Object", "System.Text.Json.JsonElement"]) {
      expect(isArgumentTypeCompatible({ alias, collectionKind: "Single" }, target), alias).toBe(true);
    }
    expect(isArgumentTypeCompatible({ alias: "String", collectionKind: "Array" }, target)).toBe(true);
  });

  it("keeps unrelated typed inputs and collection boundaries constrained", () => {
    expect(isArgumentTypeCompatible({ alias: "String", collectionKind: "Array" }, "String", "HashSet")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "Array" }, "System.Text.Json.JsonElement", "HashSet")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "HashSet" }, "System.Text.Json.JsonElement", "HashSet")).toBe(true);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "Array" }, "System.Text.Json.JsonElement", "List")).toBe(true);
    expect(isArgumentTypeCompatible({ alias: "String", collectionKind: "Single" }, "System.Text.Json.JsonElement", "Array")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "Object", collectionKind: "Single" }, "System.Text.Json.JsonElement", "Dictionary")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "Single" }, "System.Int32")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "Single" }, "System.Text.Json.JsonElement[]")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "System.IO.Stream", collectionKind: "Single" }, "System.Text.Json.JsonElement")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "Byte", collectionKind: "Array" }, "System.Text.Json.JsonElement")).toBe(false);
    expect(isArgumentTypeCompatible({ alias: "Acme.JsonElement", collectionKind: "Single" }, "System.Int32")).toBe(false);
  });
});
