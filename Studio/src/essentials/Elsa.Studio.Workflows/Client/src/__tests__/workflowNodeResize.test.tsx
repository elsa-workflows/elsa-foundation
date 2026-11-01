import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { ReactFlowProvider, type NodeProps } from "@xyflow/react";
import { WorkflowActivityNode } from "../workflow-editor/graph";
import { WorkflowNodeResizeContext, type WorkflowNodeResize } from "../workflow-editor/contexts";
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

const data: WorkflowNodeData = {
  label: "Write Line",
  activityVersionId: "write-line@1",
  childSlots: [],
  acceptsInbound: true,
  sourcePorts: []
};

// `savedWidth` is the node's explicit width in the store; `measuredWidth` is what React Flow passes as
// the `width` prop, which is the measured size (0 before the first measure), never the saved one.
function renderNode({ selected, resize = null, savedWidth, measuredWidth = 0 }: { selected: boolean; resize?: WorkflowNodeResize | null; savedWidth?: number; measuredWidth?: number }) {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  const initialNodes = [{ id: "node-1", position: { x: 0, y: 0 }, data, ...(savedWidth !== undefined ? { width: savedWidth } : {}) }];
  flushSync(() => root!.render(
    <ReactFlowProvider initialNodes={initialNodes}>
      <WorkflowNodeResizeContext.Provider value={resize}>
        <WorkflowActivityNode {...({ id: "node-1", data, selected, width: measuredWidth } as unknown as NodeProps)} />
      </WorkflowNodeResizeContext.Provider>
    </ReactFlowProvider>
  ));
  return container;
}

describe("workflow activity node resize", () => {
  it("offers a right-edge resize handle on a selected node of an editable canvas", () => {
    const rendered = renderNode({ selected: true, resize: vi.fn() });

    expect(rendered.querySelector(".wf-node-resize-control")).not.toBeNull();
  });

  it("hides the resize handle until the node is selected", () => {
    const rendered = renderNode({ selected: false, resize: vi.fn() });

    expect(rendered.querySelector(".wf-node-resize-control")).toBeNull();
  });

  it("never offers a resize handle on canvases that cannot resize", () => {
    const rendered = renderNode({ selected: true });

    expect(rendered.querySelector(".wf-node-resize-control")).toBeNull();
  });

  it("renders a saved width", () => {
    const rendered = renderNode({ selected: false, savedWidth: 400, measuredWidth: 246 });

    expect(rendered.querySelector<HTMLElement>(".wf-node")?.style.width).toBe("400px");
  });

  it("keeps the default width when the node has no saved width", () => {
    const rendered = renderNode({ selected: false, measuredWidth: 0 });

    expect(rendered.querySelector<HTMLElement>(".wf-node")?.style.width).toBe("");
  });
});
