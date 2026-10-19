import { describe, expect, it } from "vitest";
import {
  addCompensationStep,
  compensationScopeStructureKind,
  compensationStepProblems,
  compensationStepsSlotId,
  moveCompensationStep,
  nextCompensationStepName,
  readCompensationSteps,
  removeCompensationStep,
  updateCompensationStep
} from "../compensationScope";
import { deriveCompensationRuntime, isCompensationHandlerExecution, ordinalLabel } from "../compensationRuntime";
import { buildCanvas, findActivityNode, getActivityDesignerSupport, getChildSlots, replaceSlotActivities, updateActivity } from "../workflowAdapter";
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary } from "../workflowTypes";

describe("compensation scope model", () => {
  it("exposes a Steps slot first, then a forward and a compensation slot per step", () => {
    const scope = reservationScope();

    const slots = getChildSlots(scope);

    expect(slots.map(slot => slot.id)).toEqual([
      compensationStepsSlotId,
      `${compensationScopeStructureKind}:steps:steps:activity:0`,
      `${compensationScopeStructureKind}:steps:steps:compensation:0`,
      `${compensationScopeStructureKind}:steps:steps:activity:1`,
      `${compensationScopeStructureKind}:steps:steps:compensation:1`
    ]);
    expect(slots[0].mode).toBe("compensation");
    expect(slots[0].activities.map(activity => activity.nodeId)).toEqual(["reserve", "release", "confirm"]);
    expect(getActivityDesignerSupport(scope)).toBe("sequence");
  });

  it("shows only the Steps slot as a canvas badge", () => {
    const scope = reservationScope();
    const { nodes } = buildCanvas({ owner: scopeNode([]), slot: { ...getChildSlots(scopeNode([]))[0], activities: [scope] } }, [], []);

    expect(nodes[0].data.childSlots.map(slot => slot.label)).toEqual(["Steps"]);
  });

  it("writes through the Steps slot by node id and never stores it as an array", () => {
    const scope = reservationScope();
    const steps = getChildSlots(scope)[0];
    const renamed = leaf("release", "Release (edited)");

    const next = replaceSlotActivities(scope, steps, [leaf("reserve"), renamed]);

    const payloadSteps = next.structure!.payload.steps as unknown[];
    expect(Array.isArray(payloadSteps)).toBe(true);
    expect(readCompensationSteps(next)).toEqual([
      { name: "Reserve inventory", activity: leaf("reserve"), compensation: renamed },
      { name: "Confirm order", activity: null, compensation: null }
    ]);
  });

  it("updates a handler inside the scope through the generic tree update", () => {
    const root = reservationScope();

    const next = updateActivity(root, "release", activity => ({ ...activity, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: "undo" } }] }));

    expect(findActivityNode(next, "release")?.inputs).toHaveLength(1);
    expect(readCompensationSteps(next)[0].compensation?.inputs).toHaveLength(1);
    expect(readCompensationSteps(next)[1].activity?.nodeId).toBe("confirm");
  });

  it("adds, moves, renames and removes steps", () => {
    const steps = readCompensationSteps(reservationScope());

    const added = addCompensationStep(steps, 1, leaf("deliver"));
    expect(added.map(step => step.name)).toEqual(["Reserve inventory", "Step 3", "Confirm order"]);
    expect(moveCompensationStep(added, 2, 0).map(step => step.name)).toEqual(["Confirm order", "Reserve inventory", "Step 3"]);
    expect(updateCompensationStep(added, 1, { name: "Reserve delivery" })[1].name).toBe("Reserve delivery");
    expect(removeCompensationStep(added, 0).map(step => step.name)).toEqual(["Step 3", "Confirm order"]);
    expect(nextCompensationStepName([{ name: "Step 2", activity: null, compensation: null }])).toBe("Step 3");
  });

  it("reports blank and duplicate names and a missing activity per row", () => {
    const problems = compensationStepProblems([
      { name: "Reserve", activity: leaf("a"), compensation: null },
      { name: "Reserve ", activity: leaf("b"), compensation: null },
      { name: " ", activity: leaf("c"), compensation: null },
      { name: "Confirm", activity: null, compensation: null }
    ]);

    expect([...problems.entries()]).toEqual([
      [0, 'Step "Reserve" is defined more than once.'],
      [1, 'Step "Reserve" is defined more than once.'],
      [2, "Step needs a name."],
      [3, 'Step "Confirm" has no activity.']
    ]);
  });
});

