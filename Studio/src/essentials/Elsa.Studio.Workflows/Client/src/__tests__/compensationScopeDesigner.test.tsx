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
        cause: { step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "Order rejected" }
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
