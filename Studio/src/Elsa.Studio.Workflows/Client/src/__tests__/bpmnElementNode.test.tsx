import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { NodeProps } from "@xyflow/react";
import { BpmnElementNode } from "../bpmn/BpmnElementNode";
import type { BpmnNodeData } from "../bpmn/bpmnAdapter";
import { NodeSlotBadges } from "../workflow-editor/NodeSlotBadges";
import { WorkflowSlotNavigationContext, type WorkflowSlotNavigation } from "../workflow-editor/contexts";
import type { ChildSlot } from "../workflowAdapter";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return { ...actual, Handle: () => null };
});

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

function render(ui: React.ReactElement, slotNavigation: WorkflowSlotNavigation | null = null) {
  flushSync(() => root.render(
    <WorkflowSlotNavigationContext.Provider value={slotNavigation}>{ui}</WorkflowSlotNavigationContext.Provider>
  ));
}

function click(el: Element) {
  flushSync(() => el.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function badges() {
  return [...container.querySelectorAll(".wf-node-slot-badge")];
}

function slot(overrides: Partial<ChildSlot> = {}): ChildSlot {
  return {
    id: "elsa.bpmn.structure:activities",
    label: "Activities",
    property: "activities",
    cardinality: "many",
    mode: "bpmn",
    activities: [],
    ...overrides
  };
}

function bpmnNode(data: Partial<BpmnNodeData>) {
  const nodeData: BpmnNodeData = {
    element: { elementId: "sub-1", elementType: "subProcess" },
    label: "",
    childSlots: [],
    ...data
  };
  return <BpmnElementNode {...({ data: nodeData, selected: false } as unknown as NodeProps)} />;
}

const boundContainer = {
  nodeId: "node-sub",
  activityVersionId: "bpmn@1",
  activityTypeKey: "Elsa.BpmnProcess",
  label: "BPMN Process",
  icon: "activity" as const
};

describe("NodeSlotBadges", () => {
  it("renders one navigable badge per slot", () => {
    const navigate = vi.fn();
    render(<NodeSlotBadges ownerNodeId="node-1" ownerLabel="Owner" slots={[slot(), slot({ id: "s2", label: "Body" })]} />, navigate);

    expect(badges().map(badge => badge.textContent)).toEqual(["Activities", "Body"]);
    expect(badges().every(badge => badge.tagName === "BUTTON" && badge.classList.contains("nodrag"))).toBe(true);
  });

  it("renders nothing when the owner has no slots", () => {
    render(<NodeSlotBadges ownerNodeId="node-1" ownerLabel="Owner" slots={[]} />, vi.fn());
    expect(badges()).toHaveLength(0);
  });

  it("falls back to a static count badge with no navigation handler available", () => {
    render(<NodeSlotBadges ownerNodeId="node-1" ownerLabel="Owner" slots={[slot(), slot({ id: "s2" })]} />, null);

    const [only] = badges();
    expect(only.tagName).toBe("SPAN");
    expect(only.textContent).toBe("2 slots");
  });

  it("prefers a per-node onEnterSlot over the context handler", () => {
    const navigate = vi.fn();
    const perNode = vi.fn();
    render(<NodeSlotBadges ownerNodeId="node-1" ownerLabel="Owner" slots={[slot()]} onEnterSlot={perNode} />, navigate);

    click(badges()[0]);
    expect(perNode).toHaveBeenCalledWith(expect.objectContaining({ id: "elsa.bpmn.structure:activities" }));
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe("BpmnElementNode slot badges", () => {
  it("renders a badge per child slot on a subprocess", () => {
    render(bpmnNode({ boundActivity: boundContainer, childSlots: [slot()] }), vi.fn());
    expect(badges().map(badge => badge.textContent)).toEqual(["Activities"]);
  });

  it.each([
    ["start event", { elementId: "start", elementType: "startEvent" }],
    ["gateway", { elementId: "gw-1", elementType: "parallelGateway" }],
    ["unbound task", { elementId: "task-b", elementType: "task" }]
  ])("renders no badge on a %s", (_label, element) => {
    render(bpmnNode({ element, childSlots: [] }), vi.fn());
    expect(badges()).toHaveLength(0);
  });

  it("navigates with the BOUND ACTIVITY node id, not the element id", () => {
    const navigate = vi.fn();
    render(bpmnNode({ boundActivity: boundContainer, childSlots: [slot()] }), navigate);

    click(badges()[0]);

    const [ownerNodeId, , navigatedSlot] = navigate.mock.calls[0];
    expect(ownerNodeId).toBe("node-sub");
    // Routing the element id here resolves to no child and blanks the canvas; see bpmnAdapter.test.ts.
    expect(ownerNodeId).not.toBe("sub-1");
    expect(navigatedSlot).toMatchObject({ id: "elsa.bpmn.structure:activities" });
  });

  it("labels the crumb with the element name, falling back to the activity label", () => {
    const navigate = vi.fn();
    render(bpmnNode({
      element: { elementId: "sub-1", elementType: "subProcess", name: "Verify payment" },
      boundActivity: boundContainer,
      childSlots: [slot()]
    }), navigate);
    click(badges()[0]);
    expect(navigate.mock.calls[0][1]).toBe("Verify payment");

    render(bpmnNode({ boundActivity: boundContainer, childSlots: [slot()] }), navigate);
    click(badges()[0]);
    expect(navigate.mock.calls[1][1]).toBe("BPMN Process");
  });

  it("does not select the node when a badge is clicked", () => {
    const navigate = vi.fn();
    const onNodeClick = vi.fn();
    flushSync(() => root.render(
      <WorkflowSlotNavigationContext.Provider value={navigate}>
        <div onClick={onNodeClick}>{bpmnNode({ boundActivity: boundContainer, childSlots: [slot()] })}</div>
      </WorkflowSlotNavigationContext.Provider>
    ));

    click(badges()[0]);
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(onNodeClick).not.toHaveBeenCalled();
  });
});