describe("compensation runtime", () => {
  it("shows a compensated run newest first with the recovery cause", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), [
      execution("ae-scope", "scope", "Completed", "10", { outcomeNames: ["Compensated"] }),
      execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" }),
      execution("ae-2", "deliver", "Completed", "12", { parent: "ae-scope" }),
      execution("ae-3", "confirm", "Faulted", "13", { parent: "ae-scope" }),
      execution("ae-4", "cancel", "Completed", "14", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" }),
      execution("ae-5", "release", "Completed", "15", { parent: "ae-scope", handler: true, scheduledBy: "ae-1" })
    ], [incident("inc-1", "ae-3", "order.rejected", "Order rejected")]);

    expect(runtime.phase).toBe("Compensated");
    expect(runtime.rows.map(row => [row.step, row.forward.state, row.recovery.state, row.recovery.order])).toEqual([
      ["Reserve inventory", "Completed", "Compensated", 2],
      ["Reserve delivery", "Completed", "Compensated", 1],
      ["Confirm order", "Faulted", "NotApplicable", undefined]
    ]);
    expect(runtime.cause).toEqual({
      step: "Confirm order",
      activityExecutionId: "ae-3",
      incidentId: "inc-1",
      failureType: "order.rejected",
      message: "Order rejected"
    });
  });

  it("shows pending and failed recovery while the scope is still compensating", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), [
      execution("ae-scope", "scope", "Running", "10"),
      execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" }),
      execution("ae-2", "deliver", "Completed", "12", { parent: "ae-scope" }),
      execution("ae-3", "confirm", "Faulted", "13", { parent: "ae-scope" }),
      execution("ae-4", "cancel", "Faulted", "14", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" })
    ], []);

    expect(runtime.phase).toBe("Compensating");
    expect(runtime.rows.map(row => row.recovery.state)).toEqual(["Pending", "Failed", "NotApplicable"]);
  });

  it("shows a completed scope as undoable but not undone", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), [
      execution("ae-scope", "scope", "Completed", "10", { outcomeNames: ["Done"] }),
      execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" }),
      execution("ae-2", "deliver", "Completed", "12", { parent: "ae-scope" }),
      execution("ae-3", "confirm", "Completed", "13", { parent: "ae-scope" })
    ], []);

    expect(runtime.phase).toBe("Completed");
    expect(runtime.rows.map(row => row.recovery.state)).toEqual(["Registered", "Registered", "NotNeeded"]);
    expect(runtime.cause).toBeUndefined();
  });

  it("shows nothing as run before the scope starts", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), [], []);

    expect(runtime.phase).toBe("NotStarted");
    expect(runtime.rows.every(row => row.forward.state === "NotRun" && row.recovery.state === "NotApplicable")).toBe(true);
  });

  it("recognises handler executions by their scheduling cause", () => {
    expect(isCompensationHandlerExecution({ provenance: { schedulingCause: "compensation" } })).toBe(true);
    expect(isCompensationHandlerExecution({ provenance: { schedulingCause: "ActivityCompletion" } })).toBe(false);
    expect(isCompensationHandlerExecution({})).toBe(false);
    expect([1, 2, 3, 4, 11, 12, 21].map(ordinalLabel)).toEqual(["1st", "2nd", "3rd", "4th", "11th", "12th", "21st"]);
  });
});

function leaf(nodeId: string, label?: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [], ...(label ? { description: label } : {}) } as ActivityNode;
}

function scopeNode(steps: { name: string; activity: ActivityNode | null; compensation: ActivityNode | null }[]): ActivityNode {
  return {
    nodeId: "scope",
    activityVersionId: "scope-v1",
    inputs: [],
    outputs: [],
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  };
}

function reservationScope(): ActivityNode {
  return scopeNode([
    { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
    { name: "Confirm order", activity: leaf("confirm"), compensation: null }
  ]);
}

function journeyScope(): ActivityNode {
  return scopeNode([
    { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
    { name: "Reserve delivery", activity: leaf("deliver"), compensation: leaf("cancel") },
    { name: "Confirm order", activity: leaf("confirm"), compensation: null }
  ]);
}

function execution(
  id: string,
  nodeId: string,
  status: string,
  at: string,
  options: { parent?: string; handler?: boolean; scheduledBy?: string; outcomeNames?: string[] } = {}
): ActivityExecutionStateSummary {
  return {
    activityExecutionId: id,
    workflowExecutionId: "wf-1",
    executableNodeId: nodeId,
    authoredActivityId: nodeId,
    activityType: "Elsa.Test",
    activityTypeVersion: "1.0.0",
    status,
    scheduledAt: `2026-10-19T10:00:${at}Z`,
    parentActivityExecutionId: options.parent ?? null,
    schedulingActivityExecutionId: options.scheduledBy ?? null,
    outcomeNames: options.outcomeNames ?? null,
    provenance: options.handler ? { schedulingCause: "compensation", schedulingActivityExecutionId: options.scheduledBy } : { schedulingCause: "ActivityCompletion" },
    metadata: {}
  };
}

function incident(incidentId: string, activityExecutionId: string, failureType: string, message: string): IncidentStateSummary {
  return {
    incidentId,
    workflowExecutionId: "wf-1",
    activityExecutionId,
    severity: "Error",
    status: "Resolved",
    resolutionAction: "AbsorbFault",
    failureType,
    message,
    createdAt: "2026-10-19T10:00:13Z",
    isBlocking: false,
    metadata: {}
  };
}
