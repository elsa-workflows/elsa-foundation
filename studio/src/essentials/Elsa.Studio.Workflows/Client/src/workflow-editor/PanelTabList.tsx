import { StudioTabs } from "@elsa-workflows/studio-ui";
import "../graph-authoring/workbenchPanelStyles.css";
import type { WorkflowEditorPanelTab } from "./editorTypes";

export function PanelTabList({
  label,
  tabs,
  activeTabId,
  onSelect
}: {
  label: string;
  tabs: WorkflowEditorPanelTab[];
  activeTabId: string;
  onSelect(tabId: string): void;
}) {
  return <StudioTabs compact className="wf-panel-tab-list" ariaLabel={label} activeTab={activeTabId}
    tabs={tabs.map(tab => ({ id: tab.id, label: tab.title, icon: tab.icon, panelIndex: null }))} onSelect={onSelect} />;
}

export function compareWorkflowPanelTabs(left: WorkflowEditorPanelTab, right: WorkflowEditorPanelTab) {
  return left.order - right.order || left.title.localeCompare(right.title);
}
