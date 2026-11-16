import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Node } from "@xyflow/react";
import { afterEach, beforeEach, expect, it } from "vitest";
import { useGraphCanvasInteractions } from "../graph-authoring/useGraphCanvasInteractions";
import type { WorkflowNodeData } from "../workflowAdapter";
let root: Root, container: HTMLDivElement;
let current: ReturnType<typeof useGraphCanvasInteractions>;
let replaceNodes: (nodes: Node<WorkflowNodeData>[]) => void;
let visible: Node<WorkflowNodeData>[], primary: string | null;
const node = (id: string): Node<WorkflowNodeData> => ({ id, position: { x: 0, y: 0 }, data: { label: id, sourcePorts: [], childSlots: [] } as unknown as WorkflowNodeData });
function Probe() {
  const [nodes, setNodes] = useState([node("one"), node("two"), node("three")]);
  const [selection, select] = useState<string | null>(null);
  current = useGraphCanvasInteractions({ nodes, setNodes, edges: [], setEdges: () => {}, mode: "flowchart", scopeKey: "root", canAddActivities: true,
    selectedNodeId: selection, catalogByVersion: new Map(), select, commitCanvas: () => {}, placeActivity: () => null });
  replaceNodes = setNodes; visible = nodes; primary = selection;
  return <div ref={current.canvasRef} />;
}
beforeEach(() => { container = document.createElement("div"); document.body.append(container); root = createRoot(container); flushSync(() => root.render(<Probe />)); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
const selected = () => visible.filter(node => node.selected).map(node => node.id);
it("retains a Flowchart selection set independently of the inspector, including after a document rebuild", async () => {
  flushSync(() => current.onNodesChange([{ type: "select", id: "one", selected: true }, { type: "select", id: "two", selected: true }]));
  expect(selected()).toEqual(["one", "two"]); expect(primary).toBe("two");
  flushSync(() => replaceNodes([node("one"), { ...node("two"), selected: true }, node("three")]));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(selected()).toEqual(["one", "two"]);
  flushSync(() => current.onNodesChange([{ type: "select", id: "two", selected: false }]));
  expect(selected()).toEqual(["one"]); expect(primary).toBe("one");
});
it("selects a newly created block when its document projection arrives after the command", async () => {
  flushSync(() => current.selectNodes(["new-block"], "new-block"));
  flushSync(() => replaceNodes([node("new-block"), node("three")]));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(selected()).toEqual(["new-block"]); expect(primary).toBe("new-block");
});

it("keeps modifier selection when the real host click follows React Flow's selection event", () => {
  flushSync(() => { current.onNodesChange([{ type: "select", id: "one", selected: true }]); current.inspectSelectedNode("one"); });
  flushSync(() => { current.onNodesChange([{ type: "select", id: "two", selected: true }]); current.inspectSelectedNode("two"); });
  expect(selected()).toEqual(["one", "two"]); expect(primary).toBe("two");
  flushSync(() => { current.onNodesChange([{ type: "select", id: "two", selected: false }]); current.inspectSelectedNode("two"); });
  expect(selected()).toEqual(["one"]); expect(primary).toBe("one");
});
