import { expect, type Page, type TestInfo } from "@playwright/test";
import type { QuestionDefinition, QueryResult } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";
import { attachDashboardJson, canonicalDashboardDefinition, dashboardBackend, observeNativeWeaverBoundary, registerDashboardCleanup, seedDashboardRuns, tag, type Capture, type Resource } from "./dashboardJourneySupport";

/** Derive period membership from the independent runtime inspection API, never Dashboard results. */
export async function dashboardComparisonJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = await dashboardBackend(page.request);
  const boundary = observeNativeWeaverBoundary(page);
  const seeded = await seedDashboardRuns(backend, `comparison-${Date.now()}`);
  let saved: Resource | undefined;
  registerDashboardCleanup(page, async () => {
    try {
      if (saved) {
        const current = await backend.api<Resource>(`/_elsa/studio/questions/${saved.id}`);
        await backend.api(`/_elsa/studio/questions/${saved.id}`, "DELETE", undefined, tag(current.revision));
      }
    } finally { await backend.api(`/design/workflows/definitions/${seeded.definitionId}`, "DELETE"); }
  });
  try {
    const read = async (id: string) => {
      const { instance } = await backend.api<{ instance: { status: string; startedAt: string } }>(`/runtime/workflows/instances/${id}`);
      expect(instance.status).toBe("Completed"); expect(Number.isFinite(Date.parse(instance.startedAt))).toBe(true);
      return { id, status: instance.status, startedAt: instance.startedAt };
    };
    const baseline = await Promise.all(seeded.executionIds.map(read));
    const current = [];
    for (let index = 0; index < 4; index++) {
      const run = await backend.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${seeded.published.artifactId}/execute`, "POST", { sourceReferenceId: seeded.published.sourceReferenceId });
      await expect.poll(async () => (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status).toBe("Completed");
      current.push(await read(run.workflowExecutionId));
    }
    const split = Math.min(...current.map(item => Date.parse(item.startedAt)));
    expect(Math.max(...baseline.map(item => Date.parse(item.startedAt)))).toBeLessThan(split);
    const periods = {
      from: new Date(split).toISOString(), to: new Date(Math.max(...current.map(item => Date.parse(item.startedAt))) + 1000).toISOString(),
      baselineFrom: new Date(Math.min(...baseline.map(item => Date.parse(item.startedAt))) - 1000).toISOString(), baselineTo: new Date(split).toISOString()
    };
    const all = [...baseline, ...current];
    const members = (from: string, to: string) => all.filter(item => Date.parse(item.startedAt) >= Date.parse(from) && Date.parse(item.startedAt) < Date.parse(to));
    const expectedCurrent = members(periods.from, periods.to); const expectedBaseline = members(periods.baselineFrom, periods.baselineTo);
    expect(expectedCurrent.map(item => item.id).sort()).toEqual(current.map(item => item.id).sort());
    expect(expectedBaseline.map(item => item.id).sort()).toEqual(baseline.map(item => item.id).sort());
    const delta = expectedCurrent.length - expectedBaseline.length;
    expect(delta).toBe(1);
    await page.goto("/questions");
    await page.getByRole("button", { name: "Create question", exact: true }).first().click();
    await page.getByLabel("Name", { exact: true }).fill(`Compared started cohorts ${Date.now()}`);
    await page.getByRole("combobox", { name: "Dataset", exact: true }).selectOption("elsa.workflows.runs");
    await page.getByRole("group", { name: "Group by", exact: true }).getByLabel("Workflow definition", { exact: true }).check();
    await page.getByRole("combobox", { name: "Metric 1", exact: true }).selectOption("workflow.started-count");
    await page.getByLabel("Result name", { exact: true }).fill("total");
    const source = page.getByRole("group", { name: "Source filters", exact: true });
    await source.getByRole("button", { name: "Add condition", exact: true }).click();
    await source.getByRole("combobox", { name: "Field", exact: true }).selectOption("definitionId");
    await source.getByLabel("Comparison value", { exact: true }).fill(seeded.definitionId);
    await page.getByRole("combobox", { name: "Time field", exact: true }).selectOption("startedAt");
    await page.getByRole("combobox", { name: "Time range", exact: true }).selectOption("absolute");
    await page.getByLabel("From (inclusive UTC instant)", { exact: true }).fill(periods.from);
    await page.getByLabel("To (exclusive UTC instant)", { exact: true }).fill(periods.to);
    await page.getByLabel("Time zone", { exact: true }).fill("UTC");
    await page.getByRole("combobox", { name: "Compare with", exact: true }).selectOption("absolute");
    await page.getByLabel("Baseline from", { exact: true }).fill(periods.baselineFrom);
    await page.getByLabel("Baseline to", { exact: true }).fill(periods.baselineTo);
    const having = page.getByRole("group", { name: "Result conditions", exact: true });
    await having.getByRole("button", { name: "Add condition", exact: true }).click();
    await having.getByRole("combobox", { name: "Result metric", exact: true }).selectOption("delta.total");
    await having.getByLabel("Comparison value", { exact: true }).fill(String(delta));
    const preview = async () => {
      const pending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/dashboard-queries/preview"));
      await page.getByRole("button", { name: "Preview question", exact: true }).click();
      const response = await pending; expect(response.ok()).toBe(true);
      const result = await response.json() as QueryResult;
      expect(result.outcome).toBe("succeeded"); expect(result.coverage?.status).toBe("unknown");
      return { definition: response.request().postDataJSON() as QuestionDefinition, result };
    };
    const excluded = await preview(); expect(excluded.result.rows).toEqual([]);
    await capture(page, info, "comparison-excluded");
    await having.getByLabel("Comparison value", { exact: true }).fill("0");
    const included = await preview();
    const check = (result: QueryResult) => {
      expect(result.rows).toHaveLength(1);
      expect(result.rows[0].values).toMatchObject({ definitionId: { value: seeded.definitionId }, total: { value: String(expectedCurrent.length) }, "baseline.total": { value: String(expectedBaseline.length) }, "delta.total": { value: String(delta) } });
    };
    check(included.result);
    await capture(page, info, "comparison-included");
    const saving = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/_elsa/studio/questions"));
    await page.getByRole("button", { name: "Save question", exact: true }).click();
    const savedResponse = await saving; expect(savedResponse.ok()).toBe(true); saved = await savedResponse.json() as Resource;
    expect(canonicalDashboardDefinition(saved.definition)).toEqual(canonicalDashboardDefinition(included.definition));
    await page.reload();
    await expect(page.getByRole("combobox", { name: "Compare with", exact: true })).toHaveValue("absolute");
    const refresh = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/questions/${saved!.id}/execute`));
    await page.getByRole("button", { name: "Refresh results", exact: true }).click();
    const refreshed = await refresh; expect(refreshed.ok()).toBe(true); check(await refreshed.json() as QueryResult);
    await capture(page, info, "comparison-saved-reopened");
    await page.getByRole("button", { name: "View matching records for result 1", exact: true }).click();
    const drill = page.getByRole("dialog", { name: "Matching source records", exact: true });
    for (const row of expectedCurrent) await expect(drill.getByRole("cell", { name: row.id, exact: true })).toBeVisible();
    for (const row of expectedBaseline) await expect(drill.getByRole("cell", { name: row.id, exact: true })).toHaveCount(0);
    await capture(page, info, "comparison-current-drill"); await drill.press("Escape");
    await attachDashboardJson(info, "dashboard-comparison-oracle.json", { runtimeInstances: all, periods, expectedCurrent, expectedBaseline, delta, excluded: excluded.result, included: included.result, exactSavedDefinition: saved.definition });
    await boundary.verify(info);
  } finally { boundary.dispose(); }
}
