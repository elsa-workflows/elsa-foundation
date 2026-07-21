import { describe, expect, it } from "vitest";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import {
  camelize,
  defaultCollectionItem,
  describeCollectionForInput,
  describeDictionaryForInput,
  describeDictionaryType,
  describeCollectionType,
  formatTypeName,
  getLiteralDefaultValue,
  isRepeaterOptOut,
  makeCollectionElementDescriptor,
  moveCollectionItem,
  planExpressionModeTransition,
  readWrappedInput,
  toLiteralCollection,
  writeInputValue,
  withLiteralValue,
  withSyntax
} from "../activityProperties";
import { updateActivity } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";

const textDescriptor = {
  name: "Text",
  typeName: "System.String",
  displayName: "Text",
  uiHint: "singleline",
  isWrapped: true
};

describe("activity property values", () => {
  it("reads missing wrapped inputs as empty literal values with the descriptor type", () => {
    expect(readWrappedInput(activity("write"), textDescriptor)).toEqual({
      typeName: "System.String",
      expression: { type: "Literal", value: "" }
    });
  });

  it("reads older naked values as literal wrapped values", () => {
    expect(readWrappedInput({ ...activity("write"), text: "Hello" }, textDescriptor)).toEqual({
      typeName: "System.String",
      expression: { type: "Literal", value: "Hello" }
    });
  });

  it("writes naked inputs without expression envelopes", () => {
    const descriptor = { ...textDescriptor, isWrapped: false };
    const updated = writeInputValue(activity("write"), descriptor, "Raw text");

    expect(updated.text).toBe("Raw text");
  });

  it("updates literal value and syntax while preserving the wrapped shape", () => {
    const value = readWrappedInput(activity("write"), textDescriptor);
    expect(withSyntax(withLiteralValue(value, "Hello"), "JavaScript")).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "Hello" }
    });
  });

  it("camelizes descriptor property names", () => {
    expect(camelize("Text")).toBe("text");
    expect(camelize("URL")).toBe("uRL");
  });

  it("keys authored values by the descriptor's referenceKey verbatim, not the display name", () => {
    const descriptor = { ...textDescriptor, name: "CustomerId", referenceKey: "customer-id" };
    const updated = writeInputValue(activity("write"), descriptor, { typeName: "System.String", expression: { type: "Literal", value: "42" } });

    expect(updated["customer-id"]).toEqual({ typeName: "System.String", expression: { type: "Literal", value: "42" } });
    expect("customerId" in updated).toBe(false);
    expect(readWrappedInput(updated, descriptor).expression.value).toBe("42");
  });

  it("stores a WriteLine-style lowercase referenceKey under that exact key", () => {
    const descriptor = { ...textDescriptor, referenceKey: "text" };
    const updated = writeInputValue(activity("write"), descriptor, { typeName: "System.String", expression: { type: "Literal", value: "Hello" } });

    expect(updated.text).toEqual({ typeName: "System.String", expression: { type: "Literal", value: "Hello" } });
    expect("Text" in updated).toBe(false);
  });

  it("falls back to the camelized display name when the descriptor has no referenceKey", () => {
    const updated = writeInputValue(activity("write"), { ...textDescriptor, referenceKey: "  " }, "Raw");
    expect("text" in updated).toBe(true);
  });

  it("updates nested activities without mutating unrelated branches", () => {
    const left = activity("left");
    const target = activity("target");
    const root = sequenceRoot([left, sequenceRoot([target], "nested")]);

    const updated = updateActivity(root, "target", node => ({ ...node, text: "Updated" }));
    const nestedActivities = ((updated.structure?.payload.activities as ActivityNode[])[1].structure?.payload.activities ?? []) as ActivityNode[];

    expect(nestedActivities[0].text).toBe("Updated");
    expect((updated.structure?.payload.activities as ActivityNode[])[0]).toBe(left);
    expect(root).not.toBe(updated);
  });
});

