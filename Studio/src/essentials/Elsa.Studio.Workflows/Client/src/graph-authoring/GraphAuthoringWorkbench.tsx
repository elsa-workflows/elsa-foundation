import { ArrowLeft, ChevronLeft, ChevronRight, Maximize2, Minimize2 } from "lucide-react";
import type React from "react";
import type { StudioGraphAuthoringResourceKind } from "@elsa-workflows/studio-sdk";
import {
  maxInspectorWidth,
  maxPaletteWidth
} from "../workflow-editor/constants";
import type { WorkflowEditorPanelTab } from "../workflow-editor/editorTypes";
import { PanelTabList } from "../workflow-editor/PanelTabList";
import type { useSidePanelLayout } from "../workflow-editor/useSidePanelLayout";
import { GraphAuthoringWorkspace } from "./GraphAuthoringWorkspace";
import "./graphAuthoringLayouts.css";
import "./workspaceAppearance.css";

type SidePanelLayout = ReturnType<typeof useSidePanelLayout>;

interface WorkbenchPanel {
  ariaLabel: string;
  tabLabel: string;
  tabs: WorkflowEditorPanelTab[];
  activeTabId: string;
  /** Opt into stable panel containers, retaining these tabs when inactive. Hidden panels are inert. */
  keepMountedTabIds?: readonly string[];
  onSelect(tabId: string): void;
}

export function GraphAuthoringWorkbench({
  resourceKind,
  className,
  layout,
  palette,
  canvas,
  inspector
}: {
  resourceKind: StudioGraphAuthoringResourceKind;
  className?: string;
  layout: SidePanelLayout;
  /** Absent when the resource has no left panel to show (e.g. the editorial layout's inline authoring). */
  palette?: WorkbenchPanel;
  canvas: React.ReactNode;
  inspector: WorkbenchPanel;
}) {
  const activePalette = palette ? palette.tabs.find(tab => tab.id === palette.activeTabId) ?? palette.tabs[0] : undefined;
  const activeInspector = inspector.tabs.find(tab => tab.id === inspector.activeTabId) ?? inspector.tabs[0];

  return (
    <GraphAuthoringWorkspace
      resourceKind={resourceKind}
      containerRef={layout.containerRef}
      className={[layout.editorBodyClassName, palette ? "" : "palette-absent", className].filter(Boolean).join(" ")}
      style={layout.editorBodyStyle}
      palette={palette && activePalette ? (
        <aside className="wf-palette" aria-label={palette.ariaLabel}>
          <WorkbenchPanelHeader
            side="palette"
            tabLabel={palette.tabLabel}
            tabs={palette.tabs}
            activeTabId={activePalette.id}
            collapsed={layout.paletteCollapsed}
            maximized={layout.maximizedSidePanel === "palette"}
            onSelect={palette.onSelect}
            onToggleCollapsed={() => layout.toggleSidePanelCollapsed("palette")}
            onToggleMaximized={() => layout.toggleSidePanelMaximized("palette")}
          />
          {layout.paletteExpanded ? activePalette.render() : null}
        </aside>
      ) : null}
      paletteResizeHandle={palette ? (
        <WorkbenchResizeHandle
          side="palette"
          visible={layout.paletteExpanded && !layout.maximizedSidePanel && !layout.inspectorSheet}
          value={layout.paletteWidth}
          min={layout.paletteMinimumWidth}
          onPointerDown={event => layout.startSidePanelResize("palette", event)}
          onKeyDown={event => layout.handleSidePanelResizeKeyDown("palette", event)}
        />
      ) : null}
      canvas={canvas}
      inspectorResizeHandle={(
        <WorkbenchResizeHandle
          side="inspector"
          visible={layout.inspectorExpanded && !layout.maximizedSidePanel && !layout.inspectorSheet}
          value={layout.inspectorWidth}
          min={layout.inspectorMinimumWidth}
          onPointerDown={event => layout.startSidePanelResize("inspector", event)}
          onKeyDown={event => layout.handleSidePanelResizeKeyDown("inspector", event)}
        />
      )}
      inspector={(
        <aside className="wf-inspector" aria-label={inspector.ariaLabel}>
          <WorkbenchPanelHeader
            side="inspector"
            tabLabel={inspector.tabLabel}
            tabs={inspector.tabs}
            activeTabId={activeInspector.id}
            collapsed={layout.inspectorCollapsed}
            maximized={layout.maximizedSidePanel === "inspector"}
            onBackToCanvas={layout.phoneWidth && layout.inspectorExpanded ? workbench => {
              layout.toggleSidePanelCollapsed("inspector");
              requestAnimationFrame(() => focusCanvasSelection(workbench));
            } : undefined}
            onSelect={inspector.onSelect}
            onToggleCollapsed={() => layout.toggleSidePanelCollapsed("inspector")}
            onToggleMaximized={() => layout.toggleSidePanelMaximized("inspector")}
          />
          {inspector.keepMountedTabIds !== undefined ? inspector.tabs.filter(tab => layout.inspectorExpanded && tab.id === activeInspector.id || inspector.keepMountedTabIds?.includes(tab.id)).map(tab => (
            <div key={tab.id} className="wf-workbench-panel-content" hidden={!layout.inspectorExpanded || tab.id !== activeInspector.id}
              inert={!layout.inspectorExpanded || tab.id !== activeInspector.id}>
              {tab.render()}
            </div>
          )) : layout.inspectorExpanded ? activeInspector.render() : null}
        </aside>
      )}
    />
  );
}

