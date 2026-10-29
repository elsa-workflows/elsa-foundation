import { expect, test, type APIRequestContext } from "@playwright/test";

const definitionId = process.env.ELSA_WEAVER_WORKFLOW_ID;
const nodeId = process.env.ELSA_WEAVER_ACTIVITY_ID;
const backendURL = process.env.ELSA_WEAVER_OWNED_BACKEND_URL ?? process.env.ELSA_WEAVER_OWNED_URL;
if (!backendURL) throw new Error("An explicitly owned backend URL is required.");
function backendPath(path: string) {
  const resolved = new URL(path, backendURL);
  if (resolved.origin !== new URL(backendURL!).origin) throw new Error("Qualification read-back escaped the owned backend.");
  return resolved.toString();
}
if (!definitionId || !nodeId) throw new Error("The owned qualification host must supply ELSA_WEAVER_WORKFLOW_ID and ELSA_WEAVER_ACTIVITY_ID for its seeded workflow.");

async function readDefinition(request: APIRequestContext) {
  const capabilitiesResponse = await request.get(backendPath("/capabilities"));
  expect(capabilitiesResponse.ok()).toBe(true);
  const capabilities = await capabilitiesResponse.json();
  const href = capabilities.capabilities.find((item: { id: string }) => item.id === "elsa.api.workflow-design")
    ?.links.find((item: { rel: string }) => item.rel === "workflow-definitions")?.href;
  expect(href).toBeTruthy();
  const response = await request.get(backendPath(`${href.replace(/\/$/, "")}/${encodeURIComponent(definitionId!)}`));
  expect(response.ok()).toBe(true);
  return response.json();
}

test("real composed Studio reviews a native layout edit, observes working state and keeps persistence explicit", async ({ page, request }, testInfo) => {
  // No page.route or fabricated backend responses: all transport reaches the owned composed host.
  const before = await readDefinition(request);
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
  const node = page.locator(`.react-flow__node[data-id="${nodeId}"]`);
  await expect(node).toBeVisible();
  const priorPosition = await node.getAttribute("style");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const dock = page.getByRole("region", { name: "Weaver assistant" });
  await expect(dock.getByLabel("Ask Weaver")).toBeEnabled();
  await dock.getByLabel("Ask Weaver").fill(process.env.ELSA_WEAVER_LAYOUT_PROMPT ?? JSON.stringify({ toolName: "workflow_set_designer_position", arguments: { activityId: nodeId, x: 480, y: 320 } }));
  const sent = page.waitForResponse(response => response.url().includes("/_elsa/agent/sessions/") && response.url().endsWith("/messages") && response.request().method() === "POST");
  await dock.getByRole("button", { name: "Send message" }).click();
  const accepted = await sent; expect(accepted.ok()).toBe(true);
  const body = accepted.request().postDataJSON();
  expect(body.requestId).toBeTruthy();
  expect(body.contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ contentType: "workflow.definition", moduleId: "Elsa.Studio.Workflows", target: expect.objectContaining({ resourceId: definitionId }), client: expect.objectContaining({ generationId: expect.any(String) }) })]));
  const proposal = dock.locator(".agent-proposal").first();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  await expect(proposal).toContainText(definitionId!);
  await proposal.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(proposal).toHaveAttribute("data-status", "approved");
  const executed = page.waitForResponse(response => response.url().endsWith("/execute") && response.request().method() === "POST");
  const acknowledged = page.waitForResponse(response => response.url().endsWith("/acknowledgements") && response.request().method() === "POST");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  expect((await executed).status()).toBe(202);
  const acknowledgement = await acknowledged;
  expect(acknowledgement.ok()).toBe(true);
  const observation = acknowledgement.request().postDataJSON();
  expect(observation).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", schemaVersion: "1", data: { observed: true, activityId: nodeId, x: 480, y: 320 } } });
  await expect(proposal).toHaveAttribute("data-status", "executed");
  await expect(node).not.toHaveAttribute("style", priorPosition!);
  await expect(node).toHaveAttribute("style", /translate\(480px, 320px\)/);
  expect((await readDefinition(request)).draft).toEqual(before.draft);
  await proposal.getByRole("button", { name: "Undo whole change" }).click();
  await expect(node).toHaveAttribute("style", priorPosition!);
  expect((await readDefinition(request)).draft).toEqual(before.draft);
  await testInfo.attach("observed-native-receipt", { body: JSON.stringify({ contextTarget: body.contextAttachments.find((item: { contentType: string }) => item.contentType === "workflow.definition").target, observation }, null, 2), contentType: "application/json" });
});

