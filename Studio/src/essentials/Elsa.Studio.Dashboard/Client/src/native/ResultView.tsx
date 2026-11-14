import React, { useId } from "react";
import { StudioButton, StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { QueryCell, QueryColumn, QueryResult, QueryRow, Visualization } from "./types";

function cellText(cell?: QueryCell) {
  if (!cell) return "Unavailable";
  if (cell.value !== null) return cell.value;
  if (cell.nullReason === "noEligibleDenominator") return "No eligible denominator";
  if (cell.nullReason === "noEligibleValues") return "No eligible values";
  return cell.nullReason ?? "Missing";
}

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

export function ResultView({ result, visualization, stale = false, outdated = false, onDrill }: { result: QueryResult; visualization: Visualization; stale?: boolean; outdated?: boolean; onDrill?(row: QueryRow): void }) {
  if (result.outcome !== "succeeded") return <section className="named-result-state" role="status"><h3>{result.outcome === "cancelled" ? "Query cancelled" : result.outcome === "denied" ? "Access unavailable" : result.outcome === "limitExceeded" ? "Query limit reached" : result.outcome === "incompatible" ? "Question needs repair" : "Results unavailable"}</h3><p>{result.message ?? "Review the definition, source availability and your current access before retrying."}</p></section>;
  const columns = result.columns ?? [];
  const rows = result.rows ?? [];
  const selected = visualization.y.map(id => columns.find(column => column.id === id));
  const valueColumns = selected.filter((column): column is QueryColumn => Boolean(column));
  const bindingsValid = selected.every(Boolean);
  const categories = rows.map((row, index) => visualization.x ? cellText(row.values[visualization.x]) : String(index + 1));
  const canMetric = bindingsValid && valueColumns.length === 1 && rows.length === 1;
  return <section className="named-result" aria-label={visualization.label || "Question results"}>
    <div className="named-result-provenance"><p>{result.interpretation || "Results of the validated question."}</p>
      {stale ? <p role="status"><strong>Stale result.</strong> The last refresh failed; these are the last successful values.</p> : null}
      {outdated ? <p role="status"><strong>Stale result. Definition changed.</strong> These values answer the previous definition. Preview or refresh to answer the current definition.</p> : null}
      <p><strong>Coverage: {result.coverage?.status ?? "unknown"}.</strong> {result.coverage?.description ?? "Collection and retention completeness has not been established."}</p>
      {result.generatedAt ? <p>Last successful refresh: <time dateTime={result.generatedAt}>{new Date(result.generatedAt).toLocaleString()}</time></p> : null}
      {result.periods?.map(period => <p key={period.role}>{period.role}: <time dateTime={period.from}>{period.from}</time> to <time dateTime={period.to}>{period.to}</time> ({period.timeZone}; end exclusive)</p>)}
    </div>
    {rows.length === 0 ? <p role="status">No authorized records matched this question. This is an empty result, not an observed zero.</p> : <>
      {visualization.kind === "metric" ? canMetric ? <div className="named-metric"><span>{valueColumns[0].label}</span><strong>{cellText(rows[0].values[valueColumns[0].id])}</strong><span>{valueColumns[0].unit}</span></div> : <p role="status">A metric needs one result row and one value column. Choose those bindings or use a table.</p> : null}
      {visualization.kind === "bar" || visualization.kind === "line" ? bindingsValid && valueColumns.length ? <div className="named-chart">
        <p>{visualization.label || "Question chart"}. Each named series has its own labeled scale; exact values follow in the table.</p>
        {valueColumns.map(column => <NumericSeries key={column.id} rows={rows} column={column} categories={categories} kind={visualization.kind as "bar" | "line"} />)}
      </div> : <p role="status">Choose available numeric value columns for this chart. Missing bindings have not been silently substituted.</p> : null}
      {visualization.kind === "list" ? <ul className="named-result-list">{rows.map(row => <li key={row.key}>{columns.map(column => <span key={column.id}><strong>{column.label}: </strong>{cellText(row.values[column.id])}{column.unit ? ` ${column.unit}` : ""} </span>)}</li>)}</ul> : null}
      <StudioTableContainer role="region" aria-label="Exact result values" tabIndex={0}><table className="studio-table"><caption>{visualization.label || "Exact result values"}</caption><thead><tr>{columns.map(column => <th key={column.id} scope="col">{column.label}{column.unit ? ` (${column.unit})` : ""}</th>)}{onDrill ? <th scope="col">Investigate</th> : null}</tr></thead><tbody>{rows.map((row, index) => <tr key={row.key || index}>{columns.map(column => <td key={column.id}>{cellText(row.values[column.id])}</td>)}{onDrill ? <td>{row.drill ? <StudioButton type="button" disabled={outdated || stale} onClick={() => onDrill(row)} aria-label={`View matching records for result ${index + 1}`}>View matching records</StudioButton> : "Drill-down unavailable"}</td> : null}</tr>)}</tbody></table></StudioTableContainer>
      <p className="named-help">{rows.length} result {rows.length === 1 ? "row" : "rows"}{result.nextCursor ? "; more records are available through bounded source navigation." : "."}</p>
    </>}
  </section>;
}
