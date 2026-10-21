import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Node } from "@xyflow/react";
import { CompensationScopeDesigner, type CompensationScopeDesignerProps } from "../graph-authoring/CompensationScopeDesigner";
import { compensationScopeStructureKind, type CompensationStep } from "../compensationScope";
import type { CompensationScopeRuntime } from "../compensationRuntime";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("CompensationScopeDesigner", () => {
  it("pairs each step with its compensation and names both for assistive technology", () => {
    mount();

    expect(rowLabels()).toEqual([
      "Step 1, Reserve inventory, compensated by Release inventory",
      "Step 2, Confirm order, no compensation"
    ]);
    expect(button("Step 1, Reserve inventory: Reserve inventory")).not.toBeNull();
    expect(button("Compensation for Reserve inventory: Release inventory")).not.toBeNull();
    expect(container.textContent).toContain("Undoes: Reserve inventory");
    expect(button("Add compensation for Confirm order")).not.toBeNull();
  });

  it("asks for an activity for a new step, a step activity or a compensation", () => {
    const { props } = mount();

    click(button("Add compensation for Confirm order"));
    click([...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent?.trim() === "Add step")!);

    expect(props.onRequestActivity).toHaveBeenNthCalledWith(1, { kind: "compensation", index: 1 }, expect.anything());
    expect(props.onRequestActivity).toHaveBeenNthCalledWith(2, { kind: "new-step", index: 2 }, expect.anything());
  });

  it("applies edits to the current steps, so a concurrent change to an activity is kept", () => {
    const { props } = mount();

    click(button("Remove compensation for Reserve inventory"));

    const edit = (props.onChangeSteps as ReturnType<typeof vi.fn>).mock.calls[0][0] as (steps: CompensationStep[]) => CompensationStep[];
    const edited = { ...leaf("reserve"), inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: "changed meanwhile" } }] } as ActivityNode;
    const result = edit([
      { name: "Reserve inventory", activity: edited, compensation: leaf("release") },
      { name: "Confirm order", activity: leaf("confirm"), compensation: null }
    ]);

    expect(result[0].activity).toBe(edited);
    expect(result[0].compensation).toBeNull();
  });

  it("renames, reorders, removes compensation and deletes steps", () => {
    const { props } = mount();

    const input = container.querySelector<HTMLInputElement>("input[aria-label='Name of step 2']")!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(input, "Confirm");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    click(button("Move Confirm order up"));
    click(button("Remove compensation for Reserve inventory"));
    click(button("Delete step Reserve inventory"));

    // Each change is an edit of the current steps; apply it to the steps the designer rendered.
    const rendered = [
      { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
      { name: "Confirm order", activity: leaf("confirm"), compensation: null }
    ];
    const calls = (props.onChangeSteps as ReturnType<typeof vi.fn>).mock.calls
      .map(call => (call[0] as (steps: CompensationStep[]) => CompensationStep[])(rendered));
    expect(calls[0].map(step => step.name)).toEqual(["Reserve inventory", "Confirm"]);
    expect(calls[1].map(step => step.name)).toEqual(["Confirm order", "Reserve inventory"]);
    expect(calls[2][0].compensation).toBeNull();
    expect(calls[3].map(step => step.name)).toEqual(["Confirm order"]);
  });

  it("shows row problems next to the row they belong to", () => {
    mount({ owner: scope([
      { name: "Same", activity: leaf("a"), compensation: null },
      { name: "Same", activity: leaf("b"), compensation: null }
    ]), nodes: [node("a", "A"), node("b", "B")] });

    const input = container.querySelector<HTMLInputElement>("input[aria-label='Name of step 1']")!;
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(document.getElementById(input.getAttribute("aria-describedby")!)?.textContent).toContain('Step "Same" is defined more than once.');
  });

  it("shows forward and recovery state as text in run inspection and selects occurrences", () => {
    const onSelectExecution = vi.fn();
    mount({
      readOnly: true,
      onSelectExecution,
      runtime: {
        scopeActivityExecutionId: "ae-scope",
        phase: "Compensated",
        rows: [
          { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-5", order: 1 } },
          { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
        ],
        cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "Order rejected" }
      } satisfies CompensationScopeRuntime
    });

    expect(container.querySelector(".wf-compensation-phase")?.textContent).toContain("Compensated");
    expect(container.querySelector(".wf-compensation-cause")?.textContent).toContain("Recovery started because Confirm order failed: order.rejected — Order rejected.");
    expect([...container.querySelectorAll(".wf-compensation-state")].map(chip => chip.getAttribute("aria-label"))).toEqual([
      "Forward: Completed. Show this occurrence",
      "Recovery: Compensated (1st). Show this occurrence",
      "Forward: Faulted. Show this occurrence",
      "Recovery: Not applicable"
    ]);
    expect(container.querySelector("input")).toBeNull();
    expect(button("Add compensation for Confirm order")).toBeNull();

    click(button("Recovery: Compensated (1st). Show this occurrence"));
    click([...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === "Show failed step")!);
    expect(onSelectExecution.mock.calls).toEqual([["ae-5"], ["ae-3"]]);
  });

  it("shows uncertain steps and handlers that need an operator, with their attempts and incident", () => {
    const onOpenIncident = vi.fn();
    mount({
      readOnly: true,
      onSelectExecution: vi.fn(),
      onOpenIncident,
      runtime: {
        scopeActivityExecutionId: "ae-scope",
        phase: "AwaitingIntervention",
        rows: [
          { step: "Reserve inventory", forward: { state: "Uncertain", activityExecutionId: "ae-1" }, recovery: { state: "NeedsAction", activityExecutionId: "ae-6", order: 1, attempts: { count: 3, max: 3 }, incidentId: "inc-6" } },
          { step: "Confirm order", forward: { state: "NotRun" }, recovery: { state: "NotApplicable" } }
        ],
        cause: { kind: "uncertain", step: "Reserve inventory", activityExecutionId: "ae-1", failureType: "inventory.timeout" }
      } satisfies CompensationScopeRuntime
    });

    expect(container.querySelector(".wf-compensation-phase")?.textContent).toContain("Needs action");
    expect(container.querySelector(".wf-compensation-cause")?.textContent).toContain("Recovery started because Reserve inventory could not confirm its outcome: inventory.timeout.");
    expect([...container.querySelectorAll(".wf-compensation-state")].slice(0, 2).map(chip => chip.getAttribute("aria-label"))).toEqual([
      "Forward: Uncertain. Show this occurrence",
      "Recovery: Needs action (1st) · Attempt 3 of 3. Show this occurrence"
    ]);
    click(button("Show the incident Reserve inventory is waiting on"));
    expect(onOpenIncident).toHaveBeenCalledWith("inc-6");
  });

  it("moves focus between the same cell of neighbouring rows with the arrow keys", () => {
    mount({ owner: scope([
      { name: "One", activity: leaf("a"), compensation: null },
      { name: "Two", activity: leaf("b"), compensation: null }
    ]), nodes: [node("a", "A"), node("b", "B")] });

    const first = button("Step 1, One: A")!;
    first.focus();
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));

    expect(document.activeElement).toBe(button("Step 2, Two: B"));
  });

  it("offers the first step in an empty scope", () => {
    const { props } = mount({ owner: scope([]), nodes: [] });

    expect(container.textContent).toContain("No steps yet");
    click([...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent?.trim() === "Add first step")!);
    expect(props.onRequestActivity).toHaveBeenCalledWith({ kind: "new-step", index: 0 }, expect.anything());
  });
});

