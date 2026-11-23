import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, type StudioDashboardRecordLinkContribution } from "@elsa-workflows/studio-sdk";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { ResultView } from "../ResultView";
import { DrillRecordsDialog } from "../ResourceDialogs";
import {
  buildExportFilename,
  mergeDrillPages,
  neutralizeSpreadsheetFormula,
  resolveDashboardRecordLink,
  serializeRowsAsCsv,
  serializeRowsAsJson,
  serializeRowsAsMarkdown,
  serializeRowsAsTsv,
  summarizeDrillRow
} from "../resultExport";
import { initialDefinition, type DashboardResource, type DatasetDeclaration, type QueryColumn, type QueryResult, type QueryRow } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const mounted: (() => void)[] = [];
function render(content: React.ReactNode) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(content));
  mounted.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return container;
}

function setInputValue(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

afterEach(() => {
  mounted.splice(0).forEach(dispose => dispose());
  vi.restoreAllMocks();
});

const runColumns: QueryColumn[] = [
  { id: "executionId", label: "Run", type: "string" },
  { id: "definitionId", label: "Workflow definition", type: "string" },
  { id: "status", label: "Current status", type: "string" },
  { id: "startedAt", label: "Started at", type: "instant" },
  { id: "durationMs", label: "Duration", type: "decimal", unit: "ms" }
];

const runRows: QueryRow[] = [
  {
    key: "run-2",
    values: {
      executionId: { type: "string", value: "019b-run-2" },
      definitionId: { type: "string", value: "activity-def-beta" },
      status: { type: "string", value: "faulted" },
      startedAt: { type: "instant", value: "2026-11-23T14:00:00Z" },
      durationMs: { type: "decimal", value: "250.5" }
    }
  },
  {
    key: "run-1",
    values: {
      executionId: { type: "string", value: "019b-run-1" },
      definitionId: { type: "string", value: "activity-def-alpha" },
      status: { type: "string", value: "running" },
      startedAt: { type: "instant", value: "2026-11-23T12:00:00Z" },
      durationMs: { type: "decimal", value: null, nullReason: "noEligibleValues" }
    }
  },
  {
    key: "run-3",
    values: {
      executionId: { type: "string", value: "019b-run-3" },
      definitionId: { type: "string", value: "activity-def-alpha" },
      status: { type: "string", value: "completed" },
      startedAt: { type: "instant", value: "2026-11-23T13:00:00Z" },
      durationMs: { type: "decimal", value: "42" }
    }
  }
];

const workflowLinks: StudioDashboardRecordLinkContribution[] = [
  {
    id: "workflows.run",
    datasetIds: ["elsa.workflows.runs"],
    columnIds: ["executionId"],
    resolveLink: ({ cell }) => cell.value ? { path: `/workflows/instances/${encodeURIComponent(cell.value)}`, title: `Open run ${cell.value}` } : null
  },
  {
    id: "workflows.definition",
    datasetIds: ["elsa.workflows.runs"],
    columnIds: ["definitionId"],
    resolveLink: ({ cell }) => cell.value ? { path: `/workflows/definitions?definition=${encodeURIComponent(cell.value)}`, title: `Open definition ${cell.value}` } : null
  }
];

describe("resultExport utilities", () => {
  it("serializes TSV, CSV (RFC 4180 + formula neutralization), Markdown, and JSON honestly", () => {
    const columns: QueryColumn[] = [
      { id: "name", label: "Name", type: "string" },
      { id: "delta", label: "Delta", type: "decimal", unit: "%" }
    ];
    const rows: QueryRow[] = [
      { key: "1", values: { name: { type: "string", value: '=CMD("calc")' }, delta: { type: "decimal", value: "-4.5" } } },
      { key: "2", values: { name: { type: "string", value: 'a, "b"\n|c|' }, delta: { type: "decimal", value: null, nullReason: "noEligibleDenominator" } } }
    ];

    expect(neutralizeSpreadsheetFormula("=1+1")).toBe("'=1+1");
    expect(neutralizeSpreadsheetFormula("+SUM(A1)")).toBe("'+SUM(A1)");
    expect(neutralizeSpreadsheetFormula("-HYPERLINK(1)")).toBe("'-HYPERLINK(1)");
    expect(neutralizeSpreadsheetFormula("-4.5")).toBe("-4.5");

    const tsv = serializeRowsAsTsv(columns, rows);
    expect(tsv.split("\n")).toEqual([
      "Name\tDelta (%)",
      "'=CMD(\"calc\")\t-4.5",
      'a, "b" |c|\tNo eligible denominator'
    ]);

    const csv = serializeRowsAsCsv(columns, rows);
    expect(csv.split("\r\n")).toEqual([
      "Name,Delta (%)",
      '"\'=CMD(""calc"")",-4.5',
      '"a, ""b""\n|c|",No eligible denominator'
    ]);

    const md = serializeRowsAsMarkdown(columns, rows);
    expect(md).toContain("| Name | Delta (%) |");
    expect(md).toContain("| --- | ---: |");
    expect(md).toContain('\\|c\\|');
    expect(md).toContain("No eligible denominator");

    const parsed = JSON.parse(serializeRowsAsJson(columns, rows));
    expect(parsed).toEqual([
      { name: '=CMD("calc")', delta: "-4.5" },
      { name: 'a, "b"\n|c|', delta: null }
    ]);

    expect(buildExportFilename("Started Run Quality / Prod!", new Date("2026-11-23T14:20:00Z"))).toBe("started-run-quality-prod-2026-11-23.csv");
  });

  it("rejects unsafe external or protocol record links and tolerates failing contributors", () => {
    const contributors: StudioDashboardRecordLinkContribution[] = [
      { id: "broken", resolveLink: () => { throw new Error("boom"); } },
      { id: "external", resolveLink: () => ({ path: "https://evil.example/phish" }) },
      { id: "protocol-relative", resolveLink: () => ({ path: "//evil.example/phish" }) },
      { id: "js", resolveLink: () => ({ path: "javascript:alert(1)" }) },
      ...workflowLinks
    ];

    expect(resolveDashboardRecordLink(contributors, {
      datasetId: "elsa.workflows.runs",
      column: runColumns[0],
      cell: runRows[0].values.executionId,
      row: runRows[0].values
    })).toEqual({ path: "/workflows/instances/019b-run-2", title: "Open run 019b-run-2" });

    // Non-matching dataset must not resolve workflow links.
    expect(resolveDashboardRecordLink(contributors, {
      datasetId: "acme.other",
      column: runColumns[0],
      cell: runRows[0].values.executionId,
      row: runRows[0].values
    })).toBeNull();
  });

  it("merges appended drill pages without duplicating row keys and summarizes clicked rows", () => {
    const page1: QueryResult = { outcome: "succeeded", columns: runColumns, rows: [runRows[0], runRows[1]], nextCursor: "c2" };
    const page2: QueryResult = { outcome: "succeeded", columns: runColumns, rows: [runRows[1], runRows[2]], nextCursor: undefined };
    const merged = mergeDrillPages(page1, page2);

    expect(merged.rows.map(row => row.key)).toEqual(["run-2", "run-1", "run-3"]);
    expect(merged.nextCursor).toBeUndefined();
    expect(summarizeDrillRow(runRows[0], runColumns)).toBe("Run: 019b-run-2 · Workflow definition: activity-def-beta · Current status: faulted");
  });
});

describe("ResultView formatting, copy/export, filtering, sorting, and record links", () => {
  it("formats timestamps, aligns numbers, preserves exact ID cell names, and navigates record deep-links", () => {
    const navigate = vi.fn();
    const onNavigateRecord = vi.fn();
    const view = render(<ResultView
      result={{ outcome: "succeeded", columns: runColumns, rows: runRows, nextCursor: "page-2" }}
      visualization={{ kind: "table", y: [] }}
      datasetId="elsa.workflows.runs"
      recordLinks={workflowLinks}
      navigate={navigate}
      onNavigateRecord={onNavigateRecord}
      enableFilter
      enableSort
      enableRowCopy
    />);

    // Exact ID text is preserved in cell DOM for accessibility and Playwright selectors.
    const firstRowCells = [...view.querySelectorAll("tbody tr:first-child td")];
    expect(firstRowCells[0].textContent).toBe("019b-run-2");
    expect(firstRowCells[0].className).toContain("named-cell-id");
    expect(firstRowCells[3].querySelector("time")?.getAttribute("datetime")).toBe("2026-11-23T14:00:00Z");
    expect(firstRowCells[4].getAttribute("data-align")).toBe("end");
    expect(firstRowCells[4].textContent).toBe("250.5");

    // Clicking the deep-link calls onNavigateRecord and navigates in-app.
    const runLink = firstRowCells[0].querySelector<HTMLAnchorElement>("a")!;
    expect(runLink.getAttribute("href")).toContain("/workflows/instances/019b-run-2");
    flushSync(() => runLink.click());
    expect(onNavigateRecord).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith("/workflows/instances/019b-run-2");
  });

  it("filters loaded rows and sorts columns with nulls always placed last", () => {
    const view = render(<ResultView
      result={{ outcome: "succeeded", columns: runColumns, rows: runRows }}
      visualization={{ kind: "table", y: [] }}
      enableFilter
      enableSort
    />);

    // Sort by Duration ascending: 42, 250.5, null ("No eligible values" stays last).
    const durationButton = [...view.querySelectorAll<HTMLButtonElement>("thead th button")].find(button => button.textContent?.includes("Duration"))!;
    flushSync(() => durationButton.click());
    expect(durationButton.closest("th")?.getAttribute("aria-sort")).toBe("ascending");
    expect([...view.querySelectorAll("tbody tr td:first-child")].map(cell => cell.textContent)).toEqual(["019b-run-3", "019b-run-2", "019b-run-1"]);

    // Sort descending: 250.5, 42, null still last.
    flushSync(() => durationButton.click());
    expect(durationButton.closest("th")?.getAttribute("aria-sort")).toBe("descending");
    expect([...view.querySelectorAll("tbody tr td:first-child")].map(cell => cell.textContent)).toEqual(["019b-run-2", "019b-run-3", "019b-run-1"]);

    // Filter loaded rows by status "running".
    const search = view.querySelector<HTMLInputElement>('input[aria-label="Filter loaded rows"]')!;
    setInputValue(search, "running");
    expect([...view.querySelectorAll("tbody tr td:first-child")].map(cell => cell.textContent)).toEqual(["019b-run-1"]);
    expect(view.textContent).toContain("Showing 1 of 3 loaded rows");
  });

  it("copies table as TSV/Markdown/JSON with honest loaded-row and pagination feedback and supports single-row copy", async () => {
    const writeText = vi.fn(async (_text: string) => undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });

    const view = render(<ResultView
      result={{ outcome: "succeeded", columns: runColumns, rows: runRows, nextCursor: "next-page" }}
      visualization={{ kind: "table", y: [] }}
      enableFilter
      enableRowCopy
    />);

    const copyButton = [...view.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes("Copy table"))!;
    flushSync(() => copyButton.click());
    await waitUntil(() => writeText.mock.calls.length === 1);
    expect(writeText.mock.calls[0][0]).toContain("Run\tWorkflow definition\tCurrent status");
    expect(writeText.mock.calls[0][0]).toContain("2026-11-23T14:00:00Z");
    await waitUntil(() => view.textContent?.includes("Copied 3 loaded rows as TSV (more rows available).") === true);

    // Open Export menu and copy as Markdown.
    const exportTrigger = view.querySelector<HTMLButtonElement>('button[aria-label="Export table"]')!;
    flushSync(() => exportTrigger.click());
    const markdownItem = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find(item => item.textContent?.includes("Copy as Markdown"))!;
    flushSync(() => markdownItem.click());
    await waitUntil(() => writeText.mock.calls.length === 2);
    expect(writeText.mock.calls[1][0]).toContain("| Run | Workflow definition |");

    // Copy a single row via the trailing Copy button.
    const copyRow1 = view.querySelector<HTMLButtonElement>('button[aria-label="Copy row 1"]')!;
    flushSync(() => copyRow1.click());
    await waitUntil(() => writeText.mock.calls.length === 3);
    expect(writeText.mock.calls[2][0].split("\n")).toHaveLength(2);
    expect(writeText.mock.calls[2][0]).toContain("019b-run-2");
  });
});

