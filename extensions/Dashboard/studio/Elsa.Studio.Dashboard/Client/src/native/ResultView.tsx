import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import { ArrowDown, ArrowUp, ArrowUpDown, Check, Copy, Download } from "lucide-react";
import {
  qualifyStudioBackendPath,
  type StudioDashboardRecordLinkContribution
} from "@elsa-workflows/studio-sdk";
import {
  AdvancedSection,
  OverflowMenu,
  StudioButton,
  StudioSearchInput,
  StudioTableContainer,
  StudioTimestamp
} from "@elsa-workflows/studio-ui";
import {
  buildExportFilename,
  cellText,
  copyTextToClipboard,
  downloadCsvFile,
  filterQueryRows,
  formatColumnHeader,
  formatDateValue,
  isIdentifierColumn,
  isNumericColumn,
  resolveDashboardRecordLink,
  serializeRowsAsCsv,
  serializeRowsAsJson,
  serializeRowsAsMarkdown,
  serializeRowsAsTsv,
  sortQueryRows,
  type TableSortState
} from "./resultExport";
import type { QueryColumn, QueryResult, QueryRow, Visualization } from "./types";

function NumericSeries({ rows, column, categories, kind }: { rows: QueryRow[]; column: QueryColumn; categories: string[]; kind: "bar" | "line" }) {
  const titleId = useId();
  const values = rows.map(row => {
    const cell = row.values[column.id];
    return !cell || cell.value === null ? null : cell.value.trim() === "" ? Number.NaN : Number(cell.value);
  });
  if (values.some(value => value !== null && !Number.isFinite(value)))
    return <p role="status">{column.label} contains values that cannot be plotted. Exact values remain in the table.</p>;
  const populated = values.filter((value): value is number => value !== null);
  if (!populated.length) return <p role="status">{column.label}: no eligible numeric values to plot. Missing values have not been replaced with zero.</p>;
  const minimum = Math.min(0, ...populated);
  const maximum = Math.max(0, ...populated);
  const span = maximum - minimum || 1;
  const zero = -minimum / span * 100;
  const x = (index: number) => 32 + index * 536 / Math.max(1, rows.length - 1);
  const y = (value: number) => 180 - (value - minimum) / span * 155;
  // A missing observation ends its segment; never connect a line across an unknown value.
  const segments: { index: number; value: number }[][] = [];
  let segment: { index: number; value: number }[] = [];
  values.forEach((value, index) => {
    if (value === null) { if (segment.length) segments.push(segment); segment = []; }
    else segment.push({ index, value });
  });
  if (segment.length) segments.push(segment);
  return <figure className="named-chart-series" aria-labelledby={titleId} data-series={column.id}>
    <figcaption id={titleId}>{column.label}{column.unit ? ` (${column.unit})` : ""}</figcaption>
    {kind === "bar" ? <ol className="named-bars">{rows.map((row, index) => {
      const value = values[index];
      const endpoint = value === null ? zero : (value - minimum) / span * 100;
      return <li key={row.key || index}><span>{categories[index]}</span><span className="named-bar-track" aria-hidden="true">
        <span className="named-bar-zero" style={{ insetInlineStart: `${zero}%` }} />
        {value !== null ? <span className="named-bar" data-negative={value < 0} style={{ insetInlineStart: `${Math.min(zero, endpoint)}%`, width: `${Math.abs(endpoint - zero)}%` }} /> : null}
      </span><span>{cellText(row.values[column.id])}</span></li>;
    })}</ol> : <>
      <svg className="named-line" viewBox="0 0 600 220" role="img" aria-labelledby={titleId}>
        <line x1="32" x2="568" y1={y(0)} y2={y(0)} />
        <text x="2" y={y(maximum) + 4}>{maximum}</text><text x="2" y={y(minimum) + 4}>{minimum}</text>
        {segments.map((points, index) => <polyline key={index} fill="none" points={points.map(point => `${x(point.index)},${y(point.value)}`).join(" ")} />)}
        {values.map((value, index) => value === null ? null : <circle key={rows[index].key || index} cx={x(index)} cy={y(value)} r="4"><title>{categories[index]}: {cellText(rows[index].values[column.id])}</title></circle>)}
        <text x="32" y="210">{categories[0]}</text><text x="568" y="210" textAnchor="end">{categories[categories.length - 1]}</text>
      </svg>
      {values.some(value => value === null) ? <p className="named-help">Gaps represent missing values. No line connects across a gap.</p> : null}
    </>}
  </figure>;
}