describe("CompensationScopeDesigner scheduled retry (spec 261)", () => {
  it("shows a waiting retry as text and icon with its attempt and due time, and offers no action", () => {
    const dueAt = new Date(Date.now() + 60_000).toISOString();
    mount({
      readOnly: true,
      onSelectExecution: vi.fn(),
      onOpenIncident: vi.fn(),
      runtime: {
        scopeActivityExecutionId: "ae-scope",
        phase: "Compensating",
        rows: [
          { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "RetryScheduled", activityExecutionId: "ae-4", order: 1, attempts: { count: 2, max: 3 }, retryNotBefore: dueAt } },
          { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
        ]
      } satisfies CompensationScopeRuntime
    });

    const chip = container.querySelectorAll(".wf-compensation-state")[1];
    expect(chip.getAttribute("data-state")).toBe("RetryScheduled");
    expect(chip.getAttribute("aria-label")).toMatch(/^Recovery: Retry scheduled \(1st\) · Attempt 2 of 3 · after \d{1,2}:\d{2}:\d{2}.*\. Show this occurrence$/);
    expect(chip.querySelector("svg")).not.toBeNull();
    expect(container.querySelector(".wf-compensation-actions, [data-operator-action]")).toBeNull();
    expect(button("Show the incident Reserve inventory is waiting on")).toBeNull();
  });

  it("names the date of a waiting retry that is due on another day", () => {
    const dueAt = new Date(Date.now() + 2 * 24 * 60 * 60_000);
    mount({
      readOnly: true,
      onSelectExecution: vi.fn(),
      onOpenIncident: vi.fn(),
      runtime: {
        scopeActivityExecutionId: "ae-scope",
        phase: "Compensating",
        rows: [
          { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "RetryScheduled", activityExecutionId: "ae-4", order: 1, attempts: { count: 2, max: 3 }, retryNotBefore: dueAt.toISOString() } },
          { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
        ]
      } satisfies CompensationScopeRuntime
    });

    const label = container.querySelectorAll(".wf-compensation-state")[1].getAttribute("aria-label") ?? "";
    expect(label).toContain(` · after ${dueAt.toLocaleString(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit", year: "numeric", month: "short", day: "numeric" })}`);
    expect(label).toContain(String(dueAt.getFullYear()));
  });

  it("says a retry is due without a time once its due time passed", () => {
    mount({
      readOnly: true,
      runtime: {
        scopeActivityExecutionId: "ae-scope",
        phase: "Compensating",
        rows: [
          { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "RetryScheduled", order: 1, attempts: { count: 2, max: 3 }, retryNotBefore: "2020-01-01T00:00:00Z" } },
          { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
        ]
      } satisfies CompensationScopeRuntime
    });

    expect(container.querySelectorAll(".wf-compensation-state")[1].getAttribute("aria-label")).toBe("Recovery: Retry due (1st) · Attempt 2 of 3");
  });
});

