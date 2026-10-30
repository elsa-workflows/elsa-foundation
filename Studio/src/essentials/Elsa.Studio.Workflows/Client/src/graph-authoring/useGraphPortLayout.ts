import { useLayoutEffect, useRef } from "react";
import { Position, useStore, useUpdateNodeInternals, type InternalNode } from "@xyflow/react";
import { layoutGraphPorts, type GraphPortLayout, type PortNodeRect } from "./graphPortLayout";

function nodeRect(node?: InternalNode): PortNodeRect | undefined {
  const width = node?.measured.width;
  const height = node?.measured.height;
  if (!node || node.hidden || !width || !height) return undefined;
  return { ...node.internals.positionAbsolute, width, height };
}

/** Subscribe only to this node's connections; placement never enters saved graph data. */
export function useGraphPortLayout(id: string, sourceIds: readonly string[], acceptsInbound: boolean): GraphPortLayout & { toolbarOffset: number } {
  const previous = useRef<GraphPortLayout | undefined>(undefined);
  const updateNodeInternals = useUpdateNodeInternals();
  const measured = useStore(state => Boolean(nodeRect(state.nodeLookup.get(id))));
  const snapshot = useStore(state => {
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
    const rect = nodeRect(state.nodeLookup.get(id));
    const layout = layoutGraphPorts({ rect, sourceIds, acceptsInbound, incoming, outgoing, previous: previous.current });
    // Unmeasured and disconnected defaults are not a geometry choice. Start hysteresis only
    // after each handle has usable connected geometry, including when peers measure later.
    const history: GraphPortLayout | undefined = rect ? {
      target: incoming.length > 0 ? layout.target : undefined,
      sources: layout.sources.filter(port => outgoing.has(port.id))
    } : undefined;
    return { layout, history };
  }, (left, right) => JSON.stringify(left) === JSON.stringify(right));
  const { layout } = snapshot;
  // Labels scale with the canvas; NodeToolbar stays at screen size. Clear the 18px pill and
  // its 10px outside gap at the current zoom, plus the toolbar's usual 10px breathing room.
  const toolbarOffset = useStore(state => layout.sources.some(port => port.side === Position.Top)
    ? 28 * state.transform[2] + 10 : 10);

  useLayoutEffect(() => {
    previous.current = snapshot.history;
    // React Flow measures in the next animation frame, after the handles and card have moved.
    if (measured) updateNodeInternals(id);
  }, [id, snapshot, measured, updateNodeInternals]);
  return { ...layout, toolbarOffset };
}
