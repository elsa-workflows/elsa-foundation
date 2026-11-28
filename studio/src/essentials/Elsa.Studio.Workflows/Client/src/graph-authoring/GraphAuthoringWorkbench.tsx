import { ArrowLeft } from "lucide-react";
import { useEffect, useRef } from "react";
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
  const inspectorRef = useRef<HTMLElement>(null);
  const sheetWasOpen = useRef(layout.inspectorSheet);

  // Opening the sheet from the canvas (a node, its toolbar, a run selection) hides the control that had
  // focus. Keyboard users continue in the sheet, at its way back, rather than at the top of the page.
  useEffect(() => {
    const opened = layout.inspectorSheet && !sheetWasOpen.current;
    sheetWasOpen.current = layout.inspectorSheet;
    const sheet = inspectorRef.current;
    if (!opened || !sheet) return;
    const active = document.activeElement;
    const workbench = sheet.closest(".wf-editor-body");
    if (active && active !== document.body && !(workbench?.contains(active) && !sheet.contains(active))) return;
    sheet.querySelector<HTMLElement>(".wf-panel-back-button")?.focus();
  }, [layout.inspectorSheet]);

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
          visible={layout.paletteExpanded && !layout.maximizedSidePanel && !layout.inspectorSheet}
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
          visible={layout.inspectorExpanded && !layout.maximizedSidePanel && !layout.inspectorSheet}
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
        <aside ref={inspectorRef} className={["wf-inspector", inspector.className].filter(Boolean).join(" ")} aria-label={inspector.ariaLabel}>
          <WorkbenchPanelHeader
            side="inspector"
            tabLabel={inspector.tabLabel}
            tabs={inspector.tabs}
            activeTabId={activeInspector.id}
            collapsed={layout.inspectorCollapsed}
            maximized={layout.maximizedSidePanel === "inspector"}
            onBackToCanvas={layout.phoneWidth && layout.inspectorExpanded ? workbench => {
              layout.showCanvas();
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
  const panelName = side === "palette" ? "activities" : "inspector";

  return (
    <div className="wf-panel-title">
      {onBackToCanvas ? (
        <button type="button" className="wf-panel-back-button" aria-label="Back to canvas"
          onClick={event => onBackToCanvas(event.currentTarget.closest<HTMLElement>(".wf-editor-body"))}>
          <ArrowLeft size={14} aria-hidden="true" />
          <span>Canvas</span>
        </button>
      ) : null}
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
