import { useCallback, useEffect, useMemo, useState } from "react";
import { applyNodeChanges, useNodesInitialized, useReactFlow, useStore, type Node, type NodeChange } from "@xyflow/react";
import { readOnlyMinimapFitViewOptions, shouldShowReadOnlyMiniMap } from "./readOnlyMinimap";

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

/** Re-fit after xyflow measures nodes, and again when the pane shrinks (status line, error card). */
export function FitReadOnlyFlowView({ nodeCount }: { nodeCount: number }) {
  const initialized = useNodesInitialized();
  const { fitView } = useReactFlow();
  const paneWidth = useStore(state => state.width);
  const paneHeight = useStore(state => state.height);

  useEffect(() => {
    if (!initialized || paneWidth <= 0 || paneHeight <= 0) return;
    void fitView(shouldShowReadOnlyMiniMap(nodeCount) ? readOnlyMinimapFitViewOptions : { padding: 0.12 });
  }, [fitView, initialized, nodeCount, paneHeight, paneWidth]);

  return null;
}
