import { expect, type Page, type TestInfo } from "@playwright/test";
import { attachDashboardJson, dashboardBackend, observeNativeWeaverBoundary, tag, type Capture, type Resource } from "./dashboardJourneySupport";
import { captureResultViews } from "./liveDashboardsJourney";
import type { QueryResult } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";

type FeatureCatalog = { revision: string; features: { id: string; enabled: boolean; configuration: unknown }[] };

/** Actual module removal/repair and native edge values; no model, store patches or result interception. */
export async function nativeDashboardEdgesJourney(page: Page, info: TestInfo, capture: Capture) {
  info.setTimeout(300_000);
  const backend = await dashboardBackend(page.request); const boundary = observeNativeWeaverBoundary(page);
  const suffix = `edges-${Date.now()}`; const category = `In-flight ${suffix}`;
  let question: Resource | undefined; let recordId: string | undefined; let disabled = false;
  const counters = () => backend.api<{ counts: Record<string, number> }>("/__qualification/dashboard-live/counters");
  const before = await counters(); expect(before.counts["session.send"] ?? 0).toBe(0);
  async function feature(enabled: boolean) {
    const catalog = await backend.api<FeatureCatalog>("/modularity/features");
    const current = catalog.features.find(item => item.id === "WorkflowDashboardQueries"); expect(current).toBeTruthy();
    const applied = await backend.api<{ catalog: FeatureCatalog; reloadedShellCount: number }>("/modularity/features/apply", "POST", {
      revision: catalog.revision, features: catalog.features.filter(item => item.enabled || item.id === current!.id).map(item => ({ id: item.id, enabled: item.id === current!.id ? enabled : item.enabled, configuration: item.configuration }))
    });
    expect(applied.reloadedShellCount).toBeGreaterThan(0);
    expect(applied.catalog.features.find(item => item.id === current!.id)?.enabled).toBe(enabled);
    await expect.poll(async () => (await backend.api<FeatureCatalog>("/modularity/features")).features.find(item => item.id === current!.id)?.enabled).toBe(enabled);
  }
  async function preview(): Promise<QueryResult & { errorCode?: string }> {
    const pending = page.waitForResponse(response => response.request().method() === "POST" && /\/(dashboard-queries\/preview|questions\/[^/]+\/(?:preview|execute))$/.test(new URL(response.url()).pathname));
    await page.getByRole("button", { name: /^(Preview question|Refresh results)$/, exact: true }).click();
    const response = await pending; expect(response.ok()).toBe(true); return response.json() as Promise<QueryResult & { errorCode?: string }>;
  }
  const result = page.locator(".named-preview > .named-result");
  async function captureState(name: string) {
    const target = name.includes("history") ? page.getByRole("dialog", { name: "Definition history", exact: true }) : result.count().then(async count => count ? result : await page.locator(".named-preview > .named-result-state").count() ? page.locator(".named-preview > .named-result-state") : page.getByText(/^Previous result values are withheld because current data access could not be established\./));
    const element = await target; await element.scrollIntoViewIfNeeded(); await expect(element).toBeInViewport();
    await capture(page, info, name);
  }
  try {
    const bootstrap = await backend.api<{ data: { enabled: boolean; provider: { isAvailable: boolean } } }>("/_elsa/weaver/bootstrap");
    expect(bootstrap.data.enabled && bootstrap.data.provider.isAvailable).toBe(false);
    const datasets = await backend.api<{ items: { id: string; schemaRevision: string }[] }>("/_elsa/studio/dashboard-datasets");
    const workflow = datasets.items.find(item => item.id === "elsa.workflows.runs"); expect(workflow).toBeTruthy();
    question = await backend.api<Resource>("/_elsa/studio/questions", "POST", {
      name: `Explicit module repair ${suffix}`, operationKey: suffix, description: "A saved Workflow question repaired explicitly after its query module is disabled.",
      definition: { query: { schemaVersion: 1, dataset: { id: workflow!.id, schemaRevision: workflow!.schemaRevision }, form: "aggregate", metrics: [{ alias: "started", metric: "workflow.started-count", revision: "v1" }], time: { field: "startedAt", window: "last7Days", timeZone: "UTC", weekStart: "monday" }, limit: 50 }, visualization: { kind: "table", y: ["started"] }, parameterValues: {} }
    });
    const original = question;
    await feature(false); disabled = true;
    await page.goto(`/questions/${question.id}`);
    await expect(page.getByText(/^Parts of this definition are withheld under your current access\./)).toBeVisible();
    await expect(page.locator(".named-query-controls")).toHaveCount(0);
    await page.getByRole("button", { name: "History", exact: true }).click();
    let history = page.getByRole("dialog", { name: "Definition history", exact: true });
    await history.getByRole("button", { name: `Inspect revision ${original.revision}`, exact: true }).click();
    await expect(history.getByText("Definition withheld under current access.", { exact: true })).toBeVisible();
    await capture(page, info, "edge-module-history-withheld"); await history.press("Escape");
    await capture(page, info, "edge-module-unavailable");
    const startedAt = new Date(Date.now() - 3_600_000).toISOString();
    const record = await backend.api<{ id: string; customerCategory: string; status: string; completedAt: string | null }>("/_acme/processing/records", "POST", { customerCategory: category, startedAt, completedAt: null, status: "processing", readerIds: [] });
    recordId = record.id;
    expect(await backend.api(`/_acme/processing/records/${record.id}`)).toMatchObject({ id: record.id, customerCategory: category, status: "processing", completedAt: null });
    await page.getByRole("button", { name: "Replace unavailable query", exact: true }).click();
    const confirm = page.getByRole("dialog", { name: "Replace unavailable query", exact: true });
    await confirm.getByRole("button", { name: "Confirm", exact: true }).click();
    await page.getByRole("combobox", { name: "Dataset", exact: true }).selectOption("acme.processing.records");
    await page.getByRole("group", { name: "Group by", exact: true }).getByLabel("Customer category", { exact: true }).check();
    const selections = ["processing.record-count", "processing.failure-rate", "processing.average-time-ms"];
    for (let index = 0; index < selections.length; index++) {
      await page.getByRole("button", { name: "Add metric", exact: true }).click();
      await page.getByRole("combobox", { name: `Metric ${index + 1}`, exact: true }).selectOption(selections[index]);
      await page.getByLabel("Result name", { exact: true }).nth(index).fill(["count", "rate", "duration"][index]);
    }
    const filters = page.getByRole("group", { name: "Source filters", exact: true });
    await filters.getByRole("button", { name: "Add condition", exact: true }).click();
    await filters.getByRole("combobox", { name: "Field", exact: true }).selectOption("customerCategory");
    await filters.getByLabel("Comparison value", { exact: true }).fill(category);
    await page.getByRole("combobox", { name: "Time field", exact: true }).selectOption("startedAt");
    await page.getByRole("combobox", { name: "Time range", exact: true }).selectOption("absolute");
    const from = new Date(Date.now() - 86_400_000).toISOString(), to = new Date(Date.now() + 3_600_000).toISOString();
    await page.getByLabel("From (inclusive UTC instant)", { exact: true }).fill(from);
    await page.getByLabel("To (exclusive UTC instant)", { exact: true }).fill(to);
    await page.getByRole("button", { name: "Validate question", exact: true }).click();
    const inFlight = await preview(); expect(inFlight.outcome).toBe("succeeded"); expect(inFlight.coverage?.status).toBe("unknown");
    expect(inFlight.rows).toHaveLength(1);
    expect(inFlight.rows[0].values).toMatchObject({ count: { value: "1" }, rate: { value: null, nullReason: "noEligibleDenominator" }, duration: { value: null, nullReason: "noEligibleValues" } });
    await expect(result.getByRole("cell", { name: "No eligible denominator", exact: true })).toBeVisible();
    await expect(result.getByRole("cell", { name: "No eligible values", exact: true })).toBeVisible();
    await capture(page, info, "edge-no-denominator-and-values");
    await captureResultViews(page, info, capture, "edge-no-denominator-and-values", result);
    const saveResponse = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith(`/questions/${original.id}`));
    await page.getByRole("button", { name: "Save question", exact: true }).click();
    const response = await saveResponse; expect(response.ok()).toBe(true); question = await response.json() as Resource;
    expect(question.id).toBe(original.id); expect(question.revision).not.toBe(original.revision);
    const repaired = await backend.api<Resource>(`/_elsa/studio/questions/${question.id}`); expect(repaired.definition).toEqual(question.definition);
    expect(repaired.definition).toMatchObject({ query: { dataset: { id: "acme.processing.records" }, metrics: [{ alias: "count" }, { alias: "rate" }, { alias: "duration" }] } });
    await page.reload(); await preview(); await capture(page, info, "edge-repair-saved-reopened");
    await captureResultViews(page, info, capture, "edge-repair-saved-reopened", result);
    await page.getByRole("button", { name: "History", exact: true }).click(); history = page.getByRole("dialog", { name: "Definition history", exact: true });
    await history.getByRole("button", { name: `Inspect revision ${original.revision}`, exact: true }).click();
    await expect(history.getByText("Definition withheld under current access.", { exact: true })).toBeVisible();
    await history.getByRole("button", { name: `Inspect revision ${question.revision}`, exact: true }).click();
    await expect(history.locator(".named-definition-inspection")).toContainText("acme.processing.records");
    await capture(page, info, "edge-repaired-definition-history"); await history.press("Escape");
    await page.reload(); await preview();
    const successfulAt = await result.locator(".named-result-provenance time").first().getAttribute("datetime");
    await filters.getByLabel("Comparison value", { exact: true }).fill(category + " no matches");
    await expect(result.getByText("Stale result. Definition changed.", { exact: true })).toBeVisible();
    expect(await result.locator(".named-result-provenance time").first().getAttribute("datetime")).toBe(successfulAt);
    await expect(result.getByRole("button", { name: "View matching records for result 1", exact: true })).toBeDisabled();
    await capture(page, info, "edge-stale-definition-provenance");
    await captureResultViews(page, info, capture, "edge-stale-definition-provenance", result);
    const empty = await preview(); expect(empty.rows).toHaveLength(0);
    await expect(result.getByText(/^No authorized records matched this question\./)).toBeVisible();
    await captureState("edge-empty-result");
    await filters.getByLabel("Comparison value", { exact: true }).fill(category); await preview();
    await page.getByRole("combobox", { name: "Compare with", exact: true }).selectOption("absolute");
    await page.getByLabel("Baseline from", { exact: true }).fill(new Date(Date.now() - 4 * 86_400_000).toISOString());
    await page.getByLabel("Baseline to", { exact: true }).fill(new Date(Date.now() - 3 * 86_400_000).toISOString());
    const baseline = await preview(); expect(baseline.coverage?.status).toBe("unknown");
    expect(baseline.rows[0].values.count.value).toBe("1");
    expect(baseline.rows[0].values["baseline.count"].value).toBe("0"); expect(baseline.rows[0].values["delta.count"].value).toBe("1");
    expect(baseline.rows[0].values["baseline.rate"]).toMatchObject({ value: null, nullReason: "noEligibleDenominator" });
    expect(baseline.rows[0].values["baseline.duration"]).toMatchObject({ value: null, nullReason: "noEligibleValues" });
    await capture(page, info, "edge-empty-baseline-unknown-coverage");
    await captureResultViews(page, info, capture, "edge-empty-baseline-unknown-coverage", result);
    await page.getByRole("combobox", { name: "Compare with", exact: true }).selectOption("");
    const cdp = await page.context().newCDPSession(page);
    try {
      await cdp.send("Network.enable"); await cdp.send("Network.emulateNetworkConditions", { offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0 });
      await page.getByRole("button", { name: /^(Preview question|Refresh results)$/, exact: true }).click();
      await expect(page.getByText(/^Previous result values are withheld because current data access could not be established\./)).toBeVisible();
      await expect(result).toHaveCount(0); await captureState("edge-failed-transport-values-withheld");
    } finally {
      await cdp.send("Network.emulateNetworkConditions", { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }); await cdp.detach();
    }
    expect((await preview()).outcome).toBe("succeeded"); await capture(page, info, "edge-transport-retry");
    await captureResultViews(page, info, capture, "edge-transport-retry", result);
    // Restoring a feature reloads the shell; Acme's reference record store is intentionally
    // ephemeral, so finish its value observations before qualifying Workflow's bucket bound.
    await feature(true); disabled = false;
    await page.getByRole("combobox", { name: "Dataset", exact: true }).selectOption("elsa.workflows.runs");
    await page.getByRole("button", { name: "Add metric", exact: true }).click();
    await page.getByRole("combobox", { name: "Metric 1", exact: true }).selectOption("workflow.started-count");
    await page.getByLabel("Result name", { exact: true }).fill("count");
    await page.getByRole("combobox", { name: "Time field", exact: true }).selectOption("startedAt");
    await page.getByRole("combobox", { name: "Time range", exact: true }).selectOption("absolute");
    await page.getByLabel("From (inclusive UTC instant)", { exact: true }).fill(new Date(Date.now() - 32 * 86_400_000).toISOString());
    await page.getByLabel("To (exclusive UTC instant)", { exact: true }).fill(to);
    await page.getByRole("combobox", { name: "Time bucket", exact: true }).selectOption("hour");
    const bounded = await preview(); expect(bounded.outcome).toBe("limitExceeded"); expect(bounded.errorCode).toBe("QUERY_LIMIT");
    await expect(page.getByRole("heading", { name: "Query limit reached", exact: true })).toBeVisible();
    await captureState("edge-bounded-cost-rejection");
    await page.getByLabel("From (inclusive UTC instant)", { exact: true }).fill(from);
    const retry = await preview(); expect(retry.outcome).toBe("succeeded"); await captureState("edge-bounded-cost-retry");
    const after = await counters(); expect(after.counts["session.send"] ?? 0).toBe(0); await boundary.verify(info, "edge-native");
    await attachDashboardJson(info, "native-edge-proof.json", { actualModuleDisabledAndRestored: true, explicitRepairSavedNewRevision: true, sameQuestion: original.id === question.id, oldHistoryWithheld: true, newHistoryAvailable: true, noEligibleDenominator: true, noEligibleValues: true, staleReason: "definition changed", priorResultGeneratedAtRetained: true, emptyBaselineCoverage: "unknown", boundedCostError: bounded.errorCode, boundedCostRetrySucceeded: true, failureKind: "owned browser transport offline", failedValuesWithheld: true, transportRetrySucceeded: true, sdkSendsBefore: before.counts["session.send"] ?? 0, sdkSendsAfter: after.counts["session.send"] ?? 0 });
  } finally {
    boundary.dispose();
    try { if (disabled) await feature(true); }
    finally {
      if (question) {
        const current = await backend.api<Resource>(`/_elsa/studio/questions/${question.id}`);
        await backend.api(`/_elsa/studio/questions/${question.id}`, "DELETE", undefined, tag(current.revision));
      }
      if (recordId) {
        const removed = await backend.response(`/_acme/processing/records/${recordId}`, "DELETE");
        expect(removed.ok() || removed.status() === 404).toBe(true);
      }
    }
  }
}
