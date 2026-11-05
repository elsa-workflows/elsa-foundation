import { readFileSync, renameSync, writeFileSync } from "node:fs";
import { expect, test, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import type { RuntimeHistoryBookmark, RuntimeHistoryEvent, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";

const backend = process.env.ELSA_BACKEND_URL;
const restartDirectory = process.env.UX_ACCEPTANCE_RESTART_DIR;
if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !backend || !restartDirectory) throw new Error("History acceptance requires the owned runner and restart supervisor.");

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const png = info.outputPath(`${name}.png`); await page.screenshot({ path: png, fullPage: true });
  await info.attach(`${name}.png`, { path: png, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`); writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
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
async function seed(request: APIRequestContext, suffix: string, onDefinition: (id: string) => void) {
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
  let taskId = "";
  await expect.poll(async () => {
    const page = await json(request, "/runtime/user-tasks?view=mine&limit=100");
    taskId = page.items.find((item: { workflowExecutionId: string }) => item.workflowExecutionId === run.workflowExecutionId)?.id ?? "";
    return taskId;
  }, { timeout: 30_000 }).not.toBe("");
  return { workflowExecutionId: run.workflowExecutionId as string, taskId, title, definitionId: definition.definition.id as string };
}
async function history(request: APIRequestContext, run: string) {
  const events: RuntimeHistoryEvent[] = []; let cursor: string | null | undefined;
  for (let page = 0; page < 20; page++) {
    const value = await json(request, `/runtime/workflows/instances/${run}/history?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`) as RuntimeHistoryPage;
    events.push(...value.items); cursor = value.nextContinuationToken;
    if (!cursor) return { events, coverage: value.coverage };
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
test.afterEach(async ({ page }) => {
  // Keep the existing collection reference journeys empty. Only this test's disposable definitions are archived.
  for (const id of ownedDefinitions.get(page) ?? []) {
    const response = await page.request.delete(`${backend}/design/workflows/definitions/${encodeURIComponent(id)}`);
    expect(response.ok(), `Archive owned acceptance definition ${id}`).toBe(true);
  }
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
  const fixture = await seed(page.request, `${info.project.name}-${crypto.randomUUID().slice(0, 8)}`, id => definitions.push(id));
  const run = fixture.workflowExecutionId, runPath = `/workflows/instances/${run}`;
  let creation: RuntimeHistoryEvent | undefined;
  await expect.poll(async () => { creation = (await history(page.request, run)).events.find(event => event.name === "BookmarkCreated"); return !!creation; }, { timeout: 30_000 }).toBe(true);
  const owner = creation!.activityExecutionId!, bookmarkId = creation!.bookmarkId!;
  const before = await history(page.request, run);
  expect(before.coverage.capturedFromStart).toBe(true); expect(before.events.some(event => event.name === "ActivitySuspended")).toBe(true);
  await page.goto(`${runPath}?selected=${encodeURIComponent(owner)}&returnTo=%2Fworkflows%2Finstances`);
  const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
  await expect(timeline).toBeVisible(); await expect(page.getByRole("heading", { name: "Currently waiting for", exact: true })).toBeVisible();
  await expect(timeline.getByRole("button", { name: /Bookmark created/ }).first()).toBeVisible();
  await capture(page, info, "reference");
  const inspector = page.getByRole("complementary", { name: "Run details panel", exact: true });
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
  await page.reload(); await expect(inspector.getByRole("region", { name: "Bookmark lifecycle", exact: true })).toBeVisible();
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
  await capture(page, info, "retained-bookmark-consumed");
  const consumed = final!.events.find(event => event.name === "BookmarkConsumed" && event.bookmarkId === bookmarkId)!;
  expect(consumed.activityExecutionId).toBe(owner); expect(consumed.sequence).toBeGreaterThan(creation!.sequence);
  await page.goto(`${runPath}?selected=${encodeURIComponent(owner)}&historyEvent=${encodeURIComponent(consumed.historyEventId)}&returnTo=%2Fworkflows%2Finstances`);
  await expect(inspector.getByRole("region", { name: "History event details", exact: true })).toBeVisible();
  await expect(inspector).toContainText("Bookmark consumed"); await expect(inspector).toContainText("Committed runtime transition");
  await capture(page, info, "resumed-event");
  await inspector.getByRole("button", { name: `${bookmarkId} →`, exact: true }).press("Enter");
  await expect(inspector).toContainText("current eligibility: Not active"); await expect(inspector).toContainText("Consumed");
  await capture(page, info, "bookmark-consumed");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await inspector.getByRole("button", { name: "Back to activity inspection", exact: true }).press("Enter");
  await expect(inspector.getByRole("tab", { name: "Journal", exact: true })).toBeVisible();
  await inspector.getByRole("tab", { name: "Journal", exact: true }).press("Enter");
  await expect(inspector.getByRole("list", { name: "Activity journal events", exact: true })).toContainText("Bookmark consumed");
  await capture(page, info, "activity-journal");
  expect(payloadRequests).toEqual([]);
  const search = timeline.getByRole("searchbox", { name: "Search recorded events", exact: true });
  await search.fill(`absent-${crypto.randomUUID()}`); await search.press("Enter"); await expect(timeline).toContainText("No matching events in this searched range");
  await capture(page, info, "no-matches"); await timeline.getByRole("button", { name: "Clear filters", exact: true }).press("Enter");
  await timeline.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
  await expect(timeline.getByRole("button", { name: /Activity faulted/ }).first()).toBeVisible();
  await capture(page, info, "faulted-timeline");
  await page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Diagram", exact: true }).press("Enter");
  await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
  await capture(page, info, "diagram");
  const finalUrl = new URL(page.url()); expect(finalUrl.searchParams.get("selected")).toBe(owner); expect(finalUrl.searchParams.get("returnTo")).toBe("/workflows/instances");
  const facts = info.outputPath("durable-facts.json"); writeFileSync(facts, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, fixture, owner, bookmarkId, creation, consumed, before: before.events, after: final!.events }, null, 2));
  await info.attach("durable-facts", { path: facts, contentType: "application/json" });
  expect(errors).toEqual([]);
});