describe("expression mode transitions", () => {
  const plan = (
    source: "literal" | "text" | "structured" | "reference",
    target: "literal" | "text" | "structured" | "reference",
    value: unknown,
    typeName = "System.String"
  ) => planExpressionModeTransition(source, target, typeName, value, "target-default");

  it.each([
    [null],
    [undefined],
    [""],
    [[]],
    [{}],
    [Object.create(null)]
  ])("initializes an empty value from the target default without confirmation (%s)", value => {
    expect(plan("reference", "structured", value)).toEqual({
      requiresConfirmation: false,
      nextValue: "target-default"
    });
  });

  it.each([
    [false],
    [0],
    [" "],
    [["value"]],
    [{ referenceKey: "key" }],
    [new Date(0)]
  ])("does not treat a meaningful or non-plain value as empty (%s)", value => {
    expect(plan("reference", "structured", value)).toEqual({
      requiresConfirmation: true,
      nextValue: "target-default"
    });
  });

  it("preserves text between text syntaxes", () => {
    expect(plan("text", "text", "order.total + 1")).toEqual({
      requiresConfirmation: false,
      nextValue: "order.total + 1"
    });
  });

  it.each([
    [false, "false"],
    [0, "0"],
    [42, "42"],
    ["hello", "hello"]
  ])("preserves primitive Literal %s as visible text", (value, expected) => {
    expect(plan("literal", "text", value, "System.Object")).toEqual({
      requiresConfirmation: false,
      nextValue: expected
    });
  });

  it("preserves text when returning to a string Literal", () => {
    expect(plan("text", "literal", "hello", "System.String")).toEqual({
      requiresConfirmation: false,
      nextValue: "hello"
    });
  });

  it("requires confirmation when returning text to a non-string Literal", () => {
    expect(plan("text", "literal", "1 + 1", "System.Int32")).toEqual({
      requiresConfirmation: true,
      nextValue: "target-default"
    });
  });

  it.each([
    ["literal", "structured"],
    ["literal", "reference"],
    ["text", "structured"],
    ["text", "reference"],
    ["structured", "literal"],
    ["structured", "text"],
    ["structured", "structured"],
    ["structured", "reference"],
    ["reference", "literal"],
    ["reference", "text"],
    ["reference", "structured"],
    ["reference", "reference"]
  ] as const)("requires confirmation for a non-empty %s to %s transition", (source, target) => {
    expect(plan(source, target, { existing: true })).toEqual({
      requiresConfirmation: true,
      nextValue: "target-default"
    });
  });
});

describe("formatTypeName", () => {
  it("shortens namespaced and nested type names", () => {
    expect(formatTypeName("System.String")).toBe("String");
    expect(formatTypeName("MyApp.Outer+Inner")).toBe("Inner");
  });

  it("strips the assembly-qualified tail instead of leaking its fragments", () => {
    // The naive "text after the last dot" returned "0, Culture=neutral, PublicKeyToken=…" here.
    expect(formatTypeName("System.String, System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e"))
      .toBe("String");
  });

  it("rebuilds generics as friendly labels", () => {
    expect(formatTypeName("System.Collections.Generic.ICollection`1[[System.String, System.Private.CoreLib]], System.Private.CoreLib"))
      .toBe("ICollection<String>");
    expect(formatTypeName("System.Collections.Generic.ICollection`1[System.Int32]")).toBe("ICollection<Int32>");
    expect(formatTypeName("System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Int32, mscorlib]]"))
      .toBe("Dictionary<String, Int32>");
  });

  it("renders arrays, including arrays of generics", () => {
    expect(formatTypeName("System.Int32[]")).toBe("Int32[]");
    expect(formatTypeName("System.Int32[][], mscorlib")).toBe("Int32[][]");
    expect(formatTypeName("System.Collections.Generic.List`1[[System.Int32, mscorlib]][], mscorlib"))
      .toBe("List<Int32>[]");
  });

  it("handles bare arity and falls back to the raw value when unparseable", () => {
    expect(formatTypeName("System.Collections.Generic.ICollection`1")).toBe("ICollection");
    expect(formatTypeName("")).toBe("");
  });
});