test("an unrelated module contributes context, review and a persisted owner-observed effect without shared domain branches", async ({ page }, testInfo) => {
  const value = `Reviewed qualification note ${Date.now()}`;
  await page.goto("/qualification/weaver-note");
  await expect(page.getByTestId("note-value")).toBeVisible();
  const beforeResponse = await page.request.get(backendPath("/_qualification/weaver/note")); expect(beforeResponse.ok()).toBe(true);
  const before = await beforeResponse.json();
  await page.getByLabel("New note value").fill(value);
  const sent = page.waitForResponse(response => response.url().includes("/_elsa/agent/sessions/") && response.url().endsWith("/messages") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Review note change", exact: true }).click();
  const accepted = await sent; expect(accepted.ok()).toBe(true);
  const delivered = accepted.request().postDataJSON();
  expect(delivered).toMatchObject({ requestId: expect.any(String), source: { moduleId: "Weaver.Journey", actionId: "weaver.journey.propose" }, promptMode: "enqueue" });
  expect(delivered.contextAttachments).toEqual(expect.arrayContaining([
    expect.objectContaining({ id: expect.stringMatching(/^note-request:/), contentType: "weaver.journey.note", target: expect.objectContaining({ resourceId: before.id, revision: String(before.revision) }) }),
    expect.objectContaining({ id: `note:${before.id}`, contentType: "weaver.journey.note" })
  ]));
  const dock = page.getByRole("region", { name: "Weaver assistant" });
  const proposal = dock.locator(".agent-proposal").first();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  await expect(proposal).toContainText(before.id);
  await proposal.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(proposal).toHaveAttribute("data-status", "approved");
  const executed = page.waitForResponse(response => response.url().endsWith("/execute") && response.request().method() === "POST");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const execution = await executed; expect(execution.ok()).toBe(true);
  const executionBody = await execution.json();
  expect(executionBody.data.toolResult.receipt).toMatchObject({ scope: "serverPersisted", before: { resourceId: before.id }, after: { resourceId: before.id } });
  await expect(proposal).toHaveAttribute("data-status", "executed");
  await expect(page.getByTestId("note-value")).toHaveText(value);
  await expect(page.getByTestId("note-write-count")).toHaveText(String(before.writeCount + 1));
  const observedResponse = await page.request.get(backendPath("/_qualification/weaver/note")); expect(observedResponse.ok()).toBe(true);
  const observed = await observedResponse.json();
  expect(observed).toMatchObject({ value, writeCount: before.writeCount + 1 }); expect(String(observed.revision)).not.toBe(String(before.revision));
  // An identical accepted execution retry returns its recorded receipt and cannot write the file twice.
  const duplicate = await page.request.post(execution.url(), { data: execution.request().postDataJSON() });
  expect(duplicate.ok()).toBe(true);
  const repeated = await page.request.get(backendPath("/_qualification/weaver/note"));
  expect(await repeated.json()).toEqual(observed);
  await testInfo.attach("observed-note-effect", { body: JSON.stringify({ source: delivered.source, requestId: delivered.requestId, before, receipt: executionBody.data.toolResult.receipt, observed }, null, 2), contentType: "application/json" });
});
