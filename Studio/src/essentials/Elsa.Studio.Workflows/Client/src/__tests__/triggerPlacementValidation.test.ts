import { describe, expect, it } from "vitest";
import { readTriggerPlacementErrors } from "../workflow-editor/triggerPlacementValidation";

describe("typed publication Placement diagnostics", () => {
  const problem = { status: 400, errorCode: "workflow.trigger.placement", triggerNodeId: "timer",
    compositeNodeId: "sequence", compositeStructureKind: "elsa.sequence.structure", detail: "Place this trigger at an entry." };

  it("preserves the node pointer and guidance from the structured refusal", () => {
    expect(readTriggerPlacementErrors({ payload: problem })).toEqual([{ type: "workflow.trigger.placement", code: "workflow.trigger.placement",
      severity: "Error", path: "timer", message: problem.detail }]);
    expect(readTriggerPlacementErrors({ response: { data: problem } })).toHaveLength(1);
  });

  it.each([
    { ...problem, status: 500 }, { ...problem, errorCode: "ProviderIdentity" }, { ...problem, triggerNodeId: "" },
    { ...problem, compositeNodeId: null }, { ...problem, detail: "" }, null, [],
    { detail: "Start trigger 'timer' cannot start this workflow." }
  ])("does not pin an unrelated or malformed problem: %j", payload => {
    expect(readTriggerPlacementErrors({ payload })).toEqual([]);
  });
});
