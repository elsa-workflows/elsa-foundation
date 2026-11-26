import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { ReactFlowProvider, type NodeProps } from "@xyflow/react";
import { WorkflowActivityNode } from "../workflow-editor/graph";
import { WorkflowNodeResizeContext, type WorkflowNodeResize } from "../workflow-editor/contexts";
import type { WorkflowNodeData } from "../workflowAdapter";

type ResizeControlProps = {
  className?: string;
  children?: React.ReactNode;
  onResizeStart?(event: unknown, params: { width: number }): void;
  onResizeEnd?(event: unknown, params: { width: number }): void;
};

// Records every render's props so tests can check callback identity across re-renders, which the real
// control relies on: it tears down its drag listeners whenever a callback prop changes.
const resizeControlRenders: ResizeControlProps[] = [];

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    Handle: () => null,
    NodeResizeControl: (props: ResizeControlProps) => {
      resizeControlRenders.push(props);
      return <div className={["react-flow__resize-control", props.className].filter(Boolean).join(" ")}>{props.children}</div>;
    }
  };
});

let root: Root | null = null;
let container: HTMLDivElement | null = null;

afterEach(() => {
  resizeControlRenders.length = 0;
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
  const render = (nextResize: WorkflowNodeResize | null, nextMeasuredWidth: number) => flushSync(() => root!.render(
    <ReactFlowProvider initialNodes={initialNodes}>
      <WorkflowNodeResizeContext.Provider value={nextResize}>
        <WorkflowActivityNode {...({ id: "node-1", data, selected, width: nextMeasuredWidth } as unknown as NodeProps)} />
      </WorkflowNodeResizeContext.Provider>
    </ReactFlowProvider>
  ));
  render(resize, measuredWidth);
  return Object.assign(container, { rerender: render });
}

function pressKey(target: Element, key: string, shiftKey = false) {
  const event = new KeyboardEvent("keydown", { key, shiftKey, bubbles: true, cancelable: true });
  flushSync(() => target.dispatchEvent(event));
  return event;
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

  it("keeps the resize callbacks stable across re-renders so an active drag keeps its listeners", () => {
    const first = vi.fn();
    const latest = vi.fn();
    const rendered = renderNode({ selected: true, resize: first, measuredWidth: 246 });
    rendered.rerender(latest, 300);

    const [initial, rerendered] = [resizeControlRenders[0], resizeControlRenders.at(-1)!];
    expect(rerendered.onResizeStart).toBe(initial.onResizeStart);
    expect(rerendered.onResizeEnd).toBe(initial.onResizeEnd);

    rerendered.onResizeStart?.(null, { width: 246 });
    rerendered.onResizeEnd?.(null, { width: 360 });
    expect(first).not.toHaveBeenCalled();
    expect(latest).toHaveBeenCalledWith("node-1", 360, 246);
  });

  it("resizes from the keyboard through a focusable separator", () => {
    const resize = vi.fn();
    const rendered = renderNode({ selected: true, resize, savedWidth: 300, measuredWidth: 300 });
    const separator = rendered.querySelector<HTMLElement>('[role="separator"]')!;

    expect(separator.tabIndex).toBe(0);
    expect(separator.getAttribute("aria-orientation")).toBe("vertical");
    expect(separator.getAttribute("aria-valuenow")).toBe("300");
    expect(separator.getAttribute("aria-valuemin")).toBe("246");
    expect(separator.getAttribute("aria-valuemax")).toBe("640");
    expect(separator.getAttribute("aria-label")).toBe("Resize Write Line");

    const arrow = pressKey(separator, "ArrowRight");
    expect(resize).toHaveBeenLastCalledWith("node-1", 316, 300);
    expect(arrow.defaultPrevented).toBe(true);
    pressKey(separator, "ArrowLeft", true);
    expect(resize).toHaveBeenLastCalledWith("node-1", 246, 300);
    pressKey(separator, "End");
    expect(resize).toHaveBeenLastCalledWith("node-1", 640, 300);
    pressKey(separator, "Home");
    expect(resize).toHaveBeenLastCalledWith("node-1", 246, 300);
  });

  it("keeps resize keys from reaching the canvas so they do not also move the node", () => {
    const resize = vi.fn();
    const onCanvasKeyDown = vi.fn();
    const rendered = renderNode({ selected: true, resize, measuredWidth: 246 });
    document.body.addEventListener("keydown", onCanvasKeyDown);

    pressKey(rendered.querySelector('[role="separator"]')!, "ArrowRight");

    expect(resize).toHaveBeenLastCalledWith("node-1", 262, 246);
    expect(onCanvasKeyDown).not.toHaveBeenCalled();
    document.body.removeEventListener("keydown", onCanvasKeyDown);
  });

  it("keeps the default width when the node has no saved width", () => {
    const rendered = renderNode({ selected: false, measuredWidth: 0 });

    expect(rendered.querySelector<HTMLElement>(".wf-node")?.style.width).toBe("");
  });
});
