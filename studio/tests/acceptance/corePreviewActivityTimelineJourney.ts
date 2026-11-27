import { expect, request as browserRequest, type Page, type Response, type TestInfo } from "@playwright/test";
import { verifyLiveHistoryRevocation } from "./corePreviewHistoryAuthority";
import type { Capture, CorePreviewStack, Receipt } from "./corePreviewSmokeJourney";
import type { RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";

export async function corePreviewActivityTimelineJourney(page: Page, info: TestInfo, stack: CorePreviewStack,
  capture: Capture, receipt: Receipt) {
  const route = "ux-core-preview/activity-timeline";
  const api = async <T>(path: string, data?: object): Promise<T> => {
    const response = data
      ? await page.request.post(`${stack.backend}${path}`, { data })
      : await page.request.get(`${stack.backend}${path}`);
    expect(response.ok(), await response.text()).toBe(true);
    return await response.json() as T;
  };
  const node = async (nodeId: string, type: string, inputs: Record<string, unknown>, structure?: object) => {
    const catalog = await api<{ items: Array<{ definition: { activityTypeKey: string; headVersionId: string } }> }>(
      `/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const activityVersionId = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
    expect(activityVersionId, type).toBeTruthy();
    return { nodeId, activityVersionId, structure, outputs: [],
      inputs: Object.entries(inputs).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })) };
  };
  const endpoint = await node("start", "Elsa.Activities.Http.Activities.HttpEndpoint",
    { Path: route, CanStartWorkflow: true, SupportedMethods: ["POST"] });
  const answer = await node("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse", { StatusCode: 201, Body: "timeline ready" });
  const resumeRoute = `${route}/resume`;
  const wait = await node("wait", "Elsa.Activities.Http.Activities.HttpEndpoint",
    { Path: resumeRoute, CanStartWorkflow: false, SupportedMethods: ["POST"] });
  const continued = await node("continued", "Elsa.Activities.Http.Activities.WriteHttpResponse", { StatusCode: 200, Body: "timeline continued" });
  const finishRoute = `${route}/finish`;
  const finalWait = await node("final-wait", "Elsa.Activities.Http.Activities.HttpEndpoint",
    { Path: finishRoute, CanStartWorkflow: false, SupportedMethods: ["POST"] });
  const finish = await node("finish", "Elsa.Activities.Http.Activities.WriteHttpResponse", { StatusCode: 200, Body: "timeline settled" });
  const flowchart = await node("flow", "Elsa.Activities.Flowchart.Activities.Flowchart", {}, {
    kind: "elsa.flowchart.structure",
    schemaVersion: "1.0.0",
    payload: { activities: [endpoint, answer, wait, continued, finalWait, finish], startNodeId: "start",
      connections: [["start", "answer"], ["answer", "wait"], ["wait", "continued"], ["continued", "final-wait"], ["final-wait", "finish"]].map(([source, target]) => ({
        id: `${source}-${target}`, source: { nodeId: source, port: "Done" }, target: { nodeId: target }
      })) }
  });
  const workflow = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit",
    { name: "Core Preview activity timeline", state: { rootActivity: flowchart, variables: [], inputs: [], outputs: [] } });
  await api(`/publishing/workflows/${encodeURIComponent(workflow.version.id)}/publish`, {});
  const responseUrl = `${stack.backend}/workflows/http/${route}`;
  let runId: string | undefined;
  await expect.poll(async () => {
    const response = await page.request.post(responseUrl, { data: {} });
    if (response.status() === 404) return undefined;
    runId = response.headers()["x-elsa-workflow-instance-id"];
    return { status: response.status(), body: await response.text() };
  }, { timeout: 30_000 }).toEqual({ status: 201, body: "timeline ready" });
  expect(runId).toBeTruthy();

  const runPath = `/runtime/workflows/instances/${encodeURIComponent(runId!)}`;
  const runDetails = () => api<{ instance: { status: string }; activitiesDeferred: boolean; activities: object[] }>(runPath);
  expect(await runDetails()).toMatchObject({ instance: { status: "Running" }, activitiesDeferred: true, activities: [] });
  const browseResponse = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === `${runPath}/activity-executions` && url.searchParams.get("limit") === "100" &&
      !url.searchParams.has("executableNodeId") && !url.searchParams.has("ancestorActivityExecutionId") && response.ok();
  });
  await page.goto(`/workflows/instances/${encodeURIComponent(runId!)}`);
  const timeline = page.getByRole("region", { name: "Executed activities", exact: true });
  const evidence = await (await browseResponse).json() as { items: Array<{ execution: { activityExecutionId: string; authoredActivityId: string } }> };
  const rows = timeline.locator(".wf-activity-timeline-row");
  await expect(rows).toHaveCount(4);
  await expect(timeline.getByRole("button", { name: /^Http Endpoint /i })).toHaveCount(2);
  await expect(page.getByRole("button", { name: /^Http Endpoint\. Node ID: start\./i })).toBeVisible();
  const answerId = evidence.items.find(item => item.execution.authoredActivityId === "answer")?.execution.activityExecutionId;
  expect(answerId).toBeTruthy();
  const activity = timeline.locator(`.wf-activity-timeline-row[data-activity-execution-id="${answerId!}"]`);
  await expect(activity).toBeVisible();
  await expect(activity).toContainText(/Write HTTP Response/i);
  await capture(page, info, "activity-timeline");
  await activity.click();
  await expect(activity).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("complementary", { name: "Run details panel", exact: true })
    .getByRole("heading", { name: /Write HTTP Response/i })).toBeVisible();
  await capture(page, info, "activity-selection");

  const streamPath = `${runPath}/history/live`;
  const isStream = (response: Response) => new URL(response.url()).pathname.endsWith(streamPath) && response.ok();
  const streamCursor = (response: Response) => new URL(response.url()).searchParams.get("cursor");
  const firstStream = page.waitForResponse(isStream);
  const isHistory = (response: Response) => response.ok() && new URL(response.url()).pathname.endsWith(`${runPath}/history`);
  const initialHistory = page.waitForResponse(response => isHistory(response) && !new URL(response.url()).searchParams.has("cursor"));
  await page.getByRole("group", { name: "Timeline view", exact: true }).getByRole("button", { name: "Events", exact: true }).click();
  const history = page.getByRole("region", { name: "Run timeline", exact: true });
  await history.getByRole("button", { name: "All", exact: true }).click();
  const first = await (await initialHistory).json() as RuntimeHistoryPage;
  expect(first.coverage.policyId).toBe("kernel-activity-journal-v1");
  const waitingEvent = first.items.find(item => item.executableNodeId === "wait" && item.name === "ActivityStarted");
  expect(waitingEvent).toMatchObject({ workflowExecutionId: runId, authority: "CommittedTransition" });
  expect(waitingEvent?.activityExecutionId).toBeTruthy();
  expect(first.items.some(item => item.executableNodeId === "finish" ||
    item.executableNodeId === "wait" && item.name === "ActivityCompleted")).toBe(false);
  expect(first.catchUpToken).toBeTruthy();
  const historyRows = history.locator("[data-history-event-id]");
  const shownIds = () => historyRows.evaluateAll(items => items.map(item => item.getAttribute("data-history-event-id")));
  const prefixIds = first.items.map(item => item.historyEventId);
  await expect.poll(shownIds).toEqual(prefixIds);

  const opened = await firstStream;
  expect(opened.headers()["content-type"]).toContain("application/x-ndjson");
  expect(streamCursor(opened)).toBe(first.catchUpToken);
  await expect(history).toHaveAttribute("aria-busy", "false");
  await history.getByRole("button", { name: "Pause live updates", exact: true }).click();
  const resumeHistory = history.getByRole("button", { name: "Resume live updates", exact: true });
  await expect(resumeHistory).toBeVisible();
  await capture(page, info, "history-paused");

  const liveUrl = `${stack.backend}${streamPath}?limit=100&cursor=${encodeURIComponent(first.catchUpToken!)}`;
  const anonymous = await browserRequest.newContext({ maxRedirects: 0 });
  try { expect((await anonymous.get(liveUrl)).status()).toBe(401); }
  finally { await anonymous.dispose(); }
  expect((await page.request.get(`${liveUrl}&name=ActivityStarted`)).status()).toBe(400);

  let regularHistoryReads = 0;
  let liveConnections = 0;
  page.on("request", request => {
    const path = new URL(request.url()).pathname;
    if (path.endsWith(`${runPath}/history`)) regularHistoryReads++;
    if (path.endsWith(streamPath)) liveConnections++;
  });
  const resumed = await page.request.post(`${stack.backend}/workflows/http/${resumeRoute}`, { data: {} });
  expect(resumed.status(), await resumed.text()).toBe(200);
  expect(await resumed.text()).toBe("timeline continued");
  expect((await runDetails()).instance.status).toBe("Running");
  expect(await shownIds()).toEqual(prefixIds);
  const showNew = history.getByRole("button", { name: /new recorded events · Show/ });
  await expect(showNew).toHaveCount(0);

  const nextHistory = page.waitForResponse(isStream);
  await resumeHistory.click();
  const reconnected = await nextHistory;
  expect(streamCursor(reconnected)).toBe(first.catchUpToken);
  await expect(showNew).toBeVisible();
  const caughtUp = await showNew.textContent();
  expect(await shownIds()).toEqual(prefixIds);
  await capture(page, info, "history-catch-up");

  // Keep following on the same connection across another commit and inspect the real combined rows.
  const completed = await page.request.post(`${stack.backend}/workflows/http/${finishRoute}`, { data: {} });
  expect(completed.status(), await completed.text()).toBe(200);
  expect(await completed.text()).toBe("timeline settled");
  expect((await runDetails()).instance.status).toBe("Completed");
  const tail = await api<RuntimeHistoryPage>(`${runPath}/history?limit=100&cursor=${encodeURIComponent(first.catchUpToken!)}`);
  expect(tail.items).toEqual(expect.arrayContaining([
    expect.objectContaining({ executableNodeId: "wait", name: "ActivityCompleted", activityExecutionId: waitingEvent!.activityExecutionId }),
    expect.objectContaining({ executableNodeId: "finish", name: "ActivityCompleted" })
  ]));
  for (const item of tail.items) {
    expect(item.workflowExecutionId).toBe(runId);
    expect(item.authority).toBe("CommittedTransition");
    expect(item.sequence).toBeGreaterThan(first.upperSequence);
  }
  await expect(showNew).toHaveText(`${tail.items.length} new recorded events · Show`);
  expect(await showNew.textContent()).not.toBe(caughtUp);
  expect({ regularHistoryReads, liveConnections }).toEqual({ regularHistoryReads: 0, liveConnections: 1 });
  await showNew.click();
  const committedIds = [...prefixIds, ...tail.items.map(item => item.historyEventId)];
  expect(new Set(committedIds).size).toBe(committedIds.length);
  await expect.poll(shownIds).toEqual(committedIds);
  await capture(page, info, "history-committed");

  const revocation = process.env.UX_CORE_PREVIEW_HISTORY_AUTHORITY === "1"
    ? await verifyLiveHistoryRevocation(page, stack.backend, route, resumeRoute, runId!) : undefined;
  await receipt(info, "activity-timeline.json", { runId, publication: workflow.version.id, timeline: evidence,
    history: { prefix: first, resumedCursor: streamCursor(reconnected), tail, committedIds, revocation } });
}
