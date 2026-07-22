import { describe, expect, it } from "vitest";
import { formatActivitySummary } from "../activitySummary";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

/** A minimal wrapped input value, the shape the editor keeps on the expanded node. */
function wrapped(type: string, value: unknown) {
  return { typeName: "System.String", expression: { type, value } };
}

/** A catalog item carrying `inputs` as the primary-input picker consumes them. */
function catalog(inputs: unknown[]): ActivityCatalogItem {
  return {
    activityVersionId: "v1",
    activityTypeKey: "Elsa.WriteLine",
    version: "1.0.0",
    category: "Primitives",
    displayName: "Write Line",
    executionType: "Action",
    inputs,
    outputs: []
  } as unknown as ActivityCatalogItem;
}

function node(props: Record<string, unknown>): ActivityNode {
  return { nodeId: "n1", activityVersionId: "v1", inputs: [], outputs: [], ...props } as ActivityNode;
}

const textInput = { name: "Text", referenceKey: "text", typeName: "System.String" };

describe("formatActivitySummary", () => {
  it("quotes a literal string from the primary input", () => {
    const summary = formatActivitySummary(node({ text: wrapped("Literal", "Hello, World!") }), catalog([textInput]));
    expect(summary).toBe('"Hello, World!"');
  });

  it("renders numeric and boolean literals verbatim", () => {
    expect(formatActivitySummary(node({ text: wrapped("Literal", 42) }), catalog([textInput]))).toBe("42");
    expect(formatActivitySummary(node({ text: wrapped("Literal", false) }), catalog([textInput]))).toBe("false");
  });

  it("reads an unwrapped plain literal value", () => {
    expect(formatActivitySummary(node({ text: "Plain" }), catalog([textInput]))).toBe('"Plain"');
  });

  it("returns undefined for empty, null, or structured literals (falls back to kind)", () => {
    expect(formatActivitySummary(node({ text: wrapped("Literal", "   ") }), catalog([textInput]))).toBeUndefined();
    expect(formatActivitySummary(node({ text: wrapped("Literal", null) }), catalog([textInput]))).toBeUndefined();
    expect(formatActivitySummary(node({ text: wrapped("Literal", { a: 1 }) }), catalog([textInput]))).toBeUndefined();
  });

  it("shows the raw source for a text expression", () => {
    const summary = formatActivitySummary(node({ text: wrapped("JavaScript", "total > 100") }), catalog([textInput]));
    expect(summary).toBe("total > 100");
  });

  it("captions reference expressions rather than dumping the reference object", () => {
    expect(formatActivitySummary(node({ text: wrapped("Variable", { referenceKey: "x" }) }), catalog([textInput]))).toBe("Variable value");
    expect(formatActivitySummary(node({ text: wrapped("Input", { referenceKey: "y" }) }), catalog([textInput]))).toBe("Workflow input");
  });

  it("returns undefined when the activity has no browsable inputs", () => {
    expect(formatActivitySummary(node({ text: wrapped("Literal", "hidden") }), catalog([]))).toBeUndefined();
    expect(formatActivitySummary(node({ text: wrapped("Literal", "hidden") }), catalog([{ ...textInput, isBrowsable: false }]))).toBeUndefined();
  });

  it("honors an explicit order when picking the primary input", () => {
    const inputs = [
      { name: "Second", referenceKey: "second", typeName: "System.String", order: 2 },
      { name: "First", referenceKey: "first", typeName: "System.String", order: 1 }
    ];
    const summary = formatActivitySummary(
      node({ first: wrapped("Literal", "picked"), second: wrapped("Literal", "ignored") }),
      catalog(inputs)
    );
    expect(summary).toBe('"picked"');
  });

  it("resolves values from a wire-shaped node (inputs[] rather than expanded props)", () => {
    const wireNode = node({ inputs: [{ referenceKey: "text", value: { value: "From wire", expressionType: "Literal" } }] });
    expect(formatActivitySummary(wireNode, catalog([textInput]))).toBe('"From wire"');
  });
});
