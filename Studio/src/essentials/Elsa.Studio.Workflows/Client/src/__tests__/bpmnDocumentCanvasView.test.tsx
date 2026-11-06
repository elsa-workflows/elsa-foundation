import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Position, type Edge, type Node, type ReactFlowProps } from "@xyflow/react";
import { BpmnDocumentCanvasView } from "../bpmn/BpmnDocumentCanvasView";
import { BpmnDocumentCanvasConnection, BpmnDocumentCanvasElement, type BpmnDocumentViewNodeData } from "../bpmn/BpmnDocumentCanvasElements";
import { applyBpmnDocumentCommands } from "../bpmn/bpmnDocument";
import type { BpmnDocumentEdgeData } from "../bpmn/bpmnDocumentCanvas";
import { planBpmnDocumentMove, projectBpmnDocumentCanvas } from "../bpmn/bpmnDocumentCanvas";
import { previewBpmnDocumentGeometry } from "../bpmn/bpmnDocumentCanvasPreview";
import type { BpmnDocumentCanvasAcknowledgement, BpmnDocumentCanvasRequest, BpmnDocumentCanvasSnapshot, BpmnDocumentCanvasViewProps } from "../bpmn/bpmnDocumentCanvasPort";
import { documentCanvasOwner, documentCanvasScope } from "./bpmnDocumentCanvasFixture";

// Controlled boundary tests: they exercise the actual component/port, not browser hit targets or layout.
type FlowProps = ReactFlowProps<Node<BpmnDocumentViewNodeData>, Edge<BpmnDocumentEdgeData>>;
type ResizeControlProps = React.ComponentProps<typeof import("@xyflow/react").NodeResizeControl>;
let flow: FlowProps;
let resizeControls: ResizeControlProps[] = [];
vi.mock("@xyflow/react", async importOriginal => {
  const original = await importOriginal<typeof import("@xyflow/react")>();
  return { ...original, ReactFlow: (props: FlowProps) => { flow = props; return <div>{props.children}</div>; },
    Background: () => null, Controls: () => null, Handle: () => null, NodeResizeControl: (props: ResizeControlProps) => { resizeControls.push(props); return null; },
    BaseEdge: (props: { path: string; style?: React.CSSProperties; markerStart?: string; markerEnd?: string }) => <path d={props.path} style={props.style} markerStart={props.markerStart} markerEnd={props.markerEnd} /> };
});
vi.mock("../graph-authoring/GraphOverflowMiniMap", () => ({ GraphOverflowMiniMap: () => null }));
vi.mock("../graph-authoring/RetainFlowViewport", () => ({ RetainFlowViewport: () => null }));

