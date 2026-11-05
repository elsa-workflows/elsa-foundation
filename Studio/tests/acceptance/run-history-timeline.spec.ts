import { readFileSync, renameSync, writeFileSync } from "node:fs";
import { expect, test, type APIRequestContext, type Locator, type Page, type TestInfo } from "@playwright/test";
import type { RuntimeHistoryBookmark, RuntimeHistoryEvent, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { ActivityExecutionInspection } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const backend = process.env.ELSA_BACKEND_URL;
const restartDirectory = process.env.UX_ACCEPTANCE_RESTART_DIR;
if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !backend || !restartDirectory) throw new Error("History acceptance requires the owned runner and restart supervisor.");

async function revealCaptureAnchor(anchor: Locator, block: "start" | "center" = "center") {
  await anchor.scrollIntoViewIfNeeded();
  await anchor.evaluate((element, alignment) => {
    element.scrollIntoView({ block: alignment, inline: "nearest", behavior: "instant" });
    if (alignment !== "start") return;
    // Fractional line heights can clip at an integer scroll boundary. Keep the
    // heading and nearby Journal tab together inside the real viewport.
    let parent = element.parentElement;
    while (parent) {
      if (["auto", "scroll"].includes(getComputedStyle(parent).overflowY) && parent.scrollTop > 0) {
        parent.scrollBy({ top: -8, behavior: "instant" });
        break;
      }
      parent = parent.parentElement;
    }
  }, block);
}
async function assertCaptureAnchor(anchor: Locator) {
  await expect(anchor).toBeInViewport({ ratio: 1 });
  await expect.poll(() => anchor.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    const inset = Math.min(4, bounds.width / 4, bounds.height / 4);
    return [[bounds.left + inset, bounds.top + inset], [bounds.right - inset, bounds.top + inset],
      [bounds.left + inset, bounds.bottom - inset], [bounds.right - inset, bounds.bottom - inset],
      [bounds.left + bounds.width / 2, bounds.top + bounds.height / 2]].every(([x, y]) => {
      const hit = document.elementFromPoint(x, y);
      return !!hit && (element.contains(hit) || hit.contains(element));
    });
  })).toBe(true);
}
async function capture(page: Page, info: TestInfo, name: string, anchors: Locator[] = []) {
  await page.evaluate(async () => { await document.fonts.ready; await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))); });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  for (const anchor of anchors) await assertCaptureAnchor(anchor);
  const png = info.outputPath(`${name}.png`); await page.screenshot({ path: png, fullPage: true });
  for (const anchor of anchors) await assertCaptureAnchor(anchor);
  await info.attach(`${name}.png`, { path: png, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`); writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}
function eventRow(scope: Locator, event: RuntimeHistoryEvent) {
  return scope.locator(`[data-history-event-id=${JSON.stringify(event.historyEventId)}]`);
}
async function recordedRows(scope: Locator) {
  return scope.locator("[data-history-event-id]").evaluateAll(rows => rows.map(row => ({ id: row.getAttribute("data-history-event-id"), sequence: Number(row.getAttribute("data-history-sequence")) })));
}
async function json(request: APIRequestContext, path: string, data?: unknown) {
  const response = data === undefined ? await request.get(`${backend}${path}`) : await request.post(`${backend}${path}`, { data });
  expect(response.ok(), `${response.status()} ${path}: ${await response.text()}`).toBe(true);
  return response.json();
}
async function activityVersion(request: APIRequestContext, type: string) {
  const page = await json(request, `/design/activities/definitions?search=${encodeURIComponent(type)}`);
  const hit = page.items.find((item: { definition: { activityTypeKey: string } }) => item.definition.activityTypeKey === type);
  expect(hit, `Normal activity catalog contains ${type}`).toBeTruthy(); return hit.definition.headVersionId as string;
}
type OwnedTimelineTask = { workflowExecutionId: string; definitionId: string; title: string; taskId?: string };
type TimelineTaskView = { id: string; workflowExecutionId: string; title: string; status: string; revision: number; actions: string[];
  decision?: { requestId: string; response: { action: string } } | null };

async function seed(request: APIRequestContext, suffix: string, onDefinition: (id: string) => void, onRun: (task: OwnedTimelineTask) => void) {
  const actor = await json(request, "/runtime/user-task-identity");
  const taskVersion = await activityVersion(request, "Elsa.Activities.Primitives.Activities.CreateUserTask");
  const flowVersion = await activityVersion(request, "Elsa.Activities.Flowchart.Activities.Flowchart");
  const faultVersion = await activityVersion(request, "Elsa.Activities.Primitives.Activities.Fault");
  const formId = `history-${suffix}`; const title = `Purchase approval ${suffix}`;
  await json(request, "/runtime/user-task-forms", { id: formId, version: "1", name: "Timeline acceptance response", provider: "elsa.basic-forms/1", fields: [{ key: "Reason", label: "Reason", kind: "text", required: true }] });
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const task = { nodeId: "review-purchase", activityVersionId: taskVersion, inputs: [literal("Title", title), literal("AssigneeId", actor.userId), literal("Actions", ["Approve", "Reject"]), literal("FormId", formId), literal("FormVersion", "1")], outputs: [] };
  const fault = { nodeId: "diagnostic-fault", activityVersionId: faultVersion, inputs: [literal("message", "Intentional history acceptance fault")], outputs: [] };
  const rootActivity = { nodeId: "purchase-flow", activityVersionId: flowVersion, inputs: [], outputs: [], structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [task, fault], connections: [{ id: "approval-fault", source: { nodeId: task.nodeId, port: "Approve" }, target: { nodeId: fault.nodeId } }], startNodeId: task.nodeId, nodeMetadata: {}, connectionMetadata: {} } } };
  const definition = await json(request, "/design/workflows/definitions/submit", { name: title, description: "Owned native wait, restart, resume and fault evidence.", state: { variables: [], inputs: [], outputs: [], workflowActivityOptions: null, strategyOptions: null, rootActivity } });
  onDefinition(definition.definition.id);
  const published = await json(request, `/publishing/workflows/${definition.version.id}/publish`, {});
  const run = await json(request, `/runtime/workflows/executables/${published.artifactId}/execute`, { sourceReferenceId: published.sourceReferenceId });
  const owned: OwnedTimelineTask = { workflowExecutionId: run.workflowExecutionId, definitionId: definition.definition.id, title };
  onRun(owned); // Retain exact ownership even if the subsequent task-discovery assertion fails.
  let taskId = "";
  await expect.poll(async () => {
    const page = await json(request, "/runtime/user-tasks?view=mine&limit=100");
    taskId = page.items.find((item: { workflowExecutionId: string }) => item.workflowExecutionId === run.workflowExecutionId)?.id ?? "";
    if (taskId) owned.taskId = taskId;
    return taskId;
  }, { timeout: 30_000 }).not.toBe("");
  return { workflowExecutionId: run.workflowExecutionId as string, taskId, title, definitionId: definition.definition.id as string };
}
async function history(request: APIRequestContext, run: string) {
  const events: RuntimeHistoryEvent[] = []; let cursor: string | null | undefined;
  for (let page = 0; page < 20; page++) {
    const value = await json(request, `/runtime/workflows/instances/${run}/history?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`) as RuntimeHistoryPage;
    events.push(...value.items); cursor = value.nextContinuationToken;
    if (!cursor) return { events, coverage: value.coverage, catchUpToken: value.catchUpToken, upperSequence: value.upperSequence };
  }
  throw new Error("Owned fixture history exceeded its bounded scan budget.");
}
async function restartBackend(info: TestInfo, run: string) {
  const id = crypto.randomUUID(); const path = `${restartDirectory}/request.json`, temporary = `${restartDirectory}/${id}.tmp`;
  writeFileSync(temporary, JSON.stringify({ id, workflowExecutionId: run, project: info.project.name })); renameSync(temporary, path);
  let ack: { id: string; status: string; oldPid: number; newPid: number } | null = null;
  await expect.poll(() => {
    let content: string; try { content = readFileSync(`${restartDirectory}/ack.json`, "utf8"); } catch { return "pending"; }
    const value = JSON.parse(content); if (value.id !== id) return "pending"; ack = value; return value.status;
  }, { timeout: 180_000 }).toBe("restarted");
  expect(ack!.newPid).not.toBe(ack!.oldPid);
  const receipt = info.outputPath("restart-receipt.json"); writeFileSync(receipt, JSON.stringify(ack, null, 2));
  await info.attach("restart-receipt", { path: receipt, contentType: "application/json" });
}

const ownedDefinitions = new WeakMap<Page, string[]>();
const ownedTasks = new WeakMap<Page, OwnedTimelineTask>();
test.afterEach(async ({ page }, info) => {
  const original = { status: info.status, expectedStatus: info.expectedStatus, errors: info.errors.map(error => ({ message: error.message, stack: error.stack })) };
  const failures: Error[] = [];
  const operations: unknown[] = [];
  const receipt: Record<string, unknown> = { purpose: "Owned Timeline teardown only; never UI completion or restart acceptance", original,
    owned: ownedTasks.get(page), operations };
  async function attempt(label: string, action: () => Promise<void>) {
    try { await action(); } catch (cause) {
      const error = new Error(label, { cause }); failures.push(error);
      operations.push({ operation: label, failed: true, error: cause instanceof Error ? cause.message : String(cause) });
    }
  }
  // Preserve the failed experience before any guarded cleanup decision/reconciliation can alter its task/run.
  if (info.status !== info.expectedStatus) {
    await attempt("Retain pre-cleanup failure screenshot", async () => {
      const path = info.outputPath("pre-cleanup-failure.png"); await page.screenshot({ path, fullPage: true, timeout: 5_000 });
      await info.attach("pre-cleanup-failure.png", { path, contentType: "image/png" });
    });
    await attempt("Retain pre-cleanup failure accessibility", async () => {
      const path = info.outputPath("pre-cleanup-failure.aria.yml"); writeFileSync(path, await page.locator("body").ariaSnapshot({ timeout: 5_000 }));
      await info.attach("pre-cleanup-failure.aria.yml", { path, contentType: "text/yaml" });
    });
  }
  const owned = ownedTasks.get(page);
  if (owned) await attempt("Settle only the exact owned Timeline task", async () => {
    if (!(ownedDefinitions.get(page) ?? []).includes(owned.definitionId)) throw new Error("Owned task has no matching definition cleanup receipt.");
    const get = async (path: string) => {
      const response = await page.request.get(`${backend}${path}`, { timeout: 5_000 });
      if (!response.ok()) throw new Error(`${response.status()} ${path}: ${await response.text()}`);
      return response.json();
    };
    const run = await get(`/runtime/workflows/instances/${encodeURIComponent(owned.workflowExecutionId)}?activityPageSize=1`);
    if (run.instance.workflowExecutionId !== owned.workflowExecutionId || run.instance.definitionId !== owned.definitionId)
      throw new Error("Owned Timeline run/definition identity differs; no task decision submitted.");
    if (!owned.taskId) {
      // One bounded, title-filtered discovery page is used only if seed failed before returning its exact task ID.
      const taskPage = await get(`/runtime/user-tasks?view=mine&limit=100&search=${encodeURIComponent(owned.title)}`) as { items: TimelineTaskView[]; next?: string | null };
      const matches = taskPage.items.filter(task => task.workflowExecutionId === owned.workflowExecutionId && task.title === owned.title);
      if (taskPage.next || matches.length !== 1) throw new Error("Exact owned task discovery was incomplete or ambiguous; no broad inbox cleanup attempted.");
      owned.taskId = matches[0].id;
    }
    const path = `/runtime/user-tasks/${encodeURIComponent(owned.taskId)}`;
    const read = async () => {
      const task = await get(path) as TimelineTaskView;
      if (task.id !== owned.taskId || task.workflowExecutionId !== owned.workflowExecutionId || task.title !== owned.title)
        throw new Error("Task identity differs from the exact owned fixture; no decision submitted.");
      return task;
    };
    let task = await read(); operations.push({ operation: "pre-cleanup-task", task });
    if (task.status === "Open") {
      if (task.decision || !task.actions.includes("Reject")) throw new Error("Owned open task does not permit this teardown rejection.");
      const data = { revision: task.revision, requestId: `timeline-teardown-${crypto.randomUUID()}`, action: "Reject",
        data: { Reason: "Owned Timeline fixture teardown; this API rejection is not UI acceptance evidence." } };
      operations.push({ operation: "teardown-reject-not-ui-proof", taskId: task.id, request: data });
      // Exactly one normal revision-guarded submission. A conflict/outage is retained, never retried with another decision.
      const response = await page.request.post(`${backend}${path}/complete`, { data, timeout: 5_000 });
      if (!response.ok()) throw new Error(`${response.status()} owned task teardown completion: ${await response.text()}`);
      task = await response.json() as TimelineTaskView;
      operations.push({ operation: "teardown-decision-receipt-not-ui-proof", httpStatus: response.status(), task });
      if (task.decision?.requestId !== data.requestId || task.decision.response.action !== "Reject") throw new Error("Teardown decision receipt does not match its single submitted request.");
    }
    // GET performs the normal accepted-decision reconciliation. CompletionPending keeps its ORIGINAL decision;
    // it never receives a conflicting cleanup request. Bounded successful reads do not retry failed submissions.
    for (let readCount = 0; task.status === "CompletionPending" && readCount < 6; readCount++) {
      task = await read();
      if (task.status === "CompletionPending" && readCount < 5) await new Promise(resolve => setTimeout(resolve, 500));
    }
    operations.push({ operation: "post-cleanup-task-not-ui-proof", task });
    if (task.status !== "Completed" && task.status !== "Cancelled") throw new Error(`Owned task remains ${task.status}; teardown did not establish a terminal receipt.`);
  });
  // Archive every exact owned definition even when task cleanup/evidence fails; never clear another test's inbox.
  for (const id of ownedDefinitions.get(page) ?? []) await attempt(`Archive owned acceptance definition ${id}`, async () => {
    const response = await page.request.delete(`${backend}/design/workflows/definitions/${encodeURIComponent(id)}`, { timeout: 5_000 });
    if (!response.ok()) throw new Error(`${response.status()} archive owned definition: ${await response.text()}`);
    operations.push({ operation: "archive-owned-definition", definitionId: id });
  });
  await attempt("Retain separate Timeline teardown receipt", async () => {
    const path = info.outputPath("timeline-teardown-not-ui-proof.json"); writeFileSync(path, JSON.stringify(receipt, null, 2));
    await info.attach("timeline-teardown-not-ui-proof", { path, contentType: "application/json" });
  });
  if (failures.length) throw new AggregateError(failures, "Owned Timeline teardown failed; original verdict and pre-cleanup evidence remain retained.");
});

test("durable run timeline across a native task resume and owned process restart", async ({ page }, info) => {
  test.setTimeout(300_000);
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, info.project.use.colorScheme ?? "light");
  const errors: string[] = [], payloadRequests: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("request", request => { if (/value.*payload|payload.*value/.test(request.url())) payloadRequests.push(request.url()); });
  await page.goto("/workflows/definitions");
  const login = page.locator('form[action="/_elsa/identity/login"]');
  await login.getByLabel(/username/i).fill("admin"); await login.getByLabel(/password/i).fill("Password123!"); await login.getByRole("button", { name: /sign in/i }).press("Enter");
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
  const definitions: string[] = []; ownedDefinitions.set(page, definitions);
  const fixture = await seed(page.request, `${info.project.name}-${crypto.randomUUID().slice(0, 8)}`, id => definitions.push(id), task => ownedTasks.set(page, task));
  const run = fixture.workflowExecutionId, runPath = `/workflows/instances/${run}`;
  let creation: RuntimeHistoryEvent | undefined;
  await expect.poll(async () => { creation = (await history(page.request, run)).events.find(event => event.name === "BookmarkCreated"); return !!creation; }, { timeout: 30_000 }).toBe(true);
  const owner = creation!.activityExecutionId!, bookmarkId = creation!.bookmarkId!;
  const before = await history(page.request, run);
  expect(before.coverage.capturedFromStart).toBe(true); expect(before.events.some(event => event.name === "ActivitySuspended")).toBe(true);
  await page.goto(`${runPath}?selected=${encodeURIComponent(owner)}&returnTo=%2Fworkflows%2Finstances`);
  const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
  const inspector = page.getByRole("complementary", { name: "Run details panel", exact: true });
  await expect(timeline).toBeVisible(); await expect(page.getByRole("heading", { name: "Waiting at last observation", exact: true })).toBeVisible();
  await expect(inspector.getByRole("heading", { name: "CreateUserTask", exact: true })).toBeVisible();
  await eventRow(timeline, creation!).scrollIntoViewIfNeeded();
  await expect(eventRow(timeline, creation!)).toBeInViewport();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "linen");
  await capture(page, info, "reference");
  const urlBeforeStyle = page.url(), rowsBeforeStyle = await recordedRows(timeline);
  const previousStyle = await page.locator("html").getAttribute("data-workspace-style");
  const theme = page.getByRole("button", { name: "Select theme", exact: true });
  await theme.click();
  const workspaceStyle = page.getByRole("group", { name: "Workspace style", exact: true });
  const previousStyleName = (await workspaceStyle.getByRole("menuitemradio", { checked: true }).innerText()).trim();
  const minimalControlBounds: Array<{ name: string; height: number; roleHeight: number }> = [];
  try {
    // Use Studio's ordinary preference action, preserving the real authorized run and
    // palette. The original reference and every later frame keep their Linen treatment.
    await workspaceStyle.getByRole("menuitemradio", { name: "Minimal", exact: true }).click();
    await page.keyboard.press("Escape");
    await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
    const search = timeline.getByRole("searchbox", { name: "Search recorded events", exact: true });
    const submit = timeline.getByRole("button", { name: "Search", exact: true });
    const searchField = search.locator("xpath=ancestor::label[1]");
    const surfaces: Array<[string, Locator]> = [["Search field", searchField], ["Search surface", search.locator("..")], ["Search button", submit],
      ...["History scope", "History detail", "History category"].map(name => [name, timeline.getByRole("combobox", { name, exact: true })] as [string, Locator]),
      ["Refresh button", timeline.getByRole("button", { name: "Refresh run history", exact: true })],
      ["Live button", timeline.getByRole("button", { name: "Pause live updates", exact: true })],
      ["Follow button", timeline.getByRole("button", { name: "Follow latest", exact: true })]];
    for (const [name, control] of surfaces) {
      await expect(control).toBeVisible();
      const bounds = await control.evaluate(element => ({ height: element.getBoundingClientRect().height,
        roleHeight: Number.parseFloat(getComputedStyle(element).getPropertyValue("--studio-control-height")) }));
      expect(bounds.roleHeight, `${name}: Minimal density is active`).toBe(36);
      expect(bounds.height + 0.5, `${name}: rendered control honors the public role`).toBeGreaterThanOrEqual(bounds.roleHeight);
      minimalControlBounds.push({ name, ...bounds });
    }
    await search.focus(); await page.keyboard.press("Tab"); await expect(submit).toBeFocused();
    await expect(submit).toHaveCSS("outline-style", "solid"); await expect(submit).toHaveCSS("outline-width", "2px");
    expect(page.url()).toBe(urlBeforeStyle); expect(await recordedRows(timeline)).toEqual(rowsBeforeStyle);
    // `has` is resolved inside each form, so its locator must start at the searchbox.
    await revealCaptureAnchor(timeline.locator("form").filter({ has: page.getByRole("searchbox", { name: "Search recorded events", exact: true }) }));
    await capture(page, info, "minimal-controls", [searchField, submit]);
  } finally {
    await page.keyboard.press("Escape"); await theme.click();
    await workspaceStyle.getByRole("menuitemradio", { name: previousStyleName, exact: true }).click(); await page.keyboard.press("Escape");
    await expect(page.locator("html")).toHaveAttribute("data-workspace-style", previousStyle!);
  }
  expect(page.url()).toBe(urlBeforeStyle); expect(await recordedRows(timeline)).toEqual(rowsBeforeStyle);
  await inspector.getByRole("tab", { name: "Bookmarks", exact: true }).press("Enter");
  await expect(inspector.getByText(bookmarkId, { exact: true })).toBeVisible();
  await inspector.getByRole("button", { name: "View lifecycle →", exact: true }).first().press("Enter");
  await expect(inspector.getByRole("region", { name: "Bookmark lifecycle", exact: true })).toBeVisible();
  await expect(inspector).toContainText("current eligibility: Eligible");
  await capture(page, info, "bookmark-waiting");
  const beforeBookmark = await json(page.request, `/runtime/workflows/instances/${run}/history/bookmarks/${bookmarkId}`) as { bookmark: RuntimeHistoryBookmark };
  await restartBackend(info, run);
  const afterRestart = await history(page.request, run);
  for (const event of before.events) expect(afterRestart.events.find(item => item.historyEventId === event.historyEventId)).toEqual(event);
  const retained = await json(page.request, `/runtime/workflows/instances/${run}/history/bookmarks/${bookmarkId}`) as { bookmark: RuntimeHistoryBookmark };
  expect(retained.bookmark.createdHistoryEventId).toBe(beforeBookmark.bookmark.createdHistoryEventId);
  expect(before.catchUpToken).toBeTruthy();
  const oldCatchUpPath = `/runtime/workflows/instances/${run}/history?limit=100&cursor=${encodeURIComponent(before.catchUpToken!)}`;
  const catchUpAfterRestart = await json(page.request, oldCatchUpPath) as RuntimeHistoryPage;
  expect(catchUpAfterRestart.items.length).toBeLessThanOrEqual(100);
  expect(catchUpAfterRestart.items.every(event => event.sequence > before.upperSequence && !before.events.some(prior => prior.historyEventId === event.historyEventId))).toBe(true);
  await page.reload(); await expect(inspector.getByRole("region", { name: "Bookmark lifecycle", exact: true })).toBeVisible();
  await expect(inspector).toContainText("current eligibility: Eligible"); await expect(eventRow(timeline, creation!)).toBeVisible();
  await capture(page, info, "after-restart");
  const retainedBookmarkUrl = page.url();
  const taskPage = await page.context().newPage();
  taskPage.on("pageerror", error => errors.push(error.message));
  try {
    await taskPage.goto(`/user-tasks/${fixture.taskId}`); await expect(taskPage.getByRole("heading", { name: fixture.title, exact: true })).toBeVisible();
    await taskPage.getByLabel(/^Reason/).fill("Owned timeline qualification");
    const accepted = taskPage.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/runtime/user-tasks/${fixture.taskId}/complete`));
    await taskPage.getByRole("button", { name: "Approve", exact: true }).press("Enter");
    expect((await accepted).ok()).toBe(true);
  } finally { await taskPage.close(); }
  await page.bringToFront();
  let final: Awaited<ReturnType<typeof history>> | undefined;
  await expect.poll(async () => {
    final = await history(page.request, run);
    return ["BookmarkConsumed", "ActivityCompleted", "ActivityFaulted", "IncidentRecorded"].every(name => final!.events.some(event => event.name === name));
  }, { timeout: 30_000 }).toBe(true);
  expect(page.url()).toBe(retainedBookmarkUrl);
  await expect(inspector.getByRole("region", { name: "Bookmark lifecycle", exact: true })).toBeVisible();
  await expect(inspector.getByRole("button", { name: "Refresh bookmark observation", exact: true })).toBeEnabled();
  await inspector.getByRole("button", { name: "Refresh bookmark observation", exact: true }).press("Enter");
  await expect(inspector).toContainText("current eligibility: Not active"); await expect(inspector).toContainText("Consumed");
  await expect(page.locator('[aria-label="Run history summary"]')).toContainText("Faulted", { timeout: 30_000 });
  await expect(page.getByRole("heading", { name: "Waiting at last observation", exact: true })).toHaveCount(0);
  await capture(page, info, "retained-bookmark-consumed");
  const consumed = final!.events.find(event => event.name === "BookmarkConsumed" && event.bookmarkId === bookmarkId)!;
  expect(consumed.activityExecutionId).toBe(owner); expect(consumed.sequence).toBeGreaterThan(creation!.sequence);
  const catchUpAfterResume = await json(page.request, oldCatchUpPath) as RuntimeHistoryPage;
  expect(catchUpAfterResume.items.length).toBeLessThanOrEqual(100);
  expect(catchUpAfterResume.items).toContainEqual(consumed);
  expect(catchUpAfterResume.items.every(event => event.sequence > before.upperSequence && !before.events.some(prior => prior.historyEventId === event.historyEventId))).toBe(true);
  await page.goto(`${runPath}?selected=${encodeURIComponent(owner)}&historyEvent=${encodeURIComponent(consumed.historyEventId)}&returnTo=%2Fworkflows%2Finstances`);
  await expect(inspector.getByRole("region", { name: "History event details", exact: true })).toBeVisible();
  await expect(eventRow(timeline, consumed)).toBeVisible();
  await expect(inspector).toContainText("Bookmark consumed"); await expect(inspector).toContainText("Committed runtime transition");
  await eventRow(timeline, consumed).scrollIntoViewIfNeeded(); await expect(eventRow(timeline, consumed)).toBeInViewport();
  await capture(page, info, "resumed-event");
  await inspector.getByRole("button", { name: "Inspect bookmark →", exact: true }).press("Enter");
  await expect(inspector).toContainText("current eligibility: Not active"); await expect(inspector).toContainText("Consumed");
  await capture(page, info, "bookmark-consumed");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await expect(inspector.getByRole("tab", { name: "Journal", exact: true })).toBeVisible();
  await inspector.getByRole("tab", { name: "Journal", exact: true }).press("Enter");
  const journal = inspector.getByRole("list", { name: "Activity journal events", exact: true });
  await expect(journal).toContainText("Bookmark consumed");
  const activityHeading = inspector.getByRole("heading", { name: "CreateUserTask", exact: true });
  const journalTab = inspector.getByRole("tab", { name: "Journal", exact: true });
  await expect(journalTab).toHaveAttribute("aria-selected", "true");
  await expect(inspector.getByText("Loading recorded executions…", { exact: true })).toHaveCount(0);
  await revealCaptureAnchor(activityHeading, "start");
  await expect.poll(() => activityHeading.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  await capture(page, info, "activity-journal-context", [activityHeading, journalTab]);
  await eventRow(journal, consumed).scrollIntoViewIfNeeded(); await expect(eventRow(journal, consumed)).toBeInViewport();
  await revealCaptureAnchor(eventRow(journal, consumed));
  await capture(page, info, "activity-journal", [eventRow(journal, consumed)]);
  expect(payloadRequests).toEqual([]);
  await timeline.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
  await expect(eventRow(timeline, consumed)).toBeVisible();
  const runRows = await recordedRows(timeline), journalRows = await recordedRows(journal);
  expect(journalRows.length).toBeGreaterThan(0);
  for (const row of journalRows) expect(runRows).toContainEqual(row);
  expect(journalRows.map(row => row.sequence)).toEqual([...journalRows.map(row => row.sequence)].sort((left, right) => left - right));
  expect(journalRows).toContainEqual({ id: creation!.historyEventId, sequence: creation!.sequence });
  expect(journalRows).toContainEqual({ id: consumed.historyEventId, sequence: consumed.sequence });
  // Three actions from the selected activity: Bookmarks, exact lifecycle, creation/consumption.
  await inspector.getByRole("tab", { name: "Bookmarks", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "View lifecycle →", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "Inspect creation event →", exact: true }).press("Enter");
  await expect.poll(() => new URL(page.url()).searchParams.get("historyEvent")).toBe(creation!.historyEventId);
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "Inspect consumed event →", exact: true }).press("Enter");
  await expect.poll(() => new URL(page.url()).searchParams.get("historyEvent")).toBe(consumed.historyEventId);
  // A known causal receipt is reachable directly from the same committed consumption fact.
  await inspector.getByRole("button", { name: "Inspect causal event →", exact: true }).press("Enter");
  await expect.poll(() => new URL(page.url()).searchParams.get("historyEvent")).toBe(consumed.causation!.historyEventId);
  await expect(inspector).toContainText("Recorded source observation");
  const causalHeading = inspector.getByRole("heading", { name: "Delivery received", exact: true });
  await causalHeading.scrollIntoViewIfNeeded();
  const causalEvent = final!.events.find(event => event.historyEventId === consumed.causation!.historyEventId);
  expect(causalEvent, "The exact causal observation is retained in the run snapshot").toBeTruthy();
  const causalRow = eventRow(timeline, causalEvent!);
  if (info.project.use.viewport!.width >= 1024) await revealCaptureAnchor(causalRow);
  else await revealCaptureAnchor(causalHeading);
  await capture(page, info, "causal-delivery", info.project.use.viewport!.width >= 1024 ? [causalRow, causalHeading] : [causalHeading]);
  await inspector.getByRole("button", { name: "Find causal successors in this run", exact: true }).press("Enter");
  const successors = inspector.getByRole("region", { name: "Recorded causal successors", exact: true });
  await expect(eventRow(successors, consumed)).toBeVisible();
  await eventRow(successors, consumed).scrollIntoViewIfNeeded(); await expect(eventRow(successors, consumed)).toBeInViewport(); await capture(page, info, "causal-successors");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await inspector.getByText("Technical details", { exact: true }).press("Enter");
  await expect(inspector).toContainText(consumed.physicalCommitId!);
  await timeline.getByText("Recording coverage", { exact: true }).press("Enter");
  await timeline.getByText("Settled through", { exact: true }).scrollIntoViewIfNeeded();
  await expect(timeline.getByText("Settled through", { exact: true })).toBeInViewport(); await capture(page, info, "recording-coverage");
  await inspector.getByText("Atomic commit group", { exact: true }).scrollIntoViewIfNeeded();
  await expect(inspector.getByText("Atomic commit group", { exact: true })).toBeInViewport(); await capture(page, info, "atomic-group");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  // Selecting a retained main row and going Back restores its keyboard focus and exact identity.
  await eventRow(timeline, consumed).press("Enter");
  await expect(inspector.getByRole("heading", { name: "Bookmark consumed", exact: true })).toBeFocused();
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await expect(eventRow(timeline, consumed)).toBeFocused();
  await inspector.getByRole("tab", { name: "Data", exact: true }).press("Enter");
  await expect(inspector.getByRole("heading", { name: /^Inputs/ })).toBeVisible();
  await expect(inspector.getByText("Loading recorded executions…", { exact: true })).toHaveCount(0);
  const capturedTitle = inspector.getByText(fixture.title, { exact: true }).first();
  await expect(capturedTitle).toBeVisible(); await revealCaptureAnchor(capturedTitle);
  await capture(page, info, "activity-data", [capturedTitle]);
  const valueEvent = final!.events.find(event => event.name === "ValueCaptured" && event.activityExecutionId === owner && event.metadata["history.valueEvidenceIds"]);
  expect(valueEvent, "The normal runtime captured a safe exact value reference").toBeTruthy();
  await eventRow(timeline, valueEvent!).press("Enter");
  const valueInspectionResponse = page.waitForResponse(response => response.request().method() === "GET" && response.url().endsWith(`/runtime/workflows/instances/${run}/activity-executions/${owner}`));
  await inspector.getByRole("button", { name: "Inspect recorded value →", exact: true }).first().press("Enter");
  const valueInspection = await (await valueInspectionResponse).json() as ActivityExecutionInspection;
  const valueEvidenceId = (JSON.parse(valueEvent!.metadata["history.valueEvidenceIds"]) as string[])[0];
  const exactSnapshot = valueInspection.valueSnapshots.find(snapshot => snapshot.evidenceId === valueEvidenceId);
  expect(exactSnapshot, "The explicit action resolves the recorded evidence identity").toBeTruthy();
  const value = inspector.getByRole("region", { name: "Recorded value Data", exact: true });
  await expect(value).toContainText("Phase"); await expect(value).not.toContainText("Loading the exact value observation");
  await expect(value).toContainText(exactSnapshot!.evaluationPhase ?? exactSnapshot!.phase ?? "Not recorded");
  await expect(value.getByRole("heading", { name: "Recorded value · Data", exact: true })).toBeFocused();
  await value.scrollIntoViewIfNeeded(); await capture(page, info, "recorded-value");
  await value.getByRole("button", { name: "Close recorded value", exact: true }).press("Enter");
  await expect(inspector.getByRole("button", { name: "Inspect recorded value →", exact: true }).first()).toBeFocused();
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  const search = timeline.getByRole("searchbox", { name: "Search recorded events", exact: true });
  await search.fill(`absent-${crypto.randomUUID()}`); await search.press("Enter"); await expect(timeline).toContainText("No matching events in this searched range");
  await capture(page, info, "no-matches"); await timeline.getByRole("button", { name: "Clear filters", exact: true }).press("Enter");
  await timeline.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
  await timeline.getByRole("button", { name: /Activity faulted/ }).first().scrollIntoViewIfNeeded();
  await expect(timeline.getByRole("button", { name: /Activity faulted/ }).first()).toBeInViewport();
  await capture(page, info, "faulted-timeline");
  await page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Diagram", exact: true }).press("Enter");
  const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
  await expect(canvas.getByText("CreateUserTask", { exact: true })).toBeVisible();
  await expect(canvas.getByText("Fault", { exact: true })).toBeVisible();
  await canvas.getByRole("button", { name: "Fit View", exact: true }).press("Enter");
  await canvas.getByText("CreateUserTask", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "diagram");
  await page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Timeline", exact: true }).press("Enter");
  await expect(eventRow(timeline, consumed)).toBeVisible();
  const rowsBeforeOffline = await recordedRows(timeline), urlBeforeOffline = page.url();
  expect(rowsBeforeOffline.length).toBeGreaterThan(0);
  try {
    await page.context().setOffline(true);
    const offline = timeline.getByText("Offline · showing last confirmed history.", { exact: false });
    await expect(offline).toBeVisible(); expect(await recordedRows(timeline)).toEqual(rowsBeforeOffline); expect(page.url()).toBe(urlBeforeOffline);
    await offline.scrollIntoViewIfNeeded(); await capture(page, info, "stale-history");
    await timeline.getByRole("button", { name: "Retry history", exact: true }).press("Enter");
  } finally { await page.context().setOffline(false); }
  const reconnectedRead = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "GET" && url.pathname.endsWith(`/runtime/workflows/instances/${run}/history`) && !url.searchParams.has("activityExecutionId");
  });
  await timeline.getByRole("button", { name: "Refresh run history", exact: true }).press("Enter");
  expect((await reconnectedRead).ok()).toBe(true);
  await expect(timeline).not.toContainText("Offline · showing last confirmed history.");
  await expect(timeline.getByText("Live history", { exact: true })).toBeVisible();
  const rowsAfterReconnect = await recordedRows(timeline);
  for (const row of rowsBeforeOffline) expect(rowsAfterReconnect).toContainEqual(row);
  expect(page.url()).toBe(urlBeforeOffline); await capture(page, info, "reconnected-history");
  const finalUrl = new URL(page.url()); expect(finalUrl.searchParams.get("selected")).toBe(owner); expect(finalUrl.searchParams.get("returnTo")).toBe("/workflows/instances");
  const facts = info.outputPath("durable-facts.json"); writeFileSync(facts, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, fixture, owner, bookmarkId, creation, consumed, before: before.events, after: final!.events,
    minimalControls: { control: "Studio ThemeSwitcher workspace-style preference", previousStyle, previousStyleName, selectedStyle: "minimal", restoredStyle: previousStyle, bounds: minimalControlBounds },
    catchUp: { usedSamePreRestartToken: true, upperSequence: before.upperSequence, afterRestart: catchUpAfterRestart.items.map(event => event.historyEventId), afterResume: catchUpAfterResume.items.map(event => event.historyEventId) }, runRows, journalRows }, null, 2));
  await info.attach("durable-facts", { path: facts, contentType: "application/json" });
  expect(errors).toEqual([]);
});
