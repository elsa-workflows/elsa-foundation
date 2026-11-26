import type {
  StudioDashboardRecordLink,
  StudioDashboardRecordLinkContribution,
  StudioDashboardRecordLinkContext
} from "@elsa-workflows/studio-sdk";
import type { QueryCell, QueryColumn, QueryResult, QueryRow } from "./types";

const utcDateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeZone: "UTC" });
const localDateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const formulaPrefixPattern = /^[=+@\t\r]|^-(?![0-9]+(?:\.[0-9]+)?$)/;

export type TableSortDirection = "ascending" | "descending";
export interface TableSortState {
  columnId: string;
  direction: TableSortDirection;
}

export function cellText(cell?: QueryCell): string {
  if (!cell) return "Unavailable";
  if (cell.value !== null) return cell.value;
  if (cell.nullReason === "noEligibleDenominator") return "No eligible denominator";
  if (cell.nullReason === "noEligibleValues") return "No eligible values";
  return cell.nullReason ?? "Missing";
}

export function formatColumnHeader(column: QueryColumn): string {
  return column.unit ? `${column.label} (${column.unit})` : column.label;
}

export function isNumericColumn(column: QueryColumn): boolean {
  return column.type === "integer" || column.type === "decimal";
}

export function isTimestampColumn(column: QueryColumn): boolean {
  return column.type === "instant" || column.type === "date";
}

export function isIdentifierColumn(column: QueryColumn): boolean {
  return column.type === "string" && (column.id === "id" || /(?:Id|Key)$/i.test(column.id) || column.id.endsWith("_id"));
}

export function formatDateValue(value: string): string | null {
  const isoDateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value.trim());
  if (isoDateOnly) {
    const year = Number(isoDateOnly[1]);
    const month = Number(isoDateOnly[2]);
    const day = Number(isoDateOnly[3]);
    const date = new Date(Date.UTC(year, month - 1, day));
    if (Number.isFinite(date.getTime()) && date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day) {
      return utcDateFormatter.format(date);
    }
    return null;
  }
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? localDateFormatter.format(date) : null;
}

export function neutralizeSpreadsheetFormula(value: string): string {
  return formulaPrefixPattern.test(value) ? `'${value}` : value;
}

export function serializeRowsAsTsv(columns: readonly QueryColumn[], rows: readonly QueryRow[]): string {
  const header = columns.map(column => sanitizeTsvField(formatColumnHeader(column))).join("\t");
  const lines = rows.map(row => columns.map(column => sanitizeTsvField(cellText(row.values[column.id]))).join("\t"));
  return [header, ...lines].join("\n");
}

function sanitizeTsvField(value: string): string {
  const singleLine = value.replace(/[\t\r\n]+/g, " ");
  return neutralizeSpreadsheetFormula(singleLine);
}

export function serializeRowsAsCsv(columns: readonly QueryColumn[], rows: readonly QueryRow[]): string {
  const header = columns.map(column => escapeCsvField(formatColumnHeader(column))).join(",");
  const lines = rows.map(row => columns.map(column => escapeCsvField(cellText(row.values[column.id]))).join(","));
  return [header, ...lines].join("\r\n");
}

function escapeCsvField(value: string): string {
  const safe = neutralizeSpreadsheetFormula(value);
  if (/[",\r\n]|^\s|\s$/.test(safe)) {
    return `"${safe.replace(/"/g, '""')}"`;
  }
  return safe;
}

export function serializeRowsAsMarkdown(columns: readonly QueryColumn[], rows: readonly QueryRow[]): string {
  const header = `| ${columns.map(column => escapeMarkdownCell(formatColumnHeader(column))).join(" | ")} |`;
  const divider = `| ${columns.map(column => isNumericColumn(column) ? "---:" : "---").join(" | ")} |`;
  const lines = rows.map(row => `| ${columns.map(column => escapeMarkdownCell(cellText(row.values[column.id]))).join(" | ")} |`);
  return [header, divider, ...lines].join("\n");
}

function escapeMarkdownCell(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
}

export function serializeRowsAsJson(columns: readonly QueryColumn[], rows: readonly QueryRow[]): string {
  const payload = rows.map(row => Object.fromEntries(columns.map(column => {
    const cell = row.values[column.id];
    return [column.id, cell ? cell.value : null];
  })));
  return JSON.stringify(payload, null, 2);
}

export function buildExportFilename(baseName?: string, now = new Date()): string {
  const slug = (baseName ?? "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64) || "query-results";
  const datePart = Number.isFinite(now.getTime()) ? now.toISOString().slice(0, 10) : "export";
  return `${slug}-${datePart}.csv`;
}

export async function copyTextToClipboard(text: string): Promise<void> {
  if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return;
    } catch {
      // Fall back to selection copy in restricted clipboard contexts.
    }
  }
  if (typeof document === "undefined") {
    throw new Error("Clipboard is unavailable.");
  }
  const textArea = document.createElement("textarea");
  textArea.value = text;
  textArea.setAttribute("readonly", "");
  textArea.style.position = "fixed";
  textArea.style.opacity = "0";
  document.body.appendChild(textArea);
  textArea.select();
  try {
    if (!document.execCommand("copy")) throw new Error("Clipboard copy failed.");
  } finally {
    textArea.remove();
  }
}