describe("collection literal authoring", () => {
  it("recognizes generic collection types and recovers the element type", () => {
    expect(describeCollectionType("System.Collections.Generic.List`1[[System.String, mscorlib]], mscorlib"))
      .toEqual({ elementTypeName: "System.String" });
    expect(describeCollectionType("System.Collections.Generic.ICollection`1[System.Int32]"))
      .toEqual({ elementTypeName: "System.Int32" });
  });

  it("recognizes a collection without recoverable element args (bare arity)", () => {
    expect(describeCollectionType("System.Collections.Generic.ICollection`1"))
      .toEqual({ elementTypeName: null });
  });

  it("recognizes array types", () => {
    expect(describeCollectionType("System.String[]")).toEqual({ elementTypeName: "System.String" });
    expect(describeCollectionType("System.Int32[], mscorlib")).toEqual({ elementTypeName: "System.Int32" });
  });

  it("returns null for non-collection and dictionary types", () => {
    expect(describeCollectionType("System.String")).toBeNull();
    expect(describeCollectionType("")).toBeNull();
    expect(describeCollectionType("System.Collections.Generic.IDictionary`2[System.String,System.Int32]")).toBeNull();
  });

  it("treats json/code hints and an explicit flag as repeater opt-outs", () => {
    expect(isRepeaterOptOut({ name: "X", typeName: "List`1", uiHint: "json" })).toBe(true);
    expect(isRepeaterOptOut({ name: "X", typeName: "List`1", uiHint: "code" })).toBe(true);
    expect(isRepeaterOptOut({ name: "X", typeName: "List`1", uiSpecifications: { repeater: false } })).toBe(true);
    expect(isRepeaterOptOut({ name: "X", typeName: "List`1" })).toBe(false);
    expect(isRepeaterOptOut({ name: "X", typeName: "List`1", uiHint: "multiline" })).toBe(false);
  });

  it("coerces stored values into item arrays without guessing delimiters", () => {
    expect(toLiteralCollection(["a", "b"])).toEqual(["a", "b"]);
    expect(toLiteralCollection(null)).toEqual([]);
    expect(toLiteralCollection("")).toEqual([]);
    expect(toLiteralCollection('["a","b"]')).toEqual(["a", "b"]);
    // Legacy comma string is preserved as one item rather than silently split.
    expect(toLiteralCollection("Hello World, Goodbye")).toEqual(["Hello World, Goodbye"]);
  });

  it("types new item defaults from the element type", () => {
    expect(defaultCollectionItem("System.Boolean")).toBe(false);
    expect(defaultCollectionItem("System.String")).toBe("");
    expect(defaultCollectionItem(null)).toBe("");
  });

  it("builds an unwrapped element descriptor with the element type", () => {
    const element = makeCollectionElementDescriptor(
      { name: "Lines", typeName: "ICollection`1", uiHint: "singleline", description: "All lines" },
      "System.String"
    );
    expect(element).toMatchObject({ name: "Lines", typeName: "System.String", isWrapped: false, description: null });
    expect(makeCollectionElementDescriptor({ name: "X", typeName: "ICollection`1" }, null).typeName).toBe("System.String");
  });

  it("moves items immutably and ignores out-of-range moves", () => {
    const items = ["a", "b", "c"];
    expect(moveCollectionItem(items, 0, 1)).toEqual(["b", "a", "c"]);
    expect(moveCollectionItem(items, 2, 0)).toEqual(["c", "a", "b"]);
    expect(moveCollectionItem(items, 0, 0)).toBe(items);
    expect(moveCollectionItem(items, 0, 5)).toBe(items);
    expect(items).toEqual(["a", "b", "c"]);
  });
});

