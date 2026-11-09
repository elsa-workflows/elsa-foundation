import React, { createRef } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Node } from "@xyflow/react";
import { activityDragDataType } from "../workflow-editor/constants";
import type { WorkflowNodeAvailabilityLookup } from "../workflow-editor/editorTypes";
import { SequenceDesigner, sequenceNodeDragDataType } from "../graph-authoring/SequenceDesigner";
import type { ChildSlot, WorkflowNodeData, WorkflowRuntimeNodeOverlay } from "../workflowAdapter";

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

describe("SequenceDesigner", () => {
  it("offers a workflow-start toggle only on editable trigger steps", () => {
    const onSetWorkflowStart = vi.fn();
    const trigger = workflowNode("endpoint", "Http Endpoint", { workflowStartEnabled: true });
    const { render } = mount([trigger], { selectedNodeId: "endpoint", onSetWorkflowStart });
    const toggle = container.querySelector<HTMLButtonElement>("button[aria-label='Start new workflows from Http Endpoint']")!;
    expect(toggle).toBeTruthy();
    expect(toggle.getAttribute("aria-pressed")).toBe("true");
    click(toggle);
    expect(onSetWorkflowStart).toHaveBeenCalledWith("endpoint", false);
    render([{ ...trigger, data: { ...trigger.data, workflowStartEnabled: false } }]);
    const disabledToggle = container.querySelector<HTMLButtonElement>("button[aria-label='Start new workflows from Http Endpoint']")!;
    expect(disabledToggle.getAttribute("aria-pressed")).toBe("false");
    click(disabledToggle);
    expect(onSetWorkflowStart).toHaveBeenLastCalledWith("endpoint", true);
    render([trigger], { readOnly: true });
    expect(container.querySelector("button[aria-label='Start new workflows from Http Endpoint']")).toBeNull();
    render([workflowNode("endpoint", "Write Line")]);
    expect(container.querySelector("button[title='Enable workflow start']")).toBeNull();
  });
  it("renders readable cards with accessible step positions and implicit connectors", () => {
    const { ref } = mount([
      workflowNode("first", "Start", { summary: "Runs first" }),
      workflowNode("second", "Write Line"),
      workflowNode("third", "Finish")
    ]);

    expect(cardLabels()).toEqual(["Start", "Write Line", "Finish"]);
    expect([...container.querySelectorAll(".wf-sequence-step")].map(item => item.getAttribute("aria-label"))).toEqual(["1. Start", "2. Write Line", "3. Finish"]);
    expect(container.querySelectorAll(".wf-sequence-connector")).toHaveLength(2);
    expect(container.querySelector(".wf-sequence-summary")?.textContent).toBe("Runs first");
    expect(container.querySelector(".wf-sequence-step[data-id='second']")?.getAttribute("data-graph-node-id")).toBe("second");
    expect(ref.current).toBe(container.querySelector(".wf-sequence-designer"));
  });

  it("keeps insertion boundaries and connectors in place when a card is selected", () => {
    const nodes = [workflowNode("first", "Start"), workflowNode("second", "Work"), workflowNode("third", "Finish")];
    const { render } = mount(nodes);
    const structure = () => ({
      boundaries: [...container.querySelectorAll<HTMLButtonElement>(".wf-sequence-insert")].map(button => button.dataset.sequenceInsertIndex),
      groups: [...container.querySelectorAll(".wf-sequence-group")].map(group => group.className),
      connectors: container.querySelectorAll(".wf-sequence-connector").length
    });
    const before = structure();

    render(nodes, { selectedNodeId: "second" });

    expect(structure()).toEqual(before);
    expect(container.querySelector(".wf-sequence-item.is-selected .wf-sequence-actions")).not.toBeNull();
  });

  it("shows an empty state and inserts the first step at boundary zero", () => {
    const { callbacks } = mount([]);

    expect(container.textContent).toContain("No steps yet");
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Add the first step']")!);
    expect(callbacks.onInsert).toHaveBeenCalledWith(0, { clientX: 0, clientY: 0 });
    expect(container.querySelector(".wf-sequence-list")).toBeNull();
  });

  it("renders runtime status, faults, and active, historical, and contained incident actions", () => {
    const onIncidentClick = vi.fn();
    const node = workflowNode("runtime-step", "Write Line", {
      runtimeNodeId: "runtime-write-line",
      runtime: runtimeOverlay({
        status: "Running",
        subStatus: "Retrying",
        faultCount: 2,
        incidentCount: 1,
        primaryIncidentId: "active-incident",
        hasBlockingIncident: true,
        containedIncidentCount: 2,
        containedAffectedActivityCount: 1,
        containedPrimaryIncidentId: "contained-incident",
        containsBlockingIncident: true
      }),
      onIncidentClick
    });
    const historyNode = workflowNode("history-step", "Timer", {
      runtime: runtimeOverlay({
        status: "Completed",
        incidentCount: 0,
        historicalIncidentCount: 3,
        primaryIncidentId: "historical-incident"
      }),
      onIncidentClick
    });
    const { callbacks } = mount([node, historyNode], { readOnly: true, selectedNodeId: "runtime-step" });

    expect(container.querySelector(".wf-sequence-runtime-strip")?.textContent).toContain("Running · Retrying");
    expect(container.querySelector(".wf-node-runtime-count")?.textContent).toBe("2 faults");
    expect(container.querySelector(".wf-node-runtime-count")?.getAttribute("data-health")).toBe("blocking");
    expect(container.querySelector(".wf-sequence-item")?.classList.contains("faulted")).toBe(true);
    expect(stepButton("runtime-step").dataset.runtimeNodeId).toBe("runtime-write-line");

    const activeAction = container.querySelector<HTMLButtonElement>("button[data-health='blocking']")!;
    expect(activeAction.getAttribute("aria-label")).toContain("1 active, needs intervention");
    click(activeAction);
    const containedAction = container.querySelector<HTMLButtonElement>(".wf-node-contained-incident-action")!;
    expect(containedAction.getAttribute("aria-label")).toContain("2 incidents inside Write Line");
    click(containedAction);
    const historyAction = container.querySelector<HTMLButtonElement>("button[data-health='history']")!;
    expect(historyAction.textContent).toContain("3 history");
    expect(historyAction.getAttribute("aria-label")).toContain("3 resolved or suppressed incidents");
    click(historyAction);

    expect(onIncidentClick.mock.calls).toEqual([["active-incident"], ["contained-incident", null], ["historical-incident"]]);
    expect(callbacks.onSelect).not.toHaveBeenCalled();
  });

  it("tracks parent selection for runtime cards and keeps the runtime view read-only", () => {
    const node = workflowNode("runtime-step", "Write Line", {
      runtime: runtimeOverlay({ status: "Completed" })
    });
    const { callbacks, render } = mount([node], { readOnly: true, selectedNodeId: "runtime-step" });

    expect(stepButton("runtime-step").getAttribute("aria-pressed")).toBe("true");
    expect(stepButton("runtime-step").draggable).toBe(false);
    expect(container.querySelectorAll(".wf-sequence-insert")).toHaveLength(0);
    expect(container.querySelectorAll(".wf-sequence-actions")).toHaveLength(0);

    render([node], { readOnly: true, selectedNodeId: null });
    expect(stepButton("runtime-step").getAttribute("aria-pressed")).toBe("false");
    click(stepButton("runtime-step"));
    pressKey(stepButton("runtime-step"), "Delete");
    pressKey(stepButton("runtime-step"), "Backspace");

    expect(callbacks.onSelect).toHaveBeenCalledWith("runtime-step");
    expect(callbacks.onDelete).not.toHaveBeenCalled();
    expect(callbacks.onMove).not.toHaveBeenCalled();
    expect(callbacks.onDuplicate).not.toHaveBeenCalled();
    expect(callbacks.onInsert).not.toHaveBeenCalled();
    expect(callbacks.onDropActivity).not.toHaveBeenCalled();
  });

  it("does not invite edits from an empty read-only sequence", () => {
    mount([], { readOnly: true });

    expect(container.querySelector(".wf-sequence-empty")?.textContent).toContain("No steps yet");
    expect(container.querySelector(".wf-sequence-empty")?.textContent).not.toContain("Add an activity");
    expect(container.querySelectorAll(".wf-sequence-insert")).toHaveLength(0);
  });

  it("exposes every insertion boundary and accepts palette and sequence drags", () => {
    const { callbacks } = mount([
      workflowNode("first", "Start"),
      workflowNode("second", "Work"),
      workflowNode("third", "Finish")
    ]);

    const boundaries = [...container.querySelectorAll<HTMLButtonElement>(".wf-sequence-insert")];
    expect(boundaries).toHaveLength(4);
    expect(boundaries.map(button => button.dataset.sequenceInsertIndex)).toEqual(["0", "1", "2", "3"]);
    expect(boundaries[3]?.textContent).toContain("Add step");
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Insert step between Start and Work']")!);
    expect(callbacks.onInsert).toHaveBeenCalledWith(1, { clientX: 0, clientY: 0 });

    drop(boundaries[3]!, transfer({ [activityDragDataType]: "write-line-v1" }));
    expect(callbacks.onDropActivity).toHaveBeenCalledWith("write-line-v1", 3);

    drop(boundaries[1]!, transfer({ [sequenceNodeDragDataType]: "second", "text/plain": "second" }));
    expect(callbacks.onMove).toHaveBeenCalledWith("second", 1);
    expect(callbacks.onDropActivity).toHaveBeenCalledTimes(1);
  });

  it("uses original-list insertion boundaries for the selected step's move controls", () => {
    const { callbacks } = mount([
      workflowNode("first", "Start"),
      workflowNode("middle", "Work"),
      workflowNode("last", "Finish")
    ], { selectedNodeId: "middle" });

    pressKey(stepButton("middle"), "Delete");
    pressKey(stepButton("middle"), "Backspace");
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Move Work up']")!);
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Move Work down']")!);
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Delete Work']")!);
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Duplicate Work']")!);

    expect(callbacks.onMove.mock.calls).toEqual([["middle", 0], ["middle", 3]]);
    expect(callbacks.onDelete.mock.calls).toEqual([["middle"], ["middle"], ["middle"]]);
    expect(callbacks.onDuplicate).toHaveBeenCalledWith("middle");
  });

  it("moves focus through the ordered cards and selects with Enter or Space", () => {
    const { callbacks } = mount([
      workflowNode("first", "Start"),
      workflowNode("middle", "Work"),
      workflowNode("last", "Finish")
    ]);

    const first = stepButton("first");
    first.focus();
    pressKey(first, "ArrowDown");
    expect(document.activeElement).toBe(stepButton("middle"));
    expect(stepButton("middle").tabIndex).toBe(0);
    expect(first.tabIndex).toBe(-1);
    pressKey(stepButton("middle"), "ArrowUp");
    expect(document.activeElement).toBe(first);
    pressKey(first, "ArrowDown");
    pressKey(stepButton("middle"), "End");
    expect(document.activeElement).toBe(stepButton("last"));
    pressKey(stepButton("last"), "Enter");
    expect(callbacks.onSelect).toHaveBeenCalledWith("last");
    pressKey(stepButton("last"), "ArrowUp");
    expect(document.activeElement).toBe(stepButton("middle"));
    pressKey(stepButton("middle"), "Home");
    expect(document.activeElement).toBe(first);
    pressKey(first, " ");
    expect(callbacks.onSelect).toHaveBeenLastCalledWith("first");
  });

  it("keeps slots navigable in read-only mode while removing mutation affordances", () => {
    const body = childSlot("body", "Body", [workflowNodeActivity("nested-a"), workflowNodeActivity("nested-b")]);
    const { callbacks } = mount([workflowNode("container", "For Each", { childSlots: [body] })], {
      selectedNodeId: "container",
      readOnly: true
    });

    expect(container.querySelectorAll(".wf-sequence-insert")).toHaveLength(0);
    expect(container.querySelectorAll(".wf-sequence-actions")).toHaveLength(0);
    expect(stepButton("container").draggable).toBe(false);
    expect(container.querySelector(".wf-sequence-designer")?.getAttribute("aria-label")).toBe("Sequence canvas");
    expect((container.querySelector(".wf-sequence-designer") as HTMLDivElement).tabIndex).toBe(-1);
    const openSlot = container.querySelector<HTMLButtonElement>("button[aria-label='Open Body, 2 steps']")!;
    expect(openSlot.disabled).toBe(false);
    click(openSlot);
    expect(callbacks.onOpenSlot).toHaveBeenCalledWith("container", "For Each", body);
    click(stepButton("container"));
    expect(callbacks.onSelect).toHaveBeenCalledWith("container");
    pressKey(stepButton("container"), "Delete");
    pressKey(stepButton("container"), "Backspace");
    expect(callbacks.onDelete).not.toHaveBeenCalled();
    expect(callbacks.onMove).not.toHaveBeenCalled();
  });

  it("shows the existing authored-activity unavailable state", () => {
    const availabilityLookup = vi.fn<WorkflowNodeAvailabilityLookup>(() => ({
      state: "RemovedFromCatalog",
      layer: "Catalog",
      referenceKind: "ActivityType",
      reason: "This activity is no longer in the catalog."
    }));
    mount([workflowNode("removed", "Old Activity")], { availabilityLookup });

    expect(availabilityLookup).toHaveBeenCalledWith({
      activityVersionId: "removed-v1",
      activityTypeKey: "Elsa.OldActivity"
    });
    expect(container.querySelector(".wf-sequence-availability")?.getAttribute("aria-label"))
      .toBe("No longer available for new use · Removed");
    expect(stepButton("removed").getAttribute("aria-label"))
      .toContain("No longer available for new use · Removed");
  });
});

