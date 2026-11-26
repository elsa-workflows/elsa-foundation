import type React from "react";
import type { StudioGraphAuthoringResourceKind } from "@elsa-workflows/studio-sdk";
import { StudioSidePanelActions, StudioSidePanelResizeHandle } from "@elsa-workflows/studio-ui";
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
  className?: string;
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
  resourceKind?: StudioGraphAuthoringResourceKind;
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
        <StudioSidePanelResizeHandle
          side="palette"
          visible={layout.paletteExpanded && !layout.maximizedSidePanel}
          value={layout.paletteWidth}
          min={layout.paletteMinimumWidth}
          max={maxPaletteWidth}
          panelLabel="activities"
          onPointerDown={event => layout.startSidePanelResize("palette", event)}
          onKeyDown={event => layout.handleSidePanelResizeKeyDown("palette", event)}
          className="wf-side-resize-handle"
          spacerClassName="wf-side-resize-spacer"
        />
      ) : null}
      canvas={canvas}
      inspectorResizeHandle={(
        <StudioSidePanelResizeHandle
          side="inspector"
          visible={layout.inspectorExpanded && !layout.maximizedSidePanel}
          value={layout.inspectorWidth}
          min={layout.inspectorMinimumWidth}
          max={maxInspectorWidth}
          panelLabel="inspector"
          onPointerDown={event => layout.startSidePanelResize("inspector", event)}
          onKeyDown={event => layout.handleSidePanelResizeKeyDown("inspector", event)}
          className="wf-side-resize-handle"
          spacerClassName="wf-side-resize-spacer"
        />
      )}
      inspector={(
        <aside className={["wf-inspector", inspector.className].filter(Boolean).join(" ")} aria-label={inspector.ariaLabel}>
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
  const panelName = side === "palette" ? "activities" : "inspector";

  return (
    <div className="wf-panel-title">
      <PanelTabList label={tabLabel} tabs={tabs} activeTabId={activeTabId} onSelect={onSelect} />
      <StudioSidePanelActions
        side={side}
        panelLabel={panelName}
        collapsed={collapsed}
        maximized={maximized}
        onToggleCollapsed={onToggleCollapsed}
        onToggleMaximized={onToggleMaximized}
        className="wf-panel-actions"
        buttonClassName="wf-panel-action-button"
      />
    </div>
  );
}
