import { describe, expect, it } from "vitest";
import { activityPaletteNotice, getActivityPaletteContext, isActivityOfferedInContext } from "../graph-authoring/activityPaletteContext";
import { createActivityNode, getChildSlots } from "../workflowAdapter";
import { bpmnActivity, sequenceActivity, sequenceNode, writeLine } from "./fixtures";

describe("activity palette context", () => {
  const process = createActivityNode(bpmnActivity, "process");
  const root = sequenceNode("root", [process]);
  const catalog = new Map([bpmnActivity, sequenceActivity, writeLine].map(item => [item.activityVersionId, item]));
  const context = (owner = root, kind: "workflow-definition" | "activity-definition-graph" = "workflow-definition") =>
    getActivityPaletteContext(kind, root, owner.nodeId, getChildSlots(owner, catalog)[0], catalog);
  const decision = { ...writeLine, category: "BPMN", paletteHints: { containerModes: ["bpmn"], description: "Use a BPMN gateway." } };

  it("keeps ordinary activities and nested BPMN containers available irrespective of category", () => {
    expect(isActivityOfferedInContext(bpmnActivity, context())).toBe(true);
    expect(isActivityOfferedInContext({ ...writeLine, category: "BPMN" }, context())).toBe(true);
  });

  it("offers a BPMN-specific leaf in a nested process but not its enclosing sequence", () => {
    expect(isActivityOfferedInContext(decision, context())).toBe(false);
    expect(isActivityOfferedInContext(decision, context(process))).toBe(true);
    expect(activityPaletteNotice(decision, context())).toBe("Use a BPMN gateway.");
    expect(activityPaletteNotice(decision, context(process))).toBeUndefined();
  });

  it("uses the destination slot and all enclosing scopes rather than just the root", () => {
    const scoped = { ...writeLine, paletteHints: { ancestorActivityTypes: [sequenceActivity.activityTypeKey], slotIds: [getChildSlots(process, catalog)[0].id] } };
    expect(isActivityOfferedInContext(scoped, context(process))).toBe(true);
    expect(isActivityOfferedInContext(scoped, context())).toBe(false);
    expect(isActivityOfferedInContext({ ...scoped, paletteHints: { ancestorActivityTypes: ["Absent.Scope"] } }, context(process))).toBe(false);
  });

  it("keeps reusable completion inside activity definitions, including nested scopes", () => {
    const complete = { ...writeLine, paletteHints: { resourceKinds: ["activity-definition-graph"] } };
    expect(isActivityOfferedInContext(complete, context(process))).toBe(false);
    expect(isActivityOfferedInContext(complete, context(process, "activity-definition-graph"))).toBe(true);
  });
});
