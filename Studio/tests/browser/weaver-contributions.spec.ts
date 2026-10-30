import { expect, test, type APIRequestContext, type Locator, type Page } from "@playwright/test";
import { autosaveDelayMs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/constants";

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
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
  const node = page.locator(`.react-flow__node[data-id="${nodeId}"]`);
  await expect(node).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await readDefinition(request);
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
  const nativeScreenshot = testInfo.outputPath("native-observed-working-state.png");
  await page.screenshot({ path: nativeScreenshot, fullPage: true });
  await testInfo.attach("native-observed-working-state", { path: nativeScreenshot, contentType: "image/png" });
  await proposal.getByRole("button", { name: "Undo whole change" }).click();
  await expect(node).toHaveAttribute("style", priorPosition!);
  expect((await readDefinition(request)).draft).toEqual(before.draft);
  await testInfo.attach("observed-native-receipt", { body: JSON.stringify({ contextTarget: body.contextAttachments.find((item: { contentType: string }) => item.contentType === "workflow.definition").target, observation }, null, 2), contentType: "application/json" });
});

test("a stale server proposal shows its terminal refusal and drains without another owning write", async ({ page, context }, testInfo) => {
  const before = await readNote(page.request);
  const stale = await prepareNote(page, `Stale proposal ${Date.now()}`);
  await approveReviewed(page, stale.proposal);
  const other = await context.newPage();
  try {
    const winner = await prepareNote(other, `Winning proposal ${Date.now()}`);
    await approveReviewed(other, winner.proposal);
    const winnerExecution = other.waitForResponse(response => response.url().endsWith("/execute") && response.request().method() === "POST");
    await winner.proposal.getByRole("button", { name: "Apply", exact: true }).click();
    expect((await winnerExecution).ok()).toBe(true);
    await expect(winner.proposal).toHaveAttribute("data-status", "executed");
    const changed = await readNote(page.request);
    expect(changed.writeCount).toBe(before.writeCount + 1);
    const refused = page.waitForResponse(response => response.url().endsWith("/execute") && response.request().method() === "POST");
    await stale.proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const response = await refused; expect(response.status()).toBe(409);
    const terminal = await response.json();
    expect(terminal.data).toMatchObject({ executed: false, status: "failed", toolResult: { succeeded: false } });
    await expect(stale.proposal).toHaveAttribute("data-status", "failed");
    await expect(stale.dock.getByLabel("Ask Weaver")).toBeEnabled();
    await expect(stale.dock).toHaveAttribute("aria-busy", "false");
    await sendReadOnlyFollowup(page, stale);
    expect(await readNote(page.request)).toEqual(changed);
    const screenshot = testInfo.outputPath("stale-owner-refusal.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach("stale-owner-refusal", { path: screenshot, contentType: "image/png" });
    await testInfo.attach("stale-owner-readback", { body: JSON.stringify({ before, changed, terminal, final: await readNote(page.request) }, null, 2), contentType: "application/json" });
  } finally { await other.close(); }
});

test("a real Save A response delayed until after native Weaver B cannot release B to autosave or remove Undo", async ({ page }, testInfo) => {
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
  const node = page.locator(`.react-flow__node[data-id="${nodeId}"]`);
  await expect(node).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const baseline = await readDefinition(page.request); const priorPosition = await node.getAttribute("style");
  const capabilities = await (await page.request.get(backendPath("/capabilities"))).json();
  const draftLink = capabilities.capabilities.find((item: { id: string }) => item.id === "elsa.api.workflow-design")
    ?.links.find((item: { rel: string }) => item.rel === "workflow-drafts")?.href;
  expect(draftLink).toBeTruthy();
  const saveURL = backendPath(draftLink.replace("{draftId}", encodeURIComponent(baseline.draft.id)));
  let release!: () => void, arrived!: () => void, delayed = false;
  const releaseResponse = new Promise<void>(resolve => { release = resolve; });
  const actualSaveReachedOwner = new Promise<void>(resolve => { arrived = resolve; });
  const writes: unknown[] = [];
  page.on("request", request => { if (request.url() === saveURL && request.method() === "PUT") writes.push(request.postDataJSON()); });
  // Controlled real-response delay: route.fetch performs the actual owning backend IO. The exact
  // original response is fulfilled later; no payload, status, tool result or backend is fabricated.
  await page.route(saveURL, async route => {
    if (route.request().method() !== "PUT" || delayed) { await route.continue(); return; }
    delayed = true;
    const original = await route.fetch(); expect(original.ok()).toBe(true); arrived();
    await releaseResponse; await route.fulfill({ response: original });
  });
  try {
    const savedResponse = page.waitForResponse(response => response.url() === saveURL && response.request().method() === "PUT");
    await page.getByRole("button", { name: "Save", exact: true }).click(); await actualSaveReachedOwner;
    const persistedA = await readDefinition(page.request);
    await page.getByRole("button", { name: /^Weaver/ }).click();
    const dock = page.getByRole("region", { name: "Weaver assistant" });
    await dock.getByLabel("Ask Weaver").fill(JSON.stringify({ toolName: "workflow_set_designer_position", arguments: { activityId: nodeId, x: 480, y: 320 } }));
    await dock.getByRole("button", { name: "Send message" }).click();
    const proposal = dock.locator(".agent-proposal").first();
    await expect(proposal).toHaveAttribute("data-status", "awaiting-approval"); await approveReviewed(page, proposal);
    const acknowledged = page.waitForResponse(response => response.url().endsWith("/acknowledgements") && response.request().method() === "POST");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const observation = await acknowledged; expect(observation.ok()).toBe(true);
    await expect(proposal).toHaveAttribute("data-status", "executed");
    await expect(node).toHaveAttribute("style", /translate\(480px, 320px\)/);
    expect(await readDefinition(page.request)).toEqual(persistedA);
    release(); expect((await savedResponse).ok()).toBe(true);
    await expect(page.getByRole("button", { name: "Save", exact: true })).toBeEnabled();
    // Observe more than two actual configured debounce periods; an erroneous released hold writes B.
    await page.waitForTimeout(autosaveDelayMs * 2 + 200);
    expect(writes).toHaveLength(1); expect((await readDefinition(page.request)).draft).toEqual(persistedA.draft);
    const undo = proposal.getByRole("button", { name: "Undo whole change" }); await expect(undo).toBeEnabled();
    const screenshot = testInfo.outputPath("real-save-response-delay-held-b.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach("real-save-response-delay-held-b", { path: screenshot, contentType: "image/png" });
    await undo.click(); await expect(node).toHaveAttribute("style", priorPosition!);
    expect((await readDefinition(page.request)).draft).toEqual(persistedA.draft);
    await testInfo.attach("controlled-real-save-response-delay", { body: JSON.stringify({ controlledRealResponseDelay: true, saveURL, writes: writes.length, persistedLayoutA: persistedA.draft.layout, acknowledged: observation.request().postDataJSON(), wholeUndoRestoredLayoutA: true }, null, 2), contentType: "application/json" });
  } finally { release(); await page.unroute(saveURL); }
});

test("a dropped real accepted acknowledgement response retries its exact receipt without another native edit", async ({ page }, testInfo) => {
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
  const node = page.locator(`.react-flow__node[data-id="${nodeId}"]`); await expect(node).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await readDefinition(page.request); const priorPosition = await node.getAttribute("style");
  const acknowledgements: unknown[] = []; let accepted: unknown; let executions = 0;
  page.on("request", request => { if (request.method() === "POST" && request.url().endsWith("/execute")) executions++; });
  await page.route("**/client-invocations/*/acknowledgements", async route => {
    acknowledgements.push(route.request().postDataJSON());
    if (acknowledgements.length !== 1) { await route.continue(); return; }
    // The owning backend accepts the actual receipt. Only its browser response is lost.
    const original = await route.fetch(); expect(original.ok()).toBe(true); accepted = await original.json();
    expect(accepted).toMatchObject({ data: { executed: true, toolResult: { succeeded: true } } });
    await route.abort("failed");
  });
  try {
    await page.getByRole("button", { name: /^Weaver/ }).click();
    const dock = page.getByRole("region", { name: "Weaver assistant" });
    await dock.getByLabel("Ask Weaver").fill(JSON.stringify({ toolName: "workflow_set_designer_position", arguments: { activityId: nodeId, x: 480, y: 320 } }));
    const sent = page.waitForResponse(response => response.url().endsWith("/messages") && response.request().method() === "POST");
    await dock.getByRole("button", { name: "Send message" }).click(); const delivered = await sent; expect(delivered.ok()).toBe(true);
    const proposal = dock.locator(".agent-proposal").first(); await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
    await approveReviewed(page, proposal); await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    await expect(proposal.getByRole("button", { name: "Retry confirmation", exact: true })).toBeEnabled();
    await expect(dock.getByLabel("Ask Weaver")).toBeDisabled();
    await expect(node).toHaveAttribute("style", /translate\(480px, 320px\)/);
    const confirmed = page.waitForResponse(response => response.url().endsWith("/acknowledgements") && response.request().method() === "POST");
    await proposal.getByRole("button", { name: "Retry confirmation", exact: true }).click();
    const retry = await confirmed; expect(retry.ok()).toBe(true); const retryResult = await retry.json();
    expect(acknowledgements).toHaveLength(2); expect(acknowledgements[1]).toEqual(acknowledgements[0]); expect(executions).toBe(1);
    await expect(proposal).toHaveAttribute("data-status", "executed");
    await sendReadOnlyFollowup(page, { dock, messagesURL: delivered.url() });
    expect((await readDefinition(page.request)).draft).toEqual(before.draft);
    const screenshot = testInfo.outputPath("accepted-acknowledgement-response-recovered.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach("accepted-acknowledgement-response-recovered", { path: screenshot, contentType: "image/png" });
    await proposal.getByRole("button", { name: "Undo whole change" }).click(); await expect(node).toHaveAttribute("style", priorPosition!);
    await testInfo.attach("exact-acknowledgement-retry", { body: JSON.stringify({ controlledRealResponseDrop: true, accepted, retryResult, acknowledgements, executions, persistedDraftUnchanged: true, wholeUndoRestored: true, sameSessionFollowupAccepted: true }, null, 2), contentType: "application/json" });
  } finally { await page.unroute("**/client-invocations/*/acknowledgements"); }
});

test("Stop detaches a paused accepted-ACK conversation and the next prompt uses a fresh server session", async ({ page }, testInfo) => {
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
  const node = page.locator(`.react-flow__node[data-id="${nodeId}"]`); await expect(node).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await readDefinition(page.request); const priorPosition = await node.getAttribute("style");
  let release!: () => void, arrived!: () => void, accepted: unknown, executions = 0;
  const held = new Promise<void>(resolve => { arrived = resolve; }); const released = new Promise<void>(resolve => { release = resolve; });
  const streamURLs: string[] = [];
  page.on("request", request => { if (request.method() === "POST" && request.url().endsWith("/execute")) executions++; if (request.method() === "GET" && request.url().endsWith("/stream")) streamURLs.push(request.url()); });
  await page.route("**/client-invocations/*/acknowledgements", async route => {
    const original = await route.fetch(); expect(original.ok()).toBe(true); accepted = await original.json();
    expect(accepted).toMatchObject({ data: { executed: true, toolResult: { succeeded: true } } });
    arrived(); await released; await route.fulfill({ response: original });
  });
  try {
    await page.getByRole("button", { name: /^Weaver/ }).click(); const dock = page.getByRole("region", { name: "Weaver assistant" });
    await dock.getByLabel("Ask Weaver").fill(JSON.stringify({ toolName: "workflow_set_designer_position", arguments: { activityId: nodeId, x: 480, y: 320 } }));
    const originalMessage = page.waitForResponse(response => response.url().endsWith("/messages") && response.request().method() === "POST");
    await dock.getByRole("button", { name: "Send message" }).click(); const first = await originalMessage; expect(first.ok()).toBe(true);
    const proposal = dock.locator(".agent-proposal").first(); await expect(proposal).toHaveAttribute("data-status", "awaiting-approval"); await approveReviewed(page, proposal);
    const confirmed = page.waitForResponse(response => response.url().endsWith("/acknowledgements") && response.request().method() === "POST");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click(); await held;
    await expect(node).toHaveAttribute("style", /translate\(480px, 320px\)/);
    const cancel = page.waitForResponse(response => response.url().endsWith("/cancel") && response.request().method() === "POST");
    await dock.getByRole("button", { name: "Stop Weaver", exact: true }).click(); const cancellation = await cancel; expect(cancellation.ok()).toBe(true);
    const cancelBody = await cancellation.json(); expect(cancelBody.data.cancelled).toBe(false);
    release(); expect((await confirmed).ok()).toBe(true); await expect(proposal).toHaveAttribute("data-status", "executed");
    await expect(dock).toContainText("next prompt starts a new conversation"); await expect(dock.getByLabel("Ask Weaver")).toBeEnabled();
    const next = page.waitForResponse(response => response.url().endsWith("/messages") && response.request().method() === "POST");
    await dock.getByLabel("Ask Weaver").fill("Summarize the current context without changing it."); await dock.getByRole("button", { name: "Send message" }).click();
    const fresh = await next; expect(fresh.ok()).toBe(true); expect(fresh.url()).not.toBe(first.url()); await expect(dock).toHaveAttribute("aria-busy", "false");
    expect(streamURLs.filter(url => url === first.url().replace(/\/messages$/, "/stream"))).toHaveLength(1); expect(executions).toBe(1);
    expect((await readDefinition(page.request)).draft).toEqual(before.draft);
    const screenshot = testInfo.outputPath("stop-fresh-conversation-with-prior-receipt.png"); await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach("stop-fresh-conversation-with-prior-receipt", { path: screenshot, contentType: "image/png" });
    await proposal.getByRole("button", { name: "Undo whole change" }).click(); await expect(node).toHaveAttribute("style", priorPosition!);
    await testInfo.attach("stopped-conversation-detachment", { body: JSON.stringify({ controlledRealResponseDelay: true, accepted, cancelBody, oldMessagesURL: first.url(), newMessagesURL: fresh.url(), streamURLs, executions, persistedDraftUnchanged: true, wholeUndoRestored: true }, null, 2), contentType: "application/json" });
  } finally { release(); await page.unroute("**/client-invocations/*/acknowledgements"); }
});

test("genuine expiry before and after approval drains both retained outcomes without effects", async ({ page, context }, testInfo) => {
  test.setTimeout(8 * 60_000);
  const before = await readNote(page.request);
  // Prepare the unapproved proposal first, so the later approved proposal's actual expiry covers both.
  const awaitingApproval = await prepareNote(page, `Never approve after expiry ${Date.now()}`);
  const approvedPage = await context.newPage();
  try {
    const prepared = await prepareNote(approvedPage, `Never apply after expiry ${Date.now()}`);
    const approved = await approveReviewed(approvedPage, prepared.proposal);
    const expiresAt = Date.parse(approved.data.preparedInvocation.expiresAt);
    expect(Number.isFinite(expiresAt)).toBe(true);
    const waitMs = Math.max(0, expiresAt - Date.now() + 1000); expect(waitMs).toBeLessThan(6 * 60_000);
    const waitingStartedAt = new Date().toISOString();
    // One genuine wall-clock wait; no fake clock, altered proposal or accelerated lifetime.
    await page.waitForTimeout(waitMs);
    const approvalRefused = page.waitForResponse(response => response.url().endsWith("/approve") && response.request().method() === "POST");
    await awaitingApproval.proposal.getByRole("button", { name: "Approve", exact: true }).click();
    const first = await approvalRefused; expect(first.status()).toBe(409); const beforeApprovalTerminal = await first.json();
    expect(beforeApprovalTerminal.data).toMatchObject({ status: "expired", executed: false, toolResult: null });
    await expect(awaitingApproval.proposal).toHaveAttribute("data-status", "expired");
    await sendReadOnlyFollowup(page, awaitingApproval);
    const executionRefused = approvedPage.waitForResponse(response => response.url().endsWith("/execute") && response.request().method() === "POST");
    await prepared.proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const second = await executionRefused; expect(second.status()).toBe(409); const afterApprovalTerminal = await second.json();
    expect(afterApprovalTerminal.data).toMatchObject({ status: "expired", executed: false, toolResult: null });
    await expect(prepared.proposal).toHaveAttribute("data-status", "expired");
    await sendReadOnlyFollowup(approvedPage, prepared);
    expect(await readNote(page.request)).toEqual(before);
    const screenshot = testInfo.outputPath("genuine-expiry-refusal-and-recovery.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach("genuine-expiry-refusal-and-recovery", { path: screenshot, contentType: "image/png" });
    await testInfo.attach("genuine-expiry-no-effect", { body: JSON.stringify({ waitingStartedAt, approvedExpiresAt: approved.data.preparedInvocation.expiresAt, waitMs, actualAttemptAt: new Date().toISOString(), beforeApprovalTerminal, afterApprovalTerminal, before, after: await readNote(page.request), sameSessionFollowupsAccepted: true }, null, 2), contentType: "application/json" });
  } finally { await approvedPage.close(); }
});

async function sendReadOnlyFollowup(page: Page, prepared: { dock: Locator; messagesURL: string }) {
  await expect(prepared.dock.getByLabel("Ask Weaver")).toBeEnabled();
  const next = page.waitForResponse(response => response.url() === prepared.messagesURL && response.request().method() === "POST");
  await prepared.dock.getByLabel("Ask Weaver").fill("Summarize the admitted context without changing it.");
  await prepared.dock.getByRole("button", { name: "Send message" }).click(); expect((await next).ok()).toBe(true);
  await expect(prepared.dock).toHaveAttribute("aria-busy", "false");
}

async function readNote(request: APIRequestContext) {
  const response = await request.get(backendPath("/_qualification/weaver/note")); expect(response.ok()).toBe(true);
  return response.json();
}

async function prepareNote(page: Page, value: string) {
  await page.goto("/qualification/weaver-note"); await expect(page.getByTestId("note-value")).toBeVisible();
  await page.getByLabel("New note value").fill(value);
  const sent = page.waitForResponse(response => response.url().includes("/_elsa/agent/sessions/") && response.url().endsWith("/messages") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Review note change", exact: true }).click();
  const response = await sent; expect(response.ok()).toBe(true);
  const dock = page.getByRole("region", { name: "Weaver assistant" }); const proposal = dock.locator(".agent-proposal").first();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  return { dock, proposal, messagesURL: response.url() };
}

async function approveReviewed(page: Page, proposal: Locator) {
  const decision = page.waitForResponse(response => response.url().endsWith("/approve") && response.request().method() === "POST");
  await proposal.getByRole("button", { name: "Approve", exact: true }).click();
  const response = await decision; expect(response.ok()).toBe(true);
  await expect(proposal).toHaveAttribute("data-status", "approved"); return response.json();
}

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
  const noteScreenshot = testInfo.outputPath("module-observed-persisted-note.png");
  await page.screenshot({ path: noteScreenshot, fullPage: true });
  await testInfo.attach("module-observed-persisted-note", { path: noteScreenshot, contentType: "image/png" });
  await testInfo.attach("observed-note-effect", { body: JSON.stringify({ source: delivered.source, requestId: delivered.requestId, before, receipt: executionBody.data.toolResult.receipt, observed }, null, 2), contentType: "application/json" });
});
