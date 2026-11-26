import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Node } from "@xyflow/react";
import { ReadOnlyWorkflowSequence } from "../workflow-editor/ReadOnlyWorkflowSequence";
import type { WorkflowNodeData } from "../workflowAdapter";

const nodes: Node<WorkflowNodeData>[] = [{ id: "write", position: { x: 0, y: 0 }, selected: true,
  data: { label: "Write Line", activityVersionId: "write-v1", childSlots: [], acceptsInbound: true, sourcePorts: [] } }];
let root: Root, container: HTMLDivElement;
let frames: Map<number, FrameRequestCallback>;
let frameId: number;

beforeEach(() => {
  frames = new Map(); frameId = 0;
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    frames.set(++frameId, callback); return frameId;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => frames.delete(id));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => {
  flushSync(() => root.unmount()); container.remove(); vi.unstubAllGlobals();
});

function render(moveKeyboardFocus?: boolean, focusNodeId: string | null = "write") {
  flushSync(() => root.render(<><input aria-label="Activity position" /><ReadOnlyWorkflowSequence
    {...{ nodes, moveKeyboardFocus, focusNodeId, focusRequestId: 1, onSelectActivity: vi.fn(), onSelectEvidence: vi.fn() }} /></>));
  const step = container.querySelector<HTMLButtonElement>(".wf-sequence-step")!;
  const scroll = vi.fn(); Object.defineProperty(step, "scrollIntoView", { configurable: true, value: scroll });
  return { step, scroll, control: container.querySelector("input")! };
}
function reveal() {
  const pending = [...frames.values()]; frames.clear();
  flushSync(() => pending.forEach(callback => callback(0)));
}

describe("read-only Sequence reveal", () => {
  it("reveals playback selection while retaining keyboard focus on the transport control", () => {
    const { control, scroll } = render(false); control.focus(); reveal();
    expect(scroll).toHaveBeenCalledWith({ block: "center", inline: "nearest", behavior: "auto" });
    expect(document.activeElement).toBe(control);
  });
  it("continues to focus the revealed activity for explicit keyboard navigation", () => {
    const { step, scroll } = render(); reveal();
    expect(scroll).toHaveBeenCalledOnce(); expect(document.activeElement).toBe(step);
  });
  it("cancels pending reveal when the target view is hidden", () => {
    const { control } = render(false); control.focus();
    const { scroll } = render(false, null); reveal();
    expect(scroll).not.toHaveBeenCalled(); expect(document.activeElement).toBe(control);
  });
});
