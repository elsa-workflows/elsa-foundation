import { rangePositionX } from "./rangePosition";
import { expect, request, type APIRequestContext, type Locator, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import type { PlaybackPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/activityPlayback";
import type { RuntimeHistoryBookmarkPage, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { ActivityExecutionInspection, WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Cleanup = (cleanup: () => Promise<void>) => void;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: unknown } };
const types = { sequence: "Elsa.Activities.Sequence.Activities.Sequence", flowchart: "Elsa.Activities.Flowchart.Activities.Flowchart",
  foreach: "Elsa.Activities.ForEach.Activities.ForEach", parallel: "Elsa.Activities.Parallel.Activities.Parallel",
  if: "Elsa.Activities.If.Activities.If", write: "Elsa.Activities.Primitives.Activities.WriteLine",
  event: "Elsa.Activities.Primitives.Activities.Event", fault: "Elsa.Activities.Primitives.Activities.Fault" };

/** Owns only disposable, genuinely published/executed records. Renderer responses are never substituted. */
async function harness(page: Page, info: TestInfo, capture: Capture, register: Cleanup) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  if (!backend || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench required");
  if (info.project.name.startsWith("narrow")) await page.setViewportSize({ width: 320, height: 900 });
  const admin = await request.newContext({ storageState: await page.context().storageState() });
  const definitions: string[] = [], runs: string[] = [], observedRequests: string[] = [], materialRequests: string[] = [], payloadRequests: string[] = [];
  const restorers: Array<() => Promise<void>> = [];
  page.on("request", message => {
    if (message.url().includes("/history/occurrences?")) observedRequests.push(message.url());
    if (message.url().includes("/runtime/workflows/executables/")) materialRequests.push(message.url());
    if (message.url().includes("/value-evidence/") && message.url().endsWith("/payload")) payloadRequests.push(message.url());
  });
  async function api<T>(path: string, method = "GET", data?: unknown, client: APIRequestContext = admin, headers?: Record<string, string>, timeout = 15_000): Promise<T> {
    const response = await client.fetch(`${backend}${path}`, { method, data, headers, timeout });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  const path = (id: string) => `/runtime/workflows/instances/${encodeURIComponent(id)}`;
  async function details(id: string) { return api<WorkflowInstanceDetails>(path(id)); }
  async function settled(id: string, status = "Completed") {
    await expect.poll(async () => (await details(id)).instance.status, { timeout: 30_000 }).toBe(status);
    return details(id);
  }
  async function alter(id: string, kind: string, payload: unknown) {
    const accepted = await api<{ planId: string }>("/runtime/workflows/alteration-plans", "POST", {
      target: { executionIds: [id] }, alterations: [{ kind, schemaVersion: 1, payload }]
    }, admin, { "Idempotency-Key": `playback-${crypto.randomUUID()}` });
    await expect.poll(async () => (await api<{ status: string }>(`/runtime/workflows/alteration-plans/${accepted.planId}`)).status,
      { timeout: 30_000 }).toBe("Completed");
    const jobs = await api<{ items: { status: string; outcomes: unknown[] }[] }>(`/runtime/workflows/alteration-plans/${accepted.planId}/jobs/page?take=10`);
    expect(jobs.items).toHaveLength(1); expect(jobs.items[0].status).toBe("Succeeded");
    return { ...accepted, jobs: jobs.items };
  }
  register(async () => {
    const failures: unknown[] = [];
    try {
      for (const id of runs) try {
        if (!["Completed", "Faulted", "Cancelled"].includes((await details(id)).instance.status)) {
          await alter(id, "CancelWorkflow", {}); await settled(id, "Cancelled");
        }
      } catch (error) { failures.push(error); }
      for (const restore of restorers.reverse()) try { await restore(); } catch (error) { failures.push(error); }
      for (const id of definitions.reverse()) try { await api(`/design/workflows/definitions/${encodeURIComponent(id)}`, "DELETE"); }
      catch (error) { failures.push(error); }
    } finally { await admin.dispose(); }
    if (failures.length) throw new AggregateError(failures, "Owned playback cleanup failed");
  });
  const versions = new Map<string, string>();
  async function node(nodeId: string, kind: keyof typeof types, values: Record<string, unknown> = {}, payload?: unknown): Promise<Node> {
    const type = types[kind];
    if (!versions.has(type)) {
      const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
      const matches = catalog.items.filter(item => item.definition.activityTypeKey === type); expect(matches).toHaveLength(1);
      versions.set(type, matches[0].definition.headVersionId);
    }
    return { nodeId, activityVersionId: versions.get(type)!, inputs: Object.entries(values).map(([referenceKey, value]) =>
      ({ referenceKey, value: { expressionType: "Literal", value } })), outputs: [],
      ...(payload === undefined ? {} : { structure: { kind: `elsa.${kind}.structure`, schemaVersion: "1.0.0", payload } }) };
  }
  async function start(rootActivity: Node, held = false, inputs: Record<string, unknown> = {}, executionTimeout = 15_000) {
    const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
      name: `Playback ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`, state: { rootActivity, variables: [],
        inputs: Object.keys(inputs).map(referenceKey => ({ referenceKey, name: referenceKey, type: { alias: "String", collectionKind: "single" }, isNullable: false })), outputs: [],
        strategyOptions: { persistenceMode: "Durable", ...(held ? { incidentStrategy: { alias: "Qualification.PlaybackHeld", version: "1" } } : {}) } }
    });
    definitions.push(submitted.definition.id);
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const startedAt = Date.now();
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST",
      { sourceReferenceId: published.sourceReferenceId, inputs }, admin, undefined, executionTimeout);
    runs.push(run.workflowExecutionId); return { ...submitted, ...published, id: run.workflowExecutionId, setupExecutionMilliseconds: Date.now() - startedAt };
  }
  const occurrences = (id: string, query: Record<string, string> = {}) => api<PlaybackPage>(`${path(id)}/history/occurrences?${new URLSearchParams({ limit: "50", ...query })}`);
  const controls = page.getByRole("region", { name: "Activity playback", exact: true });
  async function open(id: string, count: number) {
    await page.goto(`/workflows/instances/${id}?view=timeline`);
    await expect(controls).toContainText(`Step 1 of ${count}`);
    await expect(page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Timeline", exact: true })).toHaveAttribute("aria-pressed", "true");
  }
  async function position(index: number, total: number, executionId?: string) {
    await expect(controls).toContainText(`Step ${index + 1} of ${total}`);
    await expect(controls).toHaveAttribute("aria-busy", "false");
    await expect(controls).not.toContainText("Loading step");
    if (executionId) {
      await expect(controls.locator("details")).toContainText(executionId);
    }
  }
  async function seek(index: number, total: number) {
    const range = controls.getByRole("slider", { name: "Activity position", exact: true });
    await range.focus(); await range.press("Home");
    if (index === total - 1) await range.press("End");
    else if (index) {
      const bounds = await range.boundingBox(); expect(bounds).not.toBeNull();
      await range.click({ position: { x: await rangePositionX(range, index, total), y: bounds!.height / 2 } });
      const landed = Number(await range.inputValue()); expect(Math.abs(landed - index)).toBeLessThanOrEqual(2);
      for (let i = 0; i < Math.abs(landed - index); i++) await range.press(landed > index ? "ArrowLeft" : "ArrowRight");
    }
    await position(index, total);
  }
  async function snap(name: string, target?: Locator) {
    if (target) await target.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest", behavior: "auto" }));
    else await controls.scrollIntoViewIfNeeded();
    if (target) await expect(target).toBeInViewport({ ratio: 1 });
    await capture(page, info, name);
  }
  async function inspectOwner(activityExecutionId: string, name: string) {
    const event = page.getByRole("region", { name: "History event details", exact: true });
    await event.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
    const dataTab = page.getByRole("tablist", { name: "Activity execution views", exact: true }).getByRole("tab", { name: "Data", exact: true });
    await dataTab.click(); await expect(dataTab).toHaveAttribute("aria-selected", "true");
    const details = page.locator(".wf-activity-execution-details");
    if (await details.getAttribute("open") === null) await details.locator(":scope > summary").press("Enter");
    const owner = details.locator(".wf-activity-meta-item").filter({ has: page.locator("dt").filter({ hasText: /^Activity [Ee]xecution ID$/ }) });
    const value = owner.locator("dd code");
    await expect(value).toHaveText(activityExecutionId);
    await expect(page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Timeline", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(controls.getByRole("button", { name: "Pause playback", exact: true })).toHaveCount(0);
    await snap(name, owner);
    return { expectedActivityExecutionId: activityExecutionId, inspectedActivityExecutionId: await value.textContent(), dataTabSelected: await dataTab.getAttribute("aria-selected"), capture: name };
  }
  async function oracle(name: string, facts: unknown) {
    const output = info.outputPath(name); writeFileSync(output, JSON.stringify({ source: process.env.UX_SOURCE_SHA,
      kind: "real-owned-runtime", facts, rendererOccurrenceRequests: observedRequests }, null, 2));
    await info.attach(name, { path: output, contentType: "application/json" });
  }
  async function hold(id: string) {
    const nonce = crypto.randomUUID().replaceAll("-", "");
    const route = `/__qualification/activity-playback/${id}/read-hold/${nonce}`;
    await api(route, "POST");
    const match = `**/runtime/workflows/instances/${id}/history/occurrences?*`;
    await page.route(match, request => request.continue({ headers: { ...request.request().headers(), "X-Activity-Playback-Qualification": nonce } }));
    return { async entered() {
      await expect.poll(async () => (await api<{ entered: boolean }>(route)).entered).toBe(true);
      const state = await api<{ entered: boolean; released: boolean; timedOut: boolean }>(route);
      expect(state).toEqual({ entered: true, released: false, timedOut: false }); return state;
    }, async release() { await api(route, "DELETE"); await page.unroute(match); }, route };
  }
  return { api, admin, backend, path, details, settled, alter, node, start, occurrences, controls, open, position, seek, snap, inspectOwner, oracle, hold,
    observedRequests, materialRequests, payloadRequests, restore: (action: () => Promise<void>) => restorers.push(action) };
}

export async function playbackSeekingJourney(page: Page, info: TestInfo, capture: Capture, register: Cleanup) {
  const h = await harness(page, info, capture, register);
  const repeated = await h.node("repeat", "foreach", { Collection: Array.from({ length: 198 }, (_, index) => index) },
    { body: await h.node("repeated", "write", { text: "One real retained loop occurrence" }) });
  const run = await h.start(await h.node("root", "flowchart", {}, { activities: [repeated], startNodeId: "repeat",
    connections: [], nodeMetadata: {}, connectionMetadata: {} }), false, {}, 60_000);
  await h.settled(run.id); const first = await h.occurrences(run.id);
  expect(first.totalCount).toBe("200"); expect(first.items).toHaveLength(50); expect(first.coverage.capturedFromStart).toBe(true);
  const oracle: PlaybackPage[] = [];
  for (const ordinal of ["0", "99", "199", "50"]) oracle.push(await h.occurrences(run.id, { sequenceToken: first.sequenceToken!, position: ordinal }));
  const selected = oracle.map((value, index) => value.items.find(item => item.position === ["0", "99", "199", "50"][index])!);
  expect(selected.every(Boolean)).toBe(true); expect(new Set(selected.map(item => item.activityExecutionId)).size).toBe(4);
  await h.open(run.id, 200);
  for (const [index, ordinal] of [0, 99, 199, 50].entries()) {
    await h.seek(ordinal, 200); await h.position(ordinal, 200, selected[index].activityExecutionId);
  }
  await h.snap("playback-seek-older-repeat");
  await h.seek(0, 200); const gate = await h.hold(run.id); let entered;
  try {
    const range = h.controls.getByRole("slider", { name: "Activity position", exact: true }); await range.focus(); await range.press("End");
    entered = await gate.entered(); await expect(h.controls).toContainText("Loading step 200"); await h.snap("playback-seek-pending");
    for (let i = 0; i < 9; i++) await range.press("ArrowLeft");
    await h.position(190, 200);
  } finally { await gate.release(); }
  await h.position(190, 200); await h.snap("playback-seek-latest");
  let failed = false;
  const match = `**/runtime/workflows/instances/${run.id}/history/occurrences?*`;
  await page.route(match, async route => {
    if (!failed && new URL(route.request().url()).searchParams.get("position") === "191") { failed = true; await route.abort("failed"); }
    else await route.continue();
  });
  try {
    await h.controls.getByRole("button", { name: "Next", exact: true }).click();
    await expect(h.controls).toContainText("previous confirmed position is unchanged"); await expect(h.controls).toContainText("Step 191 of 200");
    await expect(h.controls).toContainText("Requested step 192"); await expect(h.controls).not.toContainText("Loading step 192");
    await h.snap("playback-seek-failed");
    // Ordinary view navigation pauses transport and must preserve this exact recovery target.
    await page.getByRole("navigation", { name: "Run workspace views" }).getByRole("button", { name: "Timeline", exact: true }).click();
    await h.controls.getByRole("button", { name: "Retry requested activity", exact: true }).click();
    await h.position(191, 200); await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeVisible();
    await h.snap("playback-seek-retried");
  } finally { await page.unroute(match); }
  expect(failed).toBe(true);
  const requests = h.observedRequests.map(value => new URL(value).searchParams);
  expect(requests.every(query => query.get("limit") === "50")).toBe(true);
  expect(requests.length).toBeLessThanOrEqual(60);
  const allowed = (ordinal: number) => ordinal === 0 || ordinal >= 48 && ordinal <= 52 || ordinal >= 97 && ordinal <= 101 || ordinal >= 190 && ordinal <= 199;
  expect(requests.every(query => !query.has("position") || allowed(Number(query.get("position"))))).toBe(true);
  await h.oracle("playback-seeking-oracle.json", { actualCount: first.totalCount, structuralEntriesIncluded: true, oracle, selected,
    setupExecutionMilliseconds: run.setupExecutionMilliseconds, setupExecutionBudgetMilliseconds: 60_000,
    heldNativeRead: entered, transportFailure: "One renderer request deliberately aborted; no HTTP response body substituted", latestRequestedPosition: "190" });
}

export async function playbackLiveWaitJourney(page: Page, info: TestInfo, capture: Capture, register: Cleanup) {
  const h = await harness(page, info, capture, register), eventName = `playback-wait-${crypto.randomUUID()}`;
  const activities = [await h.node("before", "write", { text: "Before real wait" }),
    await h.node("wait", "event", { EventName: eventName, CanStartWorkflow: false }), await h.node("after", "write", { text: "After real resume" })];
  const run = await h.start(await h.node("root", "flowchart", {}, { activities, startNodeId: "before", connections: [
    { id: "before-wait", source: { nodeId: "before", port: "Done" }, target: { nodeId: "wait" } },
    { id: "wait-after", source: { nodeId: "wait", port: "Done" }, target: { nodeId: "after" } }
  ], nodeMetadata: {}, connectionMetadata: {} }));
  const bookmarksPath = `${h.path(run.id)}/history/bookmarks?limit=10`;
  await expect.poll(async () => (await h.api<RuntimeHistoryBookmarkPage>(bookmarksPath)).items.length, { timeout: 30_000 }).toBe(1);
  const bookmark = (await h.api<RuntimeHistoryBookmarkPage>(bookmarksPath)).items[0].bookmark;
  const frozen = await h.occurrences(run.id); expect(frozen.totalCount).toBe("3");
  const waiter = frozen.items.find(item => item.activityExecutionId === bookmark.identity.activityExecutionId)!; expect(waiter).toBeTruthy();
  await h.open(run.id, 3); await h.seek(Number(waiter.position), 3); await h.position(Number(waiter.position), 3, waiter.activityExecutionId);
  await h.snap("playback-live-waiting");
  const advancePath = `/__qualification/activity-playback/${run.id}/advance-wait`;
  let advance: unknown;
  await expect.poll(async () => {
    const result = await h.admin.post(`${h.backend}${advancePath}`, { timeout: 15_000 });
    if (result.status() === 409) return false;
    expect(result.ok(), await result.text()).toBe(true); advance = await result.json(); return true;
  }, { timeout: 30_000 }).toBe(true);
  await h.api("/runtime/workflows/stimuli", "POST", { stimulusType: bookmark.stimulusType, stimulusHash: bookmark.stimulusHash,
    mode: "ResumeOnly", input: { source: "owned-playback-qualification" } });
  await h.settled(run.id);
  const nativeCookieRead = await h.admin.get(`${h.backend}${h.path(run.id)}`); expect(nativeCookieRead.status()).toBe(200);
  const exchange = await h.admin.get(`${h.backend}/_elsa/identity/token`); expect(exchange.status()).toBe(200);
  const issued = await exchange.json() as { accessToken: string }; expect(typeof issued.accessToken).toBe("string");
  const bearer = await request.newContext({ extraHTTPHeaders: { Authorization: `Bearer ${issued.accessToken}` } });
  let freshBearerStatus: number;
  try { freshBearerStatus = (await bearer.get(`${h.backend}${h.path(run.id)}`)).status(); expect(freshBearerStatus).toBe(200); }
  finally { await bearer.dispose(); }
  await expect(h.controls.getByRole("button", { name: "Include new history", exact: true })).toBeVisible({ timeout: 15_000 });
  await h.position(Number(waiter.position), 3, waiter.activityExecutionId); await h.snap("playback-live-frozen");
  const retained = await h.occurrences(run.id, { sequenceToken: frozen.sequenceToken!, position: waiter.position });
  expect(retained.totalCount).toBe("3"); expect(retained.upperSequence).toBe(frozen.upperSequence); expect(retained.hasNewerHistory).toBe(true);
  await h.controls.getByRole("button", { name: "Include new history", exact: true }).click();
  await h.position(Number(waiter.position), 4, waiter.activityExecutionId); await h.snap("playback-live-refreshed");
  const lifecycle = await h.api<RuntimeHistoryPage>(`${h.path(run.id)}/history?${new URLSearchParams({ limit: "100", bookmarkId: bookmark.identity.bookmarkId })}`);
  const created = lifecycle.items.find(item => item.name === "BookmarkCreated")!, consumed = lifecycle.items.find(item => item.name === "BookmarkConsumed")!;
  expect(created).toBeTruthy(); expect(consumed).toBeTruthy();
  const actualWait = Date.parse(consumed.occurredAt!) - Date.parse(created.occurredAt!);
  expect(actualWait).toBeGreaterThanOrEqual(52 * 60_000); expect(actualWait).toBeLessThan(53 * 60_000);
  await page.getByRole("region", { name: "Run timeline", exact: true }).getByRole("combobox", { name: "History category", exact: true }).selectOption("BookmarkLifecycle");
  await expect(page.getByText(/Handle lifetime 52m/)).toBeVisible(); await h.snap("playback-live-recorded-wait", page.getByRole("region", { name: "Run timeline", exact: true }).getByRole("button").filter({ hasText: /Handle lifetime 52m/ }));
  // Verify the source duration before introducing a presentation clock. Playback crosses this
  // actual long wait in two one-second steps; it never advances the runtime's source clock.
  const refreshed = await h.occurrences(run.id), waitPosition = Number(waiter.position);
  expect(waitPosition).toBeGreaterThan(0); expect(waitPosition).toBeLessThan(3);
  const presentationStart = new Date(Date.now() + 60_000);
  await page.clock.install({ time: new Date(presentationStart.getTime() - 60_000) }); await page.clock.pauseAt(presentationStart);
  await h.seek(waitPosition - 1, 4);
  await h.controls.getByRole("combobox", { name: "Playback speed" }).selectOption("1");
  await h.controls.getByRole("button", { name: "Play", exact: true }).click();
  await page.clock.runFor(999); await h.position(waitPosition - 1, 4);
  await page.clock.runFor(1); await h.position(waitPosition, 4, waiter.activityExecutionId);
  await page.clock.runFor(1000); await h.position(waitPosition + 1, 4, refreshed.items[waitPosition + 1].activityExecutionId);
  await h.oracle("playback-live-oracle.json", { bookmark, frozen, retained, advance, created, consumed, actualWaitMilliseconds: actualWait,
    sourceClockOnlyForRecordedDuration: true, browserClockWasNotAdvancedUntilDurationVerified: true,
    presentationStart: presentationStart.toISOString(), compressedWaitPresentationMilliseconds: 2000,
    securityClockProof: { nativeCookieStatus: nativeCookieRead.status(), freshBearerStatus } });
}

export async function playbackCoverageJourney(page: Page, info: TestInfo, capture: Capture, register: Cleanup) {
  const h = await harness(page, info, capture, register);
  const policy = (await h.api<{ requested: Record<string, unknown> }>("/runtime/workflows/diagnostics/settings")).requested;
  h.restore(async () => { await h.api("/runtime/workflows/diagnostics/settings", "PUT", policy); });
  await h.api("/runtime/workflows/diagnostics/settings", "PUT", { scope: policy.scope, defaultLevel: "DiagnosticSnapshot",
    subjectOverrides: { durableValues: "Metadata", incidents: "Metadata", diagnostics: "Metadata" } });
  const marker = `PLAYBACK_VALUE_${crypto.randomUUID()}`, firstNode = await h.node("first", "write");
  firstNode.inputs = [{ referenceKey: "text", value: { expressionType: "WorkflowRequest", value: { memberKey: "playbackValue", path: null } } }];
  const activities = [firstNode, await h.node("second", "write", { text: "Second" }), await h.node("third", "write", { text: "Third" })];
  const run = await h.start(await h.node("root", "flowchart", {}, { activities, startNodeId: "first", connections: [
    { id: "first-second", source: { nodeId: "first", port: "Done" }, target: { nodeId: "second" } },
    { id: "second-third", source: { nodeId: "second", port: "Done" }, target: { nodeId: "third" } }
  ], nodeMetadata: {}, connectionMetadata: {} }), false, { playbackValue: marker });
  const completed = await h.settled(run.id), original = await h.occurrences(run.id); expect(original.totalCount).toBe("4");
  const first = completed.activities.find(item => item.authoredActivityId === "first")!;
  const inspectionPath = `${h.path(run.id)}/activity-executions/${first.activityExecutionId}`;
  const metadata = await h.api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(inspectionPath);
  const evidence = metadata.valueSnapshots.find(value => value.inputKey === "text")!; expect(evidence?.evidenceId).toBeTruthy();
  const payloadPath = `${inspectionPath}/value-evidence/${encodeURIComponent(evidence.evidenceId)}/payload`;
  const adminPayload = await h.api<{ payload: { preview: string } }>(payloadPath); expect(adminPayload.payload.preview).toBe(marker);
  const signedIn = await page.request.post(`${h.backend}/_elsa/identity/login`, { data: { username: "history-reader", password: "Password123!" } });
  expect(signedIn.ok()).toBe(true);
  const deniedPayload = await page.request.get(`${h.backend}${payloadPath}`); expect(deniedPayload.status()).toBe(403);
  expect(await deniedPayload.text()).not.toContain(marker);
  await h.open(run.id, 4); await h.seek(1, 4); expect(h.payloadRequests).toEqual([]);
  await h.snap("playback-access-metadata-only");
  const gate = await h.hold(run.id); let entered, authorityRevision = "";
  const restoreReader = async () => {
    const authority = await h.api<{ revision: string }>("/__qualification/principals/1/authority");
    await h.api("/__qualification/principals/1/grant-profile", "POST", { profile: "history-reader", revision: authority.revision });
  };
  h.restore(restoreReader);
  try {
    const deniedRead = page.waitForResponse(response => response.url().includes(`${h.path(run.id)}/history/occurrences?`) && response.status() === 404);
    await h.controls.getByRole("button", { name: "Next", exact: true }).click(); entered = await gate.entered();
    await h.snap("playback-access-pending");
    const authority = await h.api<{ revision: string }>("/__qualification/principals/1/authority");
    const changed = await h.api<{ revision: string }>("/__qualification/principals/1/grant-profile", "POST", { profile: "history-no-structure", revision: authority.revision });
    expect(changed.revision).not.toBe(authority.revision); authorityRevision = changed.revision;
    await gate.release(); const response = await deniedRead;
    const body = await response.text(); expect(body).not.toContain(first.activityExecutionId); expect(body).not.toContain(original.sequenceToken!);
    await expect(h.controls).toContainText("unavailable"); await expect(h.controls.locator("details")).toHaveCount(0);
    await expect(page.getByRole("region", { name: "History event details", exact: true })).toHaveCount(0);
    await h.snap("playback-access-revoked");
  } finally { await gate.release(); }
  await restoreReader();
  const adminSignIn = await page.request.post(`${h.backend}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } }); expect(adminSignIn.ok()).toBe(true);
  await h.open(run.id, 4);
  const beforePurge = await h.occurrences(run.id);
  const purge = await h.api<{ purgedThrough: string; afterPurgedThrough: string }>(`/__qualification/activity-playback/${run.id}/purge-first-prefix`, "POST");
  expect(BigInt(purge.purgedThrough)).toBeGreaterThan(0n); expect(purge.afterPurgedThrough).toBe(purge.purgedThrough);
  const invalidated = await h.occurrences(run.id, { sequenceToken: beforePurge.sequenceToken!, position: "0" }); expect(invalidated.state).toBe("range-invalidated");
  // Both the selected-event observer and the sequence observer revalidate recording.
  // A purged event can retire the cursor before another navigation is possible;
  // its opaque 404 must not be interpreted as a retention-specific disclosure.
  await expect(h.controls).toContainText(/The retained activity range changed\.|The selected recorded observation is unavailable\./, { timeout: 15_000 });
  await expect(h.controls).toContainText("No confirmed activity position");
  await expect(h.controls.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
  await expect(h.controls.getByRole("slider", { name: "Activity position", exact: true })).toHaveCount(0);
  await expect(h.controls.locator("details")).toHaveCount(0);
  await expect(page.getByRole("region", { name: "History event details", exact: true })).toHaveCount(0);
  await h.snap("playback-coverage-invalidated");
  const partial = await h.occurrences(run.id); expect(partial.state).toBe("partial"); expect(BigInt(partial.totalCount!)).toBeGreaterThan(0n);
  expect(BigInt(partial.totalCount!)).toBeLessThan(4n);
  await h.controls.getByRole("button", { name: "Refresh playback", exact: true }).click(); await h.position(0, Number(partial.totalCount));
  await expect(h.controls).toContainText("Partial playback range"); await h.snap("playback-coverage-retained");
  const nonce = crypto.randomUUID().replaceAll("-", ""), faultPath = `/__qualification/activity-playback/${run.id}/pin-read-fault/${nonce}`;
  await h.api(faultPath, "POST");
  const pinMatch = `**/runtime/workflows/executables/${run.artifactId}*`;
  await page.route(pinMatch, route => route.continue({ headers: { ...route.request().headers(), "X-Activity-Playback-Qualification": nonce } }));
  let pinFault;
  try {
    await page.reload(); await h.position(0, Number(partial.totalCount));
    pinFault = await h.api<{ entered: boolean; released: boolean; timedOut: boolean }>(`/__qualification/activity-playback/${run.id}/read-hold/${nonce}`);
    expect(pinFault).toEqual({ entered: true, released: false, timedOut: false });
    await expect(h.controls).toContainText(/pinned diagram.*unavailable/i); await h.snap("playback-coverage-pin-unavailable");
    expect(h.materialRequests.every(value => new URL(value).pathname.startsWith(`/runtime/workflows/executables/${run.artifactId}`))).toBe(true);
  } finally { await h.api(`/__qualification/activity-playback/${run.id}/read-hold/${nonce}`, "DELETE"); await page.unroute(pinMatch); }

  const profiles = JSON.parse(process.env.UX_PLAYBACK_PROFILES ?? "{}") as Record<string, { backendUrl: string; studioUrl: string; removedFeatures: string[] }>;
  expect(Object.keys(profiles).sort()).toEqual(["disabled", "uncaptured", "unsupported"]);
  const profileFacts: unknown[] = [];
  for (const name of ["disabled", "uncaptured", "unsupported"] as const) {
    const profile = profiles[name]; expect(new URL(profile.backendUrl).hostname).toBe("localhost"); expect(new URL(profile.studioUrl).hostname).toBe("localhost");
    const client = await request.newContext(); let definitionId = "";
    register(async () => { try { if (definitionId) {
      const response = await client.delete(`${profile.backendUrl}/design/workflows/definitions/${definitionId}`, { timeout: 5_000 }); expect(response.ok()).toBe(true);
    } } finally { await client.dispose(); } });
    const remote = async <T,>(path: string, method = "GET", data?: unknown): Promise<T> => {
      const response = await client.fetch(`${profile.backendUrl}${path}`, { method, data, timeout: 15_000 });
      expect(response.ok(), `${name}: ${path}: ${response.status()} ${await response.text()}`).toBe(true); return response.json() as Promise<T>;
    };
    const authenticated = await client.post(`${profile.backendUrl}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } }); expect(authenticated.ok()).toBe(true);
    const catalog = await remote<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(types.write)}`);
    const version = catalog.items.find(item => item.definition.activityTypeKey === types.write)?.definition.headVersionId; expect(version).toBeTruthy();
    const submitted = await remote<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
      name: `Playback ${name} ${info.project.name}`, state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: version,
        inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: name } }], outputs: [] } }
    }); definitionId = submitted.definition.id;
    const published = await remote<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const started = await remote<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    await expect.poll(async () => (await remote<WorkflowInstanceDetails>(h.path(started.workflowExecutionId))).instance.status, { timeout: 30_000 }).toBe("Completed");
    const admission = await remote<{ captureEnabled: boolean | null; policyId: string | null; headPresent: boolean; historyReaderPresent: boolean }>(`/__qualification/activity-playback/${started.workflowExecutionId}/admission`);
    expect(admission.captureEnabled).toBe(name === "disabled" ? false : null); expect(admission.policyId).toBeNull(); expect(admission.headPresent).toBe(false);
    expect(admission.historyReaderPresent).toBe(name !== "unsupported");
    const signIn = await page.request.post(`${profile.backendUrl}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } }); expect(signIn.ok()).toBe(true);
    await page.goto(`${profile.studioUrl}/workflows/instances/${started.workflowExecutionId}?view=timeline`);
    await expect(h.controls).toContainText(name === "disabled" ? "Recording was disabled" : name === "uncaptured" ? "No recording was captured" : /does not support|unsupported/);
    await expect(h.controls.getByRole("slider")).toHaveCount(0); await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeDisabled();
    await expect(page.getByText("Loading run history workspace…", { exact: true })).toHaveCount(0);
    await h.snap(`playback-coverage-${name}`); profileFacts.push({ name, ...profile, definitionId, published, started, admission });
  }
  await h.oracle("playback-coverage-oracle.json", { original, beforePurge, purge, invalidated, partial, metadata: { evidenceId: evidence.evidenceId,
    expectedOwnedPreview: marker, positiveAdminPreview: adminPayload.payload.preview,
    actualReaderPayloadStatus: deniedPayload.status() }, heldRead: entered, authorityRevision, pinFault, originalPin: run.artifactId, profileFacts,
    notClaimed: ["A natural dangling pin or separate artifact ACL", "An invented located internal gap", "A manufactured coarse legacy format", "An upgrade/backfill experiment"] });
}

