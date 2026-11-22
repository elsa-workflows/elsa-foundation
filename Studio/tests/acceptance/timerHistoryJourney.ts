import { expect, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import type { RuntimeHistoryBookmarkPage, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { expectQualifiedTimelineCoverage } from "./expectQualifiedTimelineCoverage";
import { waitCapturedPlaybackReady } from "./waitCapturedPlaybackReady";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Actual published Delay and the normal timer pump; no clock, timer, response or history overrides. */
export async function timerHistoryJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  if (!backend || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench is required");
  if (info.project.name === "narrow-light" || info.project.name === "narrow-dark")
    await page.setViewportSize({ width: 320, height: 900 });
  const api = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await page.request.fetch(`${backend}${path}`, { method, data, timeout: 15_000 });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  };
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const type = "Elsa.Activities.Scheduling.Activities.Delay";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(
    `/design/activities/definitions?search=${encodeURIComponent(type)}`);
  const delay = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
  expect(delay, "Normal composition registers the actual Delay activity").toBeTruthy();
  const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Timer history ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`,
    state: { variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" },
      rootActivity: { nodeId: "delay", activityVersionId: delay, outputs: [],
        inputs: [{ referenceKey: "Duration", value: { expressionType: "Literal", value: "00:00:45" } }] } }
  });
  registerCleanup(async () => { await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); });
  const published = await api<{ artifactId: string; sourceReferenceId: string }>(
    `/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
  const run = await api<{ workflowExecutionId: string }>(
    `/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
  const runPath = `/runtime/workflows/instances/${run.workflowExecutionId}`;
  let waiting!: WorkflowInstanceDetails;
  await expect.poll(async () => {
    waiting = await api<WorkflowInstanceDetails>(runPath);
    return waiting.activities.find(item => item.authoredActivityId === "delay")?.status;
  }, { timeout: 15_000 }).toBe("Suspended");
  const execution = waiting.activities.find(item => item.authoredActivityId === "delay")!;
  let scheduledHistory!: RuntimeHistoryPage;
  await expect.poll(async () => {
    scheduledHistory = await api<RuntimeHistoryPage>(`${runPath}/history?limit=100`);
    return scheduledHistory.items.filter(item => item.name === "TimerScheduled").length;
  }, { timeout: 20_000 }).toBe(1);
  expectQualifiedTimelineCoverage(scheduledHistory.coverage);
  const scheduled = scheduledHistory.items.find(item => item.name === "TimerScheduled")!;
  expect(scheduled.activityExecutionId).toBe(execution.activityExecutionId);
  expect(scheduled.metadata["history.timerId"]).toBe(`timer:${execution.activityExecutionId}`);
  expect(scheduledHistory.items.some(item => item.name === "TimerDispatched")).toBe(false);
  const bookmarkPath = `${runPath}/history/bookmarks?activityExecutionId=${encodeURIComponent(execution.activityExecutionId)}&limit=100`;
  const active = await api<RuntimeHistoryBookmarkPage>(bookmarkPath);
  expect(active.items).toHaveLength(1); expect(active.items[0].status).toBe("Active");
  expect(active.items[0].bookmark.identity.activityExecutionId).toBe(execution.activityExecutionId);
  const bookmarkId = active.items[0].bookmark.identity.bookmarkId;

  const eventUrl = (id: string) => `/workflows/instances/${run.workflowExecutionId}?view=timeline&historyEvent=${encodeURIComponent(id)}`;
  await page.goto(eventUrl(scheduled.historyEventId));
  await waitCapturedPlaybackReady(page);
  const details = page.getByRole("region", { name: "History event details", exact: true });
  await expect(details.getByRole("heading", { name: "Timer scheduled", exact: true })).toBeVisible();
  await details.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  const views = page.getByRole("tablist", { name: "Activity execution views", exact: true });
  await views.getByRole("tab", { name: "Journal", exact: true }).click();
  await expect(page.getByRole("region", { name: "Activity journal", exact: true })).toContainText("Bookmark created");
  await capture(page, info, "timer-waiting-journal");
  await views.getByRole("tab", { name: "Bookmarks", exact: true }).click();
  const inventory = page.getByRole("region", { name: "Activity bookmark history", exact: true });
  await expect(inventory).toContainText(bookmarkId);
  await expect(inventory.locator('[data-status="Active"]')).toHaveCount(1);
  await capture(page, info, "timer-active-bookmark");

  await expect.poll(async () => (await api<WorkflowInstanceDetails>(runPath)).instance.status,
    { timeout: 75_000 }).toBe("Completed");
  let completed!: RuntimeHistoryPage;
  await expect.poll(async () => {
    completed = await api<RuntimeHistoryPage>(`${runPath}/history?limit=100`);
    return completed.items.filter(item => item.name === "TimerDispatched").length;
  }, { timeout: 20_000 }).toBe(1);
  expectQualifiedTimelineCoverage(completed.coverage);
  const dispatched = completed.items.find(item => item.name === "TimerDispatched")!;
  expect(dispatched.activityExecutionId).toBe(execution.activityExecutionId);
  expect(dispatched.causation?.historyEventId).toBe(scheduled.historyEventId);
  expect(completed.items.filter(item => item.name === "TimerScheduled")).toHaveLength(1);
  for (const name of ["BookmarkConsumed", "ActivityResumeClaimed", "ActivityCompleted"])
    expect(completed.items.filter(item => item.name === name && item.activityExecutionId === execution.activityExecutionId)).toHaveLength(1);
  const consumed = await api<RuntimeHistoryBookmarkPage>(bookmarkPath);
  expect(consumed.items).toHaveLength(1); expect(consumed.items[0].status).toBe("Consumed");
  expect(consumed.items[0].bookmark.identity.bookmarkId).toBe(bookmarkId);

  await page.goto(eventUrl(dispatched.historyEventId));
  await waitCapturedPlaybackReady(page);
  await expect(details.getByRole("heading", { name: "Timer dispatched", exact: true })).toBeVisible();
  await capture(page, info, "timer-dispatched");
  await details.getByRole("button", { name: "Inspect causal event →", exact: true }).click();
  await expect(details.getByRole("heading", { name: "Timer scheduled", exact: true })).toBeVisible();
  await details.getByText("Technical details", { exact: true }).click();
  await expect(details).toContainText(scheduled.historyEventId);
  await capture(page, info, "timer-cause");
  await details.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  await views.getByRole("tab", { name: "Bookmarks", exact: true }).click();
  await expect(inventory.locator('[data-status="Consumed"]')).toHaveCount(1);
  await inventory.getByRole("button", { name: "View lifecycle →", exact: true }).click();
  const lifecycle = page.getByRole("region", { name: "Bookmark lifecycle", exact: true });
  await expect(lifecycle).toContainText(bookmarkId);
  await expect(lifecycle.locator('[data-status="Consumed"]')).toHaveCount(1);
  await expect(lifecycle.getByRole("button", { name: "Inspect consumed event →", exact: true })).toBeVisible();
  await capture(page, info, "timer-consumed-bookmark");
  const oraclePath = info.outputPath("timer-history-oracle.json");
  writeFileSync(oraclePath, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, submitted, published, run,
    execution, scheduled, dispatched, active, consumed, scheduledHistory, completed }, null, 2));
  await info.attach("timer-history-oracle.json", { path: oraclePath, contentType: "application/json" });
}
