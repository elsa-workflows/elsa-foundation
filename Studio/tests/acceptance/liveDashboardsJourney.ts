import { chromium, expect, type Browser, type BrowserContext, type Locator, type Page, type Request, type TestInfo } from "@playwright/test";
import type { QuestionDefinition, QueryPredicate, QueryResult, QueryValue } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";
import { attachDashboardJson, canonicalDashboardDefinition, dashboardBackend, seedDashboardRuns, signInDashboardReader, tag, type Backend, type Capture, type Resource } from "./dashboardJourneySupport";

/** This profile drives the real SDK adapter and native source APIs. It never supplies a provider response. */
type Counters = { category: string; counts: Record<string, number>; privacyFailures: number; physicalModelHttpRequests: string };
type NativePhase = { phase: string; before: number; after: number; delta: number };
type ProcessingSeed = { id: string; customerCategory: string; durationSeconds: number; shared: boolean; startedAt: string; completedAt: string };

function sends(value: Counters) { return value.counts["session.send"] ?? 0; }
async function counters(backend: Backend) {
  const value = await backend.api<Counters>("/__qualification/dashboard-live/counters");
  expect(value.category).toBe("actual-sdk-forwarding");
  expect(value.privacyFailures, "No source-result fact or canary may enter the SDK prompt/tool-result boundary").toBe(0);
  expect(value.counts["event.Error"] ?? 0, "The actual SDK stream must not report an error").toBe(0);
  return value;
}

function instrumentation(page: Page, backend: Backend) {
  const writes: { method: string; path: string }[] = [];
  const collect = (request: Request) => {
    const path = new URL(request.url()).pathname;
    if (["POST", "PUT", "DELETE"].includes(request.method()) && /^\/_elsa\/studio\/(questions|dashboards)(\/[^/]+)?$/.test(path))
      writes.push({ method: request.method(), path });
  };
  page.on("request", collect);
  const phases: NativePhase[] = [];
  return {
    writes, phases,
    async native<T>(phase: string, action: () => Promise<T>): Promise<T> {
      const before = sends(await counters(backend));
      const result = await action();
      const after = sends(await counters(backend));
      phases.push({ phase, before, after, delta: after - before });
      expect(after, `${phase} must add zero SDK SendAsync calls`).toBe(before);
      return result;
    },
    dispose() { page.off("request", collect); }
  };
}

/** Capture real values inside Studio/Weaver's scrolling panes, not only the page header. */
async function captureResultViews(page: Page, info: TestInfo, capture: Capture, name: string, result: Locator) {
  const target = async (suffix: string, element: Locator) => {
    await element.evaluate(value => value.scrollIntoView({ block: "end", inline: "nearest", behavior: "instant" }));
    await expect(element).toBeInViewport(); await capture(page, info, `${name}-${suffix}`);
  };
  await target("periods", result.locator(".named-result-provenance > p").last());
  const chart = result.locator(".named-chart-series");
  if (await chart.count()) await target("chart", chart.first());
  const values = result.getByRole("region", { name: "Exact result values", exact: true });
  await values.evaluate(element => { element.scrollLeft = 0; });
  await target("values", values);
  await values.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  await capture(page, info, `${name}-values-end`);
  await values.evaluate(element => { element.scrollLeft = 0; });
}

async function idle(page: Page) {
  const assistant = page.getByRole("region", { name: "Weaver assistant", exact: true });
  await expect(assistant).toBeVisible({ timeout: 180_000 });
  await expect(assistant).toHaveAttribute("aria-busy", "false", { timeout: 180_000 });
  await expect(assistant.locator('.weaver-message[data-role="assistant"][data-status="completed"]').last()).toBeVisible({ timeout: 180_000 });
  await expect(assistant.locator('.weaver-message[data-status="failed"]')).toHaveCount(0);
}

