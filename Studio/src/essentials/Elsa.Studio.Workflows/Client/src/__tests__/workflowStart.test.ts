import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { getWorkflowStartEnabled, withWorkflowStartEnabled } from "../workflowStart";
import { writeLine } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";

const input = { name: "CanStartWorkflow", referenceKey: "CanStartWorkflow", typeName: "System.Boolean", isWrapped: true };
const catalog = { ...writeLine, inputs: [input] };
const node: ActivityNode = { nodeId: "trigger", activityVersionId: "trigger-v1", inputs: [], outputs: [] };

describe("workflow start shortcut", () => {
  it.each([true, false, "true", "false"])("reads an authored literal %s and preserves argument metadata on save", value => {
    const activity = expandStateFromWire({ rootActivity: { ...node, inputs: [{
      referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value }, autoEvaluate: false
    }] } }).rootActivity!;
    const enabled = value === true || value === "true";
    expect(getWorkflowStartEnabled(activity, catalog)).toBe(enabled);
    const updated = withWorkflowStartEnabled(activity, catalog, !enabled);
    expect(getWorkflowStartEnabled(updated, catalog)).toBe(!enabled);
    expect(getWorkflowStartEnabled(activity, catalog)).toBe(enabled);
    const wire = canonicalizeStateForWire({ rootActivity: updated }).rootActivity!;
    expect(wire.inputs).toEqual([{ referenceKey: "CanStartWorkflow", autoEvaluate: false,
      value: { expressionType: "Literal", value: String(!enabled) } }]);
  });

  it("uses the catalog default, including start-first Event and BPMN activities", () => {
    expect(getWorkflowStartEnabled(node, catalog)).toBe(false);
    expect(getWorkflowStartEnabled(node, { ...catalog, inputs: [{ ...input, defaultValue: "true" }] })).toBe(true);
  });

  it("does not offer or overwrite expressions and ordinary activities", () => {
    const expression = { ...node, CanStartWorkflow: { typeName: "System.Boolean", expression: { type: "JavaScript", value: "enabled" } } };
    expect(getWorkflowStartEnabled(expression, catalog)).toBeUndefined();
    expect(withWorkflowStartEnabled(expression, catalog, true)).toBe(expression);
    expect(getWorkflowStartEnabled(node, writeLine)).toBeUndefined();
    expect(withWorkflowStartEnabled(node, writeLine, true)).toBe(node);
  });

  it("updates a plain catalog input without changing its editor representation", () => {
    const plain = { ...catalog, inputs: [{ ...input, isWrapped: false }] };
    expect(withWorkflowStartEnabled(node, plain, true).CanStartWorkflow).toBe(true);
  });
});
