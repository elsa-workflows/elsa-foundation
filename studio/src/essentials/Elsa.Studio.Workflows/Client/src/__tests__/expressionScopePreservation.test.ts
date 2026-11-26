// @vitest-environment node
import { expect, it, vi } from "vitest";
import type { StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { composeExpressionScopePreservation } from "../graph-authoring/expressionScopePreservation";
function contribution(language = "JavaScript", allows = vi.fn(() => true)): StudioExpressionEditorContribution {
  return { id: language, supports: () => true, surfaces: {}, variableAdditionPreservation: { language, revision: "1", allows } };
}
it("asks exactly the case-insensitive language owner and refuses missing or ambiguous owners", () => {
  const js = contribution(), other = contribution("Python");
  const composed = composeExpressionScopePreservation([js, other]);
  expect(composed.allowsVariableAddition({ language: "javascript" })).toBe(true);
  expect(other.variableAdditionPreservation!.allows).not.toHaveBeenCalled();
  expect(composed.allowsVariableAddition({ language: "missing" })).toBe(false);
  expect(composeExpressionScopePreservation([js, contribution()]).allowsVariableAddition({ language: "JavaScript" })).toBe(false);
  expect(composeExpressionScopePreservation([]).allowsVariableAddition({ language: "JavaScript" })).toBe(false);
});
it("refuses a faulting owner and changes the preview identity when its support is replaced or revised", () => {
  const owner = contribution();
  const initial = composeExpressionScopePreservation([owner]);
  expect(composeExpressionScopePreservation([owner]).revision).toBe(initial.revision);
  expect(composeExpressionScopePreservation([contribution()]).revision).not.toBe(initial.revision);
  owner.variableAdditionPreservation!.revision = "2";
  expect(composeExpressionScopePreservation([owner]).revision).not.toBe(initial.revision);
  owner.variableAdditionPreservation!.allows = () => { throw new Error("missing module"); };
  expect(composeExpressionScopePreservation([owner]).allowsVariableAddition({ language: "JavaScript" })).toBe(false);
});
