import React, { useId } from "react";
import { StudioButton, StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { QueryCell, QueryResult, QueryRow, Visualization } from "./types";

function cellText(cell?: QueryCell) { return !cell ? "Unavailable" : cell.value === null ? cell.nullReason === "noEligibleDenominator" ? "No eligible denominator" : cell.nullReason === "noEligibleValues" ? "No eligible values" : cell.nullReason ?? "Missing" : cell.value; }

export function ResultView({ result, visualization, stale = false, outdated = false, onDrill }: { result: QueryResult; visualization: Visualization; stale?: boolean; outdated?: boolean; onDrill?(row: QueryRow): void }) {
  const id = useId();
  if (result.outcome !== "succeeded") return <section className="named-result-state" role="status"><h3>{result.outcome === "cancelled" ? "Query cancelled" : result.outcome === "denied" ? "Access unavailable" : result.outcome === "limitExceeded" ? "Query limit reached" : result.outcome === "incompatible" ? "Question needs repair" : "Results unavailable"}</h3><p>{result.message ?? "Review the definition, source availability and your current access before retrying."}</p></section>;
  const columns = result.columns ?? [];
  const rows = result.rows ?? [];
  const valueColumns = visualization.y.filter(value => columns.some(column => column.id === value));
  const numericColumn = valueColumns[0];
  const categories = rows.map((row, index) => visualization.x ? cellText(row.values[visualization.x]) : String(index + 1));
  const numeric = numericColumn ? rows.map(row => row.values[numericColumn]?.value === null ? null : Number(row.values[numericColumn]?.value)) : [];
  const canChart = Boolean(numericColumn && numeric.length && numeric.every(value => value === null || Number.isFinite(value)));
  const maximum = Math.max(1, ...numeric.filter((value): value is number => value !== null).map(value => Math.abs(value)));
  const canMetric = valueColumns.length === 1 && rows.length === 1;
  return <section className="named-result" aria-label={visualization.label || "Question results"}>
    <div className="named-result-provenance"><p>{result.interpretation || "Results of the validated question."}</p>
      {stale ? <p role="status"><strong>Stale result.</strong> The last refresh failed; these are the last successful values.</p> : null}
      {outdated ? <p role="status"><strong>Definition changed.</strong> Preview or refresh to answer the current definition.</p> : null}
      <p><strong>Coverage: {result.coverage?.status ?? "unknown"}.</strong> {result.coverage?.description ?? "Collection and retention completeness has not been established."}</p>
      {result.generatedAt ? <p>Last successful refresh: <time dateTime={result.generatedAt}>{new Date(result.generatedAt).toLocaleString()}</time></p> : null}
      {result.periods?.map(period => <p key={period.role}>{period.role}: <time dateTime={period.from}>{period.from}</time> to <time dateTime={period.to}>{period.to}</time> ({period.timeZone}; end exclusive)</p>)}
    </div>
    {rows.length === 0 ? <p role="status">No authorized records matched this question. This is an empty result, not an observed zero.</p> : <>
      {visualization.kind === "metric" ? canMetric ? <div className="named-metric"><span>{columns.find(column => column.id === valueColumns[0])?.label}</span><strong>{cellText(rows[0].values[valueColumns[0]])}</strong><span>{columns.find(column => column.id === valueColumns[0])?.unit}</span></div> : <p role="status">A metric needs one result row and one value column. Choose those bindings or use a table.</p> : null}
      {["bar", "line"].includes(visualization.kind) ? canChart ? <div className="named-chart" role="img" aria-labelledby={`${id}-chart-title`}>
        <p id={`${id}-chart-title`}>{visualization.label || `${columns.find(column => column.id === numericColumn)?.label ?? numericColumn} by ${visualization.x ?? "result order"}`}. Exact values follow in the table.</p>
        {visualization.kind === "bar" ? <ol className="named-bars">{rows.map((row, index) => <li key={row.key}><span>{categories[index]}</span><span className="named-bar-track"><span className="named-bar" style={{ width: `${Math.abs(numeric[index] ?? 0) / maximum * 100}%` }} /></span><span>{cellText(row.values[numericColumn!])}</span></li>)}</ol> : <svg className="named-line" viewBox="0 0 600 220" aria-hidden="true"><line x1="20" x2="580" y1="200" y2="200" /><polyline fill="none" points={numeric.map((value, index) => value === null ? "" : `${20 + index * 560 / Math.max(1, rows.length - 1)},${200 - value / maximum * 170}`).join(" ")} />{numeric.map((value, index) => value === null ? null : <circle key={rows[index].key} cx={20 + index * 560 / Math.max(1, rows.length - 1)} cy={200 - value / maximum * 170} r="4" />)}</svg>}
      </div> : <p role="status">Choose a numeric value column for this chart. Missing values remain missing in the exact-value table.</p> : null}
      {visualization.kind === "list" ? <ul className="named-result-list">{rows.map(row => <li key={row.key}>{columns.map(column => <span key={column.id}><strong>{column.label}: </strong>{cellText(row.values[column.id])}{column.unit ? ` ${column.unit}` : ""} </span>)}{row.drill && onDrill ? <StudioButton type="button" onClick={() => onDrill(row)}>View matching records</StudioButton> : null}</li>)}</ul> : null}
      <StudioTableContainer role="region" aria-label="Exact result values" tabIndex={0}><table className="studio-table"><caption>{visualization.label || "Exact result values"}</caption><thead><tr>{columns.map(column => <th key={column.id} scope="col">{column.label}{column.unit ? ` (${column.unit})` : ""}</th>)}{onDrill ? <th scope="col">Investigate</th> : null}</tr></thead><tbody>{rows.map((row, index) => <tr key={row.key || index}>{columns.map(column => <td key={column.id}>{cellText(row.values[column.id])}</td>)}{onDrill ? <td>{row.drill ? <StudioButton type="button" onClick={() => onDrill(row)} aria-label={`View matching records for result ${index + 1}`}>View matching records</StudioButton> : "Drill-down unavailable"}</td> : null}</tr>)}</tbody></table></StudioTableContainer>
      <p className="named-help">{rows.length} result {rows.length === 1 ? "row" : "rows"}{result.nextCursor ? "; more records are available through bounded source navigation." : "."}</p>
    </>}
  </section>;
}