describe("dictionary literal authoring", () => {
  it.each([
    "System.Collections.Generic.IDictionary`2[System.String,System.Int32]",
    "System.Collections.Generic.IReadOnlyDictionary`2[System.String,System.Int32]",
    "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Int32, mscorlib]], mscorlib",
    "System.Collections.Generic.SortedDictionary`2[System.String,System.Int32]",
    "System.Collections.Concurrent.ConcurrentDictionary`2[System.String,System.Int32]",
    "System.Collections.Immutable.ImmutableDictionary`2[System.String,System.Int32]",
    "System.Collections.Immutable.ImmutableSortedDictionary`2[System.String,System.Int32]"
  ])("recognizes the supported string-keyed family %s", typeName => {
    expect(describeDictionaryType(typeName)).toEqual({ valueTypeName: "System.Int32" });
  });

  it("preserves a nested assembly-qualified value type", () => {
    expect(describeDictionaryType(
      "System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]], System.Private.CoreLib]], System.Private.CoreLib"
    )).toEqual({ valueTypeName: "System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]]" });
  });

  it.each([
    "System.Collections.Generic.Dictionary`2[System.Int32,System.String]",
    "System.Collections.Generic.KeyValuePair`2[System.String,System.Int32]",
    "Company.Dictionary`2[System.String,System.Int32]",
    "System.Collections.Generic.Dictionary`2",
    "System.Collections.Generic.List`1[System.String]"
  ])("rejects unsupported or indeterminate type %s", typeName => {
    expect(describeDictionaryType(typeName)).toBeNull();
  });

  it("uses an empty object as the default dictionary value", () => {
    expect(getLiteralDefaultValue({
      name: "Headers",
      typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]"
    })).toEqual({});
  });
});

describe("descriptor-aware collection/dictionary detection (collectionKind, #945)", () => {
  const input = (overrides: Partial<StudioActivityInputDescriptor>): StudioActivityInputDescriptor =>
    ({ name: "X", typeName: "System.Int32", ...overrides });

  it("treats a collectionKind of List/Array/HashSet as a collection with the element-alias typeName", () => {
    for (const kind of ["List", "Array", "HashSet"] as const) {
      expect(describeCollectionForInput(input({ typeName: "System.Int32", collectionKind: kind })))
        .toEqual({ elementTypeName: "System.Int32" });
      expect(describeDictionaryForInput(input({ typeName: "System.Int32", collectionKind: kind }))).toBeNull();
    }
  });

  it("still parses a full collection type name when the backend sends one alongside the kind", () => {
    expect(describeCollectionForInput(input({ typeName: "System.String[]", collectionKind: "Array" })))
      .toEqual({ elementTypeName: "System.String" });
  });

  it("treats a collectionKind of Dictionary as a dictionary, defaulting the value type to the alias", () => {
    expect(describeDictionaryForInput(input({ typeName: "System.Int32", collectionKind: "Dictionary" })))
      .toEqual({ valueTypeName: "System.Int32" });
    expect(describeCollectionForInput(input({ typeName: "System.Int32", collectionKind: "Dictionary" }))).toBeNull();
  });

  it("does not treat a Single collectionKind as a collection or dictionary", () => {
    expect(describeCollectionForInput(input({ typeName: "System.Int32", collectionKind: "Single" }))).toBeNull();
    expect(describeDictionaryForInput(input({ typeName: "System.Int32", collectionKind: "Single" }))).toBeNull();
  });

  it("falls back to typeName parsing when collectionKind is absent (older backends)", () => {
    expect(describeCollectionForInput(input({ typeName: "System.Collections.Generic.List`1[System.String]" })))
      .toEqual({ elementTypeName: "System.String" });
    expect(describeDictionaryForInput(input({ typeName: "System.Collections.Generic.IDictionary`2[System.String,System.Int32]" })))
      .toEqual({ valueTypeName: "System.Int32" });
    expect(describeCollectionForInput(input({ typeName: "System.Int32" }))).toBeNull();
  });
});

function activity(nodeId: string): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-version`,
    inputs: [],
    outputs: [],
    structure: null
  };
}

function sequenceRoot(activities: ActivityNode[], nodeId = "root"): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-sequence-version`,
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.sequence.structure",
      schemaVersion: "1.0.0",
      payload: { activities }
    }
  };
}
