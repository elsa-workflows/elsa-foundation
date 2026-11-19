/**
 * Lightweight auto-layout for the workflow canvas. Kept dependency-free (no dagre/elk)
 * because a single scope's graph is small and flat. Coordinates are top-left, matching
 * the @xyflow/react model and `defaultPosition` in workflowAdapter.ts.
 */

import { defaultNodeWidth, readSavedNodeWidth } from "./graph-authoring/nodeWidth";

export type LayoutMode = "flowchart" | "sequence";

export interface LayoutNode {
  id: string;
  /** Explicit (resized) width; nodes without one use the default node width. */
  width?: number;
}

export interface LayoutEdge {
  source: string;
  target: string;
}

export interface LayoutPosition {
  x: number;
  y: number;
}

// Each flowchart column starts after the widest node of the previous one plus a 194px gap,
// so the outcome port pill (rendered 10px outside the node's right edge) and the label at
// the edge midpoint both fit without overlapping for typical outcome names. With default
// 246px nodes that is a 440px column step.
const COLUMN_GAP = 194;
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

  const widthById = new Map(nodes.map(node => [node.id, readSavedNodeWidth(node.width) ?? defaultNodeWidth]));
  const columnX = new Map<number, number>();
  let x = 0;
  for (const column of [...columns.keys()].sort((a, b) => a - b)) {
    columnX.set(column, x);
    x += Math.max(...columns.get(column)!.map(id => widthById.get(id)!)) + COLUMN_GAP;
  }

  for (const [column, columnNodes] of columns) {
    columnNodes.forEach((id, row) => {
      positions.set(id, { x: columnX.get(column)!, y: row * ROW_STEP });
    });
  }

  return positions;
}
