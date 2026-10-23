import { useCallback, useEffect, useRef } from "react";
import type { Node, OnMove, ReactFlowInstance, Viewport } from "@xyflow/react";

/** First-visit and auto-layout fit: keep small diagrams at their natural size. */
export const graphFitViewOptions = { padding: 0.2, maxZoom: 1 } as const;

/** One initial fit per projected scope; later visits restore the user's viewport. */
export function useGraphViewport<TNode extends Node = Node>({ nodes, scopeKey, instance }: {
  nodes: readonly unknown[];
  scopeKey: string;
  instance: Pick<ReactFlowInstance<TNode>, "fitView" | "setViewport"> | null;
}) {
  const snapshots = useRef(new Map<string, Viewport>());
  const visited = useRef(new Set<string>());
  const projectedScope = useRef<string | null>(null);
  const staleNodes = useRef<readonly unknown[] | null>(null);
  const settledInstance = useRef<typeof instance>(null);

  // Capture moves while the canvas is mounted. Its old instance may have been reset by the time
  // Code/Properties switches back to Designer, so reading that instance during remount is too late.
  const onMoveEnd = useCallback<OnMove>((_event, viewport) => {
    snapshots.current.set(scopeKey, { ...viewport });
    visited.current.add(scopeKey);
  }, [scopeKey]);

  useEffect(() => {
    if (projectedScope.current !== scopeKey) {
      projectedScope.current = scopeKey;
      staleNodes.current = nodes;
      return;
    }
    // Hosts project the new scope in an effect. Do not fit the previous scope's node array,
    // or consume this pending projection before React Flow's delayed onInit arrives.
    if (!instance || staleNodes.current === nodes) return;
    const remounted = settledInstance.current !== instance;
    if (staleNodes.current === null && !remounted) return;

    const frame = window.requestAnimationFrame(() => {
      staleNodes.current = null;
      settledInstance.current = instance;
      const saved = snapshots.current.get(scopeKey);
      const firstVisit = !visited.current.has(scopeKey);
      visited.current.add(scopeKey);
      if (saved) {
        void instance.setViewport(saved);
      } else if ((firstVisit || remounted) && nodes.length > 0) {
        // React Flow queues this until its nodes have been measured. Keep small diagrams at
        // their natural size while fitting larger graphs inside the available canvas.
        void instance.fitView(graphFitViewOptions);
      }
    });
    return () => window.cancelAnimationFrame(frame);
  }, [instance, nodes, scopeKey]);

  return { onMoveEnd };
}
