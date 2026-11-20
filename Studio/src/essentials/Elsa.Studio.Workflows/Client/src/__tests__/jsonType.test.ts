import { describe, expect, it } from "vitest";
import { formatTypeName } from "../activityProperties";
import { isJsonTypeName, jsonTypeAlias } from "../jsonType";
import { isArgumentTypeCompatible } from "../workflowReferenceAuthoring";

// Spec 1228 User Story 4: one dynamic kind, Any, shown as JSON. Object and Elsa.Any are older names the server reads
// as Any, and activities may still declare the .NET JsonElement type.
describe("the dynamic kind", () => {
  it("is Any", () => {
    expect(jsonTypeAlias).toBe("Any");
  });

  it.each(["Any", "Any?", "Object", "System.Object", "Elsa.Any", "System.Text.Json.JsonElement", "System.Text.Json.JsonElement?"])(
    "shows %s as JSON",
    alias => {
      expect(isJsonTypeName(alias)).toBe(true);
      expect(formatTypeName(alias)).toBe(alias.endsWith("?") ? "JSON?" : "JSON");
    });

  it.each(["String", "Int32", "System.Text.Json.Nodes.JsonObject"])("does not show %s as JSON", alias => {
    expect(isJsonTypeName(alias)).toBe(false);
  });

  it("binds an Any value to a JsonElement input and the other way round", () => {
    expect(isArgumentTypeCompatible({ alias: "Any", collectionKind: "Single" }, "System.Text.Json.JsonElement")).toBe(true);
    expect(isArgumentTypeCompatible({ alias: "System.Text.Json.JsonElement", collectionKind: "Single" }, "Any")).toBe(true);
  });
});
