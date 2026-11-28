import { expect, type Page, type TestInfo } from "@playwright/test";
import type { Capture, CorePreviewStack, Receipt } from "./corePreviewSmokeJourney";
import type { WorkflowInstanceDetails, WorkflowTestRunView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

export async function corePreviewTestRunInspectorJourney(page: Page, info: TestInfo, stack: CorePreviewStack,
  capture: Capture, receipt: Receipt) {
  const api = async <T>(path: string, data?: object): Promise<T> => {
    const response = await page.request.fetch(`${stack.backend}${path}`, { method: data ? "POST" : "GET", data });
    expect(response.ok(), `${path}: ${response.status()}`).toBe(true);
    return await response.json() as T;
  };
  const type = "Elsa.Activities.Scheduling.Activities.Delay";
  const catalog = await api<{ items: Array<{ definition: { activityTypeKey: string; headVersionId: string } }> }>(
    `/design/activities/definitions?search=${encodeURIComponent(type)}`);
  const delay = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
  expect(delay).toBeTruthy();
  const workflow = await api<{ definition: { id: string } }>("/design/workflows/definitions", {
    name: "Test Run inspector",
    initialState: { inputs: [], outputs: [], variables: [], strategyOptions: { persistenceMode: "Durable" },
      rootActivity: { nodeId: "wait", activityVersionId: delay, outputs: [],
        inputs: [{ referenceKey: "Duration", value: { expressionType: "Literal", value: "00:00:10" } }] } }
  });

  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(workflow.definition.id)}`);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");
  await page.getByRole("button", { name: "Test Run", exact: true }).click();
  const update = page.getByRole("button", { name: "Update test", exact: true });
  if (await update.isVisible()) await update.click();

  const runtimeResponses: { url: string; status: number }[] = [];
  page.on("response", response => {
    if (/\/runtime\/workflows\/instances\/[^/]+$/.test(new URL(response.url()).pathname))
      runtimeResponses.push({ url: response.url(), status: response.status() });
  });
  const started = page.waitForResponse(response => new URL(response.url()).pathname.endsWith("/publishing/workflows/drafts/test-runs")
    && response.request().method() === "POST" && response.ok());
  await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  const run = await (await started).json() as WorkflowTestRunView;
  expect(run.workflowExecutionId).toBeTruthy();
  const summary = page.getByRole("region", { name: "Test run summary", exact: true });
  await expect(summary.locator(".wf-status-badge").first()).toHaveText(/^(Accepted|Running|Waiting|Pending)$/);
  await expect(page.getByText(/Execution status is unavailable|Request failed with 404|Live updates disconnected/)).toHaveCount(0);
  await capture(page, info, "test-run-started");
  await expect(summary.locator(".wf-status-badge").first()).toHaveText("Completed", { timeout: 25_000 });
  await expect(page.getByRole("button", { name: "Open run", exact: true })).toHaveCount(1);
  await expect(summary.getByRole("button", { name: "Open run", exact: true })).toBeEnabled();
  await capture(page, info, "test-run-completed");

  const instancePath = `/runtime/workflows/instances/${encodeURIComponent(run.workflowExecutionId!)}`;
  const details = await api<WorkflowInstanceDetails>(instancePath);
  expect(details.instance).toMatchObject({ workflowExecutionId: run.workflowExecutionId, artifactId: run.artifactId,
    definitionId: workflow.definition.id, status: "Completed", sourceReferenceId: run.testRunId });
  const pageOfRuns = await api<unknown>("/runtime/workflows/instances/page");
  expect(JSON.stringify(pageOfRuns)).not.toContain(run.workflowExecutionId);
  await summary.getByRole("button", { name: "Open run", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/workflows/instances/${run.workflowExecutionId}`));
  await expect(page.getByRole("region", { name: "Executed activities", exact: true })).toBeVisible();
  await capture(page, info, "test-run-opened");
  await receipt(info, "test-run-inspector.json", { run: details.instance, runtimeResponses, isolatedFromProductionList: true });
}
