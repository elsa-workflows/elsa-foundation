import { expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { studioBackendLink } from "./studioBackendLink";
import type { Capture, CorePreviewStack, Receipt } from "./corePreviewSmokeJourney";

type Activity = {
  nodeId: string;
  activityVersionId: string;
  inputs: { referenceKey: string; value: { expressionType: "Literal"; value: unknown } }[];
  outputs: [];
  structure?: { kind: string; schemaVersion: string; payload: { activities: Activity[] } };
};

const header = "x-elsa-workflow-instance-id";
const authoredPath = "k10/orders";
const body = "Order accepted by the kernel";

export async function kernelHttpJourney(page: Page, request: APIRequestContext, info: TestInfo, stack: CorePreviewStack,
  capture: Capture, receipt: Receipt) {
  async function api<T>(path: string, data?: unknown): Promise<T> {
    const response = await request.fetch(stack.backend + path, data === undefined ? {} : { method: "POST", data });
    expect(response.ok(), `${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return await response.json() as T;
  }
  await api("/_elsa/identity/login", { username: "admin", password: "Password123!" });
  async function activity(nodeId: string, type: string, values: Record<string, unknown> = {}): Promise<Activity> {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(
      "/design/activities/definitions?search=" + encodeURIComponent(type));
    const definition = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition;
    expect(definition, type).toBeDefined();
    return { nodeId, activityVersionId: definition!.headVersionId, outputs: [],
      inputs: Object.entries(values).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })) };
  }
  async function draft(name: string, activities: Activity[]) {
    const sequence = await activity("root", "Elsa.Activities.Sequence.Activities.Sequence");
    sequence.structure = { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } };
    return await api<{ definition: { id: string } }>("/design/workflows/definitions", {
      name, operationKey: crypto.randomUUID(), initialState: { rootActivity: sequence, variables: [], inputs: [], outputs: [] }
    });
  }
  async function reviewAndPublish(definition: string) {
    await page.goto(studioBackendLink(page, `/workflows/definitions?definition=${encodeURIComponent(definition)}`));
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    const publish = review.getByRole("button", { name: "Publish", exact: true });
    await expect(publish).toBeEnabled({ timeout: 60_000 });
    const outcome = page.waitForResponse(response => /\/publishing\/workflows\/[^/]+\/publish$/.test(new URL(response.url()).pathname)
      && response.request().method() === "POST");
    await publish.click();
    return await outcome;
  }

  const route = await activity("endpoint", "Elsa.Activities.Http.Activities.HttpEndpoint",
    { Path: authoredPath, CanStartWorkflow: true, SupportedMethods: ["POST"] });
  const call = await activity("order-request", "Elsa.Activities.Http.Activities.SendHttpRequest",
    { Url: stack.stub + "/core-preview/k10-order", Method: "POST", Content: "kernel-outbound" });
  const respond = await activity("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse",
    { StatusCode: 201, Body: body, ContentType: "text/plain" });
  const order = await draft("Kernel HTTP orders", [route, call, respond]);
  const publication = await reviewAndPublish(order.definition.id);
  expect(publication.ok(), await publication.text()).toBe(true);
  await page.getByRole("dialog", { name: "Published", exact: true }).getByRole("button", { name: "Close", exact: true }).click();

  await page.locator('.wf-sequence-step[data-id="endpoint"]').click();
  await page.getByRole("button", { name: "Send HTTP test request", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Send an HTTP test request", exact: true });
  const send = dialog.getByRole("button", { name: "Send request", exact: true });
  await expect(send).toBeEnabled();
  const endpoint = "/workflows/http/" + authoredPath;
  const wirePending = page.waitForResponse(response => new URL(response.url()).pathname === endpoint
    && response.request().method() === "POST");
  await send.click();
  const wire = await wirePending;
  expect(wire.status(), await wire.text()).toBe(201);
  const run = await wire.headerValue(header);
  expect(run).toBeTruthy();
  const response = dialog.getByRole("region", { name: "HTTP response" });
  await expect(response).toContainText(body);
  const link = response.getByRole("link", { name: "Inspect run", exact: true });
  await expect(link).toHaveAttribute("href", new RegExp("/workflows/instances/" + encodeURIComponent(run!) + "(?:\\?|$)"));
  await link.focus();
  await expect(link).toBeFocused();
  await capture(page, info, "kernel-http-result");
  await link.press("Enter");
  await expect(page).toHaveURL(new RegExp("/workflows/instances/" + encodeURIComponent(run!) + "(?:\\?|$)"));
  const runDetails = "/runtime/workflows/instances/" + encodeURIComponent(run!);
  await expect.poll(async () => {
    const reply = await request.get(stack.backend + runDetails);
    return reply.ok() ? (await reply.json()).instance.status : reply.status();
  }).toBe("Completed");
  await expect(page.getByText("Completed", { exact: true }).first()).toBeVisible();
  await capture(page, info, "kernel-http-run");

  const direct = await request.post(stack.backend + endpoint, { data: {} });
  expect(direct.status(), await direct.text()).toBe(201);
  const directRun = direct.headers()[header];
  expect(directRun).toBeTruthy();
  expect(directRun).not.toBe(run);
  const list = await api<{ items: { workflowExecutionId: string }[] }>(
    "/runtime/workflows/instances/page?status=Completed&take=10&definitionId=" + encodeURIComponent(order.definition.id));
  expect(list.items.map(item => item.workflowExecutionId)).toContain(run);

  const readLine = await activity("console-input", "Elsa.Activities.Primitives.Activities.ReadLine");
  const unsupported = await draft("Kernel HTTP unsupported console input", [readLine]);
  const refusal = await reviewAndPublish(unsupported.definition.id);
  expect(refusal.status()).toBe(409);
  const problem = await refusal.json() as { code?: string; errorCode?: string };
  expect(problem.code ?? problem.errorCode).toBe("definition_unsupported");
  const rejected = page.getByRole("dialog", { name: "Couldn’t publish", exact: true });
  await expect(rejected).toContainText("ReadLine does not run on this host in release 1.");
  await expect(rejected.getByRole("button", { name: "Retry publication", exact: true })).toHaveCount(0);
  await expect(rejected.getByRole("button", { name: "Close", exact: true })).toBeEnabled();
  await capture(page, info, "kernel-http-refusal");
  await receipt(info, "kernel-http.json", { definition: order.definition.id, run, directRun, refused: unsupported.definition.id });
}
