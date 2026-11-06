import { useCallback, useEffect, useRef } from "react";
import { useOnViewportChange, useReactFlow, type Viewport } from "@xyflow/react";

/**
 * React Flow resets its store and rebuilds its pan-zoom from `defaultViewport` whenever its effects
 * are torn down and mount again, which happens when a background workspace tab returns (React keeps
 * the canvas but not its effects). Render this inside a `<ReactFlow>`, after its built-in panes, to
 * put back the viewport the user left.
 */
export function RetainFlowViewport() {
  const { setViewport } = useReactFlow();
  const retained = useRef<Viewport | null>(null);
  // Record each settled pan or zoom: by the time this component's effects are torn down, React Flow
  // has already reset the viewport it would report.
  const onEnd = useCallback((viewport: Viewport) => {
    retained.current = viewport;
  }, []);
  useOnViewportChange({ onEnd });
  useEffect(() => {
    if (retained.current) void setViewport(retained.current);
  }, [setViewport]);
  return null;
}
