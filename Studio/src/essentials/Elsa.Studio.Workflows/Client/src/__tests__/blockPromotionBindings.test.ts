import { describe, expect, it, vi } from "vitest";
import type { StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { preservesJavaScriptWithAddedVariable } from "../../../../../../../extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/variableAdditionPreservation";
import { composePromotionExpressionSupport } from "../graph-authoring/promotionExpressionSupport";
import { promotionInputRead, readPromotionReference, rewritePromotionArgument } from "../graph-authoring/promotionBindings";
import { PromotionRefusal } from "../graph-authoring/blockPromotionTypes";

const owner = (): StudioExpressionEditorContribution => ({ id: "javascript", supports: () => true, surfaces: {},
  reusableBoundaryPreservation: { language: "JavaScript", revision: "closed-args-v1", allows: preservesJavaScriptWithAddedVariable } });
const argument = (source = "args.value + 1") => ({ referenceKey: "text", conversion: { profile: "authored" }, value: {
  expressionType: "JavaScript", value: { language: "JavaScript", source, capabilityProfile: "binding-pure-v1", resultType: { alias: "String", collectionKind: "Single" },
    options: {}, parameters: { value: { kind: "WorkflowRequest", memberKey: "original-input" } } }
} });
const refusal = (action: () => void) => {
  try { action(); } catch (error) { expect(error).toBeInstanceOf(PromotionRefusal); return (error as PromotionRefusal).diagnostic; }
  throw new Error("Expected a located promotion refusal");
};

describe("promotion expression boundary", () => {
  it("rewrites explicit parameter identities while retaining source, conversion and other argument data", () => {
    const authored = argument(); const originalSource = authored.value.value.source;
    rewritePromotionArgument(authored, "nodes.child.inputs[0]", composePromotionExpressionSupport([owner()]), (reference, path, parameter) => {
      expect(reference).toMatchObject({ kind: "input", key: "original-input" });
      expect(path).toBe("nodes.child.inputs[0].value.value.parameters.value"); expect(parameter).toBe(true);
      return promotionInputRead("reviewed-input", parameter);
    });
    expect(authored.value.value.source).toBe(originalSource);
    expect(authored.conversion).toEqual({ profile: "authored" });
    expect(authored.value.value.parameters.value).toEqual({ kind: "WorkflowRequest", memberKey: "reviewed-input" });
  });

  it.each(["variables.secret", "getVariable('secret')", "args.value + variables.secret", "args.value.constructor", "args.value /* variables.secret */"])("refuses an unproved dependency before rewriting: %s", source => {
    const authored = argument(source); const before = structuredClone(authored); const rewrite = vi.fn();
    expect(refusal(() => rewritePromotionArgument(authored, "nodes.child.inputs[0]", composePromotionExpressionSupport([owner()]), rewrite))).toMatchObject({ code: "opaque-expression", path: "nodes.child.inputs[0]" });
    expect(rewrite).not.toHaveBeenCalled(); expect(authored).toEqual(before);
  });

  it("does not treat the older variable-addition contribution as proof for a new closed boundary", () => {
    const old: StudioExpressionEditorContribution = { id: "old", supports: () => true, surfaces: {}, variableAdditionPreservation: { language: "JavaScript", revision: "1", allows: () => true } };
    expect(composePromotionExpressionSupport([old]).allows(argument().value.value)).toBe(false);
  });

  it("fails closed for missing, ambiguous and faulting language contributions and changes its revision", () => {
    const first = owner(); const support = composePromotionExpressionSupport([first]);
    expect(support.allows(argument().value.value)).toBe(true);
    expect(composePromotionExpressionSupport([]).allows(argument().value.value)).toBe(false);
    expect(composePromotionExpressionSupport([first, owner()]).allows(argument().value.value)).toBe(false);
    first.reusableBoundaryPreservation!.allows = () => { throw new Error("unavailable"); };
    const changed = composePromotionExpressionSupport([first]);
    expect(changed.revision).not.toBe(support.revision); expect(changed.allows(argument().value.value)).toBe(false);
  });

  it("refuses deferred/custom evaluation for parameter rewrites too", () => {
    for (const options of [{ autoEvaluate: false }, { evaluatorType: "custom" }]) {
      const authored = { ...argument(), ...options };
      expect(refusal(() => rewritePromotionArgument(authored, "nodes.child.inputs[0]", composePromotionExpressionSupport([owner()]), (_, __, parameter) => promotionInputRead("new", parameter))).code).toBe("custom-evaluation");
    }
  });

  it("never interprets a literal's string contents as references", () => {
    const authored = { referenceKey: "text", value: { expressionType: "Literal", value: { note: "variables.secret", nested: { producerNodeId: "child" } } } };
    const before = structuredClone(authored); const rewrite = vi.fn();
    rewritePromotionArgument(authored, "nodes.child.inputs[0]", undefined, rewrite);
    expect(rewrite).not.toHaveBeenCalled(); expect(authored).toEqual(before);
  });

  it("refuses unknown reference metadata and optional results rather than dropping semantics", () => {
    expect(refusal(() => readPromotionReference("Variable", { referenceKey: "x", futureScope: "outer" }, "input")).code).toBe("unsupported-reference-metadata");
    expect(refusal(() => readPromotionReference("ActivityResult", { producerNodeId: "x", projectionKey: "$result", isOptional: true }, "input")).code).toBe("unsupported-result");
  });
});
