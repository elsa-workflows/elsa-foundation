import React from "react";

export interface StudioTabItem {
  id: string;
  label: string;
}

export function StudioTabs({
  tabs,
  activeTab,
  onSelect
}: {
  tabs: StudioTabItem[];
  activeTab: string;
  onSelect(tabId: string): void;
}) {
  return (
    <div className="studio-tabs" role="tablist">
      {tabs.map(tab => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={tab.id === activeTab}
          className={tab.id === activeTab ? "active" : ""}
          onClick={() => onSelect(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}