function mount(nodes: Node<WorkflowNodeData>[], overrides: Partial<{
  selectedNodeId: string | null;
  readOnly: boolean;
  availabilityLookup: WorkflowNodeAvailabilityLookup;
  onSetWorkflowStart: (nodeId: string, enabled: boolean) => void;
}> = {}) {
  const ref = createRef<HTMLDivElement>();
  const callbacks = {
    onSelect: vi.fn(),
    onInsert: vi.fn(),
    onDropActivity: vi.fn(),
    onMove: vi.fn(),
    onDelete: vi.fn(),
    onDuplicate: vi.fn(),
    onOpenSlot: vi.fn()
  };
  flushSync(() => root.render(
    <SequenceDesigner
      nodes={nodes}
      selectedNodeId={null}
      readOnly={false}
      canvasRef={ref}
      {...callbacks}
      {...overrides}
    />
  ));
  const render = (nextNodes = nodes, nextOverrides: typeof overrides = {}) => flushSync(() => root.render(
    <SequenceDesigner
      nodes={nextNodes}
      selectedNodeId={null}
      readOnly={false}
      canvasRef={ref}
      {...callbacks}
      {...overrides}
      {...nextOverrides}
    />
  ));
  return { callbacks, ref, render };
}

function workflowNode(id: string, label: string, overrides: Partial<WorkflowNodeData> = {}): Node<WorkflowNodeData> {
  return {
    id,
    type: "workflowActivity",
    position: { x: 0, y: 0 },
    data: {
      label,
      activityVersionId: `${id}-v1`,
      activityTypeKey: `Elsa.${label.replaceAll(" ", "")}`,
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [],
      ...overrides
    }
  };
}

