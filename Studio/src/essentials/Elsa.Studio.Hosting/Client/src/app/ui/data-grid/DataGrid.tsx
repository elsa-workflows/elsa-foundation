import React from "react";
import { studioRowInteraction } from "./rowInteraction";

export interface StudioDataGridColumn<T> {
  id: string;
  header: React.ReactNode;
  render(item: T): React.ReactNode;
}

/**
 * Accessible semantics
 * --------------------
 * This grid uses the ARIA **table** pattern (`role="table"` > `role="row"` > `role="cell"` /
 * `role="columnheader"`) rather than the interactive `grid` pattern. The previous markup put
 * `role="row"` on a `<button>` — invalid, because a row is a structural role and cannot be a button —
 * and paired `role="cell"` with a non-`grid` container.
 *
 * Rows are selectable, so each row is a `<div role="row">` that carries the activation affordance
 * itself. Tab enters on the selected row or the first row. Up and Down move between rows, and Enter
 * or Space selects the focused row. When no rows are present, the optional `emptyState` slot renders
 * in place of the body.
 */
export function StudioDataGrid<T>({
  columns,
  items,
  getKey,
  selectedKey,
  onSelect,
  gridColumns,
  emptyState
}: {
  columns: StudioDataGridColumn<T>[];
  items: T[];
  getKey(item: T): string;
  selectedKey?: string;
  onSelect?(item: T): void;
  gridColumns?: string;
  emptyState?: React.ReactNode;
}) {
  const selectable = Boolean(onSelect);
  const [focusedKey, setFocusedKey] = React.useState<string>();
  const keys = items.map(getKey);
  const tabKey = [focusedKey, selectedKey].find(key => key !== undefined && keys.includes(key)) ?? keys[0];

  return (
    <div
      className="studio-data-grid"
      role="table"
      style={gridColumns ? { "--studio-grid-columns": gridColumns } as React.CSSProperties : undefined}
      onBlur={event => {
        if (!event.currentTarget.contains(event.relatedTarget)) setFocusedKey(undefined);
      }}
    >
      <div className="studio-data-grid-header" role="row">
        {columns.map(column => <span key={column.id} role="columnheader">{column.header}</span>)}
      </div>
      {items.length === 0 && emptyState !== undefined ? (
        <div className="studio-data-grid-empty" role="row">
          <span role="cell" className="studio-data-grid-empty-cell">{emptyState}</span>
        </div>
      ) : null}
      {items.map(item => {
        const key = getKey(item);
        const selected = key === selectedKey;
        const interaction = studioRowInteraction<HTMLDivElement>(onSelect ? () => onSelect(item) : undefined);
        return (
          <div
            key={key}
            className={selected ? "studio-data-grid-row selected" : "studio-data-grid-row"}
            role="row"
            aria-selected={selectable ? selected : undefined}
            {...interaction}
            tabIndex={selectable ? (key === tabKey ? 0 : -1) : undefined}
            onFocus={selectable ? event => {
              if (event.target === event.currentTarget) setFocusedKey(key);
            } : undefined}
            onKeyDown={selectable ? event => {
              if (event.defaultPrevented || event.target !== event.currentTarget) return;
              const row = event.currentTarget;
              const next = event.key === "ArrowDown" ? row.nextElementSibling
                : event.key === "ArrowUp" ? row.previousElementSibling : null;
              if (next instanceof HTMLElement && next.hasAttribute("data-studio-row-interactive")) {
                event.preventDefault();
                next.focus();
                return;
              }
              interaction.onKeyDown?.(event);
            } : undefined}
          >
            {columns.map(column => <span key={column.id} role="cell">{column.render(item)}</span>)}
          </div>
        );
      })}
    </div>
  );
}
