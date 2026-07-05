import { useTablistKeyboard } from "@elsa-workflows/studio-ui";
import type { WorkflowEditorPanelTab } from "./editorTypes";

// Renders the editor's icon + label panel tabs, delegating keyboard behaviour to the shared tablist
// primitive (studio-ui, built in #191): arrow/Home/End roving-focus navigation via `useTablistKeyboard`,
// found by its `data-tab-id`. The bespoke `wf-panel-tab-*` markup (icons, tooltips, ellipsis) is kept so
// the workflow editor's tab styling is unchanged. `aria-controls`/`id` linkage is intentionally omitted:
// these tabs' panel bodies are rendered by the parent without a `role="tabpanel"` element to point at, so
// emitting `aria-controls` here would be a dangling reference. Wiring the panel side is a separate change.
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
  const onKeyDown = useTablistKeyboard(tabs.map(tab => tab.id), activeTabId, onSelect);

  return (
    <div className="wf-panel-tab-list" role="tablist" aria-label={label} onKeyDown={onKeyDown}>
      {tabs.map(tab => {
        const isActive = tab.id === activeTabId;
        return (
          <button
            key={tab.id}
            data-tab-id={tab.id}
            type="button"
            role="tab"
            aria-selected={isActive}
            // Roving tabindex: only the active tab is in the sequential tab order; arrows reach the rest.
            tabIndex={isActive ? 0 : -1}
            className={isActive ? "active" : ""}
            title={tab.title}
            onClick={() => onSelect(tab.id)}
          >
            {tab.icon ? <span className="wf-panel-tab-icon" aria-hidden="true">{tab.icon}</span> : null}
            <span>{tab.title}</span>
          </button>
        );
      })}
    </div>
  );
}

export function compareWorkflowPanelTabs(left: WorkflowEditorPanelTab, right: WorkflowEditorPanelTab) {
  return left.order - right.order || left.title.localeCompare(right.title);
}
