import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { applyNodeChanges, useNodesInitialized, useReactFlow, useStore, type Node, type NodeChange, type Viewport } from "@xyflow/react";
import { getReadOnlyFitViewOptions, shouldShowReadOnlyMiniMap } from "./readOnlyMinimap";
import { getReadableFitViewport, pickReadOnlyFocusNode, readableEdgeMargin, readableMinimapEdgeMargin } from "./readableFitViewport";

function isSameViewport(left: Viewport, right: Viewport): boolean {
  return left.x === right.x && left.y === right.y && left.zoom === right.zoom;
}

export function mergeMeasuredNodeDimensions<T extends Node>(nodes: T[], measuredNodes: T[]): T[] {
  const measuredById = new Map(measuredNodes.map(node => [node.id, node]));
  return nodes.map(node => {
    const measuredNode = measuredById.get(node.id);
    return measuredNode && measuredNode.type === node.type && measuredNode.measured
      ? { ...node, measured: measuredNode.measured }
      : node;
  });
}

/** Keep MiniMap and fitView on measured node boxes so custom activity nodes are not 0×0. */
export function useReadOnlyFlowNodes<T extends Node>(nodes: T[]) {
  const [measuredNodes, setMeasuredNodes] = useState<T[]>([]);
  const flowNodes = useMemo(() => mergeMeasuredNodeDimensions(nodes, measuredNodes), [measuredNodes, nodes]);

  useEffect(() => {
    setMeasuredNodes(current => {
      const activeTypes = new Map(nodes.map(node => [node.id, node.type]));
      const next = current.filter(node => activeTypes.get(node.id) === node.type);
      return next.length === current.length ? current : next;
    });
  }, [nodes]);

  const onNodesChange = useCallback((changes: NodeChange<T>[]) => {
    const dimensionChanges = changes.filter(change => change.type === "dimensions");
    if (dimensionChanges.length === 0) return;
    setMeasuredNodes(current => applyNodeChanges(dimensionChanges, mergeMeasuredNodeDimensions(nodes, current)));
  }, [nodes]);

  return { flowNodes, onNodesChange };
}

/**
 * Re-fit after xyflow measures nodes, and again when the pane shrinks (status line, error card),
 * but only while the viewport is still the one the last auto-fit produced. A user pan/zoom or a
 * focus/zoom-to-node request releases ownership until this canvas remounts for a new graph.
 *
 * With `minReadableZoom`, the auto-fit never goes below that zoom: when the whole graph cannot fit
 * legibly, it shows a readable part starting at the graph's reading start (leftmost, then topmost
 * activity), clamped to the graph's edges and kept clear of the MiniMap (#4064). The Fit View control
 * keeps fitting the whole graph, so the full overview stays one click away.
 */
export function FitReadOnlyFlowView({ nodeCount, miniMap = shouldShowReadOnlyMiniMap(nodeCount), focusRequestId = 0, minReadableZoom }: { nodeCount: number; miniMap?: boolean; focusRequestId?: number; minReadableZoom?: number }) {
  const initialized = useNodesInitialized();
  const { fitView, getViewport, getNodes, getNodesBounds, setViewport } = useReactFlow();
  const paneWidth = useStore(state => state.width);
  const paneHeight = useStore(state => state.height);
  const storeMinZoom = useStore(state => state.minZoom);
  const storeMaxZoom = useStore(state => state.maxZoom);
  const lastAutoFitViewport = useRef<Viewport | null>(null);
  const seenFocusRequestId = useRef(focusRequestId);
  const autoFitReleased = useRef(false);

  useEffect(() => {
    if (focusRequestId !== seenFocusRequestId.current) {
      seenFocusRequestId.current = focusRequestId;
      autoFitReleased.current = true;
    }
    if (!initialized || paneWidth <= 0 || paneHeight <= 0 || autoFitReleased.current) return;

    const lastViewport = lastAutoFitViewport.current;
    if (lastViewport && !isSameViewport(getViewport(), lastViewport)) {
      autoFitReleased.current = true;
      return;
    }

    const options = getReadOnlyFitViewOptions(nodeCount, miniMap);
    const recordAutoFit = () => { lastAutoFitViewport.current = getViewport(); };
    if (minReadableZoom === undefined) {
      void fitView(options).then(recordAutoFit);
      return;
    }

    const nodes = getNodes();
    if (nodes.length === 0) return;
    const focusNode = pickReadOnlyFocusNode(nodes);
    void setViewport(getReadableFitViewport({
      graph: getNodesBounds(nodes),
      focus: focusNode ? getNodesBounds([focusNode]) : null,
      width: paneWidth,
      height: paneHeight,
      padding: options.padding,
      minZoom: storeMinZoom ?? 0.1,
      maxZoom: storeMaxZoom ?? 2,
      readableZoom: minReadableZoom,
      margin: miniMap ? readableMinimapEdgeMargin : readableEdgeMargin
    })).then(recordAutoFit);
  }, [fitView, focusRequestId, getNodes, getNodesBounds, getViewport, initialized, miniMap, minReadableZoom, nodeCount, paneHeight, paneWidth, setViewport, storeMaxZoom, storeMinZoom]);

  return null;
}
