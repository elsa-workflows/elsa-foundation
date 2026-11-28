import React from "react";
import {
  resolveStudioAdaptiveSidePanelLayout,
  studioPhoneWorkbenchMaximumWidth,
  useStudioSidePanelLayout,
  type StudioAdaptiveSidePanelLayout,
  type StudioSidePanelLayoutStorageKeys
} from "@elsa-workflows/studio-ui";
import {
  workflowInspectorCollapsedStorageKey,
  workflowInspectorWidthStorageKey,
  workflowPaletteCollapsedStorageKey,
  workflowPaletteWidthStorageKey,
  workflowSidePanelMaximizedStorageKey
} from "./constants";
import type { WorkflowSidePanel } from "./editorTypes";

const workflowPanelSelectors = { palette: ".wf-palette", inspector: ".wf-inspector" };

/** The widest measured workbench that sheets an open inspector (phone and 768px tablet, #4695). */
export const phoneWorkbenchMaximumWidth = studioPhoneWorkbenchMaximumWidth;

export type SidePanelLayoutStorageKeys = StudioSidePanelLayoutStorageKeys;

export interface SidePanelLayoutOptions {
  palettePresent?: boolean;
}

export type AdaptiveSidePanelLayout = StudioAdaptiveSidePanelLayout;

export type SidePanelEditorBodyStyle = React.CSSProperties & {
  "--wf-palette-width": string;
  "--wf-inspector-width": string;
};

export interface PhonePanelPresentation {
  /** The workbench is phone- or 768px-tablet-width: panels dock beside the canvas rather than float over it. */
  phoneWidth: boolean;
  /**
   * The open inspector takes the whole workbench as a sheet and the canvas is hidden until the
   * user goes back to it. Side by side, both would be left too narrow to read or select (#4695).
   */
  inspectorSheet: boolean;
}

export function resolvePhonePanelPresentation({
  containerWidth,
  inspectorExpanded,
  maximizedSidePanel
}: {
  containerWidth: number;
  inspectorExpanded: boolean;
  maximizedSidePanel: WorkflowSidePanel | null;
}): PhonePanelPresentation {
  const phoneWidth = Number.isFinite(containerWidth) && containerWidth > 0 && containerWidth <= phoneWorkbenchMaximumWidth;
  return {
    phoneWidth,
    inspectorSheet: phoneWidth && inspectorExpanded && !maximizedSidePanel
  };
}

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
    layout.maximizedSidePanel === "inspector" ? "inspector-maximized" : "",
    layout.phoneWidth ? "phone-layout" : "",
    layout.inspectorSheet ? "inspector-sheet" : ""
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