function WorkbenchPanelHeader({
  side,
  tabLabel,
  tabs,
  activeTabId,
  collapsed,
  maximized,
  onBackToCanvas,
  onSelect,
  onToggleCollapsed,
  onToggleMaximized
}: {
  side: "palette" | "inspector";
  tabLabel: string;
  tabs: WorkflowEditorPanelTab[];
  activeTabId: string;
  collapsed: boolean;
  maximized: boolean;
  /** Offered while a phone-width panel covers the canvas: returns to the canvas. */
  onBackToCanvas?(workbench: HTMLElement | null): void;
  onSelect(tabId: string): void;
  onToggleCollapsed(): void;
  onToggleMaximized(): void;
}) {
  const expandIcon = side === "palette" ? <ChevronRight size={14} /> : <ChevronLeft size={14} />;
  const collapseIcon = side === "palette" ? <ChevronLeft size={14} /> : <ChevronRight size={14} />;
  const panelName = side === "palette" ? "activities" : "inspector";

  return (
    <div className="wf-panel-title">
      {onBackToCanvas ? (
        <button type="button" className="wf-panel-back-button" aria-label="Back to canvas" title="Back to canvas"
          onClick={event => onBackToCanvas(event.currentTarget.closest<HTMLElement>(".wf-editor-body"))}>
          <ArrowLeft size={14} aria-hidden="true" />
          <span>Canvas</span>
        </button>
      ) : null}
      <PanelTabList label={tabLabel} tabs={tabs} activeTabId={activeTabId} onSelect={onSelect} />
      <span className="wf-panel-actions">
        <button
          type="button"
          className="wf-panel-action-button"
          aria-label={collapsed ? `Expand ${panelName} panel` : `Collapse ${panelName} panel`}
          title={collapsed ? "Expand" : "Collapse"}
          onClick={onToggleCollapsed}
        >
          {collapsed ? expandIcon : collapseIcon}
        </button>
        {!collapsed ? (
          <button
            type="button"
            className="wf-panel-action-button"
            aria-label={maximized ? `Restore ${panelName} panel` : `Maximize ${panelName} panel`}
            title={maximized ? "Restore" : "Maximize"}
            onClick={onToggleMaximized}
          >
            {maximized ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
          </button>
        ) : null}
      </span>
    </div>
  );
}

function WorkbenchResizeHandle({
  side,
  visible,
  value,
  min,
  onPointerDown,
  onKeyDown
}: {
  side: "palette" | "inspector";
  visible: boolean;
  value: number;
  min: number;
  onPointerDown(event: React.PointerEvent<HTMLDivElement>): void;
  onKeyDown(event: React.KeyboardEvent<HTMLDivElement>): void;
}) {
  if (!visible) return <div className="wf-side-resize-spacer" />;
  const palette = side === "palette";
  return (
    <div
      className={`wf-side-resize-handle ${palette ? "left" : "right"}`}
      role="separator"
      aria-label={`Resize ${palette ? "activities" : "inspector"} panel`}
      aria-orientation="vertical"
      aria-valuemin={min}
      aria-valuemax={palette ? maxPaletteWidth : maxInspectorWidth}
      aria-valuenow={value}
      tabIndex={0}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
    />
  );
}

/**
 * Returns keyboard focus to the canvas after a phone-width inspector sheet closes: the selected
 * activity when there is one, otherwise the control that reopens the inspector. The sheet's own
 * button is gone by then, and focus must not fall back to the page.
 */
function focusCanvasSelection(workbench: HTMLElement | null) {
  if (!workbench) return;
  const selected = workbench.querySelector<HTMLElement>(
    ".wf-canvas-shell :is(.react-flow__node.selected, .wf-sequence-step[aria-pressed='true'])"
  );
  (selected ?? workbench.querySelector<HTMLElement>(".wf-inspector .wf-panel-actions button"))?.focus();
}
