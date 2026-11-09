import { describe, expect, it } from "vitest";
import { compensationScopeStructureKind } from "../compensationScope";
import {
  compensateActivityType,
  confirmActivityType,
  deriveCompensationRuntime,
  recoveryLabel,
  recoveryStateLabel
} from "../compensationRuntime";
import type { ActivityExecutionStateSummary, ActivityNode } from "../workflowTypes";

// Spec 258 (M3) run inspection: nested scopes hand their open work to the enclosing scope, Confirm ends it, Compensate
// starts recovery on purpose, and a scope in a loop shows one occurrence at a time.
describe("nested compensation run state", () => {
  it("shows the work an inner scope handed over, with its origin, and undoes it in the outer scope's recovery", () => {
    const runtime = deriveCompensationRuntime(travelScope(), [
      execution("ae-outer", "outer", "Running", "10"),
      execution("ae-inner", "inner", "Completed", "11", { parent: "ae-outer", completed: "13", iteration: "it-1" }),
      execution("ae-hotel", "hotel", "Completed", "12", { parent: "ae-inner", completed: "12" }),
      execution("ae-charge", "charge", "Completed", "14", { parent: "ae-outer", completed: "14" }),
      execution("ae-confirm", "confirm-order", "Faulted", "15", { parent: "ae-outer" }),
      execution("ae-refund", "refund", "Completed", "16", { parent: "ae-outer", handler: true, scheduledBy: "ae-charge" }),
      execution("ae-cancel-hotel", "cancel-hotel", "Running", "17", { parent: "ae-outer", handler: true, scheduledBy: "ae-hotel" })
    ], [], { root: travelScope() });

    expect(runtime.phase).toBe("Compensating");
    expect(runtime.rows.map(row => [row.step, row.forward.state, row.recovery.state, row.recovery.order])).toEqual([
      ["Book travel", "Completed", "NotNeeded", undefined],
      ["Charge card", "Completed", "Compensated", 1],
      ["Confirm order", "Faulted", "NotApplicable", undefined]
    ]);
    expect(runtime.received).toEqual([{
      step: "Reserve hotel",
      forward: { state: "Completed", activityExecutionId: "ae-hotel" },
      recovery: { state: "Running", activityExecutionId: "ae-cancel-hotel", order: 2 },
      origin: { scopeNodeId: "inner", label: "Book hotel", activityExecutionId: "ae-inner", iterationId: "it-1" },
      handlerNodeId: "cancel-hotel"
    }]);
  });

  it("shows a completed inner scope's work as compensable by the enclosing scope, then undone there", () => {
    const before = [
      execution("ae-outer", "outer", "Running", "10"),
      execution("ae-inner", "inner", "Completed", "11", { parent: "ae-outer", completed: "13" }),
      execution("ae-hotel", "hotel", "Completed", "12", { parent: "ae-inner", completed: "12" })
    ];

    const waiting = deriveCompensationRuntime(innerScope(), before, [], { root: travelScope() });

    expect(waiting.phase).toBe("Completed");
    expect(waiting.rows[0].recovery).toEqual({
      state: "HandedOver",
      handedOverTo: { scopeNodeId: "outer", label: "Travel booking", activityExecutionId: "ae-outer" }
    });
    expect(recoveryLabel(waiting.rows[0].recovery)).toBe("Compensable by Travel booking");
    expect(recoveryStateLabel("HandedOver")).toBe("Compensable by the enclosing scope");

    const undone = deriveCompensationRuntime(innerScope(), [
      ...before,
      execution("ae-cancel-hotel", "cancel-hotel", "Completed", "20", { parent: "ae-outer", handler: true, scheduledBy: "ae-hotel" })
    ], [], { root: travelScope() });
    expect(undone.rows[0].recovery).toEqual({ state: "Compensated", activityExecutionId: "ae-cancel-hotel" });
  });

  it("keeps a completed scope's work when nothing encloses it, and when it confirms on completion", () => {
    const run = [
      execution("ae-inner", "inner", "Completed", "11", { completed: "13" }),
      execution("ae-hotel", "hotel", "Completed", "12", { parent: "ae-inner", completed: "12" })
    ];

    expect(deriveCompensationRuntime(innerScope(), run, []).rows[0].recovery.state).toBe("Kept");
    expect(deriveCompensationRuntime(innerScope({ confirmOnComplete: true }), run, []).rows[0].recovery)
      .toEqual({ state: "Confirmed", confirmedBy: "ae-inner" });
  });

  it("confirms the entries that existed when a Confirm completed, and no later ones", () => {
    const scope = scopeNode("outer", [
      { name: "Reserve hotel", activity: leaf("hotel"), compensation: leaf("cancel-hotel") },
      { name: "Confirm booking", activity: leaf("confirm-1"), compensation: null },
      { name: "Reserve car", activity: leaf("car"), compensation: leaf("cancel-car") }
    ]);

    const runtime = deriveCompensationRuntime(scope, [
      execution("ae-outer", "outer", "Running", "10"),
      execution("ae-hotel", "hotel", "Completed", "11", { parent: "ae-outer", completed: "11" }),
      execution("ae-confirm-1", "confirm-1", "Completed", "12", { parent: "ae-outer", completed: "12", type: confirmActivityType }),
      execution("ae-car", "car", "Completed", "13", { parent: "ae-outer", completed: "13" })
    ], [], { root: scope });

    expect(runtime.rows.map(row => [row.step, row.recovery.state, row.recovery.confirmedBy])).toEqual([
      ["Reserve hotel", "Confirmed", "ae-confirm-1"],
      ["Confirm booking", "NotNeeded", undefined],
      ["Reserve car", "Registered", undefined]
    ]);
  });

  it("lets a Confirm inside an inner scope confirm the outer scope it names", () => {
    const named = leaf("confirm-outer", { scope: "outer" });
    const inner = scopeNode("inner", [
      { name: "Reserve hotel", activity: leaf("hotel"), compensation: leaf("cancel-hotel") },
      { name: "Confirm all", activity: named, compensation: null }
    ], { displayName: "Book hotel" });
    const outer = scopeNode("outer", [
      { name: "Reserve flight", activity: leaf("flight"), compensation: leaf("cancel-flight") },
      { name: "Book travel", activity: inner, compensation: null }
    ], { displayName: "Travel booking" });

    const runtime = deriveCompensationRuntime(outer, [
      execution("ae-outer", "outer", "Running", "10"),
      execution("ae-flight", "flight", "Completed", "11", { parent: "ae-outer", completed: "11" }),
      execution("ae-inner", "inner", "Running", "12", { parent: "ae-outer" }),
      execution("ae-hotel", "hotel", "Completed", "13", { parent: "ae-inner", completed: "13" }),
      execution("ae-confirm-outer", "confirm-outer", "Completed", "14", { parent: "ae-inner", completed: "14", type: confirmActivityType })
    ], [], { root: outer });

    expect(runtime.rows[0].recovery).toEqual({ state: "Confirmed", confirmedBy: "ae-confirm-outer" });
  });

  it("names a Compensate as the cause and shows the step it stopped as uncertain", () => {
    const scope = scopeNode("outer", [
      { name: "Reserve hotel", activity: leaf("hotel"), compensation: leaf("cancel-hotel") },
      { name: "Pay", activity: leaf("pay"), compensation: null }
    ]);

    const runtime = deriveCompensationRuntime(scope, [
      execution("ae-outer", "outer", "Completed", "10", { outcomes: ["Compensated"] }),
      execution("ae-hotel", "hotel", "Completed", "11", { parent: "ae-outer", completed: "11" }),
      execution("ae-pay", "pay", "Cancelled", "12", { parent: "ae-outer" }),
      execution("ae-undo", "undo", "Completed", "13", { parent: "ae-pay", type: compensateActivityType }),
      execution("ae-cancel-hotel", "cancel-hotel", "Completed", "14", { parent: "ae-outer", handler: true, scheduledBy: "ae-hotel" })
    ], [], { root: scope });

    expect(runtime.phase).toBe("Compensated");
    expect(runtime.cause).toEqual({ kind: "explicit", step: "Pay", activityExecutionId: "ae-undo", requestedBy: "ae-undo" });
    expect(runtime.rows.map(row => [row.forward.state, row.recovery.state])).toEqual([
      ["Completed", "Compensated"],
      ["Uncertain", "Kept"]
    ]);
  });

  it("lists every occurrence of a scope in a loop and shows the one selected", () => {
    const run = [
      execution("ae-inner-1", "inner", "Completed", "10", { iteration: "it-1", completed: "11" }),
      execution("ae-hotel-1", "hotel", "Completed", "10", { parent: "ae-inner-1", completed: "10" }),
      execution("ae-inner-2", "inner", "Running", "20", { iteration: "it-2" }),
      execution("ae-hotel-2", "hotel", "Running", "21", { parent: "ae-inner-2" })
    ];

    const latestRun = deriveCompensationRuntime(innerScope(), run, []);
    expect(latestRun.occurrences).toEqual([
      { activityExecutionId: "ae-inner-1", label: "Occurrence 1 · iteration it-1", status: "Completed", iterationId: "it-1" },
      { activityExecutionId: "ae-inner-2", label: "Occurrence 2 · iteration it-2", status: "Running", iterationId: "it-2" }
    ]);
    expect(latestRun.scopeActivityExecutionId).toBe("ae-inner-2");
    expect(latestRun.rows[0].forward.state).toBe("Running");

    const first = deriveCompensationRuntime(innerScope(), run, [], { selectedActivityExecutionId: "ae-hotel-1" });
    expect(first.scopeActivityExecutionId).toBe("ae-inner-1");
    expect(first.rows[0].forward.state).toBe("Completed");
    expect(deriveCompensationRuntime(innerScope(), run.slice(0, 2), []).occurrences).toBeUndefined();
  });

  it("does not claim a handed-over entry is undone or kept on a partial page", () => {
    const runtime = deriveCompensationRuntime(travelScope(), [
      execution("ae-outer", "outer", "Running", "10"),
      execution("ae-inner", "inner", "Completed", "11", { parent: "ae-outer", completed: "13" }),
      execution("ae-hotel", "hotel", "Completed", "12", { parent: "ae-inner", completed: "12" }),
      execution("ae-confirm", "confirm-order", "Faulted", "15", { parent: "ae-outer" })
    ], [], { root: travelScope(), complete: false });

    expect(runtime.incomplete).toBe(true);
    expect(runtime.received?.[0].recovery.state).toBe("NotLoaded");
  });
});

