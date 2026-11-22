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
type DataGridProps<T> = {
  columns: StudioDataGridColumn<T>[];
  items: T[];
  getKey(item: T): string;
  selectedKey?: string;
  onSelect?(item: T): void;
  gridColumns?: string;
  emptyState?: React.ReactNode;
};

type ComposedGridProps = React.HTMLAttributes<HTMLDivElement> & { children: React.ReactNode };

/** A composed grid keeps responsive cell markup and column-specific controls in the caller. */
export function StudioDataGrid<T>(props: DataGridProps<T>): React.JSX.Element;
export function StudioDataGrid(props: ComposedGridProps): React.JSX.Element;
export function StudioDataGrid<T>(props: DataGridProps<T> | ComposedGridProps) {
  if ("items" in props) return <ColumnDataGrid {...props} />;
  return <div {...props} role="table" />;
}

export function StudioDataGridRow({ activate, ...props }: React.HTMLAttributes<HTMLDivElement> & { activate?(): void }) {
  const interaction = studioRowInteraction<HTMLDivElement>(activate);
  return <div {...interaction} {...props} role="row" onKeyDown={event => {
    props.onKeyDown?.(event);
    if (event.defaultPrevented || event.target !== event.currentTarget) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      const row = event.currentTarget;
      const next = event.key === "ArrowDown" ? row.nextElementSibling : row.previousElementSibling;
      if (next instanceof HTMLElement && next.hasAttribute("data-studio-row-interactive")) {
        event.preventDefault();
        next.focus();
      }
    } else {
      interaction.onKeyDown?.(event);
    }
  }} />;
}

function ColumnDataGrid<T>({ columns, items, getKey, selectedKey, onSelect, gridColumns, emptyState }: DataGridProps<T>) {
  const selectable = Boolean(onSelect);
  const [focusedKey, setFocusedKey] = React.useState<string>();
  const keys = items.map(getKey);
  const tabKey = [focusedKey, selectedKey].find(key => key !== undefined && keys.includes(key)) ?? keys[0];

  return (
    <StudioDataGrid
      className="studio-data-grid"
      style={gridColumns ? { "--studio-grid-columns": gridColumns } as React.CSSProperties : undefined}
      onBlur={event => {
        if (!event.currentTarget.contains(event.relatedTarget)) setFocusedKey(undefined);
      }}
    >
      <StudioDataGridRow className="studio-data-grid-header">
        {columns.map(column => <span key={column.id} role="columnheader">{column.header}</span>)}
      </StudioDataGridRow>
      {items.length === 0 && emptyState !== undefined ? (
        <div className="studio-data-grid-empty" role="row">
          <span role="cell" className="studio-data-grid-empty-cell">{emptyState}</span>
        </div>
      ) : null}
      {items.map(item => {
        const key = getKey(item);
        const selected = key === selectedKey;
        return (
          <StudioDataGridRow
            key={key}
            className={selected ? "studio-data-grid-row selected" : "studio-data-grid-row"}
            aria-selected={selectable ? selected : undefined}
            activate={onSelect ? () => onSelect(item) : undefined}
            tabIndex={selectable ? (key === tabKey ? 0 : -1) : undefined}
            onFocus={selectable ? event => {
              if (event.target === event.currentTarget) setFocusedKey(key);
            } : undefined}
          >
            {columns.map(column => <span key={column.id} role="cell">{column.render(item)}</span>)}
          </StudioDataGridRow>
        );
      })}
    </StudioDataGrid>
  );
}
