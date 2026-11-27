import { expect, type Page, type TestInfo } from "@playwright/test";
import type { Capture, CorePreviewStack, Receipt } from "./corePreviewSmokeJourney";

export async function corePreviewParkRecoveryJourney(page: Page, info: TestInfo, stack: CorePreviewStack,
  capture: Capture, receipt: Receipt) {
  const waitRoute = "ux-core-preview/park-recovery";
  const responseBody = "park-recovery-qualification";
  const api = async <T>(path: string, data?: object): Promise<T> => {
    const response = data ? await page.request.post(`${stack.backend}${path}`, { data }) : await page.request.get(`${stack.backend}${path}`);
    expect(response.ok(), await response.text()).toBe(true);
    return await response.json() as T;
  };
  const node = async (nodeId: string, type: string, inputs: Record<string, unknown> = {}) => {
    const catalog = await api<{ items: Array<{ definition: { activityTypeKey: string; headVersionId: string } }> }>(
      `/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const activityVersionId = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
    expect(activityVersionId, type).toBeTruthy();
    return { nodeId, activityVersionId, outputs: [],
      inputs: Object.entries(inputs).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })) };
  };
  const wait = await node("wait", "Elsa.Activities.Http.Activities.HttpEndpoint",
    { Path: waitRoute, CanStartWorkflow: false, SupportedMethods: ["POST"] });
  const answer = await node("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse", { StatusCode: 200 });
  answer.inputs.push({ referenceKey: "Body", value: { expressionType: "JavaScript", value: `'${responseBody}'` } });
  const root = await node("root", "Elsa.Activities.Sequence.Activities.Sequence");
  const workflow = await api<{ version: { id: string } }>("/design/workflows/definitions/submit", {
    name: "Parked HTTP workflow", state: { rootActivity: { ...root, structure: {
      kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [wait, answer] }
    } }, variables: [], inputs: [], outputs: [] }
  });
  const published = await api<{ artifactId: string }>(`/publishing/workflows/${encodeURIComponent(workflow.version.id)}/publish`, {});
  const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${encodeURIComponent(published.artifactId)}/execute`, {});
  const runId = encodeURIComponent(started.workflowExecutionId);
  const runPath = `/runtime/workflows/instances/${runId}`;
  const recoveryPath = `${runPath}/recovery`;
  const browserPath = `/workflows/instances/${runId}`;
  const continueRun = () => page.request.post(`${stack.backend}/workflows/http/${waitRoute}`, { data: {} });
  await expect.poll(async () => (await continueRun()).status(), { timeout: 30_000 }).toBe(500);
  await expect.poll(async () => (await api<{ status: string }>(recoveryPath)).status).toBe("Available");

  await page.goto(browserPath);
  const action = page.getByRole("button", { name: "Recover", exact: true });
  await expect(action).toBeVisible();
  await expect(page.getByText("This run is parked. Recover it to accept new work.")).toBeVisible();
  await capture(page, info, "parked-run");

  const sameRecovery = (url: URL) => url.pathname === recoveryPath;
  let denyFirstRead = false;
  await page.route(sameRecovery, async route => {
    if (route.request().method() === "GET") {
      denyFirstRead = true;
      await route.fulfill({ status: 403, headers: {
        "Access-Control-Allow-Origin": new URL(page.url()).origin, "Access-Control-Allow-Credentials": "true"
      } });
    } else await route.continue();
  });
  await page.reload();
  await expect.poll(() => denyFirstRead).toBe(true);
  await expect(action).toHaveCount(0);
  await page.unroute(sameRecovery);
  await page.getByRole("button", { name: "Refresh execution", exact: true }).click();
  await expect(action).toBeVisible();

  await page.route(sameRecovery, async route => {
    if (route.request().method() === "POST") await route.continue({ postData: JSON.stringify({ token: "stale-qualification-selection" }) });
    else await route.continue();
  });
  const stale = page.waitForResponse(response => sameRecovery(new URL(response.url())) && response.request().method() === "POST");
  await action.focus();
  await page.keyboard.press("Enter");
  expect((await stale).status()).toBe(409);
  await expect(page.getByText("This run changed. Check its current state before recovering it.")).toBeVisible();
  await expect(action).toBeEnabled();
  await capture(page, info, "park-recovery-stale");
  await page.unroute(sameRecovery);

  const admitted = page.waitForResponse(response => sameRecovery(new URL(response.url())) && response.request().method() === "POST");
  await action.click();
  expect((await admitted).status()).toBe(202);
  await expect(page.getByText("The run is ready to continue.")).toBeVisible();
  await expect(action).toHaveCount(0);
  await capture(page, info, "park-recovered");
  const continued = await continueRun();
  expect(continued.status(), await continued.text()).toBe(200);
  expect(await continued.text()).toBe(responseBody);
  expect(await api<{ instance: { workflowExecutionId: string; status: string } }>(runPath)).toMatchObject({
    instance: { workflowExecutionId: started.workflowExecutionId, status: "Completed" }
  });
  await page.getByRole("button", { name: "Refresh execution", exact: true }).click();
  await expect(page.getByText("Completed", { exact: true }).first()).toBeVisible();
  await capture(page, info, "park-run-completed");
  await receipt(info, "park-recovery.json", { runId: started.workflowExecutionId,
    authorization: "browser visibility after controlled 403; actual server grants qualified by #6039",
    stale: 409, admitted: 202, committed: "NotParked", continued: responseBody, terminal: "Completed" });
}