async function createEditor(page: Page, name: string, dataset: string) {
  await page.goto("/questions");
  await page.getByRole("button", { name: "Create question", exact: true }).first().click();
  await page.getByLabel("Name", { exact: true }).fill(name);
  const datasets = page.getByRole("combobox", { name: "Dataset", exact: true });
  await expect(datasets.locator(`option[value="${dataset}"]`)).toHaveCount(1);
  // Start authoring from the canonical empty native draft. Selecting a dataset creates
  // an incomplete aggregate until its owner-required time window is entered; that
  // populated query must pass normal validation before it can be disclosed to a model.
  // The real model discovers the installed source from the user's question instead.
  await expect(datasets).toHaveValue("");
  await expect(page.getByRole("button", { name: "Save question", exact: true })).toBeEnabled();
}

/** Require an actual module-owned clarification card; a plain-text question or guessed choice cannot pass. */
async function askAndClarify(page: Page, info: TestInfo, capture: Capture, backend: Backend, prompt: string, supported: RegExp) {
  const before = await counters(backend);
  const ask = page.getByRole("region", { name: "Ask Weaver", exact: true });
  await ask.getByLabel("Your question", { exact: true }).fill(prompt);
  await expect(ask.getByRole("button", { name: "Ask Weaver", exact: true })).toBeEnabled({ timeout: 60_000 });
  const admission = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(response.url()).pathname));
  await ask.getByRole("button", { name: "Ask Weaver", exact: true }).press("Enter");
  const admitted = await admission;
  expect(admitted.ok(), `Dashboard admission: HTTP ${admitted.status()}; ${JSON.stringify(await counters(backend))}`).toBe(true);
  const clarification = page.getByRole("region", { name: "Clarify dashboard question", exact: true }).last();
  await expect(clarification).toBeVisible({ timeout: 180_000 });
  await idle(page);
  await expect(page.getByRole("region", { name: "Proposed dashboard question", exact: true }), "The model must wait for the user's consequential clarification before proposing").toHaveCount(0);
  const choices = clarification.getByRole("button");
  const count = await choices.count(); expect(count).toBeGreaterThanOrEqual(2); expect(count).toBeLessThanOrEqual(4);
  const chosen = clarification.getByRole("button", { name: supported }).first();
  await expect(chosen, "The actual model must offer a supported, recognizable interpretation").toBeVisible();
  const choice = await chosen.innerText();
  await clarification.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" }));
  await expect(chosen).toBeInViewport();
  await capture(page, info, "live-clarification");
  await chosen.press("Enter");
  await expect(page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()).toBeVisible({ timeout: 180_000 });
  await idle(page);
  const after = await counters(backend);
  expect(sends(after) - sends(before), "Ask and the chosen clarification each reach the actual SDK").toBeGreaterThanOrEqual(2);
  expect((after.counts["event.Completed"] ?? 0) - (before.counts["event.Completed"] ?? 0)).toBeGreaterThanOrEqual(2);
  expect((after.counts["tool.return.dashboard_request_clarification"] ?? 0) - (before.counts["tool.return.dashboard_request_clarification"] ?? 0)).toBeGreaterThanOrEqual(1);
  expect((after.counts["tool.return.dashboard_preview_question"] ?? 0) - (before.counts["tool.return.dashboard_preview_question"] ?? 0)).toBeGreaterThanOrEqual(1);
  return { before, afterClarification: after, choice };
}

async function previewProposal(page: Page, proposal: Locator) {
  const response = page.waitForResponse(item => item.request().method() === "POST" && /\/_elsa\/studio\/(dashboard-queries\/preview|questions\/[^/]+\/preview)$/.test(new URL(item.url()).pathname));
  await proposal.getByRole("button", { name: "Preview proposed question", exact: true }).click();
  const actual = await response; expect(actual.ok()).toBe(true);
  const definition = actual.request().postDataJSON() as QuestionDefinition;
  const result = await actual.json() as QueryResult;
  expect(result.outcome).toBe("succeeded"); expect(result.coverage?.status).toBe("unknown");
  await expect(proposal.getByRole("region", { name: "Exact result values", exact: true })).toBeVisible();
  return { definition, result };
}