let root: Root, container: HTMLDivElement;
beforeEach(() => { resizeControls = []; container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function snapshot(): BpmnDocumentCanvasSnapshot {
  return { owner: documentCanvasOwner(), scope: documentCanvasScope,
    token: { authorityId: "host", resourceId: "draft", scopeId: "real-owner", revision: 0 } };
}
function accepted(request: BpmnDocumentCanvasRequest): BpmnDocumentCanvasAcknowledgement {
  return { kind: "accepted", current: { scope: request.scope, token: { ...request.expected, revision: request.expected.revision + 1 },
    owner: applyBpmnDocumentCommands(request.expectedOwner, request.transaction.commands ?? []) } };
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (reason: Error) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function render(props: Partial<BpmnDocumentCanvasViewProps> = {}) {
  const merged: BpmnDocumentCanvasViewProps = { snapshot: snapshot(), selection: null, readOnly: false, onSelect: vi.fn(), onTransaction: accepted, ...props };
  flushSync(() => root.render(<BpmnDocumentCanvasView {...merged} />));
  return merged;
}
function node(id = "TDI") { return flow.nodes!.find(item => item.id === id)!; }
function startMove(id = "TDI") { const value = node(id); flushSync(() => flow.onNodeDragStart!(new MouseEvent("mousedown"), value, [value])); }
function finishMove(dx = 12.125, dy = 7.25, id = "TDI") {
  const value = node(id), moved = { ...value, position: { x: value.position.x + dx, y: value.position.y + dy } };
  flushSync(() => flow.onNodesChange!([{ type: "position", id, position: moved.position, dragging: true }]));
  flushSync(() => flow.onNodeDragStop!(new MouseEvent("mouseup"), moved, [moved]));
}
function move() { startMove(); finishMove(); }
function pending() { return container.querySelector("[data-bpmn-document-canvas]")!.getAttribute("data-bpmn-pending"); }

describe("source2 canvas acknowledgement boundary", () => {
  it("selects actual semantic/DI identities and projects fractional containment without synthetic native nodes", () => {
    const select = vi.fn(); render({ onSelect: select, selection: { elementId: "T", diId: "TDI" } });
    expect(node()).toMatchObject({ selected: true, parentId: "LaneADI", position: { x: 60.25, y: 80.25 } });
    expect(flow.nodes!.some(item => item.id === "native" || item.id === "T")).toBe(false);
    flushSync(() => flow.onNodeClick!({} as React.MouseEvent, node()));
    expect(select).toHaveBeenLastCalledWith({ elementId: "T", diId: "TDI" });
    const edge = flow.edges!.find(item => item.id === "FDI")!;
    flushSync(() => flow.onEdgeClick!({} as React.MouseEvent, edge));
    expect(select).toHaveBeenLastCalledWith({ elementId: "F", diId: "FDI" });
  });

  it("holds a geometry-only preview and delegates history until the parent acknowledges the exact request", async () => {
    const ack = deferred<BpmnDocumentCanvasAcknowledgement>(), commit = vi.fn<(request: BpmnDocumentCanvasRequest) => Promise<BpmnDocumentCanvasAcknowledgement>>(() => ack.promise), history = vi.fn();
    const props = render({ onTransaction: commit, onHistory: history }), original = structuredClone(props.snapshot.owner), previous = node().position;
    move();
    expect(pending()).toBe("true"); expect(commit).toHaveBeenCalledTimes(1);
    expect(node().position).not.toEqual(previous);
    expect(props.snapshot.owner).toEqual(original);
    expect(container.querySelector("[data-bpmn-source-revision]")!.getAttribute("data-bpmn-source-revision")).toBe("0");
    flushSync(() => container.firstElementChild!.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true })));
    expect(history).not.toHaveBeenCalled();
    const request = commit.mock.calls[0][0] as BpmnDocumentCanvasRequest;
    expect(request.expected).toBe(props.snapshot.token); expect(request.expectedOwner).toBe(props.snapshot.owner);
    expect(request.transaction.commands).toEqual(expect.arrayContaining([expect.objectContaining({ kind: "bounds", shapeId: "TDI" })]));
    ack.resolve(accepted(request));
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(container.querySelector("[data-bpmn-source-revision]")!.getAttribute("data-bpmn-source-revision")).toBe("1");
    expect(props.snapshot.owner).toEqual(original);
    flushSync(() => container.firstElementChild!.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true })));
    expect(history).toHaveBeenCalledWith("undo");
  });

  it("publishes the latest drag preview synchronously before pointerup and respects an external checkpoint fence", () => {
    const begin = vi.fn(), preview = vi.fn(), commit = vi.fn(accepted), history = vi.fn();
    const editing = { generation: 1, fenced: false, begin, preview };
    const props = render({ editing, onTransaction: commit, onHistory: history });
    const original = node(); startMove();
    flow.onNodesChange!([{ type: "position", id: original.id, position: { x: original.position.x + 8.125, y: original.position.y + 4.25 }, dragging: true }]);
    expect(begin).toHaveBeenCalledWith(props.snapshot);
    expect(preview).toHaveBeenCalledTimes(1);
    expect(preview.mock.calls[0][1]).toContainEqual({ kind: "bounds", shapeId: "TDI", bounds: { ...original.data.absoluteBounds, x: original.data.absoluteBounds.x + 8.125, y: original.data.absoluteBounds.y + 4.25 } });
    expect(commit).not.toHaveBeenCalled();
    flushSync(() => root.render(<BpmnDocumentCanvasView {...props} editing={{ ...editing, fenced: true }} />));
    expect(flow.nodesDraggable).toBe(false);
    flushSync(() => flow.onNodesChange!([{ type: "position", id: original.id, position: { x: 900, y: 900 }, dragging: true }]));
    expect(preview).toHaveBeenCalledTimes(1);
    container.querySelector("[data-bpmn-document-canvas]")!.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true }));
    expect(history).not.toHaveBeenCalled();
  });

  it("restores the authoritative source after refusal and retains the reason during the next pending edit", async () => {
    const ack = deferred<BpmnDocumentCanvasAcknowledgement>(), base = snapshot();
    const commit = vi.fn<(request: BpmnDocumentCanvasRequest) => BpmnDocumentCanvasAcknowledgement | Promise<BpmnDocumentCanvasAcknowledgement>>()
      .mockReturnValueOnce({ kind: "refused", reason: "Ownership conflict retained", current: base }).mockReturnValueOnce(ack.promise);
    render({ snapshot: base, onTransaction: commit }); const previous = node().position;
    move(); await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(node().position).toEqual(previous); expect(container.textContent).toContain("Ownership conflict retained");
    move(); expect(pending()).toBe("true"); expect(container.textContent).toContain("Ownership conflict retained");
    ack.resolve(accepted(commit.mock.calls[1][0]));
    await vi.waitFor(() => expect(container.textContent).not.toContain("Ownership conflict retained"));
  });

  it("retains an uncertain request and retries its same identity rather than silently dropping or duplicating the command", async () => {
    const ack = deferred<BpmnDocumentCanvasAcknowledgement>();
    const commit = vi.fn<(request: BpmnDocumentCanvasRequest) => Promise<BpmnDocumentCanvasAcknowledgement>>()
      .mockReturnValueOnce(ack.promise).mockImplementationOnce(async request => accepted(request));
    render({ onTransaction: commit }); move(); const previewPosition = node().position;
    ack.reject(new Error("Parent unavailable"));
    await vi.waitFor(() => expect(container.textContent).toContain("outcome is uncertain"));
    expect(pending()).toBe("true"); expect(flow.nodesDraggable).toBe(false); expect(node().position).toEqual(previewPosition);
    const retry = [...container.querySelectorAll("button")].find(button => button.textContent === "Retry pending edit")!;
    flushSync(() => retry.click());
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(commit.mock.calls[1][0]).toBe(commit.mock.calls[0][0]);
  });

  it("submits the captured baseline after a live revision change and displays the host's stale result", async () => {
    const base = snapshot(), latest = { ...base, owner: applyBpmnDocumentCommands(base.owner, [{ kind: "attribute", target: { elementId: "T" }, name: "name", value: "Concurrent shared inspector edit" }]), token: { ...base.token, revision: 2 } };
    const commit = vi.fn<(request: BpmnDocumentCanvasRequest) => BpmnDocumentCanvasAcknowledgement>().mockReturnValue({ kind: "stale", reason: "Source revision changed", current: latest });
    render({ snapshot: base, onTransaction: commit }); startMove();
    render({ snapshot: latest, onTransaction: commit }); finishMove();
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(commit.mock.calls[0][0].expected).toBe(base.token); expect(commit.mock.calls[0][0].expectedOwner).toBe(base.owner);
    expect(node().data.notation.name).toBe("Concurrent shared inspector edit"); expect(container.textContent).toContain("Source revision changed");
  });

  it("does not paint a late old-source acknowledgement over a different document", async () => {
    const ack = deferred<BpmnDocumentCanvasAcknowledgement>(), commit = vi.fn<(request: BpmnDocumentCanvasRequest) => Promise<BpmnDocumentCanvasAcknowledgement>>(() => ack.promise);
    const base = snapshot(); render({ snapshot: base, onTransaction: commit }); move();
    const other = snapshot(); other.owner = { ...other.owner, nodeId: "other-owner" }; other.scope = { ...other.scope, documentOwnerNodeId: "other-owner" };
    other.token = { ...other.token, resourceId: "another-draft", revision: 10 };
    render({ snapshot: other, onTransaction: commit }); const position = node().position;
    ack.resolve(accepted(commit.mock.calls[0][0]));
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(node().position).toEqual(position);
    expect(container.querySelector("[data-bpmn-source-revision]")!.getAttribute("data-bpmn-source-revision")).toBe("10");
  });

  it("does not accept a receipt for another authority and keeps the pending request visible", async () => {
    const commit = vi.fn((request: BpmnDocumentCanvasRequest) => {
      const result = accepted(request); result.current.token = { ...result.current.token, authorityId: "wrong-host" }; return result;
    });
    render({ onTransaction: commit }); move();
    await vi.waitFor(() => expect(container.textContent).toContain("another source"));
    expect(pending()).toBe("true");
    expect(container.querySelector("[data-bpmn-source-revision]")!.getAttribute("data-bpmn-source-revision")).toBe("0");
  });

  it("honors an explicit stale refusal after source navigation without painting its different-source current value", async () => {
    const ack = deferred<BpmnDocumentCanvasAcknowledgement>(), commit = vi.fn<(request: BpmnDocumentCanvasRequest) => Promise<BpmnDocumentCanvasAcknowledgement>>(() => ack.promise);
    const base = snapshot(); render({ snapshot: base, onTransaction: commit }); move();
    const other = snapshot(); other.owner = { ...other.owner, nodeId: "other-owner" }; other.scope = { ...other.scope, documentOwnerNodeId: "other-owner" };
    other.token = { ...other.token, resourceId: "another-draft", revision: 10 };
    render({ snapshot: other, onTransaction: commit });
    ack.resolve({ kind: "stale", reason: "Resource already changed", current: other });
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(container.textContent).toContain("Resource already changed");
    expect(container.querySelector("[data-bpmn-source-revision]")!.getAttribute("data-bpmn-source-revision")).toBe("10");
  });

  it("uses the host for asynchronous scope navigation and retains its refusal", async () => {
    const navigation = deferred<void>(), open = vi.fn(() => navigation.promise), history = vi.fn();
    render({ resolveStandardScope: (_id, current) => ({ ...current, scopeElementId: "Sub" }), onNavigateStandard: open, onHistory: history });
    flushSync(() => { void node("SubDI").data.navigate!({ ...documentCanvasScope, scopeElementId: "Sub" }); });
    expect(pending()).toBe("true"); expect(flow.nodesDraggable).toBe(false);
    expect(open).toHaveBeenCalledWith({ ...documentCanvasScope, scopeElementId: "Sub" });
    flushSync(() => container.firstElementChild!.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true })));
    expect(history).not.toHaveBeenCalled();
    navigation.reject(new Error("Shared inspector checkpoint refused"));
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(container.textContent).toContain("Shared inspector checkpoint refused");
  });

  it("maps keyboard geometry changes through the same acknowledged source transaction", async () => {
    const commit = vi.fn(accepted), base = snapshot(); render({ snapshot: base, onTransaction: commit });
    const previous = node().position;
    flushSync(() => flow.onNodesChange!([{ type: "position", id: "TDI", position: { x: previous.x + 5, y: previous.y + 2 } }]));
    await vi.waitFor(() => expect(pending()).toBe("false"));
    expect(commit).toHaveBeenCalledTimes(1); expect(commit.mock.calls[0][0].expected).toBe(base.token);
    expect(node().position).toEqual({ x: previous.x + 5, y: previous.y + 2 });
  });

  it("checks live read-only state before submitting a completed gesture", () => {
    const base = snapshot(), commit = vi.fn(accepted), history = vi.fn();
    render({ snapshot: base, onTransaction: commit }); startMove();
    render({ snapshot: base, onTransaction: commit, readOnly: true, onHistory: history }); finishMove();
    expect(commit).not.toHaveBeenCalled(); expect(pending()).toBe("false");
    expect(container.textContent).toContain("read-only"); expect(flow.nodesDraggable).toBe(false);
    flushSync(() => container.firstElementChild!.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true })));
    expect(history).not.toHaveBeenCalled();
  });
});

