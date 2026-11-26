import React from "react";

export interface StudioVersionHistoryItem {
  id: string;
  ariaLabel: string;
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  status?: React.ReactNode;
}

export function StudioVersionHistory({
  label,
  items,
  selectedId,
  onSelect,
  detail,
  emptyState = "No saved versions yet.",
  selectionHint = "Choose a version to inspect.",
  listFooter
}: {
  label: string;
  items: StudioVersionHistoryItem[];
  selectedId: string | null;
  onSelect(id: string): void;
  detail?: React.ReactNode;
  emptyState?: React.ReactNode;
  selectionHint?: React.ReactNode;
  listFooter?: React.ReactNode;
}) {
  const selected = items.some(item => item.id === selectedId);

  return (
    <section className="studio-version-history" aria-label={label}>
      <div className="studio-version-history-list-pane">
        {items.length ? (
          <div className="studio-version-history-list" role="group" aria-label={`${label} versions`}>
            {items.map(item => (
              <button
                key={item.id}
                type="button"
                className="studio-version-history-entry"
                aria-label={item.ariaLabel}
                aria-pressed={item.id === selectedId}
                onClick={() => onSelect(item.id)}
              >
                <span className="studio-version-history-entry-copy">
                  <span className="studio-version-history-entry-title">{item.title}</span>
                  {item.subtitle !== undefined ? <span className="studio-version-history-entry-subtitle">{item.subtitle}</span> : null}
                </span>
                {item.status !== undefined ? <span className="studio-version-history-entry-status">{item.status}</span> : null}
              </button>
            ))}
          </div>
        ) : <p className="studio-version-history-empty" role="status">{emptyState}</p>}
        {listFooter !== undefined ? <div className="studio-version-history-footer">{listFooter}</div> : null}
      </div>
      <div className="studio-version-history-preview" role="region" aria-label={`${label} preview`}>
        {selected ? detail ?? <p className="studio-version-history-hint" role="status">{selectionHint}</p> : <p className="studio-version-history-hint" role="status">{selectionHint}</p>}
      </div>
    </section>
  );
}