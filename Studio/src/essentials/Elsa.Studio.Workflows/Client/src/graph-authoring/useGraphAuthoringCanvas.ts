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
import type { GraphCanvasCommitOptions } from "./useGraphCanvasInteractions";

export interface GraphAuthoringCanvasModel {
  nodes: Node<WorkflowNodeData>[];
  edges: WorkflowEdge[];
}

export function useGraphAuthoringCanvas<TDocument>({
  document,
  adapter,
  resetKey,
  selectedNodeId = null,
  buildModel,
  applyModel,
  reconcileDocument,
  documentSignature = stableSignature,
  onChange,
  tryChange
}: {
  document: TDocument;
  adapter: GraphDocumentAdapter<TDocument>;
  resetKey: string;
  /**
   * Re-applied whenever the canvas is rebuilt from the document, so a node stays visibly selected
   * across an inspector edit. Read through a ref: making it a dependency would rebuild (and discard
   * uncommitted canvas state) on every selection change.
   */
  selectedNodeId?: string | null;
  buildModel(root: ReturnType<GraphDocumentAdapter<TDocument>["readRoot"]>, layout: ReturnType<GraphDocumentAdapter<TDocument>["readLayout"]>, document: TDocument): GraphAuthoringCanvasModel;
  applyModel(
    document: TDocument,
    nodes: Node<WorkflowNodeData>[],
    edges: WorkflowEdge[],
    options?: GraphCanvasCommitOptions
  ): TDocument;
  reconcileDocument?(previous: TDocument, next: TDocument): TDocument;
  /** Compare server acknowledgements using the host's persisted representation. */
  documentSignature?(document: TDocument): string;
  /** Legacy notification; its return value is not a parent acknowledgement. */
  onChange(document: TDocument): void;
  /** Optional synchronous admission against the exact acknowledged baseline. */
  tryChange?(document: TDocument, expectedDocument: TDocument): boolean;
}) {
  const selectedNodeIdRef = useRef(selectedNodeId);
  selectedNodeIdRef.current = selectedNodeId;
  const derive = useCallback(
    (source: TDocument): GraphAuthoringCanvasModel => {
      const model = buildModel(adapter.readRoot(source), adapter.readLayout(source), source);
      return {
        ...model,
        nodes: model.nodes.map(node => ({ ...node, selected: node.id === selectedNodeIdRef.current }))
      };
    },
    [adapter, buildModel]
  );
  const initial = useMemo(() => derive(document), [derive, document]);
  const [nodes, setNodes] = useState(initial.nodes);
  const [edges, setEdges] = useState(initial.edges);
  const documentRef = useRef(document);
  const emittedSignatureRef = useRef<string | null>(null);
  const historyRef = useRef(createActivityGraphHistory<TDocument>(resetKey));
  const [, setHistoryVersion] = useState(0);
  const signature = documentSignature(document);

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

  const emit = useCallback((candidate: TDocument, record = true, nextHistory = historyRef.current) => {
    const previous = documentRef.current;
    const next = reconcileDocument?.(previous, candidate) ?? candidate;
    const model = derive(next);
    const nextSignature = documentSignature(next);
    const history = record ? recordActivityGraphEdit(historyRef.current, previous) : nextHistory;
    const previousEmission = emittedSignatureRef.current;
    const restoreCanvas = () => {
      emittedSignatureRef.current = previousEmission;
      const current = derive(documentRef.current);
      setNodes(current.nodes);
      setEdges(current.edges);
    };
    // Parent admission is authoritative. A child history entry or optimistic document is not a
    // receipt when the Activity Definition host has closed or advanced this provider draft.
    // Register only the echo marker early: a synchronous parent render may run the signature effect.
    emittedSignatureRef.current = nextSignature;
    try {
      if (tryChange) {
        const accepted: unknown = tryChange(next, previous);
        if (accepted !== true) {
          // A malformed async host is never an acknowledgement; observe a rejected Promise
          // without letting its eventual completion commit child history.
          void Promise.resolve(accepted).catch(() => undefined);
          restoreCanvas();
          return false;
        }
      } else {
        onChange(next);
      }
    }
    catch (error) { restoreCanvas(); throw error; }
    historyRef.current = history;
    documentRef.current = next;
    setNodes(model.nodes);
    setEdges(model.edges);
    setHistoryVersion(version => version + 1);
    return true;
  }, [derive, documentSignature, onChange, reconcileDocument, tryChange]);

  const commitCanvas = useCallback((
    nextNodes: Node<WorkflowNodeData>[],
    nextEdges: WorkflowEdge[] = edges,
    options?: GraphCanvasCommitOptions
  ) => {
    return emit(applyModel(documentRef.current, nextNodes, nextEdges, options));
  }, [applyModel, edges, emit]);

  const undo = useCallback(() => {
    const step = undoActivityGraph(historyRef.current, documentRef.current);
    if (!step) return;
    return emit(step.document, false, step.state);
  }, [emit]);

  const redo = useCallback(() => {
    const step = redoActivityGraph(historyRef.current, documentRef.current);
    if (!step) return;
    return emit(step.document, false, step.state);
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
    /** Revision-check a controlled canvas against the synchronously acknowledged document. */
    updateDocument: (recipe: (current: TDocument) => TDocument | null): boolean => {
      const next = recipe(documentRef.current);
      if (!next) return false;
      return emit(next);
    },
    undo,
    redo,
    canUndo: canUndoActivityGraph(historyRef.current),
    canRedo: canRedoActivityGraph(historyRef.current)
  };
}

function stableSignature(value: unknown) {
  return JSON.stringify(value);
}