describe("source2 notation elements", () => {
  it("converts a left annotation resize from relative coordinates using the fractional parent origin", async () => {
    const commit = vi.fn(accepted), retained = vi.fn(); render({ onTransaction: commit, selection: { elementId: "Note", diId: "NoteDI" }, editing: { generation: 1, fenced: false, begin: vi.fn(), preview: retained } });
    const original = node("NoteDI"), elementContainer = document.createElement("div"), elements = createRoot(elementContainer);
    const paint = (item: typeof original) => flushSync(() => elements.render(<BpmnDocumentCanvasElement {...item} type="bpmnDocumentElement" selected={true}
      dragging={false} draggable={true} selectable={true} deletable={false} zIndex={0} isConnectable={false}
      positionAbsoluteX={item.data.absoluteBounds.x} positionAbsoluteY={item.data.absoluteBounds.y} />));
    document.body.append(elementContainer);
    try {
      paint(original);
      const start = resizeControls.find(control => control.position === "left")!;
      flushSync(() => start.onResizeStart!({} as Parameters<NonNullable<ResizeControlProps["onResizeStart"]>>[0], {
        ...original.position, width: original.width!, height: original.height!
      }));
      const parameters = { x: original.position.x - 5.125, y: original.position.y, width: original.width! + 5.125, height: original.height! };
      flushSync(() => start.onResize!({} as Parameters<NonNullable<ResizeControlProps["onResize"]>>[0], { ...parameters, direction: [-1, 0] }));
      expect(retained).toHaveBeenCalledTimes(1);
      expect(retained.mock.calls[0][1]).toContainEqual({ kind: "bounds", shapeId: "NoteDI", bounds: { x: original.data.absoluteBounds.x - 5.125, y: original.data.absoluteBounds.y, width: parameters.width, height: parameters.height } });
      flushSync(() => flow.onNodesChange!([{ type: "position", id: original.id, position: { x: parameters.x, y: parameters.y } },
        { type: "dimensions", id: original.id, dimensions: { width: parameters.width, height: parameters.height }, resizing: true }]));
      paint(node("NoteDI"));
      const end = [...resizeControls].reverse().find(control => control.position === "left")!;
      flushSync(() => end.onResizeEnd!({} as Parameters<NonNullable<ResizeControlProps["onResizeEnd"]>>[0], parameters));
      await vi.waitFor(() => expect(pending()).toBe("false"));
      expect(commit).toHaveBeenCalledTimes(1);
      expect(commit.mock.calls[0][0].transaction.commands).toContainEqual({ kind: "bounds", shapeId: "NoteDI", bounds: {
        x: original.data.absoluteBounds.x - 5.125, y: original.data.absoluteBounds.y, width: parameters.width, height: parameters.height
      } });
    } finally { flushSync(() => elements.unmount()); elementContainer.remove(); }
  });

  it("paints geometry previews exactly like the eventual source edit while leaving source and native bindings untouched", () => {
    const owner = documentCanvasOwner(), original = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const pool = view.nodes.find(item => item.id === "PoolDI")!;
    const commands = planBpmnDocumentMove(view, new Map([[pool.id, { x: pool.position.x + 11.125, y: pool.position.y - 4.25 }]]));
    const preview = previewBpmnDocumentGeometry(view, commands);
    const canonical = projectBpmnDocumentCanvas(applyBpmnDocumentCommands(owner, commands), documentCanvasScope);
    const geometry = (value: typeof view) => ({ nodes: value.nodes.map(item => ({ id: item.id, position: item.position, bounds: item.data.absoluteBounds, label: item.data.labelBounds })),
      edges: value.edges.map(item => ({ id: item.id, points: item.data!.points, label: item.data!.labelBounds })) });
    expect(geometry(preview)).toEqual(geometry(canonical));
    expect(preview.scene).toBe(view.scene); expect(owner).toEqual(original);
  });

  it("keeps default/conditional flow presentation tied to actual source references", () => {
    const owner = documentCanvasOwner(xml => xml.replace('id="T" name=', 'id="T" default="tns:F" name='));
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.edges.find(edge => edge.id === "FDI")!.data!.isDefault).toBe(true);
    const plain = documentCanvasOwner(xml => xml.replace('<bpmn:sequenceFlow id="F" sourceRef="T" targetRef="O"/>', '<bpmn:sequenceFlow id="F" sourceRef="T" targetRef="O"><bpmn:conditionExpression>actual</bpmn:conditionExpression></bpmn:sequenceFlow>'));
    expect(projectBpmnDocumentCanvas(plain, documentCanvasScope).edges.find(edge => edge.id === "FDI")!.data!.isConditional).toBe(true);
  });
  it("keeps source routes and authored edge paint exact under a separate selection overlay", () => {
    render();
    const edge = flow.edges!.find(item => item.id === "FDI")!, data = { ...edge.data!, appearance: { fill: "red", stroke: "teal", labelColor: "navy", sources: {}, issues: [] } };
    const svg = document.createElement("div");
    svg.innerHTML = renderToStaticMarkup(<svg><BpmnDocumentCanvasConnection {...edge} data={data} selected={true} markerStart={undefined} markerEnd={undefined}
      sourceX={0} sourceY={0} targetX={1} targetY={1} sourcePosition={Position.Right} targetPosition={Position.Left} /></svg>);
    const path = svg.querySelectorAll("path[d^='M200.5']");
    expect(path).toHaveLength(2); expect(path[0].getAttribute("data-bpmn-overlay")).toBe("selection");
    expect(path[1].getAttribute("d")).toBe(data.points.map((point, index) => `${index ? "L" : "M"}${point.x},${point.y}`).join(" "));
    expect((path[1] as SVGElement).style.stroke).toBe("teal");
  });

  it("opens real native Slot identities separately from a resolved standard BPMN body", async () => {
    const openNative = vi.fn(), openStandard = vi.fn();
    render({ selection: { elementId: "T", diId: "TDI" }, resolveNativePresentation: () => ({
      activity: { nodeId: "native", activityVersionId: "exact-sequence", label: "Native body", icon: "activity" },
      childSlots: [{ id: "sequence.activities", label: "Activities", property: "activities", cardinality: "many", mode: "sequence", activities: [] }]
    }), resolveStandardScope: (_id, current) => ({ ...current, scopeElementId: "Sub" }), onOpenNativeSlot: openNative, onNavigateStandard: openStandard });
    const elementContainer = document.createElement("div"), elements = createRoot(elementContainer);
    document.body.append(elementContainer);
    try {
      expect(node().data.standardScope).toBeUndefined();
      const props = [node(), node("SubDI")];
      flushSync(() => elements.render(<>{props.map(item => <BpmnDocumentCanvasElement key={item.id} {...item} type="bpmnDocumentElement" selected={true}
        dragging={false} draggable={true} selectable={true} deletable={false} zIndex={0} isConnectable={false}
        positionAbsoluteX={item.data.absoluteBounds.x} positionAbsoluteY={item.data.absoluteBounds.y} />)}</>));
      const slots = [...elementContainer.querySelectorAll("button")];
      flushSync(() => slots.find(button => button.textContent === "Activities")!.click());
      expect(openNative).toHaveBeenCalledWith("native", expect.objectContaining({ id: "sequence.activities" }));
      await vi.waitFor(() => expect(pending()).toBe("false"));
      flushSync(() => slots.find(button => button.textContent === "Open BPMN body")!.click());
      expect(openStandard).toHaveBeenCalledWith({ ...documentCanvasScope, scopeElementId: "Sub" });
      expect(elementContainer.querySelector("[data-bpmn-element-id='T'][data-bpmn-di-id='TDI']")).not.toBeNull();
    } finally { flushSync(() => elements.unmount()); elementContainer.remove(); }
  });
});
