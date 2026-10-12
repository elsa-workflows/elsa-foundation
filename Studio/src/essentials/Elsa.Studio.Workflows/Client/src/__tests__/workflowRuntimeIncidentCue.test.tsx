import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { NodeProps } from "@xyflow/react";
import { WorkflowActivityNode } from "../workflow-editor/graph";
import type { WorkflowNodeData } from "../workflowAdapter";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return { ...actual, Handle: () => null };
});

let root: Root | null = null;
let container: HTMLDivElement | null = null;

afterEach(() => {
  if (root && container) {
    flushSync(() => root!.unmount());
    container.remove();
  }
  root = null;
  container = null;
});

describe("workflow activity runtime incident cue", () => {
  it("renders an accessible action without treating activation as node selection", () => {
    const onIncidentClick = vi.fn();
    const onNodeClick = vi.fn();
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);
    const data: WorkflowNodeData = {
      label: "Send request",
      activityVersionId: "http@1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [],
      suppressFlowPorts: true,
      runtime: {
        status: "Faulted",
        faultCount: 1,
        incidentCount: 2,
        historicalIncidentCount: 0,
        primaryIncidentId: "incident-1",
        hasBlockingIncident: true,
        selected: false
      },
      onIncidentClick
    };
    flushSync(() => root!.render(
      <div onClick={onNodeClick}>
        <WorkflowActivityNode {...({ id: "activity-node", data, selected: false } as unknown as NodeProps)} />
      </div>
    ));

    const action = container.querySelector<HTMLButtonElement>("button.wf-node-incident-action");
    expect(action?.getAttribute("aria-label")).toContain("Open incidents for Send request, 2 active, needs intervention");
    expect(action?.getAttribute("data-health")).toBe("blocking");
    expect(action?.classList.contains("nokey")).toBe(true);
    const spaceKey = new KeyboardEvent("keydown", { key: " ", bubbles: true, cancelable: true });
    const enterKey = new KeyboardEvent("keyup", { key: "Enter", bubbles: true, cancelable: true });
    flushSync(() => action?.dispatchEvent(spaceKey));
    flushSync(() => action?.dispatchEvent(enterKey));
    expect(spaceKey.defaultPrevented).toBe(false);
    flushSync(() => action?.click());

    expect(onIncidentClick).toHaveBeenCalledWith("incident-1");
    expect(onNodeClick).not.toHaveBeenCalled();
  });

  it("labels a child incident as contained and leaves the parent Running", () => {
    const onIncidentClick = vi.fn();
    const onNodeClick = vi.fn();
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);
    const data: WorkflowNodeData = {
      label: "Sequence",
      activityVersionId: "sequence@1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [],
      runtime: {
        status: "Running",
        faultCount: 1,
        incidentCount: 0,
        hasBlockingIncident: false,
        containedIncidentCount: 3,
        containedAffectedActivityCount: 1,
        containedPrimaryIncidentId: "child-incident",
        containsBlockingIncident: true,
        selected: false
      },
      onIncidentClick
    };
    flushSync(() => root!.render(
      <div onClick={onNodeClick}>
        <WorkflowActivityNode {...({ id: "sequence-node", data, selected: false } as unknown as NodeProps)} />
      </div>
    ));

    const parent = container.querySelector<HTMLElement>(".wf-node");
    const action = container.querySelector<HTMLButtonElement>("button.wf-node-contained-incident-action");
    expect(parent?.classList.contains("faulted")).toBe(false);
    expect(parent?.querySelector(".wf-node-runtime-strip")?.textContent).toContain("Running");
    expect(parent?.querySelector(".wf-node-runtime-count")?.getAttribute("data-health")).toBe("neutral");
    expect(action?.textContent).toContain("3 inside");
    expect(action?.getAttribute("data-health")).toBe("contained-blocking");
    expect(action?.getAttribute("aria-label")).toContain("Open 3 incidents inside Sequence");
    expect(action?.getAttribute("aria-label")).not.toContain("child-incident");
    expect(action?.getAttribute("aria-label")).toContain("1 child activity");
    flushSync(() => action?.click());

    expect(onIncidentClick).toHaveBeenCalledWith("child-incident", null);
    expect(onNodeClick).not.toHaveBeenCalled();
  });
});
