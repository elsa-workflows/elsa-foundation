import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { readFileSync, writeFileSync, renameSync } from "node:fs";
import { join } from "node:path";
import type { RuntimeHistoryBookmarkPage, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { waitCapturedPlaybackReady } from "./waitCapturedPlaybackReady";
import { expectQualifiedTimelineCoverage } from "./expectQualifiedTimelineCoverage";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type RestartReceipt = { requestId: string; generation: number; beforePid: number; afterPid: number; beforeExitCode: number;
  oldGroupGone: boolean; sameOwnedStoreRoot: boolean; sameBinaryAndConfiguration: boolean; readyOnSameUrl: boolean; error?: string };

/** Genuine publication, User Tasks, physical process cuts and native diagnostic facts. No fabricated renderer responses. */
export async function timelineDiagnosticsJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL, control = process.env.UX_RESTART_CONTROL_DIR;
  if (!backend || !control || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned restart composition required");
  if (["narrow-light", "narrow-dark"].includes(info.project.name)) await page.setViewportSize({ width: 320, height: 900 });
  const frame = async (target: Locator) => {
    // Drain the existing bounded readiness focus before choosing the camera target.
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    await target.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(target).toBeInViewport({ ratio: 1 });
    await expect.poll(async () => {
      const bounds = await target.boundingBox();
      const console = await page.getByRole("tab", { name: "Console", exact: true }).boundingBox();
      return bounds !== null && console !== null && bounds.y + bounds.height <= console.y;
    }).toBe(true);
    await expect.poll(() => target.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      const hit = document.elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
      return hit !== null && (element.contains(hit) || hit.contains(element));
    })).toBe(true);
  };
  const api = async <T>(path: string, method = "GET", data?: unknown, headers?: Record<string, string>): Promise<T> => {
    const response = await page.request.fetch(`${backend}${path}`, { method, data, headers, timeout: 30_000 });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  };
  const signIn = () => api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  await signIn();
  const runs: string[] = [], definitions: string[] = [], restarts: RestartReceipt[] = [];
  const path = (id: string) => `/runtime/workflows/instances/${id}`;
  const details = (id: string) => api<WorkflowInstanceDetails>(path(id));
  const history = (id: string) => api<RuntimeHistoryPage>(`${path(id)}/history?limit=100`);
  const assertRestartPrefix = (original: RuntimeHistoryPage, current: RuntimeHistoryPage) => {
    expect(current.items.slice(0, original.items.length)).toEqual(original.items);
    expect(new Set(current.items.map(item => item.historyEventId)).size).toBe(current.items.length);
    // A terminal run may still receive its dispatch acknowledgement. A process cut
    // must never append a second business execution/consumption under fresh IDs.
    for (const appended of current.items.slice(original.items.length)) {
      expect(appended.name).toBe("DeliveryDispatched");
      expect(original.items.some(item => item.name === "DeliveryReceived" && item.historyEventId === appended.causation?.historyEventId)).toBe(true);
    }
  };
  const bookmarks = (id: string) => api<RuntimeHistoryBookmarkPage>(`${path(id)}/history/bookmarks?limit=100`);
  const cancel = async (id: string) => {
    if (["Completed", "Cancelled", "Faulted"].includes((await details(id)).instance.status)) return;
    const plan = await api<{ planId: string }>("/runtime/workflows/alteration-plans", "POST", {
      target: { executionIds: [id] }, alterations: [{ kind: "CancelWorkflow", schemaVersion: 1, payload: {} }]
    }, { "Idempotency-Key": `diagnostics-${crypto.randomUUID()}` });
    await expect.poll(async () => (await api<{ status: string }>(`/runtime/workflows/alteration-plans/${plan.planId}`)).status,
      { timeout: 30_000 }).toBe("Completed");
    await expect.poll(async () => (await details(id)).instance.status, { timeout: 30_000 }).toBe("Cancelled");
  };
  registerCleanup(async () => {
    await signIn();
    for (const id of runs) await cancel(id);
    for (const id of definitions) await api(`/design/workflows/definitions/${id}`, "DELETE");
  });
  const version = async (key: string) => {
    const rows = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(
      `/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const found = rows.items.find(item => item.definition.activityTypeKey === key)?.definition.headVersionId;
    expect(found, key).toBeTruthy(); return found!;
  };
  const [flow, task, event, line] = await Promise.all([
    version("Elsa.Activities.Flowchart.Activities.Flowchart"), version("Elsa.Activities.Primitives.Activities.CreateUserTask"),
    version("Elsa.Activities.Primitives.Activities.Event"), version("Elsa.Activities.Primitives.Activities.WriteLine")
  ]);
  const input = (values: Record<string, unknown>) => Object.entries(values).map(([referenceKey, value]) =>
    ({ referenceKey, value: { expressionType: "Literal", value } }));
  const node = (nodeId: string, activityVersionId: string, values: Record<string, unknown>) =>
    ({ nodeId, activityVersionId, inputs: input(values), outputs: [] });
  const publish = async (label: string, activities: ReturnType<typeof node>[], port = "Done") => {
    const title = `Timeline ${label} ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`;
    const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
      name: title, state: { variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" },
        rootActivity: { nodeId: "root", activityVersionId: flow, inputs: [], outputs: [], structure: {
          kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities, startNodeId: activities[0].nodeId,
            connections: activities.slice(1).map((next, i) => ({ id: `edge-${i}`, source: { nodeId: activities[i].nodeId, port: i === 0 ? port : "Done" }, target: { nodeId: next.nodeId } })),
            nodeMetadata: {}, connectionMetadata: {} } } } }
    });
    definitions.push(submitted.definition.id);
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    runs.push(run.workflowExecutionId);
    await expect.poll(async () => (await details(run.workflowExecutionId)).activities.some(activity => activity.status === "Suspended"), { timeout: 30_000 }).toBe(true);
    return { title, submitted, published, id: run.workflowExecutionId };
  };
  const restart = async () => {
    // Private file rendezvous with the runner's MAIN thread; no product endpoint or arbitrary process command.
    await page.goto("/home");
    const generation = (JSON.parse(readFileSync(join(control, "state.json"), "utf8")) as { generation: number }).generation;
    const requestId = crypto.randomUUID().replaceAll("-", "");
    writeFileSync(join(control, "request.tmp"), JSON.stringify({ requestId, expectedGeneration: generation }));
    renameSync(join(control, "request.tmp"), join(control, "request.json"));
    let receipt!: RestartReceipt;
    await expect.poll(() => {
      try { receipt = JSON.parse(readFileSync(join(control, "reply.json"), "utf8")) as RestartReceipt; return receipt.requestId; }
      catch { return ""; }
    }, { timeout: 250_000 }).toBe(requestId);
    expect(receipt.error).toBeUndefined(); expect(receipt.generation).toBe(generation + 1);
    expect(receipt.afterPid).not.toBe(receipt.beforePid); expect(receipt.beforeExitCode).toBe(0);
    for (const value of [receipt.oldGroupGone, receipt.sameOwnedStoreRoot, receipt.sameBinaryAndConfiguration, receipt.readyOnSameUrl]) expect(value).toBe(true);
    restarts.push(receipt); await signIn(); return receipt;
  };
  const openOwner = async (id: string, eventId: string, tab: "Journal" | "Bookmarks") => {
    const ownerId = (await history(id)).items.find(item => item.historyEventId === eventId)?.activityExecutionId;
    expect(ownerId, "The recorded event has an exact activity owner").toBeTruthy();
    await page.goto(`/workflows/instances/${id}?view=timeline&historyEvent=${encodeURIComponent(eventId)}`);
    await waitCapturedPlaybackReady(page);
    await page.getByRole("region", { name: "History event details", exact: true }).getByRole("button", { name: "Inspect owning execution →", exact: true }).click({ timeout: 30_000 });
    await expect(page).toHaveURL(url => url.searchParams.get("selected") === ownerId, { timeout: 30_000 });
    const views = page.getByRole("tablist", { name: "Activity execution views", exact: true });
    await expect(views).toBeVisible({ timeout: 30_000 });
    // Exact-owner navigation can retain the previous inspector while its scope resolves.
    // Its occurrence controls can also disappear when a singleton read settles.
    for (const text of ["Resolving execution scope…", "Loading recorded executions…"]) {
      await expect(page.getByText(text, { exact: true })).toHaveCount(0, { timeout: 30_000 });
    }
    await waitCapturedPlaybackReady(page);
    // The real scoped journal must contain this exact native event before acting
    // on its owner's tabs; a shared Event label or an updated URL is insufficient.
    const journal = page.getByRole("region", { name: "Activity journal", exact: true });
    await expect(journal.locator(`[data-history-event-id="${eventId}"]`)).toBeVisible({ timeout: 30_000 });
    const identity = journal.getByText("Execution identity", { exact: true });
    await frame(identity);
    await identity.click({ timeout: 30_000 });
    await expect(journal.getByText(ownerId!, { exact: true })).toBeVisible({ timeout: 30_000 });
    await identity.click({ timeout: 30_000 });
    const target = views.getByRole("tab", { name: tab, exact: true });
    await frame(target);
    await target.click({ timeout: 30_000 });
    await expect(target).toHaveAttribute("aria-selected", "true", { timeout: 30_000 });
    if (tab === "Bookmarks") {
      const records = page.getByRole("region", { name: "Activity bookmark history", exact: true });
      const expected = (await bookmarks(id)).items.filter(item => item.bookmark.identity.activityExecutionId === ownerId);
      expect(expected.length).toBeGreaterThan(0);
      for (const item of expected) {
        await expect(records.getByText(item.bookmark.identity.bookmarkId, { exact: true })).toBeVisible({ timeout: 30_000 });
      }
      await expect(target).toHaveAttribute("aria-selected", "true");
    }
  };
  const openLifecycle = async (id: string, eventId: string) => {
    await openOwner(id, eventId, "Bookmarks");
    const expected = (await bookmarks(id)).items.find(item => item.createdHistoryEventId === eventId);
    expect(expected, "The native created event identifies the physical handle").toBeTruthy();
    const records = page.getByRole("region", { name: "Activity bookmark history", exact: true });
    const handle = records.locator("article").filter({ has: records.getByText(expected!.bookmark.identity.bookmarkId, { exact: true }) });
    await handle.getByRole("button", { name: "View lifecycle →", exact: true }).click({ timeout: 30_000 });
    const lifecycle = page.getByRole("region", { name: "Bookmark lifecycle", exact: true });
    await expect(lifecycle).toBeVisible();
    await expect(lifecycle.getByText("Loading exact bookmark lifecycle…", { exact: true })).toHaveCount(0);
    await expect(lifecycle.getByRole("button", { name: "Inspect creation event →", exact: true })).toBeVisible();
    await expect(lifecycle.getByText(expected!.bookmark.identity.bookmarkId, { exact: true })).toBeVisible();
    return lifecycle;
  };
  // Each project starts at wall time before admitting any inspected run. The prior project
  // may have advanced its disposable clock; never restart a live approval backwards in time.
  await restart();
  const who = await api<{ userId: string }>("/runtime/user-task-identity");
  const taskTitle = `Restart approval ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`;
  const approval = await publish("approval restart", [node("review", task, { Title: taskTitle, AssigneeId: who.userId, Actions: ["Approve"] }), node("after", line, { text: "Approval resumed once" })], "Approve");
  let taskId = "";
  await expect.poll(async () => {
    taskId = (await api<{ items: { id: string; workflowExecutionId: string }[] }>("/runtime/user-tasks?view=mine&limit=100")).items.find(item => item.workflowExecutionId === approval.id)?.id ?? "";
    return taskId;
  }, { timeout: 30_000 }).not.toBe("");
  const before = { details: await details(approval.id), history: await history(approval.id), bookmarks: await bookmarks(approval.id),
    task: await api<unknown>(`/runtime/user-tasks/${taskId}`), occurrences: await api<{ items: unknown[]; sequenceToken: string; upperSequence: string }>(`${path(approval.id)}/history/occurrences?limit=50`) };
  expectQualifiedTimelineCoverage(before.history.coverage);
  expect(before.bookmarks.items).toHaveLength(1);
  const active = before.bookmarks.items[0]; expect(active.status).toBe("Active");
  const waitingLifecycle = await openLifecycle(approval.id, active.createdHistoryEventId!);
  await expect(waitingLifecycle.locator('[data-status="Active"]')).toHaveCount(1);
  await expect(waitingLifecycle).toContainText("Eligible");
  await frame(waitingLifecycle.locator('[data-status="Active"]'));
  await expect(waitingLifecycle.getByRole("button", { name: "Inspect creation event →", exact: true })).toBeEnabled();
  await capture(page, info, "diagnostics-approval-before-restart");
  await restart();
  const restored = { details: await details(approval.id), history: await history(approval.id), bookmarks: await bookmarks(approval.id),
    task: await api<unknown>(`/runtime/user-tasks/${taskId}`), occurrences: await api<{ items: unknown[] }>(`${path(approval.id)}/history/occurrences?${new URLSearchParams({ sequenceToken: before.occurrences.sequenceToken, limit: "50" })}`) };
  expect(restored.details.instance.status).toBe(before.details.instance.status);
  expect(restored.details.activities.find(item => item.activityExecutionId === active.bookmark.identity.activityExecutionId)?.status).toBe("Suspended"); assertRestartPrefix(before.history, restored.history);
  expect(restored.bookmarks.items[0].bookmark).toEqual(active.bookmark); expect(restored.task).toEqual(before.task);
  expect(restored.occurrences.items).toEqual(before.occurrences.items);
  await page.goto(`/user-tasks/${taskId}`);
  const approve = page.getByRole("region", { name: taskTitle, exact: true }).getByRole("button", { name: "Approve", exact: true });
  await expect(approve).toBeEnabled(); await approve.scrollIntoViewIfNeeded(); await capture(page, info, "diagnostics-approval-restored");
  await approve.press("Enter");
  await expect.poll(async () => (await details(approval.id)).instance.status, { timeout: 30_000 }).toBe("Completed");
  await expect.poll(async () => (await api<{ status: string }>(`/runtime/user-tasks/${taskId}`)).status, { timeout: 30_000 }).toBe("Completed");
  const completed = { occurrences: await api<{ items: unknown[] }>(`${path(approval.id)}/history/occurrences?limit=50`), history: await history(approval.id), bookmarks: await bookmarks(approval.id), task: await api<unknown>(`/runtime/user-tasks/${taskId}`) };
  expect(completed.bookmarks.items[0].status).toBe("Consumed");
  expect(completed.history.items.filter(item => item.name === "BookmarkConsumed")).toHaveLength(1);
  let lifecycle = await openLifecycle(approval.id, active.createdHistoryEventId!);
  await expect(lifecycle.getByRole("button", { name: "Inspect consumed event →", exact: true })).toBeVisible();
  await capture(page, info, "diagnostics-approval-consumed");
  await restart();
  const retained = { occurrences: await api<{ items: unknown[] }>(`${path(approval.id)}/history/occurrences?limit=50`), history: await history(approval.id), bookmarks: await bookmarks(approval.id), task: await api<unknown>(`/runtime/user-tasks/${taskId}`) };
  assertRestartPrefix(completed.history, retained.history);
  expect(retained.occurrences.items).toEqual(completed.occurrences.items); expect(retained.bookmarks.items[0].bookmark).toEqual(active.bookmark);
  expect(retained.bookmarks.items[0].endedHistoryEventId).toBe(completed.bookmarks.items[0].endedHistoryEventId); expect(retained.task).toEqual(completed.task);
  lifecycle = await openLifecycle(approval.id, active.createdHistoryEventId!);
  await expect(lifecycle.locator('[data-status="Consumed"]')).toHaveCount(1);
  await capture(page, info, "diagnostics-approval-retained-after-restart");

  const expiry = await publish("passive expiry", [node("wait", event, { EventName: `expiry-${crypto.randomUUID()}`, CanStartWorkflow: false, ExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString() })]);
  const expiryBefore = { history: await history(expiry.id), bookmarks: await bookmarks(expiry.id) };
  expect(expiryBefore.bookmarks.items[0].bookmark.expiresAt).toBeTruthy(); expect(expiryBefore.bookmarks.items[0].eligibility).toBe("eligible");
  const advanced = await api<unknown>(`/__qualification/activity-playback/${expiry.id}/advance-expiry`, "POST", {});
  const expired = { details: await details(expiry.id), history: await history(expiry.id), bookmarks: await bookmarks(expiry.id) };
  expect(expired.details.activities.find(item => item.activityExecutionId === expiryBefore.bookmarks.items[0].bookmark.identity.activityExecutionId)?.status).toBe("Suspended"); expect(expired.history.items).toEqual(expiryBefore.history.items);
  const passive = expired.bookmarks.items[0]; expect(passive.status).toBe("Active"); expect(passive.eligibility).toBe("expired");
  expect(passive.bookmark).toEqual(expiryBefore.bookmarks.items[0].bookmark); expect(passive.endedHistoryEventId ?? null).toBeNull();
  expect(Date.parse(passive.eligibilityObservedAt)).toBeGreaterThanOrEqual(Date.parse(passive.bookmark.expiresAt!));
  lifecycle = await openLifecycle(expiry.id, passive.createdHistoryEventId!);
  await expect(lifecycle).toContainText("Expired"); await expect(lifecycle).toContainText("It does not wake, time out or cancel the activity.");
  await capture(page, info, "diagnostics-passive-expiry");
  await openOwner(expiry.id, passive.createdHistoryEventId!, "Journal");
  const expiryJournal = page.getByRole("region", { name: "Activity journal", exact: true });
  await expect(expiryJournal).toContainText("Bookmark created");
  await frame(expiryJournal.getByText("Bookmark created", { exact: true }));
  await capture(page, info, "diagnostics-passive-expiry-journal");
  await cancel(expiry.id); // Explicit later native cancellation, never attributed to clock passage.

  const duplicate = await publish("duplicate", [node("first", event, { EventName: `duplicate-${crypto.randomUUID()}`, CanStartWorkflow: false }), node("second", event, { EventName: `held-${crypto.randomUUID()}`, CanStartWorkflow: false })]);
  const duplicateBefore = await bookmarks(duplicate.id);
  const dispatch = await api<{ statuses: string[]; matchedReadBranches: number }>(`/__qualification/activity-playback/${duplicate.id}/duplicate-event`, "POST", {});
  expect(dispatch.statuses).toEqual(["Dispatched", "Duplicate"]); expect(dispatch.matchedReadBranches).toBe(2);
  const duplicateHistory = await history(duplicate.id), duplicateBookmarks = await bookmarks(duplicate.id);
  const duplicateEvent = duplicateHistory.items.filter(item => item.name === "DeliveryDuplicate"); expect(duplicateEvent).toHaveLength(1);
  expect(duplicateHistory.items.filter(item => item.name === "BookmarkConsumed")).toHaveLength(1);
  expect(duplicateBookmarks.items.filter(item => item.status === "Consumed")).toHaveLength(1);
  expect(duplicateBookmarks.items.find(item => item.status === "Consumed")!.bookmark.identity.bookmarkId).toBe(duplicateBefore.items[0].bookmark.identity.bookmarkId);
  await page.goto(`/workflows/instances/${duplicate.id}?view=timeline&historyEvent=${duplicateEvent[0].historyEventId}`);
  await waitCapturedPlaybackReady(page);
  const duplicateDetails = page.getByRole("region", { name: "History event details", exact: true });
  await expect(duplicateDetails).toContainText("Duplicate delivery observed");
  await frame(duplicateDetails.getByRole("heading", { name: "Duplicate delivery observed", exact: true }));
  await capture(page, info, "diagnostics-duplicate-delivery");
  lifecycle = await openLifecycle(duplicate.id, duplicateBefore.items[0].createdHistoryEventId!);
  await expect(lifecycle.locator('[data-status="Consumed"]')).toHaveCount(1); await capture(page, info, "diagnostics-single-consumption");
  await page.goto(`/workflows/instances/${duplicate.id}?view=timeline`); await waitCapturedPlaybackReady(page);
  const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
  await timeline.getByText("Filter events", { exact: true }).click();
  await timeline.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
  await timeline.getByText("Filter events", { exact: true }).click();
  await expect(timeline).toHaveAttribute("aria-busy", "false");
  const rows = timeline.getByRole("list", { name: "Run history events", exact: true }).locator("[data-history-event-id]");
  await expect(rows.first()).toBeVisible();
  const rendererIds = () => rows.evaluateAll(elements => elements.map(element => element.getAttribute("data-history-event-id")));
  const candidateIds = await rendererIds();
  const selectedEventId = candidateIds.at(-1)!;
  const selectedRow = timeline.locator(`[data-history-event-id="${selectedEventId}"]`);
  await selectedRow.press("Enter");
  await expect(selectedRow).toHaveAttribute("aria-pressed", "true");
  const selectedDetails = page.getByRole("region", { name: "History event details", exact: true });
  await expect(selectedDetails).toBeVisible();
  const selectedHeading = await selectedDetails.getByRole("heading").first().textContent();
  // Establish the failure baseline AFTER the deliberate selection/filter transition.
  // The separate outside-filter selected-event affordance is not a loaded timeline row.
  const beforeStaleIds = await rendererIds();
  expect(beforeStaleIds.length).toBeGreaterThan(1);
  expect(beforeStaleIds.every(id => duplicateHistory.items.some(event => event.historyEventId === id))).toBe(true);
  const failedRequests: string[] = [], recoveredRequests: string[] = [];
  let staleIds: (string | null)[] = [];
  const matcher = (url: URL) => url.pathname === `${path(duplicate.id)}/history`;
  await page.route(matcher, async route => { failedRequests.push(route.request().url()); await route.abort("failed"); });
  try {
    await expect.poll(() => failedRequests.length, { timeout: 30_000 }).toBeGreaterThan(0);
    await expect(timeline.getByText("Showing last confirmed history", { exact: true })).toBeVisible();
    await expect(timeline.getByRole("list", { name: "Run history events", exact: true })).toBeVisible();
    await expect(timeline.getByRole("button", { name: "Retry history", exact: true })).toBeVisible();
    staleIds = await rendererIds(); expect(staleIds).toEqual(beforeStaleIds);
    await expect(selectedRow).toHaveAttribute("aria-pressed", "true");
    await expect(selectedDetails.getByRole("heading").first()).toHaveText(selectedHeading!);
    const retry = timeline.getByRole("button", { name: "Retry history", exact: true });
    await frame(retry);
    await capture(page, info, "diagnostics-history-stale");
  } finally { await page.unroute(matcher); }
  const response = page.waitForResponse(message => matcher(new URL(message.url())) && message.ok());
  await timeline.getByRole("button", { name: "Retry history", exact: true }).click();
  recoveredRequests.push((await response).url()); await expect(timeline.getByText("Live history", { exact: true })).toBeVisible();
  await expect(timeline.getByRole("button", { name: "Retry history", exact: true })).toHaveCount(0);
  expect((await history(duplicate.id)).items).toEqual(duplicateHistory.items);
  const recoveredIds = await rendererIds(); expect(recoveredIds).toEqual(beforeStaleIds);
  await expect(selectedRow).toHaveAttribute("aria-pressed", "true");
  await expect(selectedDetails.getByRole("heading").first()).toHaveText(selectedHeading!);
  await capture(page, info, "diagnostics-history-recovered");
  const oracle = info.outputPath("timeline-diagnostics-oracle.json");
  writeFileSync(oracle, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, approval, taskId, before, restored, completed, retained, restarts,
    expiry, expiryBefore, advanced, expired, duplicate, duplicateBefore, dispatch, duplicateHistory, duplicateBookmarks, failedRequests, recoveredRequests, renderer: { beforeStaleIds, staleIds, recoveredIds, selectedEventId, selectedHeading },
    boundaries: { expiry: "Deadline authored before FIRST native creation; actual source clock advances only after real quiescence", duplicate: "Actual native dispatcher/EF reads/actor idempotency; nonterminal second wait", stale: "Abort genuine renderer reads; no substituted response body", restart: "Main-thread-owned distinct backend processes reopen the same owned store" } }, null, 2));
  await info.attach("timeline-diagnostics-oracle.json", { path: oracle, contentType: "application/json" });
  await cancel(duplicate.id);
}