async function refine(page: Page, info: TestInfo, capture: Capture, backend: Backend, message: string) {
  const proposals = page.getByRole("region", { name: "Proposed dashboard question", exact: true });
  const count = await proposals.count();
  const before = await counters(backend);
  await proposals.last().getByRole("button", { name: "Refine proposal", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Refine proposed question", exact: true });
  await expect(dialog).toBeVisible();
  await dialog.getByRole("textbox").fill(message);
  await dialog.getByRole("button", { name: "Confirm", exact: true }).press("Enter");
  await expect(dialog).toBeHidden();
  await expect.poll(() => proposals.count(), { timeout: 180_000 }).toBeGreaterThan(count);
  await idle(page);
  const after = await counters(backend);
  expect(sends(after), "Refinement must reach the real SDK, rather than replacing a native definition in the runner").toBeGreaterThan(sends(before));
  expect((after.counts["event.Completed"] ?? 0) - (before.counts["event.Completed"] ?? 0)).toBeGreaterThanOrEqual(1);
  expect((after.counts["tool.return.dashboard_preview_question"] ?? 0) - (before.counts["tool.return.dashboard_preview_question"] ?? 0)).toBeGreaterThanOrEqual(1);
  await capture(page, info, "live-refined-proposal");
  return { before, after };
}

function sameQuery(initial: QuestionDefinition, final: QuestionDefinition) {
  // The user asked only to change the chart. Current metadata is still validated server-side.
  expect(final.query).toEqual(initial.query);
  expect(final.parameterValues ?? {}).toEqual(initial.parameterValues ?? {});
  expect(initial.visualization.kind).toBe("table");
  expect(final.visualization.kind).toBe("bar");
  expect(final.visualization).not.toEqual(initial.visualization);
}

function metricColumn(definition: QuestionDefinition, result: QueryResult, metric: string) {
  const selected = definition.query.metrics.filter(item => item.metric === metric);
  expect(selected).toHaveLength(1);
  expect(definition.query.metrics).toHaveLength(1);
  const alias = selected[0].alias;
  const columns = result.columns.filter(item => item.id === alias || item.id === `current.${alias}`);
  expect(columns).toHaveLength(1);
  return columns[0].id;
}

function resolve(value: QueryValue | undefined, definition: QuestionDefinition) {
  if (!value?.parameter) return value?.literal;
  return definition.parameterValues?.[value.parameter]?.literal ?? definition.query.parameters.find(item => item.name === value.parameter)?.default?.literal;
}
function conjuncts(predicate?: QueryPredicate): QueryPredicate[] {
  if (!predicate) return [];
  expect(predicate.any, "The independent oracle requires a conjunctive source cohort").toBeUndefined();
  return predicate.all ? predicate.all.flatMap(conjuncts) : [predicate];
}

function workflowOracle(definition: QuestionDefinition, result: QueryResult, definitionId: string, expected: number) {
  expect(definition.query.dataset.id).toBe("elsa.workflows.runs");
  expect(definition.query.form).toBe("aggregate");
  expect(definition.query.groupBy).toEqual(["status"]);
  expect(definition.query.time).toMatchObject({ field: "startedAt", window: "last7Days", timeZone: "UTC" });
  expect(definition.query.comparison).toBeUndefined(); expect(definition.query.having).toBeUndefined();
  const filters = conjuncts(definition.query.filters);
  const cohort = filters.filter(item => item.field === "definitionId"); expect(cohort).toHaveLength(1);
  expect(cohort[0].operator).toBe("eq"); expect(resolve(cohort[0].value, definition)).toBe(definitionId);
  for (const filter of filters.filter(item => item.field !== "definitionId")) {
    expect(filter.field).toBe("runKind"); expect(filter.operator).toBe("eq"); expect(resolve(filter.value, definition)).toBe("publishedRun");
  }
  const metric = metricColumn(definition, result, "workflow.started-count");
  expect(result.rows).toHaveLength(1); expect(result.rows[0].values.status.value).toBe("completed");
  expect(Number(result.rows[0].values[metric].value)).toBe(expected);
  return { expected, actual: result.rows[0].values[metric].value, definitionId, source: "actual published executable runs read from native runtime API" };
}

function acmeOracle(definition: QuestionDefinition, result: QueryResult, seeds: ProcessingSeed[], viewer = false) {
  expect(definition.query.dataset.id).toBe("acme.processing.records");
  expect(definition.query.groupBy).toEqual(["customerCategory"]);
  expect(definition.query.form).toBe("aggregate"); expect(definition.query.comparison).toBeUndefined(); expect(definition.query.having).toBeUndefined();
  expect(definition.query.metrics[0]).toMatchObject({ metric: "processing.average-time-ms", revision: "finished-valid-pairs-v1" });
  const metric = metricColumn(definition, result, "processing.average-time-ms");
  expect(result.columns.find(item => item.id === metric)?.unit).toBe("milliseconds");
  const visible = seeds.filter(item => !viewer || item.shared);
  const categories = [...new Set(visible.map(item => item.customerCategory))];
  expect(result.rows).toHaveLength(categories.length);
  const oracle = categories.map(category => {
    const cohort = visible.filter(item => item.customerCategory === category);
    // Numeric expectations derive from independently seeded timestamps, never another query result.
    const expected = cohort.reduce((total, item) => total + (Date.parse(item.completedAt) - Date.parse(item.startedAt)), 0) / cohort.length;
    const rows = result.rows.filter(row => row.values.customerCategory.value === category); expect(rows).toHaveLength(1);
    expect(Number(rows[0].values[metric].value)).toBe(expected);
    return { category, expectedMilliseconds: expected, actualMilliseconds: rows[0].values[metric].value, eligibleSourceIds: cohort.map(item => item.id) };
  });
  return { viewer, oracle, source: "actual Acme create/read API timestamps; no source result supplied to the model" };
}

async function saveAndReopen(page: Page, info: TestInfo, capture: Capture, backend: Backend, monitor: ReturnType<typeof instrumentation>, definition: QuestionDefinition) {
  expect(monitor.writes, "Ask, clarification, refinement and preview must not save anything").toEqual([]);
  await monitor.native("use-unsaved-draft", async () => {
    await page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().getByRole("button", { name: "Use this question", exact: true }).press("Enter");
    await expect(page.getByRole("button", { name: "Undo applied proposal", exact: true })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "Visualization", exact: true })).toHaveValue("bar");
    expect(monitor.writes, "Use changes only the ordinary native draft").toEqual([]);
    await capture(page, info, "live-applied-draft");
  });
  const saved = await monitor.native("explicit-save", async () => {
    const backendSelection = new URL(page.url()).searchParams.get("backend");
    const pending = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/questions$/.test(response.url()));
    await page.getByRole("button", { name: "Save question", exact: true }).press("Enter");
    const response = await pending; expect(response.ok()).toBe(true);
    const value = await response.json() as Resource;
    expect(canonicalDashboardDefinition(value.definition)).toEqual(canonicalDashboardDefinition(definition)); expect(value.ownerId).not.toBe("dashboard-qa-reader");
    expect(response.headers().etag).toBe(`"${value.revision}"`);
    await expect(page).toHaveURL(url => url.pathname === `/questions/${value.id}` && url.searchParams.get("backend") === backendSelection);
    expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/questions/${value.id}/audience`)).items).toEqual([]);
    await capture(page, info, "live-saved"); return value;
  });
  expect(monitor.writes).toEqual([{ method: "POST", path: "/_elsa/studio/questions" }]);
  const readback = await monitor.native("reopen-saved-definition", async () => {
    await page.reload();
    await expect(page.getByRole("heading", { name: saved.name, level: 2, exact: true })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "Visualization", exact: true })).toHaveValue("bar");
    const value = await backend.api<Resource>(`/_elsa/studio/questions/${saved.id}`);
    expect(value.revision).toBe(saved.revision); expect(value.definition).toEqual(saved.definition);
    await capture(page, info, "live-reopened"); return value;
  });
  await attachDashboardJson(info, "live-readback.json", { saved, readback, authoredMeaningRetained: true, savedNativeDefinitionRetained: true });
  return saved;
}

async function refresh(page: Page, question: Resource) {
  const pending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/questions/${question.id}/execute`));
  await page.getByRole("button", { name: "Refresh results", exact: true }).click();
  const response = await pending; expect(response.ok()).toBe(true);
  expect(response.request().headers()["if-match"]).toBe(`"${question.revision}"`);
  const result = await response.json() as QueryResult;
  expect(result.outcome).toBe("succeeded"); expect(result.coverage?.status).toBe("unknown");
  await expect(page.getByRole("region", { name: "Exact result values", exact: true }).last()).toBeVisible();
  return result;
}