export interface ResultViewProps {
  result: QueryResult;
  visualization: Visualization;
  stale?: boolean;
  outdated?: boolean;
  compactProvenance?: boolean;
  onDrill?(row: QueryRow): void;
  datasetId?: string;
  recordLinks?: readonly StudioDashboardRecordLinkContribution[];
  navigate?(path: string): void;
  onNavigateRecord?(): void;
  enableFilter?: boolean;
  enableSort?: boolean;
  enableRowCopy?: boolean;
  stickyHeader?: boolean;
  exportFilename?: string;
}

export function ResultView({
  result,
  visualization,
  stale = false,
  outdated = false,
  compactProvenance = true,
  onDrill,
  datasetId,
  recordLinks,
  navigate,
  onNavigateRecord,
  enableFilter = false,
  enableSort = false,
  enableRowCopy = false,
  stickyHeader = false,
  exportFilename
}: ResultViewProps) {
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState<TableSortState | null>(null);
  const [exportStatus, setExportStatus] = useState<string>("");
  const [copiedTable, setCopiedTable] = useState(false);
  const [copiedRowKey, setCopiedRowKey] = useState<string | null>(null);
  const statusTimer = useRef<number | undefined>(undefined);
  const rowCopyTimer = useRef<number | undefined>(undefined);

  useEffect(() => () => {
    window.clearTimeout(statusTimer.current);
    window.clearTimeout(rowCopyTimer.current);
  }, []);

  const columns = useMemo(() => result.columns ?? [], [result.columns]);
  const rows = useMemo(() => result.rows ?? [], [result.rows]);
  const activeSort = sort && columns.some(column => column.id === sort.columnId) ? sort : null;

  const visibleRows = useMemo(() => {
    const filtered = enableFilter && filter.trim() ? filterQueryRows(rows, columns, filter) : rows;
    return enableSort && activeSort ? sortQueryRows(filtered, columns, activeSort) : filtered;
  }, [rows, columns, enableFilter, filter, enableSort, activeSort]);

  if (result.outcome !== "succeeded") return <section className="named-result-state" role="status"><h3>{result.outcome === "cancelled" ? "Query cancelled" : result.outcome === "denied" ? "Access unavailable" : result.outcome === "limitExceeded" ? "Query limit reached" : result.outcome === "incompatible" ? "Question needs repair" : "Results unavailable"}</h3><p>{result.message ?? "Review the definition, source availability and your current access before retrying."}</p></section>;

  const selected = visualization.y.map(id => columns.find(column => column.id === id));
  const valueColumns = selected.filter((column): column is QueryColumn => Boolean(column));
  const bindingsValid = selected.every(Boolean);
  const categories = rows.map((row, index) => visualization.x ? cellText(row.values[visualization.x]) : String(index + 1));
  const canMetric = bindingsValid && valueColumns.length === 1 && rows.length === 1;
  const interpretation = result.interpretation || "Results of the validated question.";
  const coverageStatus = result.coverage?.status ?? "unknown";
  const coverageDescription = result.coverage?.description ?? "Collection and retention completeness has not been established.";
  const freshnessWarnings = <>
    {stale ? <p role="status"><strong>Stale result.</strong> The last refresh failed; these are the last successful values.</p> : null}
    {outdated ? <p role="status"><strong>Stale result. Definition changed.</strong> These values answer the previous definition. Preview or refresh to answer the current definition.</p> : null}
  </>;
  const refreshDetails = <>
    {result.generatedAt ? <p className="named-detail-row"><span className="named-detail-label">Last successful refresh:</span> <StudioTimestamp value={result.generatedAt} format="date-time" /></p> : null}
    {result.periods?.map(period => <p key={period.role} className="named-detail-row"><span className="named-detail-label">{period.role}:</span> <StudioTimestamp value={period.from} format="date-time" /> to <StudioTimestamp value={period.to} format="date-time" /> <span className="named-detail-meta">({period.timeZone}; end exclusive)</span></p>)}
  </>;

  const isFiltered = enableFilter && filter.trim().length > 0;

  const announceExport = (message: string, markCopiedTable = false) => {
    window.clearTimeout(statusTimer.current);
    setExportStatus(message);
    setCopiedTable(markCopiedTable);
    statusTimer.current = window.setTimeout(() => {
      setExportStatus("");
      setCopiedTable(false);
    }, 2600);
  };

  const describeScope = (count: number) => {
    const noun = count === 1 ? "row" : "rows";
    return isFiltered ? `${count} filtered ${noun} (of ${rows.length} loaded)` : `${count} loaded ${noun}`;
  };

  const formatScopeSuffix = () => (result.nextCursor ? " (more rows available)" : "");

  const copyRows = async (format: "tsv" | "markdown" | "csv" | "json") => {
    if (!visibleRows.length) return;
    const label = format === "tsv" ? "TSV" : format === "markdown" ? "Markdown" : format === "csv" ? "CSV" : "JSON";
    const text = format === "tsv"
      ? serializeRowsAsTsv(columns, visibleRows)
      : format === "markdown"
        ? serializeRowsAsMarkdown(columns, visibleRows)
        : format === "csv"
          ? serializeRowsAsCsv(columns, visibleRows)
          : serializeRowsAsJson(columns, visibleRows);
    try {
      await copyTextToClipboard(text);
      announceExport(`Copied ${describeScope(visibleRows.length)} as ${label}${formatScopeSuffix()}.`, format === "tsv");
    } catch {
      announceExport(`Could not copy rows as ${label}.`);
    }
  };

  const downloadCsv = () => {
    if (!visibleRows.length) return;
    try {
      const filename = buildExportFilename(exportFilename || visualization.label || "query-results");
      downloadCsvFile(filename, serializeRowsAsCsv(columns, visibleRows));
      announceExport(`Downloaded ${describeScope(visibleRows.length)} as CSV${formatScopeSuffix()}.`);
    } catch {
      announceExport("Could not download CSV.");
    }
  };

  const copySingleRow = async (row: QueryRow, rowKey: string, index: number) => {
    try {
      await copyTextToClipboard(serializeRowsAsTsv(columns, [row]));
      window.clearTimeout(rowCopyTimer.current);
      setCopiedRowKey(rowKey);
      rowCopyTimer.current = window.setTimeout(() => setCopiedRowKey(null), 1600);
      announceExport(`Copied row ${index + 1} as TSV.`);
    } catch {
      announceExport(`Could not copy row ${index + 1}.`);
    }
  };

  const toggleSort = (columnId: string) => {
    setSort(current => {
      if (!current || current.columnId !== columnId) return { columnId, direction: "ascending" };
      if (current.direction === "ascending") return { columnId, direction: "descending" };
      return null;
    });
  };

  const renderCell = (column: QueryColumn, row: QueryRow) => {
    const cell = row.values[column.id];
    const text = cellText(cell);
    if (!cell || cell.value === null) {
      return <td key={column.id} className="named-cell-null" data-align={isNumericColumn(column) ? "end" : undefined}>{text}</td>;
    }
    if (column.type === "instant") {
      return <td key={column.id} className="named-cell-timestamp"><StudioTimestamp value={cell.value} format="date-time" /></td>;
    }
    if (column.type === "date") {
      const formatted = formatDateValue(cell.value);
      return <td key={column.id} className="named-cell-timestamp"><time dateTime={cell.value} title={cell.value}>{formatted ?? cell.value}</time></td>;
    }
    if (isNumericColumn(column)) {
      return <td key={column.id} className="named-cell-numeric" data-align="end">{cell.value}</td>;
    }
    const link = resolveDashboardRecordLink(recordLinks, { datasetId, column, cell, row: row.values });
    const identifier = isIdentifierColumn(column);
    if (link) {
      const href = qualifyStudioBackendPath(link.path);
      return <td key={column.id} className={identifier ? "named-cell-id" : "named-cell-text"} title={link.title ?? cell.value}>
        <a
          className="named-record-link"
          href={href}
          title={link.title ?? link.label ?? cell.value}
          onClick={event => {
            if (navigate && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey && event.button === 0) {
              event.preventDefault();
              onNavigateRecord?.();
              navigate(link.path);
            }
          }}
        >
          {cell.value}
        </a>
      </td>;
    }
    if (identifier) {
      return <td key={column.id} className="named-cell-id" title={cell.value}>{cell.value}</td>;
    }
    return <td key={column.id} className="named-cell-text" data-long={cell.value.length > 60 ? "true" : undefined} title={cell.value.length > 60 ? cell.value : undefined}>{cell.value}</td>;
  };

  const totalColumns = columns.length + (onDrill ? 1 : 0) + (enableRowCopy ? 1 : 0);

  return <section className="named-result" aria-label={visualization.label || "Question results"}>
    <div className="named-result-provenance" data-compact={compactProvenance ? "true" : undefined}>
      {compactProvenance ? <>
        <p><strong>Coverage: {coverageStatus}.</strong></p>
        {freshnessWarnings}
        <div className="named-result-details"><AdvancedSection label="Result details"><div className="named-result-details-content">
          <p>{interpretation}</p><p>{coverageDescription}</p>{refreshDetails}
        </div></AdvancedSection></div>
      </> : <>
        <p>{interpretation}</p>
        {freshnessWarnings}
        <p><strong>Coverage: {coverageStatus}.</strong> {coverageDescription}</p>
        {refreshDetails}
      </>}
    </div>
    {rows.length === 0 ? <p role="status">No authorized records matched this question. This is an empty result, not an observed zero.</p> : <>
      {visualization.kind === "metric" ? canMetric ? <div className="named-metric"><span>{valueColumns[0].label}</span><strong>{cellText(rows[0].values[valueColumns[0].id])}</strong><span>{valueColumns[0].unit}</span></div> : <p role="status">A metric needs one result row and one value column. Choose those bindings or use a table.</p> : null}
      {visualization.kind === "bar" || visualization.kind === "line" ? bindingsValid && valueColumns.length ? <div className="named-chart">
        <p>{visualization.label || "Question chart"}. Each named series has its own labeled scale; exact values follow in the table.</p>
        {valueColumns.map(column => <NumericSeries key={column.id} rows={rows} column={column} categories={categories} kind={visualization.kind as "bar" | "line"} />)}
      </div> : <p role="status">Choose available numeric value columns for this chart. Missing bindings have not been silently substituted.</p> : null}
      {visualization.kind === "list" ? <ul className="named-result-list">{rows.map(row => <li key={row.key}>{columns.map(column => <span key={column.id}><strong>{column.label}: </strong>{cellText(row.values[column.id])}{column.unit ? ` ${column.unit}` : ""} </span>)}</li>)}</ul> : null}
      <div className="named-result-table-toolbar">
        {enableFilter ? <StudioSearchInput
          className="named-result-filter"
          aria-label="Filter loaded rows"
          placeholder="Filter loaded rows…"
          value={filter}
          onChange={event => setFilter(event.target.value)}
        /> : null}
        <div className="named-result-table-actions">
          <StudioButton
            size="sm"
            type="button"
            disabled={visibleRows.length === 0}
            title="Copy visible rows as tab-separated values"
            onClick={() => void copyRows("tsv")}
          >
            {copiedTable ? <Check size={14} aria-hidden="true" /> : <Copy size={14} aria-hidden="true" />}
            {copiedTable ? "Copied" : "Copy table"}
          </StudioButton>
          <OverflowMenu
            ariaLabel="Export table"
            triggerText="Export"
            triggerIcon={<Download size={14} aria-hidden="true" />}
            items={[
              { id: "copy-markdown", label: "Copy as Markdown", disabled: visibleRows.length === 0, onSelect: () => void copyRows("markdown") },
              { id: "copy-csv", label: "Copy as CSV", disabled: visibleRows.length === 0, onSelect: () => void copyRows("csv") },
              { id: "download-csv", label: "Download CSV", disabled: visibleRows.length === 0, onSelect: downloadCsv },
              { id: "copy-json", label: "Copy as JSON", disabled: visibleRows.length === 0, onSelect: () => void copyRows("json") }
            ]}
          />
        </div>
      </div>
      {exportStatus ? <p className="named-result-export-status" role="status" aria-live="polite">{exportStatus}</p> : null}
      <StudioTableContainer
        className={stickyHeader ? "named-result-table-scroll named-result-table-scroll--sticky" : "named-result-table-scroll"}
        role="region"
        aria-label="Exact result values"
        tabIndex={0}
      >
        <table className="studio-table named-result-table">
          <caption className={visualization.label ? "named-table-caption" : "named-sr-only"}>{visualization.label || "Exact result values"}</caption>
          <thead>
            <tr>
              {columns.map(column => {
                const numeric = isNumericColumn(column);
                const columnSort = activeSort?.columnId === column.id ? activeSort.direction : undefined;
                const headerLabel = formatColumnHeader(column);
                return <th
                  key={column.id}
                  scope="col"
                  data-align={numeric ? "end" : undefined}
                  aria-sort={enableSort ? (columnSort ?? "none") : undefined}
                >
                  {enableSort ? <button
                    type="button"
                    className="named-sort-header"
                    data-align={numeric ? "end" : undefined}
                    onClick={() => toggleSort(column.id)}
                  >
                    <span>{headerLabel}</span>
                    {columnSort === "ascending"
                      ? <ArrowUp size={13} aria-hidden="true" />
                      : columnSort === "descending"
                        ? <ArrowDown size={13} aria-hidden="true" />
                        : <ArrowUpDown size={13} className="named-sort-idle" aria-hidden="true" />}
                  </button> : headerLabel}
                </th>;
              })}
              {onDrill ? <th scope="col">Investigate</th> : null}
              {enableRowCopy ? <th scope="col" className="named-col-row-action"><span className="named-sr-only">Copy row</span></th> : null}
            </tr>
          </thead>
          <tbody>
            {visibleRows.length === 0 ? <tr>
              <td colSpan={Math.max(1, totalColumns)} className="named-cell-empty-filter">
                <span>No loaded rows match &ldquo;{filter.trim()}&rdquo;.</span>{" "}
                <StudioButton size="sm" type="button" onClick={() => setFilter("")}>Clear filter</StudioButton>
              </td>
            </tr> : visibleRows.map((row, index) => {
              const rowKey = row.key || String(index);
              return <tr key={rowKey}>
                {columns.map(column => renderCell(column, row))}
                {onDrill ? <td>{row.drill ? <StudioButton type="button" disabled={outdated || stale} onClick={() => onDrill(row)} aria-label={`View matching records for result ${index + 1}`}>View matching records</StudioButton> : "Drill-down unavailable"}</td> : null}
                {enableRowCopy ? <td className="named-cell-row-action">
                  <StudioButton
                    size="sm"
                    variant="ghost"
                    type="button"
                    aria-label={`Copy row ${index + 1}`}
                    title="Copy row as TSV"
                    onClick={() => void copySingleRow(row, rowKey, index)}
                  >
                    {copiedRowKey === rowKey ? <Check size={14} aria-hidden="true" /> : <Copy size={14} aria-hidden="true" />}
                  </StudioButton>
                </td> : null}
              </tr>;
            })}
          </tbody>
        </table>
      </StudioTableContainer>
      <p className="named-help">
        {isFiltered
          ? `Showing ${visibleRows.length} of ${rows.length} loaded ${rows.length === 1 ? "row" : "rows"}`
          : `${rows.length} result ${rows.length === 1 ? "row" : "rows"}`}
        {result.nextCursor ? "; more records are available through bounded source navigation." : "."}
      </p>
    </>}
  </section>;
}