function workflowNodeActivity(id: string) {
  return { nodeId: id, activityVersionId: `${id}-v1`, inputs: [], outputs: [] };
}

function runtimeOverlay(overrides: Partial<WorkflowRuntimeNodeOverlay> = {}): WorkflowRuntimeNodeOverlay {
  return {
    faultCount: 0,
    incidentCount: 0,
    hasBlockingIncident: false,
    selected: false,
    ...overrides
  };
}

function childSlot(id: string, label: string, activities: ChildSlot["activities"]): ChildSlot {
  return { id, label, property: id, cardinality: "many", mode: "sequence", activities };
}

function cardLabels() {
  return [...container.querySelectorAll(".wf-sequence-title")].map(item => item.textContent);
}

function stepButton(id: string) {
  return container.querySelector<HTMLButtonElement>(`.wf-sequence-step[data-id='${id}']`)!;
}

function click(button: HTMLButtonElement) {
  flushSync(() => button.click());
}

function pressKey(button: HTMLButtonElement, key: string) {
  const event = new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true });
  flushSync(() => button.dispatchEvent(event));
}

function transfer(entries: Record<string, string>): DataTransfer {
  const data = new Map(Object.entries(entries));
  return {
    types: [...data.keys()],
    effectAllowed: "none",
    dropEffect: "none",
    setData(type: string, value: string) { data.set(type, value); },
    getData(type: string) { return data.get(type) ?? ""; },
    clearData(type?: string) { if (type) data.delete(type); else data.clear(); },
    files: [] as unknown as FileList,
    items: [] as unknown as DataTransferItemList
  } as unknown as DataTransfer;
}

function drop(target: HTMLButtonElement, dataTransfer: DataTransfer) {
  const event = new Event("drop", { bubbles: true, cancelable: true });
  Object.defineProperty(event, "dataTransfer", { value: dataTransfer });
  flushSync(() => target.dispatchEvent(event));
}