async function savedAppearances(page: Page, info: TestInfo, capture: Capture, backend: Backend, monitor: ReturnType<typeof instrumentation>, question: Resource) {
  await monitor.native("saved-native-appearances", async () => {
    for (const mode of ["light", "dark"] as const) {
      await page.evaluate(value => { localStorage.setItem("elsa-studio-theme-mode", value); }, mode);
      await page.reload();
      await expect(page.getByRole("heading", { name: question.name, level: 2, exact: true })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);
      await expect(page.getByRole("combobox", { name: "Visualization", exact: true })).toHaveValue("bar");
      await refresh(page, question);
      for (const narrow of [false, true]) {
        await page.setViewportSize(narrow ? { width: 360, height: 900 } : { width: 1440, height: 1000 });
        await capture(page, info, `live-saved-${mode}-${narrow ? "narrow" : "desktop"}`);
        await captureResultViews(page, info, capture, `live-saved-${mode}-${narrow ? "narrow" : "desktop"}`, page.locator(".named-preview > .named-result"));
      }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    expect((await counters(backend)).privacyFailures).toBe(0);
  });
}

async function removeQuestion(backend: Backend, id?: string) {
  if (!id) return;
  const resource = await backend.api<Resource>(`/_elsa/studio/questions/${id}`);
  await backend.api(`/_elsa/studio/questions/${id}`, "DELETE", undefined, tag(resource.revision));
}

export async function liveWorkflowDashboardJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = await dashboardBackend(page.request);
  const monitor = instrumentation(page, backend); let saved: Resource | undefined;
  let ownedSeed: Awaited<ReturnType<typeof seedDashboardRuns>> | undefined;
  try {
    const seeded = ownedSeed = await seedDashboardRuns(backend, `live-${Date.now()}`);
    await backend.api("/__qualification/dashboard-live/canaries", "POST", seeded.executionIds);
    await createEditor(page, `Live Workflow meaning ${Date.now()}`, "elsa.workflows.runs");
    const turns = await askAndClarify(page, info, capture, backend,
      `For workflow definition ${seeded.definitionId}, count published runs in the last seven days in UTC grouped by current status. I might mean runs started during that period or completions occurring during that period. Ask me to choose the supported interpretation before proposing. Offer the runs-started cohort explicitly. Use only this definition, the started-count measure, no comparison or result conditions, and begin with a table. Explain retained coverage rather than claiming complete history.`,
      /started|starting|start cohort/i);
    const initial = await monitor.native("initial-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    const beforeOracle = workflowOracle(initial.definition, initial.result, seeded.definitionId, 3);
    await capture(page, info, "live-proposal-preview");
    await captureResultViews(page, info, capture, "live-proposal-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    const refinement = await refine(page, info, capture, backend, "Keep the exact accepted query, cohort, metric, parameters and UTC window. Change only presentation from a table to a bar chart, using current status as category and the started-count metric as its value. Do not add comparison periods, filters, result conditions or other metrics.");
    const final = await monitor.native("refined-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    sameQuery(initial.definition, final.definition); workflowOracle(final.definition, final.result, seeded.definitionId, 3);
    await capture(page, info, "live-refined-preview");
    await captureResultViews(page, info, capture, "live-refined-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    saved = await saveAndReopen(page, info, capture, backend, monitor, final.definition);
    const reopened = await monitor.native("reopened-native-refresh", () => refresh(page, saved!));
    workflowOracle(final.definition, reopened, seeded.definitionId, 3);
    const fresh = await monitor.native("native-new-run-and-refresh", async () => {
      const run = await backend.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${seeded.published.artifactId}/execute`, "POST", { sourceReferenceId: seeded.published.sourceReferenceId });
      await expect.poll(async () => (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
      seeded.executionIds.push(run.workflowExecutionId);
      await backend.api("/__qualification/dashboard-live/canaries", "POST", seeded.executionIds);
      return { run, result: await refresh(page, saved!) };
    });
    const afterOracle = workflowOracle(final.definition, fresh.result, seeded.definitionId, 4);
    await capture(page, info, "live-refreshed");
    await monitor.native("native-saved-drill", async () => {
      await page.getByRole("button", { name: "View matching records for result 1", exact: true }).click();
      const drill = page.getByRole("dialog", { name: "Matching source records", exact: true });
      for (const id of seeded.executionIds) await expect(drill.getByRole("cell", { name: id, exact: true })).toBeVisible();
      await capture(page, info, "live-drill"); await drill.press("Escape");
    });
    await savedAppearances(page, info, capture, backend, monitor, saved);
    await attachDashboardJson(info, "live-oracle.json", { before: beforeOracle, after: afterOracle, sourceExecutionIds: seeded.executionIds, observedInitialInstances: seeded.observedInstances, query: final.definition.query });
    await attachDashboardJson(info, "live-counters.json", { turns, refinement, after: await counters(backend), nativePhases: monitor.phases, physicalModelHttpRequests: "not exposed by the public SDK factory seam; counts above are actual SDK SendAsync invocations" });
  } finally {
    monitor.dispose();
    try { await removeQuestion(backend, saved?.id); }
    finally { if (ownedSeed) await backend.api(`/design/workflows/definitions/${ownedSeed.definitionId}`, "DELETE"); }
  }
}

async function seedProcessing(backend: Backend) {
  const day = new Date(); day.setUTCHours(0, 0, 0, 0); day.setUTCDate(day.getUTCDate() - 1);
  const from = day.toISOString(); const to = new Date(day.getTime() + 86_400_000).toISOString();
  const seeds: ProcessingSeed[] = [];
  for (const [index, source] of [{ customerCategory: "Retail", durationSeconds: 60, shared: true }, { customerCategory: "Retail", durationSeconds: 180, shared: false }, { customerCategory: "Enterprise", durationSeconds: 300, shared: true }].entries()) {
    const startedAt = new Date(day.getTime() + 43_200_000 + index * 600_000).toISOString();
    const completedAt = new Date(Date.parse(startedAt) + source.durationSeconds * 1000).toISOString();
    const created = await backend.api<{ id: string }>("/_acme/processing/records", "POST", { customerCategory: source.customerCategory, startedAt, completedAt, status: "completed", readerIds: source.shared ? ["dashboard-qa-reader"] : [] });
    const observed = await backend.api<{ id: string; startedAt: string; completedAt: string; customerCategory: string }>(`/_acme/processing/records/${created.id}`);
    expect(Date.parse(observed.startedAt)).toBe(Date.parse(startedAt)); expect(Date.parse(observed.completedAt)).toBe(Date.parse(completedAt)); expect(observed.customerCategory).toBe(source.customerCategory);
    seeds.push({ ...source, id: created.id, startedAt: observed.startedAt, completedAt: observed.completedAt });
  }
  return { seeds, from, to };
}

async function shareQuestion(page: Page, info: TestInfo, capture: Capture) {
  await page.getByRole("button", { name: "Share", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: /^Share / });
  await dialog.getByLabel("Find authorized users or roles", { exact: true }).fill("dashboard-reader");
  await dialog.getByRole("button", { name: "Search audience", exact: true }).click();
  await dialog.getByRole("listitem").filter({ hasText: "dashboard-reader" }).getByRole("button", { name: "Add audience", exact: true }).click();
  await capture(page, info, "live-share");
  await dialog.getByRole("button", { name: "Save audience", exact: true }).click();
  await expect(dialog).toBeHidden();
}

async function grantProfile(backend: Backend, profile: "dashboard-live-reader" | "dashboard-live-no-category") {
  const authority = await backend.api<{ revision: string }>("/__qualification/principals/0/authority");
  await backend.api("/__qualification/principals/0/grant-profile", "POST", { profile, revision: authority.revision });
}

export async function liveAcmeDashboardJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = await dashboardBackend(page.request);
  const monitor = instrumentation(page, backend); let saved: Resource | undefined;
  let readerBrowser: Browser | undefined; let readerContext: BrowserContext | undefined;
  try {
    // The runner owns tracing for both owner and independently launched reader contexts.
    readerBrowser = await chromium.launch();
    readerContext = await readerBrowser.newContext({ baseURL: process.env.ELSA_STUDIO_URL, viewport: { width: 1440, height: 1000 }, colorScheme: "light" });
    const reader = await readerContext.newPage(); const readerErrors: string[] = [];
    reader.on("pageerror", error => readerErrors.push(error.message));
    const source = await seedProcessing(backend);
    await backend.api("/__qualification/dashboard-live/canaries", "POST", source.seeds.map(item => item.id));
    await grantProfile(backend, "dashboard-live-reader");
    await createEditor(page, `Live Acme customer duration ${Date.now()}`, "acme.processing.records");
    const turns = await askAndClarify(page, info, capture, backend,
      `For Acme processing records started from ${source.from} inclusive to ${source.to} exclusive in UTC, compare average elapsed processing time grouped by customer category, Retail and Enterprise. I am unsure whether in-progress records count as zero or whether only finished records with valid completion pairs belong to the mean. Ask me to choose first and explicitly offer the supported finished valid pairs meaning. Begin with a table, use the declared average elapsed metric, no comparison period or result conditions, and explain the source coverage limitation.`,
      /finished|valid|completed/i);
    const initial = await monitor.native("initial-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    const ownerBefore = acmeOracle(initial.definition, initial.result, source.seeds);
    expect(Date.parse(initial.definition.query.time?.from ?? "")).toBe(Date.parse(source.from));
    expect(Date.parse(initial.definition.query.time?.to ?? "")).toBe(Date.parse(source.to));
    expect(initial.definition.query.time).toMatchObject({ field: "startedAt", timeZone: "UTC" });
    await capture(page, info, "live-proposal-preview");
    await captureResultViews(page, info, capture, "live-proposal-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    const refinement = await refine(page, info, capture, backend, "Keep the exact accepted finished-valid-pairs average, customer-category grouping and explicit started interval in UTC. Change only the presentation from table to a bar chart, customer category on the category axis and average elapsed milliseconds on the value axis. Do not change or add filters, metrics or comparison periods.");
    const final = await monitor.native("refined-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    sameQuery(initial.definition, final.definition); acmeOracle(final.definition, final.result, source.seeds);
    await capture(page, info, "live-refined-preview");
    await captureResultViews(page, info, capture, "live-refined-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    saved = await saveAndReopen(page, info, capture, backend, monitor, final.definition);
    const ownerRefresh = await monitor.native("reopened-native-refresh", () => refresh(page, saved!));
    const ownerAfter = acmeOracle(final.definition, ownerRefresh, source.seeds);
    await capture(page, info, "live-refreshed");
    await monitor.native("native-share", () => shareQuestion(page, info, capture));
    // Sharing is itself a saved revision. Every subsequent read and execution uses its current revision.
    saved = await backend.api<Resource>(`/_elsa/studio/questions/${saved.id}`);
    const viewer = await monitor.native("native-independent-viewer-refresh", async () => {
      await signInDashboardReader(reader, info);
      await reader.getByRole("button", { name: "Shared with me", exact: true }).click();
      await reader.getByRole("link", { name: saved!.name, exact: true }).click();
      await expect(reader.getByRole("button", { name: "Save question", exact: true })).toHaveCount(0);
      await expect(reader.getByRole("button", { name: "Share", exact: true })).toHaveCount(0);
      const result = await refresh(reader, saved!);
      const oracle = acmeOracle(final.definition, result, source.seeds, true);
      await capture(reader, info, "live-shared-reader");
      await captureResultViews(reader, info, capture, "live-shared-reader", reader.locator(".named-preview > .named-result"));
      // Real reader authorization must withhold the owner's unshared record and preserve category grants on allowed records.
      const exchange = await reader.request.get(`${backend.base}/_elsa/identity/token`); expect(exchange.ok()).toBe(true);
      const { accessToken } = await exchange.json() as { accessToken: string }; expect(typeof accessToken).toBe("string");
      const denied = await reader.request.get(`${backend.base}/_acme/processing/records/${source.seeds.find(item => !item.shared)!.id}`, { headers: { Authorization: `Bearer ${accessToken}` } });
      expect(denied.status()).toBe(404);
      for (let index = 1; index <= result.rows.length; index++) {
        await reader.getByRole("button", { name: `View matching records for result ${index}`, exact: true }).click();
        const drill = reader.getByRole("dialog", { name: "Matching source records", exact: true });
        const category = result.rows[index - 1].values.customerCategory.value;
        for (const record of source.seeds.filter(item => item.shared && item.customerCategory === category)) await expect(drill.getByRole("cell", { name: record.id, exact: true })).toBeVisible();
        for (const record of source.seeds.filter(item => !item.shared)) await expect(drill.getByRole("cell", { name: record.id, exact: true })).toHaveCount(0);
        await drill.press("Escape");
      }
      return oracle;
    });
    await monitor.native("native-reauthenticated-field-revocation", async () => {
      await grantProfile(backend, "dashboard-live-no-category");
      // Native IAM grants are carried by a signed principal. As in the native journey,
      // observe changed grants through a new normal authenticated reader session.
      await readerContext!.clearCookies(); await signInDashboardReader(reader, info);
      await reader.goto(`/questions/${saved!.id}`);
      await expect(reader.getByText(/^Parts of this definition are withheld under your current access\./)).toBeVisible();
      await expect(reader.locator(".named-query-controls")).toHaveCount(0);
      const exchange = await reader.request.get(`${backend.base}/_elsa/identity/token`); expect(exchange.ok()).toBe(true);
      const { accessToken } = await exchange.json() as { accessToken: string }; expect(typeof accessToken).toBe("string");
      const denied = await reader.request.post(`${backend.base}/_elsa/studio/questions/${saved!.id}/execute`, { data: {}, headers: { ...tag(saved!.revision), Authorization: `Bearer ${accessToken}` } });
      expect(denied.status(), "The newly authenticated principal lacks the required business field").toBe(403);
      await expect(reader.getByRole("region", { name: "Exact result values", exact: true })).toHaveCount(0);
      await capture(reader, info, "live-reader-no-category");
      await attachDashboardJson(info, "live-authority.json", { readerReauthenticated: true, executeStatus: denied.status(), scope: "current grants in a new normal Identity principal; outstanding token invalidation is not qualified" });
      await grantProfile(backend, "dashboard-live-reader");
    });
    await savedAppearances(page, info, capture, backend, monitor, saved);
    await attachDashboardJson(info, "live-oracle.json", { sourceInterval: { from: source.from, to: source.to }, sourceRecords: source.seeds, ownerBefore, ownerAfter, viewer, sourcePersistence: "Acme reference store is ephemeral; page reload is verified, process-restart durability is not claimed", query: final.definition.query });
    await attachDashboardJson(info, "live-counters.json", { turns, refinement, after: await counters(backend), nativePhases: monitor.phases, physicalModelHttpRequests: "not exposed by the public SDK factory seam; counts above are actual SDK SendAsync invocations" });
    expect(readerErrors, "Uncaught independent reader browser errors").toEqual([]);
  } finally {
    monitor.dispose();
    try { await readerContext?.close(); }
    finally {
      try { await readerBrowser?.close(); }
      finally {
        try { await grantProfile(backend, "dashboard-live-reader"); }
        finally { await removeQuestion(backend, saved?.id); }
      }
    }
  }
}