function leaf(nodeId: string, inputs: Record<string, unknown> = {}): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-v1`,
    inputs: Object.entries(inputs).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })),
    outputs: []
  };
}

function scopeNode(
  nodeId: string,
  steps: { name: string; activity: ActivityNode | null; compensation: ActivityNode | null }[],
  { displayName, confirmOnComplete }: { displayName?: string; confirmOnComplete?: boolean } = {}
): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-v1`,
    inputs: confirmOnComplete ? [{ referenceKey: "confirmOnComplete", value: { expressionType: "Literal", value: true } }] : [],
    outputs: [],
    ...(displayName ? { displayName } : {}),
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  };
}

function innerScope(options: { confirmOnComplete?: boolean } = {}): ActivityNode {
  return scopeNode("inner", [{ name: "Reserve hotel", activity: leaf("hotel"), compensation: leaf("cancel-hotel") }], { displayName: "Book hotel", ...options });
}

// An outer scope whose first step is an inner scope, then a charge with a refund, then a confirmation without one.
function travelScope(): ActivityNode {
  return scopeNode("outer", [
    { name: "Book travel", activity: innerScope(), compensation: null },
    { name: "Charge card", activity: leaf("charge"), compensation: leaf("refund") },
    { name: "Confirm order", activity: leaf("confirm-order"), compensation: null }
  ], { displayName: "Travel booking" });
}

function execution(
  id: string,
  nodeId: string,
  status: string,
  at: string,
  options: { parent?: string; handler?: boolean; scheduledBy?: string; outcomes?: string[]; completed?: string; iteration?: string; type?: string } = {}
): ActivityExecutionStateSummary {
  return {
    activityExecutionId: id,
    workflowExecutionId: "wf-1",
    executableNodeId: nodeId,
    authoredActivityId: nodeId,
    activityType: options.type ?? "Elsa.Test",
    activityTypeVersion: "1.0.0",
    status,
    scheduledAt: `2026-10-20T10:00:${at}Z`,
    completedAt: options.completed ? `2026-10-20T10:00:${options.completed}Z` : null,
    parentActivityExecutionId: null,
    schedulingActivityExecutionId: options.scheduledBy ?? null,
    outcomeNames: options.outcomes ?? null,
    provenance: {
      parentActivityExecutionId: options.parent ?? null,
      schedulingActivityExecutionId: options.scheduledBy ?? null,
      iterationId: options.iteration ?? null,
      schedulingCause: options.handler ? "compensation" : "ActivityCompletion"
    },
    metadata: {}
  };
}
