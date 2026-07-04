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
  return (
    <div className="wf-panel-tab-list" role="tablist" aria-label={label}>
      {tabs.map(tab => (
        <button
          type="button"
          role="tab"
          aria-selected={tab.id === activeTabId}
          className={tab.id === activeTabId ? "active" : ""}
          key={tab.id}
          title={tab.title}
          onClick={() => onSelect(tab.id)}
        >
          {tab.icon ? <span className="wf-panel-tab-icon" aria-hidden="true">{tab.icon}</span> : null}
          <span>{tab.title}</span>
        </button>
      ))}
    </div>
  );
}

export function compareWorkflowPanelTabs(left: WorkflowEditorPanelTab, right: WorkflowEditorPanelTab) {
  return left.order - right.order || left.title.localeCompare(right.title);
}