describe("CompensationScopeDesigner nesting states (spec 258)", () => {
  it("lists handed-over work with a link to the inner scope it came from", () => {
    const onSelectExecution = vi.fn();
    mount({ readOnly: true, onSelectExecution, runtime: {
      scopeActivityExecutionId: "ae-outer",
      phase: "Compensating",
      rows: [
        { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-4", order: 1 } },
        { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
      ],
      received: [{
        step: "Reserve hotel",
        forward: { state: "Completed", activityExecutionId: "ae-hotel" },
        recovery: { state: "Running", activityExecutionId: "ae-cancel-hotel", order: 2 },
        origin: { scopeNodeId: "inner", label: "Book hotel", activityExecutionId: "ae-inner", iterationId: "it-2" }
      }]
    } });

    expect(document.getElementById(container.querySelector(".wf-compensation-received")!.getAttribute("aria-labelledby")!)?.textContent)
      .toBe("Handed over by inner scopes");
    expect(rowLabels().at(-1)).toBe("Reserve hotel, from Book hotel (iteration it-2)");
    click(button("Show Book hotel (iteration it-2), where Reserve hotel ran"));
    expect(onSelectExecution).toHaveBeenCalledWith("ae-inner");
    expect(button("Recovery: Undoing (2nd). Show this occurrence")).not.toBeNull();
  });

  it("shows Confirmed and compensable-by states as text and icon, and opens what confirmed a step", () => {
    const onSelectExecution = vi.fn();
    mount({ readOnly: true, onSelectExecution, runtime: {
      scopeActivityExecutionId: "ae-inner",
      phase: "Completed",
      rows: [
        { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Confirmed", confirmedBy: "ae-confirm" } },
        { step: "Confirm order", forward: { state: "Completed", activityExecutionId: "ae-3" },
          recovery: { state: "HandedOver", handedOverTo: { scopeNodeId: "outer", label: "Travel booking", activityExecutionId: "ae-outer" } } }
      ]
    } });

    const chips = [...container.querySelectorAll(".wf-compensation-state[data-kind='recovery']")];
    expect(chips.map(chip => chip.getAttribute("aria-label"))).toEqual(["Recovery: Confirmed", "Recovery: Compensable by Travel booking. Show this occurrence"]);
    expect(chips.every(chip => chip.querySelector("svg"))).toBe(true);
    click(button("Show what confirmed Reserve inventory"));
    expect(onSelectExecution).toHaveBeenCalledWith("ae-confirm");
    click(button("Recovery: Compensable by Travel booking. Show this occurrence"));
    expect(onSelectExecution).toHaveBeenLastCalledWith("ae-outer");
  });

  it("names a Compensate request as the cause and says a cancelled scope undid nothing", () => {
    const onSelectExecution = vi.fn();
    mount({ readOnly: true, onSelectExecution, runtime: {
      scopeActivityExecutionId: "ae-scope",
      phase: "Compensated",
      rows: [
        { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-4", order: 1 } },
        { step: "Confirm order", forward: { state: "Uncertain", activityExecutionId: "ae-3" }, recovery: { state: "Kept" } }
      ],
      cause: { kind: "explicit", step: "Confirm order", activityExecutionId: "ae-undo", requestedBy: "ae-undo" }
    } });

    expect(container.querySelector(".wf-compensation-cause")?.textContent).toContain("Recovery started because a Compensate activity asked for it while Confirm order was running.");
    click([...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === "Show request")!);
    expect(onSelectExecution).toHaveBeenCalledWith("ae-undo");

    mount({ readOnly: true, runtime: {
      scopeActivityExecutionId: "ae-scope",
      phase: "Cancelled",
      rows: [{ step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Kept" } }]
    } });
    expect(container.querySelector(".wf-compensation-phase")?.textContent).toContain("Cancelled — nothing undone");
  });

  it("picks a scope occurrence from a labelled list when the scope ran more than once", () => {
    const onSelectExecution = vi.fn();
    mount({ readOnly: true, onSelectExecution, runtime: {
      scopeActivityExecutionId: "ae-inner-2",
      phase: "Running",
      rows: [{ step: "Reserve inventory", forward: { state: "Running", activityExecutionId: "ae-2" }, recovery: { state: "NotApplicable" } }],
      occurrences: [
        { activityExecutionId: "ae-inner-1", label: "Occurrence 1 · iteration it-1", status: "Completed", iterationId: "it-1" },
        { activityExecutionId: "ae-inner-2", label: "Occurrence 2 · iteration it-2", status: "Running", iterationId: "it-2" }
      ]
    } });

    const picker = container.querySelector<HTMLSelectElement>(".wf-compensation-occurrence select")!;
    expect([...picker.labels!].map(label => label.textContent)).toEqual(["Occurrence"]);
    expect(picker.value).toBe("ae-inner-2");
    expect([...picker.options].map(option => option.textContent)).toEqual(["Occurrence 1 · iteration it-1 · Completed", "Occurrence 2 · iteration it-2 · Running"]);
    expect(document.getElementById(picker.getAttribute("aria-describedby")!)?.textContent).toBe("This scope ran 2 times. Choose the occurrence to show.");
    flushSync(() => {
      picker.value = "ae-inner-1";
      picker.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(onSelectExecution).toHaveBeenCalledWith("ae-inner-1");
  });
});

function mount(overrides: Partial<CompensationScopeDesignerProps> = {}) {
  const props: CompensationScopeDesignerProps = {
    owner: scope([
      { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
      { name: "Confirm order", activity: leaf("confirm"), compensation: null }
    ]),
    nodes: [node("reserve", "Reserve inventory"), node("release", "Release inventory"), node("confirm", "Confirm order")],
    selectedNodeId: null,
    readOnly: false,
    onSelect: vi.fn(),
    onChangeSteps: vi.fn(),
    onRequestActivity: vi.fn(),
    ...overrides
  };
  flushSync(() => root.render(<CompensationScopeDesigner {...props} />));
  return { props };
}

function rowLabels() {
  return [...container.querySelectorAll(".wf-compensation-row")].map(row => row.getAttribute("aria-label"));
}

function button(label: string) {
  return container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
}

function click(element: HTMLElement | null | undefined) {
  if (!element) throw new Error("Element not found.");
  flushSync(() => element.click());
}

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

function scope(steps: CompensationStep[]): ActivityNode {
  return {
    nodeId: "scope",
    activityVersionId: "scope-v1",
    inputs: [],
    outputs: [],
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  };
}

function node(id: string, label: string): Node<WorkflowNodeData> {
  return {
    id,
    type: "workflowActivity",
    position: { x: 0, y: 0 },
    data: { label, activityVersionId: `${id}-v1`, activityTypeKey: `Elsa.${label.replaceAll(" ", "")}`, childSlots: [], acceptsInbound: true, sourcePorts: [] }
  };
}
