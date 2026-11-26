import React from "react";
import { resolveStudioAdaptiveSidePanelLayout, useStudioSidePanelLayout, type StudioAdaptiveSidePanelLayout, type StudioSidePanelLayoutStorageKeys } from "@elsa-workflows/studio-ui";
import {
  workflowInspectorCollapsedStorageKey,
  workflowInspectorWidthStorageKey,
  workflowPaletteCollapsedStorageKey,
  workflowPaletteWidthStorageKey,
  workflowSidePanelMaximizedStorageKey
} from "./constants";
import type { WorkflowSidePanel } from "./editorTypes";

const workflowPanelSelectors = { palette: ".wf-palette", inspector: ".wf-inspector" };

export type SidePanelLayoutStorageKeys = StudioSidePanelLayoutStorageKeys;

export interface SidePanelLayoutOptions {
  palettePresent?: boolean;
}

export type AdaptiveSidePanelLayout = StudioAdaptiveSidePanelLayout;

export type SidePanelEditorBodyStyle = React.CSSProperties & {
  "--wf-palette-width": string;
  "--wf-inspector-width": string;
};

/**
 * Resolves rendered panel sizes from the available workbench width without changing the saved
 * user preferences. When both panels no longer fit at their minimum widths, the activity palette
 * becomes a rail first. On phones, both become rails until a tool is explicitly opened,
 * retaining room for the canvas without changing saved preferences. Hosts without a
 * palette allocate that width and its resize handle directly to the canvas and inspector.
 */
export function resolveAdaptiveSidePanelLayout(input: {
  containerWidth: number;
  preferredPaletteWidth: number;
  preferredInspectorWidth: number;
  paletteCollapsed: boolean;
  inspectorCollapsed: boolean;
  maximizedSidePanel: WorkflowSidePanel | null;
  responsiveExpandedSide: WorkflowSidePanel | null;
  resizePriority?: WorkflowSidePanel | null;
  palettePresent?: boolean;
}): AdaptiveSidePanelLayout {
  return resolveStudioAdaptiveSidePanelLayout(input);
}

const workflowSidePanelStorageKeys: SidePanelLayoutStorageKeys = {
  paletteWidth: workflowPaletteWidthStorageKey,
  inspectorWidth: workflowInspectorWidthStorageKey,
  paletteCollapsed: workflowPaletteCollapsedStorageKey,
  inspectorCollapsed: workflowInspectorCollapsedStorageKey,
  maximized: workflowSidePanelMaximizedStorageKey
};

export function useSidePanelLayout(
  storageKeys: SidePanelLayoutStorageKeys = workflowSidePanelStorageKeys,
  options: SidePanelLayoutOptions = {}
) {
  const palettePresent = options.palettePresent ?? true;
  const layout = useStudioSidePanelLayout(storageKeys, {
    palettePresent,
    panelSelectors: workflowPanelSelectors,
    resizingBodyClassName: "wf-side-panel-resizing"
  });
  const editorBodyClassName = [
    "wf-editor-body",
    layout.paletteCollapsed ? "palette-collapsed" : "",
    layout.inspectorCollapsed ? "inspector-collapsed" : "",
    layout.maximizedSidePanel === "palette" ? "palette-maximized" : "",
    layout.maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  const editorBodyStyle: SidePanelEditorBodyStyle = {
    "--wf-palette-width": `${layout.paletteWidth}px`,
    "--wf-inspector-width": `${layout.inspectorWidth}px`
  };

  return {
    ...layout,
    editorBodyClassName,
    editorBodyStyle
  };
}
