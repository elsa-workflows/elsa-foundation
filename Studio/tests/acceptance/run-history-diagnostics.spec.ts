import { readFileSync, renameSync, writeFileSync } from "node:fs";
import { expect, test, type APIRequestContext, type Browser, type Locator, type Page, type Response, type TestInfo } from "@playwright/test";
import type { RuntimeHistoryBookmark, RuntimeHistoryBookmarkPage, RuntimeHistoryEvent, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { ActivityExecutionInspection, WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const directory = process.env.UX_ACCEPTANCE_DIAGNOSTICS_DIR;
if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !directory) throw new Error("Diagnostics require the private owned-host supervisor.");
type Account = { userName: string; password: string; profile: string };
type Acknowledgement = { id: string; status: string; sourceSha: string; backendUrl: string; studioUrl: string; receiptFile: string; accounts?: Account[];
  retention?: { workflowExecutionId: string; purgedThrough: number };
  expiry?: { workflowExecutionId: string; activityExecutionId: string; bookmarkId: string; lastSequence: number; expiresAt: string;
    fixtureKind: string; stockAuthorableExpiry: boolean; producer: string; physicalCommitId: string; checkpointId: string; fencingToken: number } };
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: unknown } };
type Publication = { artifactId: string; sourceReferenceId: string };
type Task = { id: string; workflowExecutionId: string; title: string; status: string; revision: number };
const types = {
  sequence: "Elsa.Activities.Sequence.Activities.Sequence", each: "Elsa.Activities.ForEach.Activities.ForEach",
  parallel: "Elsa.Activities.Parallel.Activities.Parallel", task: "Elsa.Activities.Primitives.Activities.CreateUserTask",
  inline: "Elsa.Activities.Primitives.Activities.Inline", event: "Elsa.Activities.Primitives.Activities.Event",
  compensation: "Elsa.Activities.Compensation.Activities.CompensationScope", fault: "Elsa.Activities.Primitives.Activities.Fault"
};
async function operation(info: TestInfo, operation: string, values: Record<string, unknown> = {}) {
  const id = crypto.randomUUID(), temporary = `${directory}/${id}.tmp`;
  writeFileSync(temporary, JSON.stringify({ id, project: info.project.name, operation, ...values })); renameSync(temporary, `${directory}/request.json`);
  let answer: Acknowledgement | undefined;
  await expect.poll(() => {
    let contents: string; try { contents = readFileSync(`${directory}/ack.json`, "utf8"); } catch { return "pending"; }
    const ack = JSON.parse(contents) as Acknowledgement;
    if (ack.id !== id) return "pending"; answer = ack; return ack.status;
  }, { timeout: 180_000 }).toBe("ready");
  expect(answer!.sourceSha).toBe(process.env.UX_SOURCE_SHA); return answer!;
}
async function api<T>(request: APIRequestContext, backend: string, path: string, data?: unknown, method: "get" | "post" | "put" = data === undefined ? "get" : "post", timeout = 30_000) {
  const response = await request[method](`${backend}${path}`, data === undefined ? { timeout } : { data, timeout });
  expect(response.ok(), `${response.status()} ${path}: ${await response.text()}`).toBe(true); return await response.json() as T;
}
async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const png = info.outputPath(`${name}.png`); await page.screenshot({ path: png, fullPage: true }); await info.attach(`${name}.png`, { path: png, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`); writeFileSync(aria, await page.locator("body").ariaSnapshot()); await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}
async function signIn(page: Page, info: TestInfo, userName = "admin", password = "Password123!") {
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, info.project.use.colorScheme ?? "light");
  await page.goto("/workflows/instances");
  const login = page.locator('form[action="/_elsa/identity/login"]');
  await login.getByLabel(/username/i).fill(userName); await login.getByLabel(/password/i).fill(password); await login.getByRole("button", { name: /sign in/i }).press("Enter");
  await expect(login).toHaveCount(0); await expect(page.getByRole("button", { name: /^Account:/ })).toBeVisible({ timeout: 60_000 });
}
function row(scope: Locator, event: RuntimeHistoryEvent) { return scope.locator(`[data-history-event-id=${JSON.stringify(event.historyEventId)}]`); }
async function rowIds(scope: Locator) { return scope.locator("[data-history-event-id]").evaluateAll(items => items.map(item => item.getAttribute("data-history-event-id"))); }
const inspectorOf = (page: Page) => page.getByRole("complementary", { name: "Run details panel", exact: true });
const timelineOf = (page: Page) => page.getByRole("region", { name: "Run timeline", exact: true });
async function ensureSmallEvent(timeline: Locator, event: RuntimeHistoryEvent) {
  await expect(timeline.locator("[data-history-event-id]").first()).toBeVisible();
  for (let index = 0; index < 6; index++) {
    if (await row(timeline, event).count()) return;
    const more = timeline.getByRole("button", { name: "Load more history", exact: true });
    await expect(more).toBeEnabled(); const before = await timeline.locator(".wf-history-count").textContent(); await more.press("Enter");
    await expect.poll(() => timeline.locator(".wf-history-count").textContent()).not.toBe(before);
  }
  throw new Error("Small fixture target exceeded six bounded UI pages.");
}
async function smallHistory(page: Page, backend: string, run: string) {
  const items: RuntimeHistoryEvent[] = []; let cursor: string | null | undefined;
  for (let index = 0; index < 6; index++) {
    const history = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${run}/history?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`);
    expect(history.items.length).toBeLessThanOrEqual(100); items.push(...history.items); cursor = history.nextContinuationToken;
    if (!cursor) return { items, coverage: history.coverage };
  }
  throw new Error("Small native diagnostics exceeded its six-page fixture budget; never use this collector for the long run.");
}
async function activeBookmarks(page: Page, backend: string, run: string) {
  return api<RuntimeHistoryBookmarkPage>(page.request, backend, `/runtime/workflows/instances/${run}/history/bookmarks?status=Active&limit=100`);
}
async function waitTasks(page: Page, backend: string, run: string, count: number) {
  let tasks: Task[] = [];
  await expect.poll(async () => {
    const value = await api<{ items: Task[] }>(page.request, backend, "/runtime/user-tasks?view=mine&limit=100");
    tasks = value.items.filter(item => item.workflowExecutionId === run && ["Open", "Claimed"].includes(item.status));
    if (tasks.length !== count) {
      const observed = await api<WorkflowInstanceDetails>(page.request, backend, `/runtime/workflows/instances/${run}`);
      if (["Completed", "Faulted", "Cancelled"].includes(observed.instance.status)) {
        const incidents = observed.incidents?.map(item => ({ incidentId: item.incidentId, status: item.status, failureType: item.failureType }));
        throw new Error(`Task fixture ${run} became ${observed.instance.status} with ${tasks.length}/${count} open tasks; incidents: ${JSON.stringify(incidents ?? [])}`);
      }
    }
    return tasks.length;
  }, { timeout: 300_000 }).toBe(count); return tasks;
}
async function complete(page: Page, backend: string, task: Task) {
  return api(page.request, backend, `/runtime/user-tasks/${task.id}/complete`, { revision: task.revision, requestId: crypto.randomUUID(), action: "Approve", data: {} });
}
async function selectedRun(page: Page, run: string, owner: string, extra = "") {
  await page.goto(`/workflows/instances/${run}?selected=${encodeURIComponent(owner)}${extra}`);
  await expect(inspectorOf(page).getByRole("heading", { name: "CreateUserTask", exact: true })).toBeVisible();
}
async function freshReader(browser: Browser, info: TestInfo, studio: string, account: Account) {
  const context = await browser.newContext({ baseURL: studio, viewport: info.project.use.viewport, colorScheme: info.project.use.colorScheme });
  const page = await context.newPage(); await signIn(page, info, account.userName, account.password); return { context, page };
}