describe("DrillRecordsDialog and append-style Load more pagination", () => {
  it("renders a wide dialog with context description and toggles expanded size", () => {
    const onClose = vi.fn();
    render(<DrillRecordsDialog
      questionName="Started run quality"
      rowSummary="Current status: running"
      datasetId="elsa.workflows.runs"
      recordLinks={workflowLinks}
      result={{ outcome: "succeeded", columns: runColumns, rows: [runRows[0]], nextCursor: "cursor-2" }}
      loading={false}
      onLoadMore={vi.fn()}
      onClose={onClose}
    />);

    const dialog = document.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.className).toContain("named-dialog--wide");
    expect(dialog.textContent).toContain("Started run quality — Current status: running");
    expect(dialog.querySelector('button[aria-label="Close Matching source records"]')).not.toBeNull();

    const expand = [...dialog.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes("Expand"))!;
    flushSync(() => expand.click());
    expect(dialog.className).toContain("named-dialog--expanded");
    expect(expand.textContent).toContain("Restore size");
  });

  it("appends subsequent drill pages instead of replacing existing rows and preserves deep-links", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const dataset: DatasetDeclaration = { id: "elsa.workflows.runs", ownerModuleId: "Elsa.Workflows", schemaRevision: "runs-v1", title: "Workflow runs", description: "Runs", fields: [], metrics: [] };
    const savedQuestion: DashboardResource = {
      id: "q1", kind: "question", name: "Started runs", description: "", ownerId: "user-a", revision: "r1",
      capabilities: { canRead: true, canEdit: true, canCopy: true, canShare: true, canDelete: true },
      definition: initialDefinition(dataset), definitionState: "available", tiles: [], createdAt: "", updatedAt: ""
    };
    const api = stubApi();
    for (const link of workflowLinks) api.dashboardRecordLinks?.add(link);
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : savedQuestion) as typeof api.backend.http.getJson;
    api.backend.http.postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/drill")) {
        const cursor = (body as { cursor?: string })?.cursor;
        if (cursor === "cursor-2") {
          return { outcome: "succeeded", columns: runColumns, rows: [runRows[2]] } satisfies QueryResult;
        }
        return { outcome: "succeeded", columns: runColumns, rows: [runRows[0], runRows[1]], nextCursor: "cursor-2" } satisfies QueryResult;
      }
      return {
        outcome: "succeeded",
        columns: [{ id: "status", label: "Current status", type: "string" }, { id: "count", label: "Count", type: "integer" }],
        rows: [{ key: "r1", values: { status: { type: "string", value: "running" }, count: { type: "integer", value: "3" } }, drill: { token: "t1" } }]
      } satisfies QueryResult;
    }) as typeof api.backend.http.postJson;

    const navigate = vi.fn();
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={navigate} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent?.trim() === "Preview"));
    flushSync(() => [...view.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === "Preview")!.click());
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent?.trim() === "View matching records"));
    flushSync(() => [...view.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === "View matching records")!.click());

    await waitUntil(() => document.querySelector('[role="dialog"] tbody tr') !== null);
    const dialog = document.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain("Started runs — Current status: running · Count: 3");
    expect(dialog.querySelectorAll("tbody tr")).toHaveLength(2);

    const loadMore = [...dialog.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === "Load more records")!;
    flushSync(() => loadMore.click());
    await waitUntil(() => dialog.querySelectorAll("tbody tr").length === 3);
    expect([...dialog.querySelectorAll("tbody tr td:first-child")].map(cell => cell.textContent)).toEqual(["019b-run-2", "019b-run-1", "019b-run-3"]);
    expect([...dialog.querySelectorAll("button")].some(button => button.textContent?.trim() === "Load more records")).toBe(false);

    // Clicking a deep-linked run closes the dialog and navigates to the run inspector.
    const link = dialog.querySelector<HTMLAnchorElement>('a[href*="/workflows/instances/019b-run-2"]')!;
    flushSync(() => link.click());
    expect(navigate).toHaveBeenCalledWith("/workflows/instances/019b-run-2");
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });
});
