import React from "react";

export interface StudioDataGridColumn<T> {
  id: string;
  header: React.ReactNode;
  render(item: T): React.ReactNode;
}

export function StudioDataGrid<T>({
  columns,
  items,
  getKey,
  selectedKey,
  onSelect,
  gridColumns
}: {
  columns: StudioDataGridColumn<T>[];
  items: T[];
  getKey(item: T): string;
  selectedKey?: string;
  onSelect?(item: T): void;
  gridColumns?: string;
}) {
  return (
    <div className="studio-data-grid" role="table" style={gridColumns ? { "--studio-grid-columns": gridColumns } as React.CSSProperties : undefined}>
      <div className="studio-data-grid-header" role="row">
        {columns.map(column => <span key={column.id} role="columnheader">{column.header}</span>)}
      </div>
      {items.map(item => {
        const key = getKey(item);
        return (
          <button
            key={key}
            type="button"
            className={key === selectedKey ? "studio-data-grid-row selected" : "studio-data-grid-row"}
            role="row"
            onClick={() => onSelect?.(item)}
          >
            {columns.map(column => <span key={column.id} role="cell">{column.render(item)}</span>)}
          </button>
        );
      })}
    </div>
  );
}
