import type { CSSProperties, ReactNode } from "react";
import type { StudioGraphAuthoringResourceKind } from "@elsa-workflows/studio-sdk";

export interface GraphAuthoringWorkspaceProps {
  resourceKind: StudioGraphAuthoringResourceKind;
  className?: string;
  style?: CSSProperties;
  palette: ReactNode;
  paletteResizeHandle?: ReactNode;
  canvas: ReactNode;
  inspectorResizeHandle?: ReactNode;
  inspector: ReactNode;
}

/**
 * Shared workbench composition for graph resources. The resource host supplies controlled content
 * and retains persistence/lifecycle ownership; this component fixes the common palette/canvas/
 * inspector landmark order used by workflow and Activity Definition graph authoring.
 */
export function GraphAuthoringWorkspace({
  resourceKind,
  className,
  style,
  palette,
  paletteResizeHandle,
  canvas,
  inspectorResizeHandle,
  inspector
}: GraphAuthoringWorkspaceProps) {
  return (
    <div
      className={["graph-authoring-workspace", className].filter(Boolean).join(" ")}
      style={style}
      data-graph-authoring-resource={resourceKind}
    >
      {palette}
      {paletteResizeHandle}
      {canvas}
      {inspectorResizeHandle}
      {inspector}
    </div>
  );
}
