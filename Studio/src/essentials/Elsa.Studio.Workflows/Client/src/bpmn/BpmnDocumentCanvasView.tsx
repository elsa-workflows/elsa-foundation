import { useEffect, useId, useMemo, useRef, useState } from "react";
import { applyNodeChanges, Background, Controls, ReactFlow, type Node, type NodeChange, type ReactFlowInstance } from "@xyflow/react";
import { GraphOverflowMiniMap } from "../graph-authoring/GraphOverflowMiniMap";
import { RetainFlowViewport } from "../graph-authoring/RetainFlowViewport";
import { planBpmnDocumentMove, planBpmnDocumentResize, projectBpmnDocumentCanvas, type BpmnDocumentCanvas, type BpmnDocumentNodeData } from "./bpmnDocumentCanvas";
import { assessBpmnDocumentResize } from "./bpmnDocumentPolicy";
import { isSubProcessElementType } from "./bpmnTypes";
import { previewBpmnDocumentGeometry } from "./bpmnDocumentCanvasPreview";
import { BpmnDocumentCanvasConnection, BpmnDocumentCanvasElement, type BpmnDocumentViewNodeData } from "./BpmnDocumentCanvasElements";
import type { BpmnBounds, BpmnDocumentCommand, BpmnScopeAddress } from "./bpmnDocument";
import type { BpmnDocumentCanvasAcknowledgement, BpmnDocumentCanvasRequest, BpmnDocumentCanvasSnapshot, BpmnDocumentCanvasViewProps } from "./bpmnDocumentCanvasPort";
import "./bpmnDocumentCanvasView.css";

const nodeTypes = { bpmnDocumentElement: BpmnDocumentCanvasElement };
const edgeTypes = { bpmnDocumentConnection: BpmnDocumentCanvasConnection };
function sameScope(left: BpmnScopeAddress, right: BpmnScopeAddress) {
  return left.documentOwnerNodeId === right.documentOwnerNodeId && left.diagramId === right.diagramId && left.scopeElementId === right.scopeElementId;
}
function sameAuthority(left: BpmnDocumentCanvasSnapshot, right: BpmnDocumentCanvasSnapshot) {
  return sameScope(left.scope, right.scope) && left.token.authorityId === right.token.authorityId
    && left.token.resourceId === right.token.resourceId && left.token.scopeId === right.token.scopeId;
}
interface Gesture { snapshot: BpmnDocumentCanvasSnapshot; view: BpmnDocumentCanvas }
interface PendingEdit extends Gesture {
  request: BpmnDocumentCanvasRequest;
  handler: BpmnDocumentCanvasViewProps["onTransaction"];
}

