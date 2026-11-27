import { expect, type Page, type Response, type TestInfo } from "@playwright/test";
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
  const finish = await node("finish", "Elsa.Activities.Http.Activities.WriteHttpResponse", { StatusCode: 200, Body: "timeline settled" });
  const flowchart = await node("flow", "Elsa.Activities.Flowchart.Activities.Flowchart", {}, {
    kind: "elsa.flowchart.structure",
    schemaVersion: "1.0.0",
    payload: { activities: [endpoint, answer, wait, finish], startNodeId: "start",
      connections: [["start", "answer"], ["answer", "wait"], ["wait", "finish"]].map(([source, target]) => ({
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

  const idle = await page.waitForResponse(response => isHistory(response) && new URL(response.url()).searchParams.get("cursor") === first.catchUpToken);
  const waiting = await idle.json() as RuntimeHistoryPage;
  expect(waiting.items).toEqual([]);
  expect(waiting.upperSequence).toBe(first.upperSequence);
  expect(waiting.catchUpToken).toBeTruthy();
  await history.getByRole("button", { name: "Pause live updates", exact: true }).click();
  const resumeHistory = history.getByRole("button", { name: "Resume live updates", exact: true });
  await expect(resumeHistory).toBeVisible();
  await capture(page, info, "history-paused");

  const resumed = await page.request.post(`${stack.backend}/workflows/http/${resumeRoute}`, { data: {} });
  expect(resumed.status(), await resumed.text()).toBe(200);
  expect(await resumed.text()).toBe("timeline settled");
  await expect.poll(async () => (await runDetails()).instance.status).toBe("Completed");
  expect(await shownIds()).toEqual(prefixIds);
  const showNew = history.getByRole("button", { name: /new recorded events · Show/ });
  await expect(showNew).toHaveCount(0);

  const nextHistory = page.waitForResponse(response => isHistory(response) && new URL(response.url()).searchParams.has("cursor"));
  await resumeHistory.click();
  const update = await nextHistory;
  expect(new URL(update.url()).searchParams.get("cursor")).toBe(waiting.catchUpToken);
  const tail = await update.json() as RuntimeHistoryPage;
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
  expect(await shownIds()).toEqual(prefixIds);
  await capture(page, info, "history-catch-up");
  await showNew.click();
  const committedIds = [...prefixIds, ...tail.items.map(item => item.historyEventId)];
  expect(new Set(committedIds).size).toBe(committedIds.length);
  await expect.poll(shownIds).toEqual(committedIds);
  await capture(page, info, "history-committed");

  await receipt(info, "activity-timeline.json", { runId, publication: workflow.version.id, timeline: evidence,
    history: { prefix: first, waiting, resumedCursor: new URL(update.url()).searchParams.get("cursor"), tail, committedIds } });
}
