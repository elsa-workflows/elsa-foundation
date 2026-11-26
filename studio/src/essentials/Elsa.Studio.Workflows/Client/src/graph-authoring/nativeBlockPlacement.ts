interface PlacementNode {
  id: string;
  position: { x: number; y: number };
  height?: number;
  measured?: { height?: number };
}

/** Place below the host's projected scope; keep its existing horizontal extent and node positions. */
export function nextNativeBlockPosition(nodes: readonly PlacementNode[], primaryNodeId: string | null) {
  const finite = (value: number | undefined, fallback: number) => typeof value === "number" && Number.isFinite(value) ? value : fallback;
  let y = 100;
  for (const node of nodes) {
    // Unmeasured native cards reserve space until React Flow supplies their actual bounds.
    const height = Math.max(256, finite(node.height, 0), finite(node.measured?.height, 0));
    y = Math.max(y, finite(node.position.y, 0) + height + 48);
  }
  const anchor = nodes.find(node => node.id === primaryNodeId) ?? nodes[0];
  return { x: finite(anchor?.position.x, 100), y };
}