export async function playbackTransportJourney(page: Page, info: TestInfo, capture: Capture, register: Cleanup) {
  const h = await harness(page, info, capture, register);
  await page.emulateMedia({ reducedMotion: "reduce" });
  const choice = await h.node("choice", "if", { Condition: true }, {
    then: await h.node("taken", "write", { text: "Taken branch" }), else: await h.node("not-visited", "write", { text: "No recorded visit expected" }) });
  const each = await h.node("repeat", "foreach", { Collection: ["first", "second"] }, { body: await h.node("repeated", "write", { text: "Repeated occurrence" }) });
  const parallel = await h.node("parallel", "parallel", {}, { branches: [
    { name: "left", activity: await h.node("left", "write", { text: "Left" }) },
    { name: "right", activity: await h.node("right", "write", { text: "Right" }) }
  ] });
  const run = await h.start(await h.node("root", "sequence", {}, { activities: [choice, each, parallel] }));
  const observed = await h.settled(run.id); const sequence = await h.occurrences(run.id);
  expect(sequence.state).toBe("partial"); expect(sequence.coverage.state).toBe("partial");
  expect(sequence.coverage.limitation).toContain("cannot establish the source history settlement");
  expect(sequence.totalCount).toBe("9"); expect(sequence.items).toHaveLength(9);
  const repeated = observed.activities.filter(item => item.authoredActivityId === "repeated"); expect(repeated).toHaveLength(2);
  expect(sequence.items.filter(item => repeated.some(row => row.activityExecutionId === item.activityExecutionId))).toHaveLength(2);
  expect(observed.activities.some(item => item.authoredActivityId === "not-visited")).toBe(false);
  expect(new Set(sequence.items.map(item => item.activityExecutionId)).size).toBe(9);
  const branches = observed.activities.filter(item => ["left", "right"].includes(item.authoredActivityId)); expect(branches).toHaveLength(2);
  expect(sequence.items.filter(item => branches.some(row => row.activityExecutionId === item.activityExecutionId)).every(item => item.parentActivityExecutionId)).toBe(true);
  const clockStart = new Date(Date.parse(sequence.readAt) + 60_000);
  await page.clock.install({ time: new Date(clockStart.getTime() - 60_000) }); await h.open(run.id, 9); await page.clock.pauseAt(clockStart);
  await h.position(0, 9, sequence.items[0].activityExecutionId);
  const next = h.controls.getByRole("button", { name: "Next", exact: true });
  await next.focus(); await next.press("Enter"); await h.position(1, 9, sequence.items[1].activityExecutionId); await expect(next).toBeFocused();
  await h.controls.locator("details summary").click();
  await h.snap("playback-transport-next");
  await h.controls.locator("details summary").click();
  const forward = [sequence.items[0].activityExecutionId, sequence.items[1].activityExecutionId];
  for (let index = 2; index < sequence.items.length; index++) {
    await next.click(); await h.position(index, 9, sequence.items[index].activityExecutionId);
    forward.push(sequence.items[index].activityExecutionId);
  }
  const reverse = [sequence.items.at(-1)!.activityExecutionId];
  for (let index = sequence.items.length - 2; index >= 0; index--) {
    await h.controls.getByRole("button", { name: "Previous", exact: true }).click();
    await h.position(index, 9, sequence.items[index].activityExecutionId); reverse.push(sequence.items[index].activityExecutionId);
  }
  expect(forward).toEqual(sequence.items.map(item => item.activityExecutionId)); expect(reverse).toEqual([...forward].reverse());
  const autoplay = [sequence.items[0].activityExecutionId];
  await h.controls.getByRole("button", { name: "Play", exact: true }).click();
  for (let index = 1; index < sequence.items.length; index++) {
    await page.clock.runFor(1000); await h.position(index, 9, sequence.items[index].activityExecutionId);
    autoplay.push(sequence.items[index].activityExecutionId);
  }
  expect(autoplay).toEqual(forward); await expect(h.controls.getByRole("button", { name: "Replay", exact: true })).toBeEnabled();
  for (const [speed, cadence] of [["0.5", 2000], ["1", 1000], ["2", 500], ["4", 250]] as const) {
    await h.seek(0, 9); await h.controls.getByRole("combobox", { name: "Playback speed" }).selectOption(speed);
    await h.controls.getByRole("button", { name: "Play", exact: true }).click();
    await page.clock.runFor(cadence - 1); await h.position(0, 9);
    await page.clock.runFor(1); await h.position(1, 9, sequence.items[1].activityExecutionId);
    await h.controls.getByRole("button", { name: "Pause playback", exact: true }).click();
    await page.clock.runFor(cadence * 4); await h.position(1, 9);
  }
  const screens = page.getByRole("tablist", { name: "Open screens", exact: true });
  const runTabLabel = await screens.getByRole("tab", { selected: true }).getAttribute("aria-label"); expect(runTabLabel).toBeTruthy();
  await h.controls.getByRole("button", { name: "Play", exact: true }).click();
  await page.getByRole("button", { name: "New tab", exact: true }).click(); await expect(h.controls).toBeHidden();
  await page.clock.runFor(10_000);
  await screens.getByRole("tab", { name: runTabLabel!, exact: true }).click(); await h.position(1, 9, sequence.items[1].activityExecutionId);
  await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeVisible();
  await page.clock.runFor(10_000); await h.position(1, 9, sequence.items[1].activityExecutionId);
  await h.snap("playback-transport-paused");
  const primaryViews = page.getByRole("navigation", { name: "Run workspace views", exact: true });
  await h.controls.getByRole("button", { name: "Play", exact: true }).click();
  await primaryViews.getByRole("button", { name: "Diagram", exact: true }).click();
  await page.clock.runFor(10_000); await h.position(1, 9, sequence.items[1].activityExecutionId);
  await h.seek(3, 9); await h.position(3, 9, sequence.items[3].activityExecutionId);
  const selectedNode = page.locator(`.wf-run-diagram-view [data-runtime-node-id="${sequence.items[3].executableNodeId}"][data-selected="true"]`);
  // The transport is paused, but its reveal and visible-evidence effects still need presentation frames.
  // Interleave bounded frame drains with real network settling; do not scroll the node to mask failed reveal.
  await expect(selectedNode).toBeVisible();
  await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeVisible();
  const visibleLoading = page.locator(".wf-run-diagram-view").getByText("Loading visible execution evidence…", { exact: true }).filter({ visible: true });
  await page.clock.runFor(300);
  await expect.poll(async () => {
    await page.clock.runFor(50);
    const inViewport = await selectedNode.evaluate(element => {
      const box = element.getBoundingClientRect();
      return box.top >= 0 && box.bottom <= innerHeight && box.left >= 0 && box.right <= innerWidth;
    });
    return inViewport && await visibleLoading.count() === 0;
  }, { timeout: 10_000, intervals: [50, 100, 250] }).toBe(true);
  await expect(page.locator(".wf-run-diagram-view").getByText("Some visible execution evidence could not be loaded. Refresh to retry.", { exact: true }).filter({ visible: true })).toHaveCount(0);
  await expect(selectedNode).toBeInViewport({ ratio: 1 });
  await h.position(3, 9, sequence.items[3].activityExecutionId);
  await expect(h.controls.getByRole("slider", { name: "Activity position", exact: true })).toBeFocused();
  await capture(page, info, "playback-transport-diagram");
  await primaryViews.getByRole("button", { name: "Timeline", exact: true }).click(); await h.seek(1, 9);
  const event = page.getByRole("region", { name: "History event details", exact: true });
  await h.controls.getByRole("button", { name: "Play", exact: true }).click();
  await event.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  await h.position(1, 9, sequence.items[1].activityExecutionId);
  const dataTab = page.getByRole("tablist", { name: "Activity execution views", exact: true }).getByRole("tab", { name: "Data", exact: true });
  await dataTab.click(); await expect(dataTab).toHaveAttribute("aria-selected", "true");
  await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeVisible();
  const activityDetails = page.locator(".wf-activity-execution-details");
  if (await activityDetails.getAttribute("open") === null) await activityDetails.locator(":scope > summary").press("Enter");
  const exactOwnerRow = activityDetails.locator(".wf-activity-meta-item").filter({ has: page.locator("dt").filter({ hasText: /^Activity [Ee]xecution ID$/ }) });
  const exactOwner = exactOwnerRow.locator("dd code");
  await expect(exactOwner).toHaveText(sequence.items[1].activityExecutionId);
  await page.clock.runFor(10_000); await h.position(1, 9, sequence.items[1].activityExecutionId);
  await h.snap("playback-transport-activity", exactOwnerRow);
  const timeline = page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Timeline", exact: true });
  await timeline.click();
  const sameObservation = page.locator(`[data-history-event-id="${sequence.items[1].historyEventId}"]`);
  await sameObservation.click(); await expect(sameObservation).toBeFocused(); await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await expect(event).toContainText(sequence.items[1].activityExecutionId); await expect(event).toContainText(sequence.items[1].historyEventId);
  await page.clock.runFor(10_000); await h.position(1, 9, sequence.items[1].activityExecutionId);
  await expect(h.controls.getByRole("button", { name: "Play", exact: true })).toBeVisible(); await h.snap("playback-transport-return", sameObservation);
  const repeatedOwnerUi = [];
  const repeatedOccurrences = sequence.items.filter(item => repeated.some(row => row.activityExecutionId === item.activityExecutionId));
  for (const [index, entry] of repeatedOccurrences.entries()) {
    await h.seek(Number(entry.position), 9); await h.position(Number(entry.position), 9, entry.activityExecutionId);
    repeatedOwnerUi.push(await h.inspectOwner(entry.activityExecutionId, index === 0 ? "playback-transport-repeat-first" : "playback-transport-repeat-second"));
    await h.position(Number(entry.position), 9, entry.activityExecutionId);
  }
  await h.seek(8, 9); await expect(next).toBeDisabled(); await expect(h.controls.getByRole("button", { name: "Replay", exact: true })).toBeEnabled();
  await h.snap("playback-transport-end");
  await h.controls.getByRole("button", { name: "Replay", exact: true }).click(); await h.position(0, 9);
  await h.controls.getByRole("button", { name: "Pause playback", exact: true }).click();
  const root = observed.activities.find(item => item.authoredActivityId === "root")!;
  // Cadence assertions are complete and transport is paused. A full document navigation must
  // restore ordinary browser scheduling so the newly bootstrapped app can finish loading.
  await page.clock.resume();
  await page.goto(`/workflows/instances/${run.id}?${new URLSearchParams({ historyEvent: sequence.items[1].historyEventId,
    historyActivity: root.activityExecutionId, historyDescendants: "1" })}`);
  await h.position(1, 9, sequence.items[1].activityExecutionId);
  await page.getByRole("region", { name: "Run timeline", exact: true }).getByRole("checkbox", { name: "Include descendants", exact: true }).uncheck();
  await h.position(0, 1, root.activityExecutionId);
  await expect(page.getByRole("region", { name: "History event details", exact: true })).toContainText(root.activityExecutionId);
  await h.snap("playback-transport-scoped");
  const held = await h.start(await h.node("held-fault", "fault", { code: "playback.held", message: "Deliberate qualification fault" }), true);
  await expect.poll(async () => (await h.details(held.id)).incidents?.filter(item => item.resolutionOutcome?.actionKind === "WaitForIntervention").length ?? 0).toBe(1);
  const before = await h.details(held.id); expect(before.instance.status).toBe("Running");
  const source = before.activities.find(item => item.status === "Faulted")!; expect(source).toBeTruthy();
  const applied = await h.alter(held.id, "RetryFaultedActivity", { sourceActivityExecutionId: source.activityExecutionId, reason: "Playback identity qualification" });
  await expect.poll(async () => (await h.details(held.id)).activities.length).toBe(2);
  const after = await h.details(held.id), retried = after.activities.find(item => item.activityExecutionId !== source.activityExecutionId)!;
  expect(retried).toBeTruthy(); expect(after.activities.find(item => item.activityExecutionId === source.activityExecutionId)?.status).toBe("Superseded");
  const sourceInspection = await h.api<ActivityExecutionInspection>(`${h.path(held.id)}/activity-executions/${source.activityExecutionId}`);
  const successorInspection = await h.api<ActivityExecutionInspection>(`${h.path(held.id)}/activity-executions/${retried.activityExecutionId}`);
  expect(sourceInspection.workflowExecutionId).toBe(held.id); expect(sourceInspection.activityExecutionId).toBe(source.activityExecutionId);
  expect(successorInspection.workflowExecutionId).toBe(held.id); expect(successorInspection.activityExecutionId).toBe(retried.activityExecutionId);
  expect(sourceInspection.status).toBe("Superseded"); expect(retried.activityExecutionId).not.toBe(source.activityExecutionId);
  const retryHistory = await h.api<RuntimeHistoryPage>(`${h.path(held.id)}/history?${new URLSearchParams({ limit: "2",
    activityExecutionId: source.activityExecutionId, name: "ActivityRetryScheduled" })}`);
  expect(retryHistory.items).toHaveLength(1); expect(retryHistory.nextContinuationToken).toBeNull();
  expect(retryHistory.items[0].activityExecutionId).toBe(source.activityExecutionId);
  expect(retryHistory.items[0].metadata["history.relatedActivityExecutionId"]).toBe(retried.activityExecutionId);
  const retrySequence = await h.occurrences(held.id); expect(retrySequence.totalCount).toBe("2");
  await h.open(held.id, 2); await next.click(); await h.position(1, 2, retried.activityExecutionId);
  await h.controls.getByRole("button", { name: "Previous", exact: true }).click(); await h.position(0, 2, source.activityExecutionId);
  const retryOwnerUi = [await h.inspectOwner(source.activityExecutionId, "playback-transport-retry-source")];
  await h.position(0, 2, source.activityExecutionId);
  await next.click(); await h.position(1, 2, retried.activityExecutionId);
  retryOwnerUi.push(await h.inspectOwner(retried.activityExecutionId, "playback-transport-retry"));
  await h.position(1, 2, retried.activityExecutionId);
  const cancellation = await h.alter(held.id, "CancelWorkflow", {}); const cancelled = await h.settled(held.id, "Cancelled");
  await h.oracle("playback-transport-oracle.json", { sequence, forward, reverse, autoplay, source, retried, sourceInspection, successorInspection, retryHistory, applied, retrySequence,
    repeatedOwnerUi, retryOwnerUi, cancellation, cancelled: { workflowExecutionId: cancelled.instance.workflowExecutionId, status: cancelled.instance.status },
    retainedWorkspaceBackgroundMilliseconds: 10_000, pausedAfterReturnMilliseconds: 10_000,
    reducedMotion: true, presentationClockStart: clockStart.toISOString() });
}
