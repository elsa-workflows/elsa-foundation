import {
  studioCollapsedSidePanelWidth as collapsedSidePanelWidth,
  studioDefaultInspectorWidth as defaultInspectorWidth,
  studioDefaultPaletteWidth as defaultPaletteWidth,
  studioMaxInspectorWidth as maxInspectorWidth,
  studioMaxPaletteWidth as maxPaletteWidth,
  studioMinInspectorWidth as minInspectorWidth,
  studioMinPaletteWidth as minPaletteWidth,
  studioSidePanelResizeStep as sidePanelResizeStep
} from "@elsa-workflows/studio-ui";

export const activityDragDataType = "application/x-elsa-activity-version-id";
export const pointerDragThreshold = 6;
export const autosaveDelayMs = 1200;
export const historyCaptureDelayMs = 250;
export const definitionPageSizes = [10, 25, 50];
export const defaultDefinitionPageSize = 10;
export {
  collapsedSidePanelWidth,
  defaultInspectorWidth,
  defaultPaletteWidth,
  maxInspectorWidth,
  maxPaletteWidth,
  minInspectorWidth,
  minPaletteWidth,
  sidePanelResizeStep
};
export const workflowPaletteWidthStorageKey = "elsa-studio-workflow-palette-width";
export const workflowInspectorWidthStorageKey = "elsa-studio-workflow-inspector-width";
export const workflowPaletteCollapsedStorageKey = "elsa-studio-workflow-palette-collapsed";
export const workflowInspectorCollapsedStorageKey = "elsa-studio-workflow-inspector-collapsed";
export const workflowSidePanelMaximizedStorageKey = "elsa-studio-workflow-side-panel-maximized";
export const activityGraphPaletteWidthStorageKey = "elsa-studio-activity-graph-palette-width";
export const activityGraphInspectorWidthStorageKey = "elsa-studio-activity-graph-inspector-width";
export const activityGraphPaletteCollapsedStorageKey = "elsa-studio-activity-graph-palette-collapsed";
export const activityGraphInspectorCollapsedStorageKey = "elsa-studio-activity-graph-inspector-collapsed";
export const activityGraphSidePanelMaximizedStorageKey = "elsa-studio-activity-graph-side-panel-maximized";
// Shown on AI-only toolbar actions (Risks/Propose) when the Weaver provider is not configured/reachable.
export const weaverUnavailableTitle = "Risk analysis requires the Weaver assistant, which is not configured.";