export function downloadCsvFile(filename: string, csv: string): void {
  const blob = new Blob([`\uFEFF${csv}`], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.style.display = "none";
  document.body.appendChild(link);
  try {
    link.click();
  } finally {
    link.remove();
    URL.revokeObjectURL(url);
  }
}

export function isSafeStudioPath(path: string | null | undefined): path is string {
  return typeof path === "string" && path.startsWith("/") && !path.startsWith("//") && !/[\r\n]/.test(path);
}

export function resolveDashboardRecordLink(
  contributors: readonly StudioDashboardRecordLinkContribution[] | undefined,
  context: StudioDashboardRecordLinkContext
): StudioDashboardRecordLink | null {
  if (!contributors?.length || context.cell.value === null) return null;
  for (const contributor of contributors) {
    if (contributor.datasetIds?.length && (!context.datasetId || !contributor.datasetIds.includes(context.datasetId))) {
      continue;
    }
    if (contributor.columnIds?.length && !contributor.columnIds.includes(context.column.id)) {
      continue;
    }
    try {
      const candidate = contributor.resolveLink(context);
      if (candidate && isSafeStudioPath(candidate.path)) {
        return candidate;
      }
    } catch {
      // Ignore a misbehaving link contributor so authorized query results still render.
    }
  }
  return null;
}

export function filterQueryRows(rows: readonly QueryRow[], columns: readonly QueryColumn[], query: string): QueryRow[] {
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) return [...rows];
  return rows.filter(row => columns.some(column => cellText(row.values[column.id]).toLowerCase().includes(trimmed)));
}

export function sortQueryRows(rows: readonly QueryRow[], columns: readonly QueryColumn[], sort: TableSortState | null): QueryRow[] {
  if (!sort) return [...rows];
  const column = columns.find(item => item.id === sort.columnId);
  if (!column) return [...rows];
  const factor = sort.direction === "ascending" ? 1 : -1;
  return rows
    .map((row, index) => ({ row, index }))
    .sort((left, right) => {
      const a = left.row.values[column.id]?.value ?? null;
      const b = right.row.values[column.id]?.value ?? null;
      if (a === null && b === null) return left.index - right.index;
      if (a === null) return 1;
      if (b === null) return -1;
      const compared = compareTypedValues(a, b, column.type);
      return compared !== 0 ? compared * factor : left.index - right.index;
    })
    .map(entry => entry.row);
}

function compareTypedValues(left: string, right: string, type: string): number {
  if (type === "integer" || type === "decimal") {
    const a = Number(left);
    const b = Number(right);
    if (Number.isFinite(a) && Number.isFinite(b)) return a - b;
  }
  if (type === "instant" || type === "date") {
    const a = Date.parse(left);
    const b = Date.parse(right);
    if (Number.isFinite(a) && Number.isFinite(b)) return a - b;
  }
  if (type === "boolean") {
    const a = left.toLowerCase() === "true" ? 1 : 0;
    const b = right.toLowerCase() === "true" ? 1 : 0;
    if (a !== b) return a - b;
  }
  return left.localeCompare(right, undefined, { numeric: true, sensitivity: "base" });
}

export function mergeDrillPages(previous: QueryResult | undefined, next: QueryResult): QueryResult {
  if (!previous || previous.outcome !== "succeeded" || next.outcome !== "succeeded") return next;
  const seen = new Set(previous.rows.map(row => row.key).filter(Boolean));
  const mergedRows = [...previous.rows];
  for (const row of next.rows ?? []) {
    if (row.key && seen.has(row.key)) continue;
    if (row.key) seen.add(row.key);
    mergedRows.push(row);
  }
  return {
    ...next,
    columns: next.columns?.length ? next.columns : previous.columns,
    rows: mergedRows
  };
}

export function summarizeDrillRow(row: QueryRow | undefined, columns: readonly QueryColumn[] | undefined): string | undefined {
  if (!row || !columns?.length) return undefined;
  const parts: string[] = [];
  for (const column of columns) {
    const cell = row.values[column.id];
    if (!cell || cell.value === null) continue;
    const value = cell.value.length > 40 ? `${cell.value.slice(0, 37)}…` : cell.value;
    parts.push(`${column.label}: ${value}`);
    if (parts.length >= 3) break;
  }
  return parts.length ? parts.join(" · ") : undefined;
}
