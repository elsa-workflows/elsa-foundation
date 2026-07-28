import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  applyEdgeChanges,
  applyNodeChanges,
  type EdgeChange,
  type Node,
  type NodeChange
} from "@xyflow/react";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { WorkflowEdge } from "../workflow-editor/editorTypes";
import {
  canRedoActivityGraph,
  canUndoActivityGraph,
  createActivityGraphHistory,
  recordActivityGraphEdit,
  redoActivityGraph,
  resetActivityGraphHistory,
  undoActivityGraph
} from "../activityGraphHistory";
import type { GraphDocumentAdapter } from "./graphDocumentAdapter";

export interface GraphAuthoringCanvasModel {
  nodes: Node<WorkflowNodeData>[];
  edges: WorkflowEdge[];
}

export function useGraphAuthoringCanvas<TDocument>({
  document,
  adapter,
  resetKey,
  buildModel,
  applyModel,
  onChange
}: {
  document: TDocument;
  adapter: GraphDocumentAdapter<TDocument>;
  resetKey: string;
  buildModel(root: ReturnType<GraphDocumentAdapter<TDocument>["readRoot"]>, layout: ReturnType<GraphDocumentAdapter<TDocument>["readLayout"]>): GraphAuthoringCanvasModel;
  applyModel(document: TDocument, nodes: Node<WorkflowNodeData>[], edges: WorkflowEdge[]): TDocument;
  onChange(document: TDocument): void;
}) {
  const derive = useCallback(
    (source: TDocument) => buildModel(adapter.readRoot(source), adapter.readLayout(source)),
    [adapter, buildModel]
  );
  const initial = useMemo(() => derive(document), [derive, document]);
  const [nodes, setNodes] = useState(initial.nodes);
  const [edges, setEdges] = useState(initial.edges);
  const documentRef = useRef(document);
  const emittedSignatureRef = useRef<string | null>(null);
  const historyRef = useRef(createActivityGraphHistory<TDocument>(resetKey));
  const [, setHistoryVersion] = useState(0);
  const signature = stableSignature(document);

  useEffect(() => {
    historyRef.current = resetActivityGraphHistory(historyRef.current, resetKey);
    documentRef.current = document;
    const model = derive(document);
    setNodes(model.nodes);
    setEdges(model.edges);
    setHistoryVersion(version => version + 1);
  }, [derive, document, resetKey]);

  useEffect(() => {
    if (emittedSignatureRef.current === signature) {
      emittedSignatureRef.current = null;
      return;
    }
    historyRef.current = createActivityGraphHistory<TDocument>(resetKey);
    setHistoryVersion(version => version + 1);
  }, [resetKey, signature]);

  const emit = useCallback((next: TDocument, record = true) => {
    if (record) {
      historyRef.current = recordActivityGraphEdit(historyRef.current, documentRef.current);
    }
    documentRef.current = next;
    emittedSignatureRef.current = stableSignature(next);
    const model = derive(next);
    setNodes(model.nodes);
    setEdges(model.edges);
    setHistoryVersion(version => version + 1);
    onChange(next);
  }, [derive, onChange]);

  const commitCanvas = useCallback((
    nextNodes: Node<WorkflowNodeData>[],
    nextEdges: WorkflowEdge[] = edges
  ) => {
    emit(applyModel(documentRef.current, nextNodes, nextEdges));
  }, [applyModel, edges, emit]);

  const undo = useCallback(() => {
    const step = undoActivityGraph(historyRef.current, documentRef.current);
    if (!step) return;
    historyRef.current = step.state;
    emit(step.document, false);
  }, [emit]);

  const redo = useCallback(() => {
    const step = redoActivityGraph(historyRef.current, documentRef.current);
    if (!step) return;
    historyRef.current = step.state;
    emit(step.document, false);
  }, [emit]);

  return {
    nodes,
    edges,
    setNodes,
    setEdges,
    onNodesChange: (changes: NodeChange<Node<WorkflowNodeData>>[]) =>
      setNodes(current => applyNodeChanges(changes, current)),
    onEdgesChange: (changes: EdgeChange<WorkflowEdge>[]) =>
      setEdges(current => applyEdgeChanges(changes, current)),
    commitCanvas,
    commitDocument: emit,
    undo,
    redo,
    canUndo: canUndoActivityGraph(historyRef.current),
    canRedo: canRedoActivityGraph(historyRef.current)
  };
}

function stableSignature(value: unknown) {
  return JSON.stringify(value);
}
