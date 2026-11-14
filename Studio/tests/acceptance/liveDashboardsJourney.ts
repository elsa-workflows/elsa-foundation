import * as keyboardUi from "./dashboardKeyboard";
import { readFileSync, writeFileSync } from "node:fs";
import { chromium, expect, request as playwrightRequest, type Browser, type BrowserContext, type Locator, type Page, type Request, type TestInfo } from "@playwright/test";
import type { QuestionDefinition, QueryResult } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";
import { attachDashboardJson, canonicalDashboardDefinition, dashboardBackend, signInDashboardReader, tag, type Backend, type Capture, type Resource } from "./dashboardJourneySupport";

import { executeTrendRun, seedWorkflowTrend, workflowTrendOracle, type TrendSeed } from "./liveWorkflowTrendSupport";

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
export async function captureResultViews(page: Page, info: TestInfo, capture: Capture, name: string, result: Locator) {
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
  await keyboardUi.click(page.getByRole("button", { name: "Create question", exact: true }).first());
  await keyboardUi.fill(page.getByLabel("Name", { exact: true }), name);
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
  await keyboardUi.fill(ask.getByLabel("Your question", { exact: true }), prompt);
  await expect(ask.getByRole("button", { name: "Ask Weaver", exact: true })).toBeEnabled({ timeout: 60_000 });
  const admission = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(response.url()).pathname));
  await keyboardUi.press(ask.getByRole("button", { name: "Ask Weaver", exact: true }), "Enter");
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
  await keyboardUi.press(chosen, "Enter");
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
  await keyboardUi.click(proposal.getByRole("button", { name: "Preview proposed question", exact: true }));
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
  await keyboardUi.click(proposals.last().getByRole("button", { name: "Refine proposal", exact: true }));
  const dialog = page.getByRole("dialog", { name: "Refine proposed question", exact: true });
  await expect(dialog).toBeVisible();
  await keyboardUi.fill(dialog.getByRole("textbox"), message);
  await keyboardUi.press(dialog.getByRole("button", { name: "Confirm", exact: true }), "Enter");
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
    await keyboardUi.press(page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().getByRole("button", { name: "Use this question", exact: true }), "Enter");
    await expect(page.getByRole("button", { name: "Undo applied proposal", exact: true })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "Visualization", exact: true })).toHaveValue("bar");
    expect(monitor.writes, "Use changes only the ordinary native draft").toEqual([]);
    await capture(page, info, "live-applied-draft");
  });
  const saved = await monitor.native("explicit-save", async () => {
    const backendSelection = new URL(page.url()).searchParams.get("backend");
    const pending = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/questions$/.test(response.url()));
    await keyboardUi.press(page.getByRole("button", { name: "Save question", exact: true }), "Enter");
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
  await keyboardUi.click(page.getByRole("button", { name: "Refresh results", exact: true }));
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
  const monitor = instrumentation(page, backend);
  let saved: Resource | undefined; let dashboard: Resource | undefined; let seed: TrendSeed | undefined;
  let readerBrowser: Browser | undefined; let readerContext: BrowserContext | undefined; let retained = false;
  try {
    seed = await seedWorkflowTrend(backend, `live-${Date.now()}`);
    await backend.api("/__qualification/dashboard-live/canaries", "POST", seed.facts.map(item => item.id));
    await createEditor(page, `Live weekly failure meaning ${Date.now()}`, "elsa.workflows.runs");
    const ids = seed.definitions.map(item => item.id).join(", ");
    const turns = await askAndClarify(page, info, capture, backend,
      `Use one definitionId IN filter containing ONLY workflow definitions ${ids}. Show whose failure rate increased this calendar week so far versus the same elapsed part of the previous calendar week, Monday starts in UTC. I might mean the current outcomes of runs STARTED in each period, or outcomes of COMPLETIONS during each period. Ask me to choose before proposing; explicitly offer the supported started-cohort current-status meaning. Initially group by workflow definition identity, include ALL run kinds with NO run-kind filter, use only the declared failure-rate metric with result alias rate and delta.rate greater than zero, no time buckets, and begin with a table. Explain unknown retention coverage.`,
      /started|starting|start cohort/i);
    const initial = await monitor.native("initial-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    const beforeOracle = workflowTrendOracle(initial.definition, initial.result, seed, false);
    await capture(page, info, "live-proposal-preview");
    await captureResultViews(page, info, capture, "live-proposal-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    const refinement = await refine(page, info, capture, backend,
      "Refine the actual query: group by the CURRENT FOLDER PATH (currentFolder), instead of definition identity, and exclude test runs with runKind not equal to testRun. Excluding testRun must not silently mean only publishedRun. Keep the exact two-definition restriction, started-cohort failure-rate measure, Monday calendar-week-to-date UTC window, equal-elapsed prior-week comparison, and positive delta.rate condition. Change the presentation to a bar chart for currentFolder, retaining readable current, baseline and delta failure-rate values. Do not substitute a metric or interval.");
    const final = await monitor.native("refined-native-preview", () => previewProposal(page, page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last()));
    const refinedOracle = workflowTrendOracle(final.definition, final.result, seed, true);
    expect(final.definition.query.groupBy).not.toEqual(initial.definition.query.groupBy);
    expect(final.definition.query.filters).not.toEqual(initial.definition.query.filters);
    expect(final.definition.visualization.kind).toBe("bar");
    expect(refinedOracle.expected[0].current).toBe(75); expect(refinedOracle.expected[0].baseline).toBe(25); expect(refinedOracle.expected[0].delta).toBe(50);
    expect(beforeOracle.expected[0].current).toBe(62.5); expect(beforeOracle.expected[0].delta).toBe(12.5);
    await attachDashboardJson(info, "live-query-refinement.json", { groupingChanged: true, filtersChanged: true, actualSdkRefinement: true, before: beforeOracle.expected, after: refinedOracle.expected });
    await capture(page, info, "live-refined-preview");
    await captureResultViews(page, info, capture, "live-refined-preview", page.getByRole("region", { name: "Proposed dashboard question", exact: true }).last().locator(".named-result"));
    saved = await saveAndReopen(page, info, capture, backend, monitor, final.definition);
    workflowTrendOracle(final.definition, await monitor.native("reopened-native-refresh", () => refresh(page, saved!)), seed, true);
    const fresh = await monitor.native("native-new-run-and-refresh", async () => {
      const increasing = seed!.definitions[0];
      seed!.facts.push(await executeTrendRun(backend, increasing.id, increasing.faultPublished, increasing.faultVersion, new Date().toISOString(), true));
      await backend.api("/__qualification/dashboard-live/canaries", "POST", seed!.facts.map(item => item.id));
      return refresh(page, saved!);
    });
    const afterOracle = workflowTrendOracle(final.definition, fresh, seed, true);
    expect(afterOracle.expected[0].current).toBe(80); expect(afterOracle.expected[0].delta).toBe(55);
    await capture(page, info, "live-refreshed");
    await monitor.native("native-saved-drill", () => inspectTrendDrill(page, info, capture, "live-drill", afterOracle.expected[0].currentIds, seed!));
    await savedAppearances(page, info, capture, backend, monitor, saved);
    await monitor.native("native-share", () => shareQuestion(page, info, capture, "live-question-share"));
    saved = await backend.api<Resource>(`/_elsa/studio/questions/${saved.id}`);
    dashboard = await monitor.native("native-dashboard-arrange", () => arrangeTrendDashboard(page, info, capture, backend, saved!));
    await monitor.native("native-dashboard-share", () => shareQuestion(page, info, capture, "live-dashboard-share"));
    dashboard = await backend.api<Resource>(`/_elsa/studio/dashboards/${dashboard.id}`);
    await grantProfile(backend, "dashboard-live-reader");
    readerBrowser = await chromium.launch();
    readerContext = await readerBrowser.newContext({ baseURL: process.env.ELSA_STUDIO_URL, viewport: { width: 1440, height: 1000 } });
    const reader = await readerContext.newPage();
    await monitor.native("native-independent-viewer-refresh", async () => {
      await signInDashboardReader(reader, info); await keyboardUi.startKeyboardEvidence(reader); await reader.goto(`/dashboards/${dashboard!.id}`);
      await expect(reader.getByRole("button", { name: "Save layout", exact: true })).toHaveCount(0);
      await expect(reader.locator(".named-tile .named-result")).toHaveCount(2);
      await capture(reader, info, "live-dashboard-reader");
      await inspectTrendDrill(reader, info, capture, "live-dashboard-reader-drill", afterOracle.expected[0].currentIds, seed!, reader.locator(".named-tile").first());
    });
    const manifest: RetainedTrend = { question: saved, dashboard, definition: final.definition, seed };
    writeFileSync(`${process.env.UX_ACCEPTANCE_OUT}/workflow-retained.json`, JSON.stringify(manifest, null, 2));
    await attachDashboardJson(info, "live-retained-definitions.json", { dashboardId: dashboard.id, questionId: saved.id, dashboardRevision: dashboard.revision, questionRevision: saved.revision, tileIds: dashboard.tiles.map(item => item.id), deferredToOwnedRestart: true });
    await attachDashboardJson(info, "live-oracle.json", { before: beforeOracle, refined: refinedOracle, after: afterOracle, sourceFacts: seed.facts, sourceStartInstants: "controlled real start-handler clock; no claim about historical execution duration", query: final.definition.query });
    await attachDashboardJson(info, "live-counters.json", { turns, refinement, after: await counters(backend), nativePhases: monitor.phases, physicalModelHttpRequests: "not exposed by public SDK factory; actual SDK SendAsync counts retained" });
    if (keyboardUi.keyboardOnly) await attachDashboardJson(info, "reader-keyboard-proof.json", await keyboardUi.keyboardEvidence(reader));
    retained = true;
  } finally {
    monitor.dispose();
    // Explicit context closure invokes Playwright Test's trace-export hook. Browser.close()
    // alone shuts down its transport before the independently owned context is exported.
    try { await readerContext?.close(); }
    finally {
      try { await readerBrowser?.close(); }
      finally { if (!retained) await cleanupRetainedTrend(backend, saved?.id, dashboard?.id, seed); }
    }
  }
}

type RetainedTrend = { question: Resource; dashboard: Resource; definition: QuestionDefinition; seed: TrendSeed };
async function cleanupRetainedTrend(backend: Backend, questionId?: string, dashboardId?: string, seed?: TrendSeed) {
  if (dashboardId) {
    const current = await backend.api<Resource>(`/_elsa/studio/dashboards/${dashboardId}`);
    await backend.api(`/_elsa/studio/dashboards/${dashboardId}`, "DELETE", undefined, tag(current.revision));
  }
  try { await removeQuestion(backend, questionId); }
  finally { for (const item of seed?.definitions ?? []) await backend.api(`/design/workflows/definitions/${item.id}`, "DELETE"); }
}
async function inspectTrendDrill(page: Page, info: TestInfo, capture: Capture, name: string, ids: string[], seed: TrendSeed, scope: Locator = page.locator(".named-preview")) {
  await keyboardUi.click(scope.getByRole("button", { name: "View matching records for result 1", exact: true }));
  const dialog = page.getByRole("dialog", { name: "Matching source records", exact: true });
  for (const id of ids) await expect(dialog.getByRole("cell", { name: id, exact: true })).toBeVisible();
  for (const fact of seed.facts.filter(item => !ids.includes(item.id))) await expect(dialog.getByRole("cell", { name: fact.id, exact: true })).toHaveCount(0);
  await capture(page, info, name); await keyboardUi.press(dialog, "Escape");
}
async function arrangeTrendDashboard(page: Page, info: TestInfo, capture: Capture, backend: Backend, question: Resource) {
  await page.goto("/dashboards"); await keyboardUi.click(page.getByRole("button", { name: "Create dashboard", exact: true }).first());
  await keyboardUi.fill(page.getByLabel("Name", { exact: true }), `Live arranged weekly rates ${Date.now()}`);
  for (let index = 0; index < 2; index++) {
    await keyboardUi.click(page.getByRole("button", { name: "Add saved question", exact: true }));
    const picker = page.getByRole("dialog", { name: "Add saved question", exact: true });
    await keyboardUi.fill(picker.getByLabel("Search accessible questions", { exact: true }), question.name);
    await keyboardUi.click(picker.getByRole("button", { name: "Search questions", exact: true }));
    await keyboardUi.press(picker.getByRole("button", { name: `Add ${question.name}`, exact: true }), "Enter");
  }
  await keyboardUi.selectOption(page.getByRole("combobox", { name: "Size for question 1", exact: true }), "wide");
  await keyboardUi.press(page.getByRole("button", { name: "Move question 1 later", exact: true }), "Enter");
  await expect(page.getByRole("button", { name: "Move question 2 earlier", exact: true })).toBeFocused();
  const pending = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/dashboards$/.test(response.url()));
  await keyboardUi.press(page.getByRole("button", { name: "Save layout", exact: true }), "Enter");
  const response = await pending; expect(response.ok()).toBe(true); const saved = await response.json() as Resource;
  expect(saved.tiles.map(item => item.questionId)).toEqual([question.id, question.id]); expect(saved.tiles.map(item => item.size)).toEqual(["medium", "wide"]);
  expect(new Set(saved.tiles.map(item => item.id)).size).toBe(2);
  await page.reload(); await expect(page.locator(".named-tile .named-result")).toHaveCount(2);
  expect((await backend.api<Resource>(`/_elsa/studio/dashboards/${saved.id}`)).tiles).toEqual(saved.tiles);
  await capture(page, info, "live-dashboard-arranged"); return saved;
}