test("diagnose exact executions, long history, retention and restricted access", async ({ page, browser }, info) => {
  const start = await operation(info, "start"), backend = start.backendUrl;
  const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
  await signIn(page, info);
  const versions = new Map<string, string>();
  for (const key of Object.values(types)) {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(page.request, backend, `/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const item = catalog.items.find(item => item.definition.activityTypeKey === key); expect(item, key).toBeTruthy(); versions.set(key, item!.definition.headVersionId);
  }
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const node = (nodeId: string, type: string, inputs: Record<string, unknown> = {}, structure?: { kind: string; payload: unknown }): Node => ({
    nodeId, activityVersionId: versions.get(type)!, inputs: Object.entries(inputs).map(([key, value]) => literal(key, value)), outputs: [],
    ...(structure ? { structure: { ...structure, schemaVersion: "1.0.0" } } : {})
  });
  const sequence = (id: string, activities: Node[]) => node(id, types.sequence, {}, { kind: "elsa.sequence.structure", payload: { activities } });
  async function publish(rootActivity: Node, name: string): Promise<Publication> {
    const definition = await api<{ version: { id: string } }>(page.request, backend, "/design/workflows/definitions/submit", {
      name: `${name} ${info.project.name}`, description: "Owned normal run-history diagnostics fixture.", state: { variables: [], inputs: [], outputs: [], workflowActivityOptions: null, strategyOptions: null, rootActivity }
    });
    return api(page.request, backend, `/publishing/workflows/${definition.version.id}/publish`, {});
  }
  async function execute(publication: Publication, timeout = 30_000) {
    const run = await api<{ workflowExecutionId: string }>(page.request, backend, `/runtime/workflows/executables/${publication.artifactId}/execute`, { sourceReferenceId: publication.sourceReferenceId }, "post", timeout); return run.workflowExecutionId;
  }
  const actor = await api<{ userId: string }>(page.request, backend, "/runtime/user-task-identity");
  const task = (id: string) => node(id, types.task, { Title: "Diagnostic review", AssigneeId: actor.userId, Actions: ["Approve"] });
  const repeated = node("repeat", types.each, { Collection: [1, 2] }, { kind: "elsa.foreach.structure", payload: { body: task("repeated-task") } });
  const parallel = node("parallel", types.parallel, {}, { kind: "elsa.parallel.structure", payload: { branches: [{ name: "left", activity: task("left-task") }, { name: "right", activity: task("right-task") }] } });
  const run = await execute(await publish(sequence("scope-root", [repeated, parallel]), "Repeated and parallel diagnostics"));
  let tasks = await waitTasks(page, backend, run, 1);
  const first = (await activeBookmarks(page, backend, run)).items[0]; expect(first).toBeTruthy();
  const firstOwner = first.bookmark.identity.activityExecutionId;
  await selectedRun(page, run, firstOwner);
  await complete(page, backend, tasks[0]); tasks = await waitTasks(page, backend, run, 1);
  const second = (await activeBookmarks(page, backend, run)).items.find(item => item.bookmark.identity.activityExecutionId !== firstOwner); expect(second).toBeTruthy();
  await expect(inspectorOf(page)).toContainText("Completed", { timeout: 30_000 });
  expect(new URL(page.url()).searchParams.get("selected")).toBe(firstOwner);
  const firstJournal = inspectorOf(page).getByRole("list", { name: "Activity journal events", exact: true });
  await expect(firstJournal.locator(`[data-history-event-id=${JSON.stringify(first.createdHistoryEventId)}]`)).toBeVisible();
  const secondEvents = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${run}/history?activityExecutionId=${encodeURIComponent(second!.bookmark.identity.activityExecutionId)}&limit=100`);
  const firstJournalIds = await rowIds(firstJournal);
  for (const event of secondEvents.items) expect(firstJournalIds).not.toContain(event.historyEventId);
  await firstJournal.locator("[data-history-event-id]").first().scrollIntoViewIfNeeded(); await capture(page, info, "repeated-execution");
  await complete(page, backend, tasks[0]); tasks = await waitTasks(page, backend, run, 2);
  let branches: RuntimeHistoryBookmark[] = [];
  await expect.poll(async () => { branches = (await activeBookmarks(page, backend, run)).items; return branches.length; }).toBe(2);
  await selectedRun(page, run, branches[0].bookmark.identity.activityExecutionId);
  const branchJournal = inspectorOf(page).getByRole("list", { name: "Activity journal events", exact: true });
  await expect(branchJournal.locator(`[data-history-event-id=${JSON.stringify(branches[0].createdHistoryEventId)}]`)).toBeVisible(); await expect(inspectorOf(page).getByRole("checkbox", { name: "Include descendants", exact: true })).not.toBeChecked();
  const sibling = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${run}/history?activityExecutionId=${encodeURIComponent(branches[1].bookmark.identity.activityExecutionId)}&limit=100`);
  const branchJournalIds = await rowIds(branchJournal);
  for (const event of sibling.items) expect(branchJournalIds).not.toContain(event.historyEventId);
  await branchJournal.locator("[data-history-event-id]").first().scrollIntoViewIfNeeded();
  await capture(page, info, "parallel-execution");
  const childCreation = await api<{ event: RuntimeHistoryEvent }>(page.request, backend, `/runtime/workflows/instances/${run}/history/events/${encodeURIComponent(branches[0].createdHistoryEventId!)}`);
  expect(childCreation.event.parentActivityExecutionId).toBeTruthy();
  const structuralHistory = await smallHistory(page, backend, run);
  const parallelOwner = structuralHistory.items.find(event => event.executableNodeId === "parallel" && event.activityExecutionId)?.activityExecutionId;
  expect(parallelOwner).toBeTruthy();
  await page.goto(`/workflows/instances/${run}?selected=${encodeURIComponent(parallelOwner!)}`);
  const descendants = inspectorOf(page).getByRole("checkbox", { name: "Include descendants", exact: true });
  await expect(descendants).not.toBeChecked(); await descendants.check();
  for (const branch of branches) await expect(inspectorOf(page).locator(`[data-history-event-id=${JSON.stringify(branch.createdHistoryEventId)}]`)).toBeVisible();
  for (const item of tasks) await complete(page, backend, item);
  let finished: Awaited<ReturnType<typeof smallHistory>> | undefined;
  await expect.poll(async () => { finished = await smallHistory(page, backend, run); return finished.items.some(event => event.name === "WorkflowCompleted"); }, { timeout: 30_000 }).toBe(true);
  await page.goto(`/workflows/instances/${run}?selected=${encodeURIComponent(firstOwner)}`);
  await expect(page.locator('[aria-label="Run history summary"]')).toContainText("Completed");
  const completedEvent = finished!.items.find(event => event.name === "WorkflowCompleted")!;
  await ensureSmallEvent(timelineOf(page), completedEvent);
  await row(timelineOf(page), completedEvent).scrollIntoViewIfNeeded(); await expect(row(timelineOf(page), completedEvent)).toBeInViewport(); await capture(page, info, "completed-run");

  // Control only browser transport latency; the pending request and subsequent body come from the normal API.
  const transport = await page.context().newCDPSession(page);
  const loadingResponse = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.ok() && url.pathname.endsWith(`/runtime/workflows/instances/${run}/history`)
      && url.searchParams.get("category") === "WorkflowLifecycle" && !url.searchParams.has("cursor") && !url.searchParams.has("activityExecutionId");
  }, { timeout: 60_000 }).then(response => ({ ok: true as const, response }), error => ({ ok: false as const, error }));
  try {
    await transport.send("Network.enable");
    await transport.send("Network.emulateNetworkConditions", { offline: false, latency: 3000, downloadThroughput: -1, uploadThroughput: -1 });
    // Change a real server query on the mounted workbench; shell-module bootstrap is outside this capture.
    await timelineOf(page).getByRole("combobox", { name: "History category", exact: true }).selectOption("WorkflowLifecycle");
    const loading = timelineOf(page).getByText("Loading recorded history…", { exact: true });
    await expect(loading).toBeVisible({ timeout: 60_000 }); await expect(timelineOf(page)).toHaveAttribute("aria-busy", "true");
    await loading.scrollIntoViewIfNeeded(); await capture(page, info, "loading-history");
    const received = await loadingResponse;
    if (!received.ok) throw received.error;
    const loaded = await received.response.json() as RuntimeHistoryPage;
    expect(loaded.items.length).toBeGreaterThan(0); expect(loaded.items.every(event => event.category === "WorkflowLifecycle")).toBe(true);
    await expect(timelineOf(page).locator("[data-history-event-id]").first()).toBeVisible();
  } finally {
    await transport.send("Network.emulateNetworkConditions", { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }); await transport.detach();
  }
  await timelineOf(page).getByRole("combobox", { name: "History category", exact: true }).selectOption("");

  // A stock structural activity genuinely resolves a child fault; no closed incident is seeded.
  const recovered = await execute(await publish(node("absorbing-scope", types.compensation, { faultAfterCompensation: false }, {
    kind: "elsa.compensation-scope.structure", payload: { steps: [{ name: "Observed failure", activity: node("absorbed-fault", types.fault, { message: "Owned diagnostic structural fault" }) }] }
  }), "Resolved historical fault diagnostics"));
  let recoveredHistory: Awaited<ReturnType<typeof smallHistory>> | undefined, recoveredDetails: WorkflowInstanceDetails | undefined;
  await expect.poll(async () => {
    recoveredHistory = await smallHistory(page, backend, recovered);
    recoveredDetails = await api(page.request, backend, `/runtime/workflows/instances/${recovered}`);
    return recoveredHistory.items.some(event => event.name === "IncidentResolved") && recoveredDetails!.instance.activeIncidentCount === 0;
  }, { timeout: 30_000 }).toBe(true);
  const historicalFault = recoveredHistory!.items.find(event => event.name === "ActivityFaulted")!;
  const resolved = recoveredHistory!.items.find(event => event.name === "IncidentResolved")!;
  expect(historicalFault).toBeTruthy(); expect(recoveredDetails!.incidents?.some(item => item.incidentId === resolved.incidentId && item.status === "Resolved")).toBe(true);
  await page.goto(`/workflows/instances/${recovered}?selected=${encodeURIComponent(historicalFault.activityExecutionId!)}`);
  await expect(page.getByRole("button", { name: /No active incidents/ })).toBeVisible();
  await row(timelineOf(page), historicalFault).scrollIntoViewIfNeeded(); await capture(page, info, "historical-fault");
  await row(timelineOf(page), resolved).press("Enter"); await inspectorOf(page).getByRole("button", { name: "Inspect incident →", exact: true }).press("Enter");
  await expect(inspectorOf(page)).toContainText("Resolved"); await capture(page, info, "resolved-incident");

  const longRun = await execute(await publish(sequence("long-root", [node("warmup", types.each, { Collection: Array(1000).fill(0) }, { kind: "elsa.foreach.structure", payload: { body: node("long-inline", types.inline, { Expression: 0 }) } }), task("late-task")]), "Long retained history diagnostics"), 300_000);
  const lateTasks = await waitTasks(page, backend, longRun, 1);
  const headPage = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${longRun}/history?limit=1`);
  expect(headPage.coverage.lastSequence).toBeGreaterThanOrEqual(10_000);
  const late = (await activeBookmarks(page, backend, longRun)).items[0]; expect(late.firstRecordedSequence).toBeGreaterThan(10_000);
  const prefix = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${longRun}/history?limit=100`);
  expect(prefix.items.length).toBeLessThanOrEqual(100); expect(prefix.nextContinuationToken).toBeTruthy();
  const earlier = prefix.items.find(event => event.executableNodeId === "long-inline" && event.name === "ActivityCompleted" && event.activityExecutionId)!; expect(earlier).toBeTruthy();
  const observedIds = new Set<string>(), pageReads: Promise<void>[] = [], pageSizes: number[] = [];
  const observeHistory = (response: Response) => {
    if (!response.ok() || !new URL(response.url()).pathname.endsWith(`/runtime/workflows/instances/${longRun}/history`)) return;
    // An aborted navigation/read delivered no row body; successful bodies remain individually bounded.
    pageReads.push(response.json().then((value: RuntimeHistoryPage) => { pageSizes.push(value.items.length); for (const event of value.items) observedIds.add(event.historyEventId); }).catch(() => {}));
  };
  page.on("response", observeHistory);
  await page.goto(`/workflows/instances/${longRun}?selected=${encodeURIComponent(earlier.activityExecutionId!)}&bookmark=${encodeURIComponent(late.bookmark.identity.bookmarkId)}`);
  await expect(inspectorOf(page).getByRole("region", { name: "Bookmark lifecycle", exact: true })).toBeVisible();
  await expect(inspectorOf(page)).toContainText("current eligibility: Eligible");
  const lateEligibility = inspectorOf(page).getByText("Active · current eligibility: Eligible", { exact: true });
  await lateEligibility.scrollIntoViewIfNeeded(); await expect(lateEligibility).toBeInViewport(); await capture(page, info, "long-exact-bookmark");
  await inspectorOf(page).getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  const longTimeline = timelineOf(page); await row(longTimeline, earlier).press("Enter");
  await inspectorOf(page).getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter"); await expect(row(longTimeline, earlier)).toBeFocused();
  await complete(page, backend, lateTasks[0]);
  const newEvents = longTimeline.getByRole("button", { name: /new recorded events · Show/ });
  await expect(newEvents).toBeVisible({ timeout: 30_000 }); expect(new URL(page.url()).searchParams.get("selected")).toBe(earlier.activityExecutionId);
  await expect(row(longTimeline, earlier)).toBeFocused(); await newEvents.scrollIntoViewIfNeeded(); await capture(page, info, "long-new-events"); await newEvents.press("Enter");
  const gap = longTimeline.getByText(/Earlier snapshot history is not fully loaded/); await gap.scrollIntoViewIfNeeded(); await expect(gap).toBeInViewport(); await capture(page, info, "long-unloaded-range");
  await Promise.all(pageReads); expect(pageSizes.length).toBeGreaterThan(0); expect(Math.max(...pageSizes)).toBeLessThanOrEqual(100); expect(observedIds.size).toBeLessThan(1000);
  page.off("response", observeHistory);

  const protectedValue = `protected-history-${crypto.randomUUID()}`;
  const protectedRun = await execute(await publish(node("private-input", types.inline, { Expression: protectedValue }), "Protected value diagnostics"));
  let protectedHistory: Awaited<ReturnType<typeof smallHistory>> | undefined;
  await expect.poll(async () => { protectedHistory = await smallHistory(page, backend, protectedRun); return protectedHistory.items.some(event => event.name === "WorkflowCompleted"); }).toBe(true);
  const protectedEvent = protectedHistory!.items.find(event => event.name === "ValueCaptured")!;
  const protectedEvidenceId = (JSON.parse(protectedEvent.metadata["history.valueEvidenceIds"]) as string[])[0];
  const original = await api<{ requested: { scope: string; defaultLevel: string; subjectOverrides: Record<string, string> } }>(page.request, backend, "/runtime/workflows/diagnostics/settings");
  try {
    await api(page.request, backend, "/runtime/workflows/diagnostics/settings", { scope: "host-default", defaultLevel: "Metadata", subjectOverrides: {} }, "put");
    const metadataRun = await execute(await publish(node("metadata-input", types.inline, { Expression: protectedValue }), "Metadata capture diagnostics"));
    let metadataHistory: Awaited<ReturnType<typeof smallHistory>> | undefined;
    await expect.poll(async () => { metadataHistory = await smallHistory(page, backend, metadataRun); return metadataHistory.items.some(event => event.name === "ValueCaptured"); }).toBe(true);
    const event = metadataHistory!.items.find(event => event.name === "ValueCaptured")!;
    await page.goto(`/workflows/instances/${metadataRun}?selected=${encodeURIComponent(event.activityExecutionId!)}&historyEvent=${encodeURIComponent(event.historyEventId)}`);
    await inspectorOf(page).getByRole("button", { name: "Inspect recorded value →", exact: true }).first().press("Enter");
    const value = inspectorOf(page).getByRole("region", { name: "Recorded value Data", exact: true });
    await expect(value).toContainText(/metadata.only/i); await expect(value).not.toContainText(protectedValue); await value.scrollIntoViewIfNeeded(); await capture(page, info, "metadata-capture");
  } finally { await api(page.request, backend, "/runtime/workflows/diagnostics/settings", original.requested, "put"); }

  const partialRun = await execute(await publish(node("retention-input", types.inline, { Expression: "Retained suffix" }), "Explicit retention diagnostics"));
  let partialBefore: Awaited<ReturnType<typeof smallHistory>> | undefined;
  await expect.poll(async () => { partialBefore = await smallHistory(page, backend, partialRun); return partialBefore.items.some(event => event.name === "WorkflowCompleted"); }).toBe(true);
  const expiredTemplate = await publish(node("legacy-wait", types.event, { EventName: `native-expiry-${crypto.randomUUID()}`, CanStartWorkflow: false }), "Native legacy wait port fixture (stock authoring has no expiry)");
  const prepared = await operation(info, "prepare", { completedRun: partialRun, throughSequence: partialBefore!.items[0].sequence, expiryTemplateArtifactId: expiredTemplate.artifactId });
  await info.attach("native-preparation", { path: prepared.receiptFile, contentType: "application/json" });
  expect(prepared.expiry?.fixtureKind).toBe("native-legacy-wait-command"); expect(prepared.expiry?.stockAuthorableExpiry).toBe(false);
  expect(prepared.expiry?.producer).toBe("WorkflowCreateBookmarkSchedulerWorkHandler"); expect(prepared.expiry?.physicalCommitId).toBeTruthy(); expect(prepared.expiry?.fencingToken).toBeGreaterThanOrEqual(0);
  await page.goto(`/workflows/instances/${partialRun}`);
  await expect(timelineOf(page)).toContainText("Partial recorded history.");
  await timelineOf(page).getByText("Recording coverage", { exact: true }).press("Enter");
  await timelineOf(page).getByText("Purged through", { exact: true }).scrollIntoViewIfNeeded(); await capture(page, info, "partial-history");
  await page.goto(`/workflows/instances/${partialRun}?historyEvent=${encodeURIComponent(partialBefore!.items[0].historyEventId)}`);
  await expect(timelineOf(page)).toContainText("Partial recorded history.");
  await expect(inspectorOf(page)).toContainText(/unavailable|no longer retained/i);
  const purgedTarget = inspectorOf(page).getByRole("region", { name: "History event details", exact: true }).getByRole("status");
  await purgedTarget.scrollIntoViewIfNeeded(); await expect(purgedTarget).toBeInViewport(); await capture(page, info, "purged-exact-event");
  const expiry = prepared.expiry!;
  await page.goto(`/workflows/instances/${expiry.workflowExecutionId}?selected=${encodeURIComponent(expiry.activityExecutionId)}&bookmark=${encodeURIComponent(expiry.bookmarkId)}`);
  await expect(inspectorOf(page)).toContainText("Active · current eligibility: Expired", { timeout: 30_000 });
  await expect(inspectorOf(page)).toContainText("It does not wake, time out or cancel the activity.");
  const expiryReply = await api<{ bookmark: RuntimeHistoryBookmark }>(page.request, backend, `/runtime/workflows/instances/${expiry.workflowExecutionId}/history/bookmarks/${encodeURIComponent(expiry.bookmarkId)}`);
  expect(expiryReply.bookmark.status).toBe("Active"); expect(expiryReply.bookmark.eligibility).toBe("expired");
  expect(Date.parse(expiryReply.bookmark.eligibilityObservedAt)).toBeGreaterThanOrEqual(Date.parse(expiry.expiresAt));
  const expiryOwner = await api<ActivityExecutionInspection>(page.request, backend, `/runtime/workflows/instances/${expiry.workflowExecutionId}/activity-executions/${encodeURIComponent(expiry.activityExecutionId)}`);
  expect(expiryOwner.status).toBe("Suspended");
  const expiredObserved = await api<RuntimeHistoryPage>(page.request, backend, `/runtime/workflows/instances/${expiry.workflowExecutionId}/history?limit=100`);
  expect(expiredObserved.coverage.lastSequence).toBe(expiry.lastSequence); expect(expiredObserved.items.some(event => /Consumed|Cancelled|Timeout/.test(event.name))).toBe(false);
  await inspectorOf(page).getByText("Expiry makes this handle unmatchable.", { exact: false }).scrollIntoViewIfNeeded(); await capture(page, info, "expired-native-bookmark");

  for (const profile of ["metadata", "payload-denied", "denied"]) {
    const account = prepared.accounts!.find(account => account.profile === profile)!;
    const reader = await freshReader(browser, info, start.studioUrl, account);
    try {
      reader.page.on("pageerror", error => errors.push(error.message));
      const discovery = await reader.page.request.get(`${backend}/capabilities`);
      expect(discovery.status(), `${profile} reader must be able to discover the backend capabilities`).toBe(200);
      const designCatalog = await reader.page.request.get(`${backend}/design/activities/catalog`, { maxRedirects: 0 });
      expect([403, 302], `${profile} reader must retain its lack of Design authority`).toContain(designCatalog.status());
      if (designCatalog.status() === 302) {
        const location = designCatalog.headers().location;
        expect(location, "Cookie authorization denial must identify its destination").toBeTruthy();
        const deniedLocation = new URL(location!, backend);
        expect(deniedLocation.origin).toBe(new URL(backend).origin);
        expect(deniedLocation.pathname).toBe("/Account/AccessDenied");
      }
      const runUrl = `/workflows/instances/${protectedRun}?selected=${encodeURIComponent(protectedEvent.activityExecutionId!)}&historyEvent=${encodeURIComponent(protectedEvent.historyEventId)}`;
      if (profile === "denied") await reader.page.goto(runUrl);
      else {
        const [browserCatalog] = await Promise.all([
          reader.page.waitForResponse(response => response.request().method() === "GET"
            && new URL(response.url()).origin === new URL(backend).origin && new URL(response.url()).pathname === "/design/activities/catalog"),
          reader.page.goto(runUrl)
        ]);
        expect(browserCatalog.status(), `${profile} browser must receive the actual Design denial`).toBe(403);
      }
      const deniedPayload = await reader.page.request.get(`${backend}/runtime/workflows/instances/${protectedRun}/activity-executions/${protectedEvent.activityExecutionId}/value-evidence/${protectedEvidenceId}/payload`);
      if (profile === "denied") {
        expect([403, 404]).toContain(deniedPayload.status());
        const deniedHistory = await reader.page.request.get(`${backend}/runtime/workflows/instances/${protectedRun}/history?limit=100`); expect([403, 404]).toContain(deniedHistory.status());
        await expect(reader.page.getByRole("main")).toContainText(/unavailable|not found|access|could not/i);
        await expect(reader.page.locator("[data-history-event-id]")).toHaveCount(0); await capture(reader.page, info, "denied-run");
      } else {
        expect(deniedPayload.status()).toBe(403);
        const inspection = await api<ActivityExecutionInspection>(reader.page.request, backend, `/runtime/workflows/instances/${protectedRun}/activity-executions/${protectedEvent.activityExecutionId}`);
        expect(JSON.stringify(inspection)).not.toContain(protectedValue);
        await expect(inspectorOf(reader.page)).toBeVisible();
        await expect(reader.page.getByText(/Activity definitions are not available with your permissions/)).toBeVisible();
        await inspectorOf(reader.page).getByRole("button", { name: "Inspect recorded value →", exact: true }).first().press("Enter");
        const value = inspectorOf(reader.page).getByRole("region", { name: "Recorded value Data", exact: true });
        await expect(value).toContainText(profile === "metadata" ? /hidden by permissions|redacted/i : /Additional permission is required/i);
        await expect(reader.page.locator("body")).not.toContainText(protectedValue); await value.scrollIntoViewIfNeeded(); await capture(reader.page, info, profile === "metadata" ? "metadata-reader" : "payload-denied");
      }
    } finally { await reader.context.close(); }
  }
  // A real foreign ID under a different real run cannot resolve to that run's latest event.
  await page.goto(`/workflows/instances/${run}?historyEvent=${encodeURIComponent(protectedEvent.historyEventId)}`);
  await expect(timelineOf(page).locator("[data-history-event-id]").first()).toBeVisible();
  await expect(inspectorOf(page)).toContainText(/unavailable|no longer retained/i); await expect(inspectorOf(page)).not.toContainText(protectedValue);
  const foreignTarget = inspectorOf(page).getByRole("region", { name: "History event details", exact: true }).getByRole("status");
  await foreignTarget.scrollIntoViewIfNeeded(); await expect(foreignTarget).toBeInViewport(); await capture(page, info, "foreign-exact-event");

  await page.close(); // No enabled-profile observer remains active while the owned store/profile changes.
  const disabled = await operation(info, "disabled"); await info.attach("disabled-profile", { path: disabled.receiptFile, contentType: "application/json" });
  const disabledContext = await browser.newContext({ baseURL: disabled.studioUrl, viewport: info.project.use.viewport, colorScheme: info.project.use.colorScheme });
  try {
    const disabledPage = await disabledContext.newPage(); disabledPage.on("pageerror", error => errors.push(error.message)); await signIn(disabledPage, info);
    const disabledActor = await api<{ userId: string }>(disabledPage.request, disabled.backendUrl, "/runtime/user-task-identity");
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(disabledPage.request, disabled.backendUrl, `/design/activities/definitions?search=${encodeURIComponent(types.task)}`);
    const version = catalog.items.find(item => item.definition.activityTypeKey === types.task)!.definition.headVersionId;
    const definition = await api<{ version: { id: string } }>(disabledPage.request, disabled.backendUrl, "/design/workflows/definitions/submit", { name: "New admission with history disabled", description: "Owned admission-time disabled profile.", state: {
      variables: [], inputs: [], outputs: [], workflowActivityOptions: null, strategyOptions: null,
      rootActivity: { nodeId: "disabled-task", activityVersionId: version, inputs: [literal("Title", "History disabled approval"), literal("AssigneeId", disabledActor.userId), literal("Actions", ["Approve"])], outputs: [] }
    } });
    const publication = await api<Publication>(disabledPage.request, disabled.backendUrl, `/publishing/workflows/${definition.version.id}/publish`, {});
    const admission = await api<{ workflowExecutionId: string }>(disabledPage.request, disabled.backendUrl, `/runtime/workflows/executables/${publication.artifactId}/execute`, { sourceReferenceId: publication.sourceReferenceId });
    await waitTasks(disabledPage, disabled.backendUrl, admission.workflowExecutionId, 1);
    const disabledHistory = await api<RuntimeHistoryPage>(disabledPage.request, disabled.backendUrl, `/runtime/workflows/instances/${admission.workflowExecutionId}/history?limit=100`);
    expect(disabledHistory.coverage.state).toBe("disabled"); expect(disabledHistory.items).toEqual([]);
    await disabledPage.goto(`/workflows/instances/${admission.workflowExecutionId}`);
    await expect(timelineOf(disabledPage)).toContainText("History capture was disabled when this run was admitted.");
    await timelineOf(disabledPage).getByText("History capture was disabled when this run was admitted.", { exact: true }).first().scrollIntoViewIfNeeded(); await capture(disabledPage, info, "disabled-history");
  } finally { await disabledContext.close(); }
  const facts = info.outputPath("diagnostic-facts.json"); writeFileSync(facts, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA,
    repeated: { workflowExecutionId: run, firstOwner, secondOwner: second!.bookmark.identity.activityExecutionId, parallelOwner, branches: branches.map(branch => branch.bookmark.identity.activityExecutionId) },
    historicalFault: { workflowExecutionId: recovered, fault: historicalFault.historyEventId, resolved: resolved.historyEventId, activeIncidentCount: recoveredDetails!.instance.activeIncidentCount },
    loadingTransport: { kind: "browser-cdp", latencyMs: 3000, syntheticResponse: false },
    longHistory: { workflowExecutionId: longRun, lastSequence: headPage.coverage.lastSequence, lateBookmark: late.bookmark.identity.bookmarkId, earlierExecution: earlier.activityExecutionId, pageSizes, distinctBrowserEvents: observedIds.size },
    retention: prepared.retention, expiry, restrictedProfiles: prepared.accounts!.map(account => account.profile), crossRunExactTargetRefused: true }, null, 2));
  await info.attach("diagnostic-facts", { path: facts, contentType: "application/json" }); expect(errors).toEqual([]);
});
