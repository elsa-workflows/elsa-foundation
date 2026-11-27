import { expect, type Page, type TestInfo } from "@playwright/test";
import type { Capture, CorePreviewStack, Receipt } from "./corePreviewSmokeJourney";

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
  const flowchart = await node("flow", "Elsa.Activities.Flowchart.Activities.Flowchart", {}, {
    kind: "elsa.flowchart.structure",
    schemaVersion: "1.0.0",
    payload: { activities: [endpoint, answer], startNodeId: "start",
      connections: [{ id: "start-answer", source: { nodeId: "start", port: "Done" }, target: { nodeId: "answer" } }] }
  });
  const workflow = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit",
    { name: "Core Preview activity timeline", state: { rootActivity: flowchart, variables: [], inputs: [], outputs: [] } });
  await api(`/publishing/workflows/${encodeURIComponent(workflow.version.id)}/publish`, {});
  const responseUrl = `${stack.backend}/workflows/http/${route}`;
  await expect.poll(async () => {
    const response = await page.request.post(responseUrl, { data: {} });
    return response.status() === 404 ? undefined : { status: response.status(), body: await response.text() };
  }, { timeout: 30_000 }).toEqual({ status: 201, body: "timeline ready" });

  const runsPath = `/runtime/workflows/instances/page?definitionId=${encodeURIComponent(workflow.definition.id)}&status=Completed&take=1`;
  let runId: string | undefined;
  await expect.poll(async () => {
    runId = (await api<{ items: Array<{ workflowExecutionId: string }> }>(runsPath)).items[0]?.workflowExecutionId;
    return runId;
  }).toBeTruthy();
  const runPath = `/runtime/workflows/instances/${encodeURIComponent(runId!)}`;
  const details = await api<{ activitiesDeferred: boolean; activities: object[] }>(runPath);
  expect(details).toMatchObject({ activitiesDeferred: true, activities: [] });
  const browseResponse = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === `${runPath}/activity-executions` && url.searchParams.get("limit") === "100" &&
      !url.searchParams.has("executableNodeId") && !url.searchParams.has("ancestorActivityExecutionId") && response.ok();
  });
  await page.goto(`/workflows/instances/${encodeURIComponent(runId!)}`);
  const timeline = page.getByRole("region", { name: "Executed activities", exact: true });
  const evidence = await (await browseResponse).json() as { items: Array<{ execution: { activityExecutionId: string; authoredActivityId: string } }> };
  const rows = timeline.locator(".wf-activity-timeline-row");
  await expect(rows).toHaveCount(3);
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

  await receipt(info, "activity-timeline.json", { runId, publication: workflow.version.id, timeline: evidence });
}
