import React from "react";

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
 * itself: it is focusable via roving `tabIndex` (only the selected row, or the first row when nothing
 * is selected, is in the tab order), exposes `aria-selected`, and activates on Enter/Space as well as
 * click. This keeps valid table semantics while remaining fully keyboard-operable. When no rows are
 * present, the optional `emptyState` slot renders in place of the body.
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
  // Roving tabindex anchor: the selected row, or the first row if the selection isn't in view.
  const rovingKey = items.some(item => getKey(item) === selectedKey) ? selectedKey : items.length > 0 ? getKey(items[0]) : undefined;

  return (
    <div
      className="studio-data-grid"
      role="table"
      style={gridColumns ? { "--studio-grid-columns": gridColumns } as React.CSSProperties : undefined}
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
        const activate = selectable ? () => onSelect?.(item) : undefined;
        return (
          <div
            key={key}
            className={selected ? "studio-data-grid-row selected" : "studio-data-grid-row"}
            role="row"
            aria-selected={selectable ? selected : undefined}
            tabIndex={selectable ? (key === rovingKey ? 0 : -1) : undefined}
            onClick={activate}
            onKeyDown={selectable ? event => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                activate?.();
              }
            } : undefined}
          >
            {columns.map(column => <span key={column.id} role="cell">{column.render(item)}</span>)}
          </div>
        );
      })}
    </div>
  );
}