export async function restartWorkflowDashboardJourney(page: Page, info: TestInfo, capture: Capture) {
  if (process.env.UX_DASHBOARD_RESTART !== "1") throw new Error("The real owned-host restart phase is required");
  const manifest = JSON.parse(readFileSync(`${process.env.UX_ACCEPTANCE_OUT}/workflow-retained.json`, "utf8")) as RetainedTrend;
  // Keep the owner's cleanup/observation client separate from the UI's later reader login.
  const ownerClient = await playwrightRequest.newContext();
  const backend = await dashboardBackend(ownerClient); const before = await counters(backend);
  expect(sends(before)).toBe(0);
  try {
    const bootstrap = await backend.api<{ data: { enabled: boolean; provider: { isAvailable: boolean } } }>("/_elsa/weaver/bootstrap");
    expect(bootstrap.data.enabled && bootstrap.data.provider.isAvailable).toBe(false);
    const question = await backend.api<Resource>(`/_elsa/studio/questions/${manifest.question.id}`);
    const dashboard = await backend.api<Resource>(`/_elsa/studio/dashboards/${manifest.dashboard.id}`);
    expect(question.revision).toBe(manifest.question.revision); expect(question.definition).toEqual(manifest.question.definition);
    expect(dashboard.revision).toBe(manifest.dashboard.revision); expect(dashboard.tiles).toEqual(manifest.dashboard.tiles);
    await page.goto(`/dashboards/${dashboard.id}`); await expect(page.locator(".named-tile .named-result")).toHaveCount(2);
    await expect(page.getByRole("combobox", { name: "Size for question 2", exact: true })).toHaveValue("wide");
    await capture(page, info, "restart-dashboard-owner");
    await page.goto(`/questions/${question.id}`);
    const result = await refresh(page, question); const oracle = workflowTrendOracle(manifest.definition, result, manifest.seed, true);
    await captureResultViews(page, info, capture, "restart-question", page.locator(".named-preview > .named-result"));
    await inspectTrendDrill(page, info, capture, "restart-owner-drill", oracle.expected[0].currentIds, manifest.seed);
    await page.context().clearCookies(); await signInDashboardReader(page, info); await page.goto(`/dashboards/${dashboard.id}`);
    await expect(page.getByRole("button", { name: "Save layout", exact: true })).toHaveCount(0);
    await expect(page.locator(".named-tile .named-result")).toHaveCount(2);
    await capture(page, info, "restart-dashboard-reader");
    await inspectTrendDrill(page, info, capture, "restart-reader-drill", oracle.expected[0].currentIds, manifest.seed, page.locator(".named-tile").first());
    const after = await counters(backend); expect(sends(after)).toBe(0);
    await attachDashboardJson(info, "restart-proof.json", { providerDisabled: true, sameQuestion: true, sameDashboard: true, sameTileOrderAndSizes: true, distinctHostLifetimes: true, sdkSendsBefore: sends(before), sdkSendsAfter: sends(after), independentlyComputed: oracle.expected, questionId: question.id, dashboardId: dashboard.id });
  } finally {
    try { await cleanupRetainedTrend(backend, manifest.question.id, manifest.dashboard.id, manifest.seed); }
    finally { await ownerClient.dispose(); }
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

async function shareQuestion(page: Page, info: TestInfo, capture: Capture, captureName = "live-share") {
  await keyboardUi.click(page.getByRole("button", { name: "Share", exact: true }));
  const dialog = page.getByRole("dialog", { name: /^Share / });
  await keyboardUi.fill(dialog.getByLabel("Find authorized users or roles", { exact: true }), "dashboard-reader");
  await keyboardUi.click(dialog.getByRole("button", { name: "Search audience", exact: true }));
  await keyboardUi.click(dialog.getByRole("listitem").filter({ hasText: "dashboard-reader" }).getByRole("button", { name: "Add audience", exact: true }));
  await capture(page, info, captureName);
  // Sharing changes the dashboard revision and starts its ordinary two-wide tile
  // refresh. Observe both completed reads before adding an independent API readback;
  // a third concurrent read correctly exceeds the native per-subject admission bound.
  const tileIds = await page.locator(".named-tile[data-tile-id]").evaluateAll(nodes => nodes.map(node => node.getAttribute("data-tile-id")!));
  const refreshed = tileIds.map(id => page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith(`/tiles/${id}/execute`)));
  await keyboardUi.click(dialog.getByRole("button", { name: "Save audience", exact: true }));
  await expect(dialog).toBeHidden();
  for (const response of await Promise.all(refreshed)) expect(response.ok()).toBe(true);
  await expect(page.getByRole("button", { name: /^Cancel refresh for question / })).toHaveCount(0);
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
      await signInDashboardReader(reader, info); await keyboardUi.startKeyboardEvidence(reader);
      await keyboardUi.click(reader.getByRole("button", { name: "Shared with me", exact: true }));
      await keyboardUi.click(reader.getByRole("link", { name: saved!.name, exact: true }));
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
        await keyboardUi.click(reader.getByRole("button", { name: `View matching records for result ${index}`, exact: true }));
        const drill = reader.getByRole("dialog", { name: "Matching source records", exact: true });
        const category = result.rows[index - 1].values.customerCategory.value;
        for (const record of source.seeds.filter(item => item.shared && item.customerCategory === category)) await expect(drill.getByRole("cell", { name: record.id, exact: true })).toBeVisible();
        for (const record of source.seeds.filter(item => !item.shared)) await expect(drill.getByRole("cell", { name: record.id, exact: true })).toHaveCount(0);
        await keyboardUi.press(drill, "Escape");
      }
      return oracle;
    });
    await monitor.native("native-reauthenticated-field-revocation", async () => {
      await grantProfile(backend, "dashboard-live-no-category");
      // Native IAM grants are carried by a signed principal. As in the native journey,
      // observe changed grants through a new normal authenticated reader session.
      await readerContext!.clearCookies(); await signInDashboardReader(reader, info); await keyboardUi.startKeyboardEvidence(reader);
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
    if (keyboardUi.keyboardOnly) await attachDashboardJson(info, "reader-keyboard-proof.json", await keyboardUi.keyboardEvidence(reader));
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

/** An actual provider must explain unavailable business facts without substituting a metric. */
export async function liveUnsupportedDashboardJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = await dashboardBackend(page.request); const monitor = instrumentation(page, backend);
  const queries: string[] = [];
  const collect = (request: Request) => {
    if (request.method() === "POST" && /\/(dashboard-queries\/preview|questions\/[^/]+\/(?:preview|execute))$/.test(new URL(request.url()).pathname)) queries.push(request.url());
  };
  page.on("request", collect);
  try {
    const beforeResources = await backend.api<{ items: Resource[] }>("/_elsa/studio/questions");
    await createEditor(page, `Unsupported financial meaning ${Date.now()}`, "acme.processing.records");
    const prompt = "For Acme processing records, calculate net profit per customer category using revenue minus cost. If revenue, cost or profit are not exposed by the declared dataset, explain that limitation and preserve this request. Do not replace financial profit with processing time or record counts. Do not invent numbers.";
    const before = await counters(backend); const ask = page.getByRole("region", { name: "Ask Weaver", exact: true });
    await keyboardUi.fill(ask.getByLabel("Your question", { exact: true }), prompt);
    await ask.scrollIntoViewIfNeeded(); await capture(page, info, "live-unsupported-request");
    await expect(ask.getByRole("button", { name: "Ask Weaver", exact: true })).toBeEnabled({ timeout: 60_000 });
    const admission = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(response.url()).pathname));
    await keyboardUi.press(ask.getByRole("button", { name: "Ask Weaver", exact: true }), "Enter");
    expect((await admission).ok()).toBe(true); await idle(page);
    const assistant = page.getByRole("region", { name: "Weaver assistant", exact: true });
    await expect(assistant.locator('.weaver-message[data-role="user"]').filter({ hasText: prompt })).toBeVisible();
    const explanation = assistant.locator('.weaver-message[data-role="assistant"][data-status="completed"]').last();
    await expect(explanation).toContainText(/profit|revenue|cost/i);
    await expect(explanation).toContainText(/not (?:exposed|available|provided|include)|does(?:n't| not)|cannot|can(?:not|'t|’t)|unavailable|unsupported|missing/i);
    await expect(page.getByRole("region", { name: "Proposed dashboard question", exact: true })).toHaveCount(0);
    await expect(page.getByRole("combobox", { name: "Dataset", exact: true })).toHaveValue("");
    await expect(page.getByRole("group", { name: "Metrics", exact: true }).getByRole("combobox")).toHaveCount(0);
    await expect(page.locator(".named-result")).toHaveCount(0);
    await explanation.scrollIntoViewIfNeeded(); await expect(explanation).toBeInViewport();
    await capture(page, info, "live-unsupported-explanation");
    const userMessage = assistant.locator('.weaver-message[data-role="user"]').filter({ hasText: prompt });
    await userMessage.scrollIntoViewIfNeeded(); await capture(page, info, "live-unsupported-preserved-question");
    const after = await counters(backend);
    expect(sends(after)).toBeGreaterThan(sends(before));
    expect(after.counts["event.Completed"] ?? 0).toBeGreaterThan(before.counts["event.Completed"] ?? 0);
    expect(queries).toEqual([]); expect(monitor.writes).toEqual([]);
    const afterResources = await backend.api<{ items: Resource[] }>("/_elsa/studio/questions");
    expect(afterResources.items.map(item => [item.id, item.revision])).toEqual(beforeResources.items.map(item => [item.id, item.revision]));
    await attachDashboardJson(info, "unsupported-proof.json", { before, after, originalRequestPreserved: true, limitationTextObserved: true, proposedQuestions: 0, nativeQueryExecutions: queries.length, resourceWrites: monitor.writes.length, savedResourcesUnchanged: true, selectedDatasetStillEmpty: true, structuredMetricSubstitution: false, explanation: await explanation.innerText(), proseSemanticAcceptance: "requires independent review" });
  } finally { page.off("request", collect); monitor.dispose(); }
}
