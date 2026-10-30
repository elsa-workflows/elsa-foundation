import { useLayoutEffect, useRef } from "react";
import { useStore, useUpdateNodeInternals, type InternalNode } from "@xyflow/react";
import { layoutGraphPorts, type GraphPortLayout, type PortNodeRect } from "./graphPortLayout";

function nodeRect(node?: InternalNode): PortNodeRect | undefined {
  const width = node?.measured.width;
  const height = node?.measured.height;
  if (!node || node.hidden || !width || !height) return undefined;
  return { ...node.internals.positionAbsolute, width, height };
}

/** Subscribe only to this node's connections; placement never enters saved graph data. */
export function useGraphPortLayout(id: string, sourceIds: readonly string[], acceptsInbound: boolean): GraphPortLayout {
  const previous = useRef<GraphPortLayout | undefined>(undefined);
  const updateNodeInternals = useUpdateNodeInternals();
  const measured = useStore(state => Boolean(nodeRect(state.nodeLookup.get(id))));
  const layout = useStore(state => {
    const incoming: PortNodeRect[] = [];
    const outgoing = new Map<string, PortNodeRect[]>();
    for (const connection of state.connectionLookup.get(id)?.values() ?? []) {
      if (connection.source === connection.target || state.edgeLookup.get(connection.edgeId)?.hidden) continue;
      const isSource = connection.source === id;
      const peer = nodeRect(state.nodeLookup.get(isSource ? connection.target : connection.source));
      if (!peer) continue;
      if (!isSource) incoming.push(peer);
      else {
        const portId = connection.sourceHandle ?? sourceIds[0];
        if (portId !== undefined) outgoing.set(portId, [...(outgoing.get(portId) ?? []), peer]);
      }
    }
    return layoutGraphPorts({ rect: nodeRect(state.nodeLookup.get(id)), sourceIds, acceptsInbound, incoming, outgoing, previous: previous.current });
  }, (left, right) => JSON.stringify(left) === JSON.stringify(right));

  useLayoutEffect(() => {
    previous.current = layout;
    // React Flow measures in the next animation frame, after the handles and card have moved.
    if (measured) updateNodeInternals(id);
  }, [id, layout, measured, updateNodeInternals]);
  return layout;
}
