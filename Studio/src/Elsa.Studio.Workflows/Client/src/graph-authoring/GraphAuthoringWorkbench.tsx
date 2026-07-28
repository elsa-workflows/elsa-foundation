import { ChevronLeft, ChevronRight, Maximize2, Minimize2 } from "lucide-react";
import type React from "react";
import type { StudioGraphAuthoringResourceKind } from "@elsa-workflows/studio-sdk";
import {
  maxInspectorWidth,
  maxPaletteWidth,
  minInspectorWidth,
  minPaletteWidth
} from "../workflow-editor/constants";
import type { WorkflowEditorPanelTab } from "../workflow-editor/editorTypes";
import { PanelTabList } from "../workflow-editor/PanelTabList";
import type { useSidePanelLayout } from "../workflow-editor/useSidePanelLayout";
import { GraphAuthoringWorkspace } from "./GraphAuthoringWorkspace";

type SidePanelLayout = ReturnType<typeof useSidePanelLayout>;

interface WorkbenchPanel {
  ariaLabel: string;
  tabLabel: string;
  tabs: WorkflowEditorPanelTab[];
  activeTabId: string;
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
  palette: WorkbenchPanel;
  canvas: React.ReactNode;
  inspector: WorkbenchPanel;
}) {
  const activePalette = palette.tabs.find(tab => tab.id === palette.activeTabId) ?? palette.tabs[0];
  const activeInspector = inspector.tabs.find(tab => tab.id === inspector.activeTabId) ?? inspector.tabs[0];

  return (
    <GraphAuthoringWorkspace
      resourceKind={resourceKind}
      className={[layout.editorBodyClassName, className].filter(Boolean).join(" ")}
      style={layout.editorBodyStyle}
      palette={(
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
      )}
      paletteResizeHandle={(
        <WorkbenchResizeHandle
          side="palette"
          visible={layout.paletteExpanded && !layout.maximizedSidePanel}
          value={layout.paletteWidth}
          onPointerDown={event => layout.startSidePanelResize("palette", event)}
          onKeyDown={event => layout.handleSidePanelResizeKeyDown("palette", event)}
        />
      )}
      canvas={canvas}
      inspectorResizeHandle={(
        <WorkbenchResizeHandle
          side="inspector"
          visible={layout.inspectorExpanded && !layout.maximizedSidePanel}
          value={layout.inspectorWidth}
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
            onSelect={inspector.onSelect}
            onToggleCollapsed={() => layout.toggleSidePanelCollapsed("inspector")}
            onToggleMaximized={() => layout.toggleSidePanelMaximized("inspector")}
          />
          {layout.inspectorExpanded ? activeInspector.render() : null}
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
  onSelect(tabId: string): void;
  onToggleCollapsed(): void;
  onToggleMaximized(): void;
}) {
  const expandIcon = side === "palette" ? <ChevronRight size={14} /> : <ChevronLeft size={14} />;
  const collapseIcon = side === "palette" ? <ChevronLeft size={14} /> : <ChevronRight size={14} />;
  const panelName = side === "palette" ? "activities" : "inspector";

  return (
    <div className="wf-panel-title">
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
  onPointerDown,
  onKeyDown
}: {
  side: "palette" | "inspector";
  visible: boolean;
  value: number;
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
      aria-valuemin={palette ? minPaletteWidth : minInspectorWidth}
      aria-valuemax={palette ? maxPaletteWidth : maxInspectorWidth}
      aria-valuenow={value}
      tabIndex={0}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
    />
  );
}