/** Unenabled source2 view foundation. Canonical admission, history, checkpoint fences and persistence remain in the resource host. */
export function BpmnDocumentCanvasView(props: BpmnDocumentCanvasViewProps) {
  const sessionId = useId(), sequence = useRef(0), live = useRef(props);
  live.current = props;
  const [receipt, setReceipt] = useState<BpmnDocumentCanvasSnapshot | null>(null);
  const [preview, setPreview] = useState<Gesture | null>(null);
  const previewRef = useRef<Gesture | null>(null), resizing = useRef(false), flowInstance = useRef<Pick<ReactFlowInstance, "screenToFlowPosition"> | null>(null);
  const paintPreview = (value: Gesture | null) => { previewRef.current = value; setPreview(value); };
  const [pending, setPending] = useState<PendingEdit | null>(null);
  const pendingRef = useRef<PendingEdit | null>(null), gesture = useRef<Gesture | null>(null);
  const inFlight = useRef<PendingEdit | null>(null);
  const navigationPending = useRef(false), [navigating, setNavigating] = useState(false);
  const [failure, setFailure] = useState<string | null>(null), [notice, setNotice] = useState<string | null>(null);
  // A returned authoritative receipt can paint before React delivers its matching parent props.
  // It is never an independently edited owner, save buffer, history stack or revision authority.
  const snapshot = receipt && sameAuthority(receipt, props.snapshot) && receipt.token.revision > props.snapshot.token.revision ? receipt : props.snapshot;
  const projected = useMemo(() => {
    try { return { view: projectBpmnDocumentCanvas(snapshot.owner, snapshot.scope), error: null }; }
    catch (error) { return { view: null, error: error instanceof Error ? error.message : String(error) }; }
  }, [snapshot.owner, snapshot.scope]);
  const view = preview && sameAuthority(preview.snapshot, snapshot) ? preview.view : projected.view;
  const blocked = !!pending || navigating || !!props.editing?.fenced;
  const statusMessage = failure ?? notice ?? projected.error;

  useEffect(() => { live.current.onStateChange?.({ pending: blocked, failed: !!failure, message: statusMessage }); },
    [blocked, failure, statusMessage]);
  useEffect(() => {
    if (!pendingRef.current) { gesture.current = null; resizing.current = false; previewRef.current = null; setPreview(null); }
  }, [props.editing?.generation]);

  const beginGesture = () => {
    if (live.current.readOnly || live.current.editing?.fenced || pendingRef.current || navigationPending.current || !projected.view) return;
    try { live.current.editing?.begin(snapshot); }
    catch (error) { setNotice(error instanceof Error ? error.message : String(error)); return; }
    gesture.current = { snapshot, view: projected.view };
  };
  const complete = (edit: PendingEdit, result: BpmnDocumentCanvasAcknowledgement) => {
    if (pendingRef.current !== edit) return;
    const matchesRequest = sameAuthority(edit.snapshot, result.current) && result.current.owner.nodeId === edit.snapshot.owner.nodeId
      && result.current.token.revision >= edit.snapshot.token.revision;
    if (result.kind === "accepted" && !matchesRequest) {
      setFailure("The host returned an acknowledgement for another source. The pending edit is retained; retry after the host reconciles it.");
      return;
    }
    if (matchesRequest && sameAuthority(edit.snapshot, live.current.snapshot)) setReceipt(result.current);
    pendingRef.current = null;
    setPending(null); paintPreview(null); gesture.current = null; setFailure(null);
    setNotice(result.kind === "accepted" ? null : result.reason);
  };
  const execute = async (edit: PendingEdit) => {
    if (inFlight.current === edit) return;
    inFlight.current = edit;
    setFailure(null);
    try { complete(edit, await edit.handler(edit.request)); }
    catch (error) {
      if (pendingRef.current === edit) setFailure(`${error instanceof Error ? error.message : String(error)} The edit outcome is uncertain; the pending request is retained.`);
    } finally { if (inFlight.current === edit) inFlight.current = null; }
  };
  const submit = (base: Gesture, commands: BpmnDocumentCommand[], label: string) => {
    if (pendingRef.current || navigationPending.current || !commands.length) return;
    if (live.current.readOnly || live.current.editing?.fenced) { setNotice("This source is read-only or completing another action; the geometry change is retained by its controller."); return; }
    const edit: PendingEdit = { ...base, handler: live.current.onTransaction,
      request: { expected: base.snapshot.token, expectedOwner: base.snapshot.owner, scope: base.snapshot.scope,
        sessionId, sequence: ++sequence.current, label, transaction: { commands } } };
    paintPreview({ ...base, view: previewBpmnDocumentGeometry(base.view, commands) });
    pendingRef.current = edit; setPending(edit);
    void execute(edit);
  };
  const finishMove = (positions: ReadonlyMap<string, { x: number; y: number }>) => {
    const base = gesture.current ?? (projected.view ? { snapshot, view: projected.view } : null);
    if (!base || pendingRef.current || navigationPending.current) return;
    try {
      const commands = planBpmnDocumentMove(base.view, positions);
      if (!commands.length) { gesture.current = null; paintPreview(null); }
      else submit(base, commands, "Move BPMN shapes");
    } catch (error) { gesture.current = null; paintPreview(null); setNotice(error instanceof Error ? error.message : String(error)); }
  };
  const finishResize = (diId: string, bounds: BpmnBounds, direction: string) => {
    const base = gesture.current ?? (projected.view ? { snapshot, view: projected.view } : null);
    if (!base || pendingRef.current || navigationPending.current) return;
    try {
      const commands = planBpmnDocumentResize(base.view, diId, bounds, direction);
      if (!commands.length) { gesture.current = null; paintPreview(null); }
      else submit(base, commands, "Resize BPMN shape");
    }
    catch (error) { gesture.current = null; paintPreview(null); setNotice(error instanceof Error ? error.message : String(error)); }
  };
  const previewResize = (diId: string, bounds: BpmnBounds, direction: string) => {
    const base = gesture.current;
    if (!base || live.current.readOnly || live.current.editing?.fenced || pendingRef.current) return;
    try { live.current.editing?.preview(base.snapshot, planBpmnDocumentResize(base.view, diId, bounds, direction), "Resize BPMN shape"); }
    catch (error) { setNotice(error instanceof Error ? error.message : String(error)); }
  };
  const onNodesChange = (changes: NodeChange<Node<BpmnDocumentNodeData>>[]) => {
    if (live.current.readOnly || live.current.editing?.fenced || pendingRef.current || navigationPending.current || !projected.view) return;
    const geometry = changes.filter(change => change.type === "position" || change.type === "dimensions" && change.resizing);
    if (!geometry.length) return;
    if (!gesture.current) {
      // React Flow keyboard movement uses position changes without a pointer drag start.
      const positions = geometry.flatMap(change => change.type === "position" && change.position ? [[change.id, change.position] as const] : []);
      if (positions.length) finishMove(new Map(positions));
      return;
    }
    const base = gesture.current;
    const current = previewRef.current?.view ?? base.view;
    const next = { ...base, view: { ...current, nodes: applyNodeChanges<Node<BpmnDocumentNodeData>>(geometry, current.nodes) } };
    try {
      const previous = new Map(base.view.nodes.map(node => [node.id, node.position]));
      if (!resizing.current) live.current.editing?.preview(base.snapshot, planBpmnDocumentMove(base.view, new Map(next.view.nodes.filter(node => node.position.x !== previous.get(node.id)?.x || node.position.y !== previous.get(node.id)?.y).map(node => [node.id, node.position]))), "Move BPMN shapes");
      paintPreview(next);
    } catch (error) { setNotice(error instanceof Error ? error.message : String(error)); }
  };
  const navigate = async (action: () => void | Promise<void>) => {
    if (pendingRef.current || navigationPending.current) return;
    navigationPending.current = true; setNavigating(true);
    try { await action(); }
    catch (error) { setNotice(error instanceof Error ? error.message : String(error)); }
    finally { navigationPending.current = false; setNavigating(false); }
  };
  const nodeById = new Map(view?.nodes.map(node => [node.id, node]));
  const nodes: Node<BpmnDocumentViewNodeData>[] = (view?.nodes ?? []).map(node => ({ ...node,
    selected: props.selection?.elementId === node.data.semanticElementId && (!props.selection.diId || props.selection.diId === node.id),
    ariaLabel: `${node.data.notation.name || node.data.semanticType}. BPMN element ${node.data.semanticElementId}. Diagram shape ${node.id}.`,
    data: { ...node.data, parentAbsolutePosition: node.parentId ? nodeById.get(node.parentId)?.data.absoluteBounds ?? { x: 0, y: 0 } : { x: 0, y: 0 }, blocked,
      canResize: !props.readOnly && assessBpmnDocumentResize(node.data.element, { ...node.data.absoluteBounds, isExpanded: node.data.notation.isExpanded }).allowed,
      presentation: node.data.binding ? props.resolveNativePresentation?.(node.data.semanticElementId, node.data.binding) : undefined,
      standardScope: isSubProcessElementType(node.data.semanticType) && !node.data.binding?.childNodeId
        ? props.resolveStandardScope?.(node.data.semanticElementId, snapshot.scope) : undefined,
      navigate: props.onNavigateStandard ? target => navigate(() => props.onNavigateStandard!(target)) : undefined,
      openNative: props.onOpenNativeSlot ? (ownerId, slot) => navigate(() => props.onOpenNativeSlot!(ownerId, slot)) : undefined,
      beginResize: () => { beginGesture(); resizing.current = true; }, previewResize, finishResize } }));
  const edges = (view?.edges ?? []).map(edge => ({ ...edge, selected: !!props.selection && props.selection.elementId === edge.data?.semanticElementId
    && (!props.selection.diId || props.selection.diId === edge.id) }));
  return <div className={["wf-bpmn-document-canvas", props.className].filter(Boolean).join(" ")}
    data-bpmn-document-canvas="true" data-bpmn-pending={blocked} data-bpmn-source-revision={snapshot.token.revision}
    onKeyDown={event => {
      const target = event.target;
      if (target instanceof HTMLElement && target.closest("input,textarea,select,[contenteditable='true']")) return;
      if ((event.key === "Enter" || event.key === " ") && target instanceof Element
        && (target.classList.contains("react-flow__node") || target.classList.contains("react-flow__edge"))) {
        const diId = target.getAttribute("data-id"), selectedNode = nodes.find(node => node.id === diId), selectedEdge = edges.find(edge => edge.id === diId);
        const elementId = selectedNode?.data.semanticElementId ?? selectedEdge?.data?.semanticElementId;
        if (elementId && diId) { event.preventDefault(); props.onSelect({ elementId, diId }); }
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
        event.preventDefault(); event.stopPropagation();
        if (!live.current.readOnly && !live.current.editing?.fenced && !pendingRef.current && !navigationPending.current) props.onHistory?.(event.shiftKey ? "redo" : "undo");
      }
    }}>
    {projected.error ? <p role="alert">{projected.error}</p> : <ReactFlow
      nodes={nodes} edges={edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes} onInit={instance => { flowInstance.current = instance; }}
      nodesDraggable={!props.readOnly && !blocked} nodesConnectable={false} edgesReconnectable={false} deleteKeyCode={null}
      onNodesChange={onNodesChange} onNodeDragStart={() => { resizing.current = false; beginGesture(); }}
      onNodeDragStop={(_event, node, movedNodes) => finishMove(new Map((movedNodes.length ? movedNodes : [node]).map(moved => [moved.id, moved.position])))}
      onNodeClick={(event, node) => {
        if (props.placing && props.onPlace && flowInstance.current && !props.readOnly && !props.editing?.fenced) props.onPlace(flowInstance.current.screenToFlowPosition({ x: event.clientX, y: event.clientY }), node.data.semanticElementId);
        else props.onSelect({ elementId: node.data.semanticElementId, diId: node.id });
      }}
      onEdgeClick={(_event, edge) => props.onSelect({ elementId: edge.data!.semanticElementId, diId: edge.id })}
      onPaneClick={event => {
        if (props.placing && props.onPlace && flowInstance.current && !props.readOnly && !props.editing?.fenced) props.onPlace(flowInstance.current.screenToFlowPosition({ x: event.clientX, y: event.clientY }));
        else props.onSelect(null);
      }}
      minZoom={0.1} maxZoom={2} nodeDragThreshold={5} selectionOnDrag multiSelectionKeyCode={["Shift", "Meta", "Control"]}
      fitView fitViewOptions={{ padding: 0.2 }} ariaLabelConfig={{
        "node.a11yDescription.default": "Press Enter to inspect this BPMN element. Arrow keys move selected shapes when editing is available.",
        "edge.a11yDescription.default": "Press Enter to inspect this BPMN connection."
      }}>
      <Background gap={18} size={1} /><Controls /><RetainFlowViewport /><GraphOverflowMiniMap />
    </ReactFlow>}
    {blocked ? <p className="wf-bpmn-document-status" role="status">{navigating ? "Waiting for the host to open this scope…" : failure ? "Pending edit needs reconciliation." : "Waiting for the source to accept this edit…"}</p> : null}
    {failure || notice ? <div className="wf-bpmn-document-notice" role="alert">{failure ?? notice}
      {failure && pending ? <button type="button" onClick={() => { void execute(pending); }}>Retry pending edit</button> : null}</div> : null}
    {view?.diagnostics.length ? <details className="wf-bpmn-document-diagnostics"><summary>{view.diagnostics.length} diagram notice(s)</summary>
      <ul>{view.diagnostics.map((diagnostic, index) => <li key={`${diagnostic.diId ?? diagnostic.elementId}-${index}`}>{diagnostic.message}</li>)}</ul></details> : null}
  </div>;
}
