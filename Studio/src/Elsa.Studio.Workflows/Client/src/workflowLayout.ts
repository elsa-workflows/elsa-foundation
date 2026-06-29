/**
 * Lightweight auto-layout for the workflow canvas. Kept dependency-free (no dagre/elk)
 * because a single scope's graph is small and flat. Coordinates are top-left, matching
 * the @xyflow/react model and `defaultPosition` in workflowAdapter.ts.
 */

export type LayoutMode = "flowchart" | "sequence";

export interface LayoutNode {
  id: string;
}

export interface LayoutEdge {
  source: string;
  target: string;
}

export interface LayoutPosition {
  x: number;
  y: number;
}

// Spacing chosen to match the canvas' native feel (sequence uses 280px steps).
const COLUMN_STEP = 320;
const ROW_STEP = 140;

export function computeAutoLayout(
  nodes: readonly LayoutNode[],
  edges: readonly LayoutEdge[],
  mode: LayoutMode
): Map<string, LayoutPosition> {
  if (mode === "sequence") return layoutSequence(nodes);
  return layoutFlowchart(nodes, edges);
}

function layoutSequence(nodes: readonly LayoutNode[]): Map<string, LayoutPosition> {
  const positions = new Map<string, LayoutPosition>();
  nodes.forEach((node, index) => {
    positions.set(node.id, { x: index * 280, y: 0 });
  });
  return positions;
}

function layoutFlowchart(
  nodes: readonly LayoutNode[],
  edges: readonly LayoutEdge[]
): Map<string, LayoutPosition> {
  const positions = new Map<string, LayoutPosition>();
  if (nodes.length === 0) return positions;

  const ids = new Set(nodes.map(node => node.id));
  const validEdges = edges.filter(edge => ids.has(edge.source) && ids.has(edge.target));

  // Nodes with no edges at all get parked in a trailing column so they don't
  // clutter the main flow.
  const connected = new Set<string>();
  for (const edge of validEdges) {
    connected.add(edge.source);
    connected.add(edge.target);
  }

  // Longest-path layering via bounded relaxation (Bellman-Ford style). The cap at
  // nodes.length keeps cycles from looping forever.
  const layer = new Map<string, number>();
  for (const node of nodes) layer.set(node.id, 0);
  for (let iteration = 0; iteration < nodes.length; iteration += 1) {
    let changed = false;
    for (const edge of validEdges) {
      const candidate = (layer.get(edge.source) ?? 0) + 1;
      if (candidate > (layer.get(edge.target) ?? 0) && candidate <= nodes.length) {
        layer.set(edge.target, candidate);
        changed = true;
      }
    }
    if (!changed) break;
  }

  const maxLayer = Math.max(0, ...nodes.filter(n => connected.has(n.id)).map(n => layer.get(n.id) ?? 0));
  const disconnectedColumn = connected.size > 0 ? maxLayer + 1 : 0;

  // Group by column, preserving the input order within each column for stability.
  const columns = new Map<number, string[]>();
  for (const node of nodes) {
    const column = connected.has(node.id) ? layer.get(node.id) ?? 0 : disconnectedColumn;
    const bucket = columns.get(column);
    if (bucket) bucket.push(node.id);
    else columns.set(column, [node.id]);
  }

  for (const [column, columnNodes] of columns) {
    columnNodes.forEach((id, row) => {
      positions.set(id, { x: column * COLUMN_STEP, y: row * ROW_STEP });
    });
  }

  return positions;
}
