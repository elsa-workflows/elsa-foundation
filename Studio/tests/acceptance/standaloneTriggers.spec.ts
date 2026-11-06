import { randomUUID } from "node:crypto";
import { expect, test, type Page } from "@playwright/test";
import { TriggerJourney, configured, definitionPath, input, literal, source, triggerPath, type Authored, type DispatchBoundary, type WorkflowState } from "./standaloneTriggersSupport";
import { verifyBpmnInputRefusals, verifyCronAndOverdueRecovery, verifyOptionalInputSemantics, verifyScheduledRetention } from "./standaloneTriggerOrdinaryCases";
import { verifyEmbeddedHttpRouteConflicts, verifyHttpTimeoutAndDisconnect } from "./standaloneTriggerHttpCases";
import { verifyHttpAdmissionAndQueueFaults, verifyHttpWriteFailure } from "./standaloneTriggerFaultCases";
import { verifyConcurrentNodeClaims } from "./standaloneTriggerConcurrentCases";
import { verifyUnreadableScheduleAdmission } from "./standaloneTriggerFaultCases";
import type { DeliveryView, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

const journeys = new WeakMap<Page, TriggerJourney>();
const browserErrors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }, info) => {
  test.setTimeout(240_000);
  const mode = String(info.project.metadata.themeMode ?? info.project.use.colorScheme ?? "light");
  await page.addInitScript(mode => {
    localStorage.setItem("elsa-studio-theme", "meridian");
    localStorage.setItem("elsa-studio-theme-mode", mode);
    localStorage.setItem("elsa-studio-workspace-style", "linen");
  }, mode);
  const errors: string[] = []; browserErrors.set(page, errors); page.on("pageerror", error => errors.push(error.message));
  const journey = new TriggerJourney(page, info); journeys.set(page, journey);
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin"); await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
  await journey.api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  // One palette with every explicit mode makes the appearance evidence reproducible.
  await expect(page.locator("html")).toHaveAttribute("data-theme", "meridian");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);
});
test.afterEach(async ({ page }) => {
  const journey = journeys.get(page);
  if (journey) {
    try { await journey.cleanup(); }
    finally { await journey.finish(); }
  }
  expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]);
});

async function replace(journey: TriggerJourney, authored: Authored, state: WorkflowState) {
  await journey.api(`/design/workflows/drafts/${authored.draftId}`, "PUT", { state, layout: [] });
  const version = await journey.api<{ id: string }>(`/design/workflows/drafts/${authored.draftId}/promote`, "POST", { operationKey: randomUUID() });
  return journey.api(`/publishing/workflows/${version.id}/publish`, "POST", { action: "replace", slotName: "default" });
}
async function pause(journey: TriggerJourney, id: string, capture?: string) {
  await journey.open(id); await journey.page.getByRole("button", { name: "Pause new starts", exact: true }).press("Enter");
  await journey.review(); await expect.poll(async () => (await journey.read(id)).desiredState).toBe("Paused");
  if (capture) await journey.capture(capture);
}
async function inspectRuns(journey: TriggerJourney, delivery: DeliveryView, expected: Record<string, unknown>[]) {
  const started = await journey.started(delivery);
  for (let index = 0; index < started.recipients.length; index++) {
    const recipient = started.recipients[index];
    expect(recipient.executionReference?.kind).not.toBe("Bpmn");
    await journey.outputs(recipient.executionReference!.id, expected[index]);
  }
  return started;
}
async function editPolicy(journey: TriggerJourney, id: string, policy: "RequireAll" | "AcceptAvailable") {
  await journey.open(id); await journey.page.getByRole("button", { name: "Edit configuration", exact: true }).click();
  await journey.page.getByLabel("HTTP admission policy", { exact: true }).selectOption(policy);
  await journey.page.getByRole("button", { name: "Review changes", exact: true }).click(); await journey.review();
}

test("standalone trigger management", async ({ page }) => {
  const j = journeys.get(page)!;
  await j.capture("triggers-reference");
  await page.goto("/workflows/triggers?backend=legacy");
  await expect(page.getByRole("heading", { name: "No triggers yet", exact: true })).toBeVisible(); await j.capture("triggers-empty");
  await page.getByRole("button", { name: "Create trigger", exact: true }).first().press("Enter");
  await page.getByLabel("Trigger type", { exact: true }).selectOption("event:1");
  await page.getByLabel("Trigger name", { exact: true }).fill("Uncommitted entry point");
  await j.capture("trigger-create");
  await page.getByRole("button", { name: "Create trigger", exact: true }).press("Enter");
  await expect(page.getByText("Event name is required.", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Trigger name", { exact: true })).toHaveValue("Uncommitted entry point"); await j.capture("trigger-validation");
  await page.getByRole("button", { name: "Cancel", exact: true }).press("Enter");
  expect((await j.api<{ items: unknown[] }>("/runtime/standalone-triggers")).items).toHaveLength(0);
  const event = await j.create("event", "Shared Event");
  await expect(page.getByRole("heading", { name: "No live associations", exact: true })).toBeVisible(); await j.capture("trigger-no-subscribers");
  await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
  await page.getByLabel("Trigger description", { exact: true }).fill("Updated through the real configuration review");
  await page.getByRole("button", { name: "Review changes", exact: true }).press("Enter");
  await j.review(); await page.reload();
  expect((await j.read(event.id)).description).toBe("Updated through the real configuration review"); await j.capture("trigger-details");
  await j.create("timer", "Shared Timer"); await j.create("cron", "Shared Cron"); await j.create("http", "Shared HTTP");
  await page.goto("/workflows/triggers?backend=legacy");
  await expect(page.getByRole("region", { name: "Triggers table", exact: true }).getByRole("row")).toHaveCount(5); await j.capture("triggers-populated");
  await page.getByRole("searchbox", { name: "Search triggers", exact: true }).fill(`No result ${j.stamp}`);
  await page.getByRole("button", { name: "Search", exact: true }).press("Enter");
  await expect(page.getByRole("heading", { name: "No matching triggers", exact: true })).toBeVisible(); await j.capture("triggers-no-matches");
  j.observed.push("Each initial family created through UI, persisted and reread through real APIs", "Cancelled create left no resource", "Validation retained the edit", "Empty, no matches, populated and no association states");
});

test("standalone trigger associations and inputs", async ({ page }, info) => {
  const j = journeys.get(page)!;
  const first = await j.echo("Declared string and integer");
  const second = await j.echo("Declared string and decimal", [input("message"), input("amount", "Decimal")]);
  const trigger = await j.create("event", "Shared mapped Event");
  const before = await j.api<{ state: unknown }>(`/design/workflows/drafts/${first.authored.draftId}`);
  const publicationRequests: string[] = [];
  const watch = (request: { method(): string; url(): string }) => { if (request.method() === "POST" && new URL(request.url()).pathname.includes("/publishing/")) publicationRequests.push(request.url()); };
  page.on("request", watch);
  const firstSubscription = await j.add(trigger.id, first.authored.definition.id, [source("message", ["metadata", "eventName"]), configured("count", 7)]);
  await j.open(trigger.id); await page.reload(); await expect(page.getByRole("region", { name: "Workflow subscriptions", exact: true })).toContainText(first.authored.definition.id); await j.capture("trigger-subscriptions");
  await page.goto(definitionPath(first.authored.definition.id)); await page.getByRole("tab", { name: "Triggers", exact: true }).click();
  await expect(page.getByRole("region", { name: "Workflow subscriptions", exact: true })).toContainText(trigger.name); await j.capture("workflow-live-triggers");
  await j.add(trigger.id, second.authored.definition.id, [configured("message", "Independent mapping"), configured("amount", 7.25)], true);
  await j.open(trigger.id); await page.reload(); expect(await j.subscriptions(trigger.id)).toHaveLength(2);
  const row = page.getByRole("row").filter({ hasText: first.authored.definition.id }); await row.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByText("Preview with sample data", { exact: true }).click();
  await page.getByLabel("Sample source envelope", { exact: true }).fill(JSON.stringify({ payload: { actual: "synthetic preview only" }, metadata: { eventName: "Sample source name" } }));
  const runsBefore = await j.deliveries(trigger.id);
  await page.getByRole("button", { name: "Preview mapped inputs", exact: true }).press("Enter");
  await expect(page.getByRole("region", { name: "Mapped sample inputs", exact: true })).toContainText("Sample source name"); await j.capture("mapping-preview");
  expect(await j.deliveries(trigger.id)).toEqual(runsBefore);
  await page.getByLabel("Configured value for count", { exact: true }).fill("wrong number");
  await page.getByRole("button", { name: "Review subscription", exact: true }).click();
  await expect(page.getByRole("alert").first()).toBeVisible(); await expect(page.getByLabel("Configured value for count", { exact: true })).toHaveValue("wrong number"); await j.capture("mapping-errors");
  await page.getByLabel("Configured value for count", { exact: true }).fill("8");
  await page.getByRole("checkbox", { name: "Authorize this shared trigger to invoke the workflow", exact: true }).check();
  await page.getByRole("button", { name: "Review subscription", exact: true }).click(); await j.review();
  expect((await j.subscriptions(trigger.id)).find(item => item.target.definitionId === second.authored.definition.id)?.mappings).toEqual([configured("message", "Independent mapping"), configured("amount", 7.25)]);
  await page.goto(definitionPath(first.authored.definition.id)); await page.getByRole("tab", { name: "Triggers", exact: true }).click();
  await page.getByRole("row").filter({ hasText: trigger.name }).getByRole("button", { name: "Remove", exact: true }).click(); await j.review();
  await page.reload(); await page.getByRole("tab", { name: "Triggers", exact: true }).click();
  await expect(page.getByRole("heading", { name: "No live associations", exact: true })).toBeVisible();
  await j.open(trigger.id); expect((await j.subscriptions(trigger.id)).map(item => item.id)).not.toContain(firstSubscription.id);
  expect((await j.api<{ state: unknown }>(`/design/workflows/drafts/${first.authored.draftId}`)).state).toEqual(before.state);
  expect(publicationRequests).toHaveLength(0); page.off("request", watch);
  // Both editors contact the real server. The stale review must retain its command.
  await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
  await page.getByLabel("Trigger description", { exact: true }).fill("Local edit retained after conflict");
  await page.getByRole("button", { name: "Review changes", exact: true }).click(); await expect(page.getByRole("button", { name: "Apply reviewed change", exact: true })).toBeEnabled();
  const other = await page.context().newPage();
  try {
    const concurrent = new TriggerJourney(other, info); await concurrent.open(trigger.id);
    await other.getByRole("button", { name: "Edit configuration", exact: true }).click(); await other.getByLabel("Trigger description", { exact: true }).fill("Other editor won the revision");
    await other.getByRole("button", { name: "Review changes", exact: true }).click(); await concurrent.review(); j.requests.push(...concurrent.requests);
    const conflict = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/changes/apply"));
    await page.getByRole("button", { name: "Apply reviewed change", exact: true }).click(); expect((await conflict).status()).toBe(409);
    await expect(page.getByText(/changed|conflict|stale/i).first()).toBeVisible(); await j.capture("subscription-conflict");
    await page.getByRole("button", { name: "Back to editing", exact: true }).click(); await expect(page.getByLabel("Trigger description", { exact: true })).toHaveValue("Local edit retained after conflict");
  } finally { await other.close(); }
  await page.goto("/workflows/triggers?backend=legacy");
  await replace(j, second.authored, { ...second.state, inputs: [input("message"), input("amount", "String")] });
  await j.open(trigger.id); await expect.poll(async () => (await j.subscriptions(trigger.id))[0].compatibility).toBe("Blocked");
  await page.reload(); await expect(page.getByRole("region", { name: "Workflow subscriptions", exact: true })).toContainText(/Blocked|incompatib/i); await j.capture("target-incompatible");
  j.observed.push("Association created from both directions; edit and remove persisted on both surfaces", "Mapping preview caused no admission", "Different typed contracts retained independent mappings", "Live associations did not save draft or publish graph", "Real concurrent update refused with HTTP 409 and retained local edit", "Incompatible published target remained identifiable");
  await verifyOptionalInputSemantics(j);
});

test("standalone event and schedule starts", async ({ page }) => {
  const j = journeys.get(page)!;
  const one = await j.echo("First independent subscriber"); const two = await j.echo("Second independent subscriber");
  const event = await j.create("event", "Broadcast entry");
  for (const target of [one, two]) await j.add(event.id, target.authored.definition.id, [source("message", ["metadata", "eventName"]), configured("count", 7)]);
  await j.enable(event.id); await j.event(String(event.configuration.eventName));
  const eventDelivery = await j.waitDelivery(event.id, new Set(), 2);
  await inspectRuns(j, eventDelivery, [{ message: event.configuration.eventName, count: 7 }, { message: event.configuration.eventName, count: 7 }]);
  await j.inspectDelivery(eventDelivery, "event-delivery");
  const another = await j.create("event", "Another independent entry");
  await j.add(another.id, one.authored.definition.id, [configured("message", "another standalone source"), configured("count", 8)]);
  await j.enable(another.id); await j.event(String(another.configuration.eventName));
  await inspectRuns(j, await j.waitDelivery(another.id), [{ message: "another standalone source", count: 8 }]);
  const actions = { pause: (id: string, capture?: string) => pause(j, id, capture), replace: (authored: Authored, state: WorkflowState) => replace(j, authored, state) };
  await verifyScheduledRetention(j, [one, two], actions);
  await verifyCronAndOverdueRecovery(j, [one, two], actions);
  j.observed.push("Actual PublishEvent producer fanned out to two independently completed native executions", "One workflow started from another standalone resource");
  await verifyUnreadableScheduleAdmission(j);
});

test("standalone HTTP modes", async ({ page }) => {
  const j = journeys.get(page)!;
  const healthy = await j.echo("Healthy HTTP subscriber"); const unavailable = await j.echo("Unavailable HTTP subscriber");
  const http = await j.create("http", "Asynchronous fan out");
  for (const target of [healthy, unavailable]) await j.add(http.id, target.authored.definition.id, [source("message", ["metadata", "method"]), configured("count", 7)]);
  await j.enable(http.id); await j.capture("http-fanout-mode");
  const accepted = await j.sourceRequest(http, { actual: "source request" }, 202);
  expect(accepted.body.admission).toBe("Accepted"); expect(accepted.body.acceptedCount).toBe(2);
  const delivery = await j.waitDelivery(http.id, new Set(), 2); await inspectRuns(j, delivery, [{ message: "POST", count: 7 }, { message: "POST", count: 7 }]);
  await j.inspectDelivery(delivery, "http-admitted");
  await expect.poll(async () => (await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${delivery.id}`)).transport?.state).toBe("WriteCompleted");
  await page.reload(); await j.capture("http-transport-outcome");
  await j.api(`/publishing/workflows/${unavailable.authored.definition.id}/slots/default`, "DELETE");
  const refused = await j.sourceRequest(http, { actual: "RequireAll refuses entire cohort" });
  expect(refused.response.ok()).toBe(false); expect(refused.body.acceptedCount).toBe(0);
  expect(refused.body.recipients).toEqual(expect.arrayContaining([expect.objectContaining({ admission: "NotAcceptedDueToPolicy" })]));
  await j.open(http.id); await j.capture("http-policy-refused");
  await editPolicy(j, http.id, "AcceptAvailable");
  const partial = await j.sourceRequest(http, { actual: "AcceptAvailable identifies partial admission" }, 202);
  expect(partial.body).toMatchObject({ admission: "PartiallyAccepted", admissionPolicy: "AcceptAvailable", acceptedCount: 1, refusedCount: 1 });
  await j.inspectDelivery(await j.api(`/runtime/standalone-trigger-deliveries/${partial.body.deliveryId}`), "http-partially-admitted");
  await j.api(`/publishing/workflows/${healthy.authored.definition.id}/slots/default`, "DELETE");
  const zero = await j.sourceRequest(http, { actual: "No eligible subscribers" }); expect(zero.response.ok()).toBe(false); expect(zero.body.acceptedCount).toBe(0);
  await j.open(http.id); await j.capture("http-zero-accepted");
  await j.sourceRequest(http, "{malformed", 400, { "Content-Type": "application/json" }); await j.capture("http-rejected");
  await j.api(`/publishing/workflows/${healthy.authored.definition.id}/slots/default/restore`, "POST", {});
  const conflict = await j.create("http", "Conflicting endpoint", { route: String(http.configuration.path) });
  await j.add(conflict.id, healthy.authored.definition.id, [configured("message", "conflict"), configured("count", 7)]);
  await j.open(conflict.id); await page.getByRole("button", { name: "Enable trigger", exact: true }).click();
  await expect(page.getByRole("button", { name: "Apply reviewed change", exact: true })).toBeDisabled(); await j.capture("http-route-conflict");
  const responseState: WorkflowState = { inputs: [input("message"), input("count", "Int32")], outputs: [], variables: [], rootActivity: {
    nodeId: "reply", activityVersionId: await j.version("Elsa.Activities.Http.Activities.WriteHttpResponse"), outputs: [], inputs: [literal("StatusCode", 201), literal("Body", "Response from the actual subscribed workflow"), literal("ContentType", "text/plain")] } };
  const owner = await j.submit("HTTP response owner", responseState); await j.publish(owner);
  const sync = await j.create("http", "Workflow response", { mode: "WorkflowResponse" });
  await j.add(sync.id, owner.definition.id, [configured("message", "workflow response source"), configured("count", 7)]); await j.enable(sync.id); await j.capture("http-workflow-response-mode");
  const response = await j.sourceRequest(sync, { actual: "workflow owns response" }, 201); expect(response.body.text).toBe("Response from the actual subscribed workflow");
  const ambiguous = await j.submit("Second HTTP response owner", responseState); await j.publish(ambiguous);
  await j.open(sync.id); await page.getByRole("button", { name: "Add workflow", exact: true }).click();
  await page.getByLabel("Workflow and publication slot", { exact: true }).selectOption(JSON.stringify({ definitionId: ambiguous.definition.id, slotName: "default" }));
  for (const [name, value] of [["message", "second owner"], ["count", "7"]]) { await page.getByLabel(`Value source for ${name}`, { exact: true }).selectOption("Literal"); await page.getByLabel(`Configured value for ${name}`, { exact: true }).fill(value); }
  await page.getByRole("checkbox", { name: "Enable this subscription", exact: true }).check(); await page.getByRole("checkbox", { name: "Authorize this shared trigger to invoke the workflow", exact: true }).check();
  await page.getByRole("button", { name: "Review subscription", exact: true }).click(); await expect(page.getByRole("button", { name: "Apply reviewed change", exact: true })).toBeDisabled(); await j.capture("http-response-owner-error");
  j.observed.push("Actual HTTP requests exercised both explicit response modes", "RequireAll refused complete mixed cohort; AcceptAvailable returned explicit partial 202; zero accepted failed", "Client received exact workflow-owned status and body", "Successful response write observation remained separate from run completion", "Malformed JSON and conflicting standalone endpoint were refused");
  await verifyEmbeddedHttpRouteConflicts(j, http, healthy.authored.definition.id);
  await verifyHttpTimeoutAndDisconnect(j);
  await verifyHttpAdmissionAndQueueFaults(j);
  await verifyHttpWriteFailure(j);
});

test("standalone delivery lifecycle and recovery", async ({ page }) => {
  const j = journeys.get(page)!;
  const target = await j.echo("Serving slot version changes"); const event = await j.create("event", "Retained delivery");
  await j.add(event.id, target.authored.definition.id, [source("message", ["metadata", "eventName"]), configured("count", 7)]); await j.enable(event.id);
  await j.event(String(event.configuration.eventName)); const initial = await j.waitDelivery(event.id);
  await j.inspectDelivery(initial, "delivery-admission"); const started = await j.started(initial); await j.inspectDelivery(started, "delivery-subscriber-outcomes");
  const captured = initial.recipients[0].capturedVersion; expect(captured).toBeTruthy();
  await replace(j, target.authored, target.state); const before = new Set((await j.deliveries(event.id)).map(item => item.id));
  await j.event(String(event.configuration.eventName)); const later = await j.waitDelivery(event.id, before);
  expect(later.recipients[0].capturedVersion).not.toBe(captured);
  const old = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${initial.id}`); expect(old.recipients[0].capturedVersion).toBe(captured);
  await j.inspectDelivery(old, "delivery-captured-version");
  await pause(j, event.id); await j.api(`/publishing/workflows/${target.authored.definition.id}/slots/default`, "DELETE");
  await j.open(event.id); await page.reload(); await expect(page.getByRole("region", { name: "Workflow subscriptions", exact: true })).toContainText(/not serving|unpublished|blocked/i); await j.capture("target-unpublished");
  const retained = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${initial.id}`); expect(retained.recipients[0].capturedVersion).toBe(captured);
  expect(retained.recipients[0].executionReference?.id).toBe(started.recipients[0].executionReference?.id);
  j.observed.push("Accepted delivery captured serving version one; later acceptance captured replacement version two", "Pause/unpublish retained exact old target and execution identity");
  const recoveryTargets = [await j.echo("Captured before process failure"), await j.echo("Pending cancellation before process failure")];
  const recoveryHttp = await j.create("http", "Durable process recovery");
  for (const recipientTarget of recoveryTargets) await j.add(recoveryHttp.id, recipientTarget.authored.definition.id, [configured("message", "captured version one input"), configured("count", 17)]);
  await j.enable(recoveryHttp.id);
  let recoveryPromise: ReturnType<TriggerJourney["host"]> | undefined;
  try {
    await j.host("arm-dispatch-boundary", { triggerId: recoveryHttp.id });
    const admitted = await j.sourceRequest(recoveryHttp, { actual: "durably accept both recipients before process failure" }, 202);
    expect(admitted.body).toMatchObject({ admission: "Accepted", acceptedCount: 2, refusedCount: 0 });
    const boundary = (await j.host("await-dispatch-boundary")).boundary as DispatchBoundary;
    expect(boundary).toMatchObject({ nodeIdentity: "A", sourceRevision: process.env.UX_SOURCE_SHA, phase: "DispatchCommitted", blockAfterFirstSeal: true });
    expect(boundary.deliveryId).toBe(admitted.body.deliveryId);
    expect(boundary.claimOwnerId).toBeTruthy(); expect(boundary.claimExpiresAt).toBeTruthy(); expect(boundary.claimFencingToken).toBeGreaterThan(0);
    let held = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${admitted.body.deliveryId}`);
    expect(held.acceptedCount).toBe(2);
    expect(held.recipients.map(item => item.id).sort()).toEqual(boundary.cohortRecipients.map(item => item.recipientId).sort());
    const sealed = held.recipients.find(item => item.id === boundary.recipientId)!;
    const pending = held.recipients.find(item => item.id !== boundary.recipientId)!;
    expect(sealed).toMatchObject({ dispatch: "DispatchCommitted", plannedExecutionIdentity: boundary.plannedExecutionId });
    expect(sealed.executionReference).toBeFalsy(); expect(pending.dispatch).toBe("NotDispatched"); expect(pending.executionReference).toBeFalsy();
    const sealedTarget = recoveryTargets.find(item => item.authored.definition.id === sealed.target!.definitionId)!;
    expect(boundary.capturedTarget.artifactId).toBe(sealedTarget.published.artifactId);
    await j.inspectDelivery(held, "delivery-dispatch-committed");
    const sealedFrame = page.getByRole("region", { name: `Recipient ${sealed.id}`, exact: true }).locator("..");
    await expect(sealedFrame.getByRole("button", { name: "Cancel pending recipient", exact: true })).toBeDisabled();
    await expect(sealedFrame.getByRole("link", { name: "Inspect actual run", exact: true })).toHaveCount(0);
    await expect(sealedFrame).toContainText("Recovery still owes the exact command"); await j.capture("cancel-dispatch-committed");
    const committedCancellation = await j.change(await j.read(recoveryHttp.id), { kind: "CancelPendingRecipients", deliveryId: held.id, expectedDeliveryRevision: held.revision,
      recipients: [{ recipientId: sealed.id, expectedRevision: sealed.revision }] });
    expect(committedCancellation.effects).toEqual(expect.arrayContaining([expect.objectContaining({ recipientId: sealed.id, outcome: "DispatchCommitted" })]));
    held = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    await j.inspectDelivery(held, "delivery-held-cohort");
    const pendingFrame = page.getByRole("region", { name: `Recipient ${pending.id}`, exact: true }).locator("..");
    await pendingFrame.getByRole("button", { name: "Cancel pending recipient", exact: true }).click();
    const cancelled = await j.review("pending-cancel-review");
    expect(cancelled.effects).toEqual(expect.arrayContaining([expect.objectContaining({ recipientId: pending.id, outcome: "Cancelled" })]));
    await expect(page.getByRole("region", { name: `Recipient ${pending.id}`, exact: true })).toContainText("Cancelled"); await j.capture("pending-cancelled");
    await pause(j, recoveryHttp.id);
    const replacementState = structuredClone(sealedTarget.state);
    (replacementState.rootActivity.structure!.payload.activities as { inputs: unknown[] }[])[0].inputs = [literal("name", "message"), literal("value", "replacement version two")];
    const replacement = await replace(j, sealedTarget.authored, replacementState);
    expect(replacement).toBeTruthy();
    let changed = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    expect(changed.recipients.find(item => item.id === sealed.id)!.capturedVersion).toBe(sealed.capturedVersion);
    expect(changed.recipients.find(item => item.id === sealed.id)!.currentVersion).not.toBe(sealed.capturedVersion);
    await j.api(`/publishing/workflows/${sealedTarget.authored.definition.id}/slots/default`, "DELETE");
    changed = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    expect(changed.recipients.find(item => item.id === sealed.id)!.capturedVersion).toBe(sealed.capturedVersion);
    expect(changed.recipients.find(item => item.id === sealed.id)!.executionReference).toBeFalsy();
    recoveryPromise = j.host("recover-on-peer");
    const peerReady = await j.recoveryPeerReady();
    expect(peerReady.previousProcessId).toBe(boundary.processId); expect(peerReady.previousBootId).toBe(boundary.bootId);
    const afterProcessLoss = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    expect(afterProcessLoss.recipients.find(item => item.id === sealed.id)).toMatchObject({ dispatch: "DispatchCommitted" });
    expect(afterProcessLoss.recipients.find(item => item.id === sealed.id)!.executionReference).toBeFalsy();
    expect(afterProcessLoss.recipients.find(item => item.id === pending.id)).toMatchObject({ dispatch: "Cancelled" });
    await j.inspectDelivery(afterProcessLoss, "delivery-partial-failure");
    const recovery = await recoveryPromise;
    expect(recovery.recovery).toMatchObject({ previousNodeIdentity: "A", nodeIdentity: "B", stopSignal: "SIGKILL", naturalLeaseWaitSeconds: 61, leaseMutated: false, clockChanged: false });
    expect(recovery.previousProcessId).toBe(boundary.processId); expect(recovery.processId).not.toBe(boundary.processId);
    let recovered: DeliveryView | undefined;
    await expect.poll(async () => {
      recovered = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
      return recovered.recipients.find(item => item.id === sealed.id)?.executionReference?.id;
    }, { timeout: 45_000 }).toBe(boundary.plannedExecutionId);
    await j.outputs(boundary.plannedExecutionId, { message: "captured version one input", count: 17 });
    recovered = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    expect(recovered.recipients.find(item => item.id === sealed.id)).toMatchObject({ capturedVersion: sealed.capturedVersion, dispatch: "Started", executionReference: { id: boundary.plannedExecutionId } });
    expect(recovered.recipients.find(item => item.id === pending.id)).toMatchObject({ dispatch: "Cancelled" });
    expect(recovered.recipients.find(item => item.id === pending.id)!.executionReference).toBeFalsy();
    const neverStarted = await page.request.get(`${j.base}/runtime/workflows/instances/${pending.plannedExecutionIdentity}`);
    expect(neverStarted.status()).toBe(404);
    j.requests.push({ kind: "actual-cancelled-root-remains-absent-after-peer-recovery", path: `/runtime/workflows/instances/${pending.plannedExecutionIdentity}`, status: neverStarted.status() });
    const instance = await j.api<{ instance: { artifactId: string; definitionVersionId: string }; activities: { activityExecutionId: string; firstCheckpointId?: string; lastCheckpointId?: string; provenance: { parentActivityExecutionId?: string | null } }[] }>(`/runtime/workflows/instances/${boundary.plannedExecutionId}`);
    expect(instance.instance.artifactId).toBe(boundary.capturedTarget.artifactId); expect(instance.instance.definitionVersionId).toBe(boundary.capturedTarget.definitionVersionId);
    const roots = instance.activities.filter(item => !item.provenance.parentActivityExecutionId); expect(roots).toHaveLength(1);
    expect(roots[0].firstCheckpointId).toBeTruthy(); expect(roots[0].lastCheckpointId).toBeTruthy();
    const again = await j.api<typeof instance>(`/runtime/workflows/instances/${boundary.plannedExecutionId}`);
    expect(again.activities.filter(item => !item.provenance.parentActivityExecutionId)).toEqual(roots);
    const peer = ((await j.host("inspect-dispatch-boundary")).boundary as { peer: DispatchBoundary }).peer;
    expect(peer).toMatchObject({ nodeIdentity: "B", processId: recovery.processId, sourceRevision: boundary.sourceRevision, deliveryId: boundary.deliveryId,
      recipientId: boundary.recipientId, plannedExecutionId: boundary.plannedExecutionId, sealFingerprint: boundary.sealFingerprint, phase: "TargetAccepted" });
    expect(peer.bootId).not.toBe(boundary.bootId); expect(peer.capturedTarget).toEqual(boundary.capturedTarget); expect(peer.acceptanceReceipt).toBeTruthy();
    expect(peer.claimOwnerId).not.toBe(boundary.claimOwnerId); expect(peer.claimFencingToken).toBeGreaterThan(boundary.claimFencingToken!);
    await j.inspectDelivery(recovered, "delivery-recovered");
    j.observed.push("A real HTTP 202 durably accepted two native roots; the acceptance-only decorator verified the physical DispatchCommitted seal before enqueue", "Normal UI review cancelled only the unsealed recipient; a normal attempted committed cancellation returned DispatchCommitted, with no run link before actual target acceptance", "Pause, serving-slot replacement and unpublication preserved the captured original material and mapped inputs", "SIGKILL of owned process A followed by a distinct boot/node B over the same stores recovered after a full natural lease interval; original artifact, execution identity, one inspected root and checkpoint identities were retained; cancelled root remained absent");
  } finally {
    if (recoveryPromise) await recoveryPromise;
    await j.host("restore-features");
  }
  await verifyConcurrentNodeClaims(j);
  j.observed.push("The HTTP journey separately retains the successful-recipient versus shed-recipient retry suppression witness with exact source/root/checkpoint identities");
});

test("standalone genuine BPMN inputs", async ({ page }) => {
  const j = journeys.get(page)!;
  const worker = await j.echo("Actual declared-input service");
  const process = await j.submit("Genuine authored process", { inputs: [input("processMessage")], outputs: [], variables: [], rootActivity: {
    nodeId: "process", activityVersionId: await j.version("Elsa.Activities.Bpmn.Activities.BpmnProcess"), inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [], elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "service", elementType: "task", name: "Consume declared input" }, { elementId: "end", elementType: "endEvent" }],
      sequenceFlows: [{ flowId: "start-service", sourceRef: "start", targetRef: "service" }, { flowId: "service-end", sourceRef: "service", targetRef: "end" }]
    } } } });
  await page.goto(definitionPath(process.definition.id));
  await page.locator('.react-flow__node[data-id="service"]').press("Enter");
  await page.getByRole("button", { name: "Bind a published service workflow", exact: true }).press("Enter");
  const picker = page.getByRole("combobox", { name: /workflow/i }).first();
  await picker.fill(`Actual declared-input service ${j.stamp}`); await page.getByRole("option").filter({ hasText: `Actual declared-input service ${j.stamp}` }).first().click();
  await page.getByLabel("Service publication", { exact: true }).selectOption({ label: "default" });
  const usePublication = page.getByRole("button", { name: "Use this service publication", exact: true }); await usePublication.click();
  await expect(page.getByRole("heading", { name: "Map service workflow inputs", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Cancel input mapping", exact: true }).press("Enter"); await expect(usePublication).toBeFocused();
  await usePublication.press("Enter");
  await page.getByRole("button", { name: "Apply service input mapping", exact: true }).press("Enter");
  await expect(page.getByRole("alert").filter({ hasText: "Map this required service input" }).first()).toBeVisible(); await j.capture("bpmn-worker-input-validation");
  await page.getByLabel("Service input message source", { exact: true }).selectOption("Input"); await page.getByLabel("Process input for service message", { exact: true }).selectOption("processMessage");
  await page.getByLabel("Service input count source", { exact: true }).selectOption("Input");
  await expect(page.getByLabel("Process input for service count", { exact: true }).locator('option[value="processMessage"]')).toBeDisabled();
  await expect(page.getByLabel("Service input undeclaredWorkerInput source", { exact: true })).toHaveCount(0);
  await page.getByLabel("Service input count source", { exact: true }).selectOption("Literal"); await page.getByLabel("JSON value for service count", { exact: true }).fill("7"); await j.capture("bpmn-worker-input-mapping");
  await page.getByRole("button", { name: "Apply service input mapping", exact: true }).press("Enter");
  await expect(page.getByRole("region", { name: "Saved service input mappings", exact: true })).toContainText("Process input: processMessage");
  // Save valid serving mappings through the editor before the separate crafted publication controls.
  const save = page.getByRole("button", { name: "Save", exact: true }); if (await save.isEnabled()) await save.press("Enter");
  await expect.poll(async () => JSON.stringify((await j.api<{ state: unknown }>(`/design/workflows/drafts/${process.draftId}`)).state)).toContain('"kind":"Input","name":"processMessage"');
  await page.reload(); await page.locator('.react-flow__node[data-id="service"]').press("Enter");
  await expect(page.getByRole("region", { name: "Saved service input mappings", exact: true })).toContainText("Configured JSON: 7"); await j.capture("bpmn-worker-input-reopened");
  const authoredState = await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${process.draftId}`);
  expect(JSON.stringify(authoredState.state)).toContain('"formatVersion":2');
  const version = await j.api<{ id: string }>(`/design/workflows/drafts/${process.draftId}/promote`, "POST", { operationKey: randomUUID() });
  await j.api(`/interchange/bpmn/versions/${version.id}/publish-direct`, "POST", { requestId: randomUUID() });
  await verifyBpmnInputRefusals(j, process, version.id, authoredState.state);
  const event = await j.create("event", "Start genuine authored process");
  await j.add(event.id, process.definition.id, [source("processMessage", ["metadata", "eventName"])]); await j.enable(event.id); await j.event(String(event.configuration.eventName));
  const delivery = await j.started(await j.waitDelivery(event.id)); const recipient = delivery.recipients[0];
  const executionId = recipient.executionReference!.id;
  let inspection: { execution: { status: string }; attempts: { workerExecutionId: string; status: string }[] } | undefined;
  await expect.poll(async () => { inspection = await j.api(`/bpmn/executions/${executionId}/inspection`); return inspection!.attempts.length; }, { timeout: 45_000 }).toBe(1);
  await j.outputs(inspection!.attempts[0].workerExecutionId, { message: event.configuration.eventName, count: 7 });
  await page.goto(`/workflows/bpmn/runs/${executionId}?backend=legacy`); await expect(page.getByRole("heading", { name: "BPMN run", exact: true })).toBeVisible(); await j.capture("bpmn-declared-input-run");
  await expect.poll(async () => (await j.api<{ execution: { status: string } }>(`/bpmn/executions/${executionId}/inspection`)).execution.status, { timeout: 45_000 }).toBe("TerminalSettled");
  await page.reload(); await j.capture("bpmn-worker-input-consumed");
  j.observed.push("Actual changed service-worker editor authored model-input and literal mappings", "Cancel returned focus; missing required mappings rejected", "Saved mappings survived editor reload", "Real authored direct BPMN publication started through standalone Event, worker consumed declared inputs, ordinary model continuation settled");
});

test("standalone access and availability", async ({ page, browser }) => {
  const j = journeys.get(page)!;
  const trigger = await j.create("event", "Scoped operator resource");
  const actor = await j.api<{ canRead: boolean; canManage: boolean; canOperate: boolean }>("/runtime/standalone-triggers/identity");
  expect(actor).toMatchObject({ canRead: true, canManage: true, canOperate: true });
  const setup = await j.host("setup-access");
  expect(setup.previousProcessId).not.toBe(setup.processId);
  const sourceOwnedId = setup.setup!.sourceOwnedTriggerId;
  const scoped = await browser.newContext({ baseURL: process.env.ELSA_STUDIO_URL, viewport: page.viewportSize() ?? undefined });
  const mode = String(j.info.project.metadata.themeMode ?? j.info.project.use.colorScheme ?? "light");
  await scoped.addInitScript(mode => {
    localStorage.setItem("elsa-studio-theme", "meridian"); localStorage.setItem("elsa-studio-theme-mode", mode);
    localStorage.setItem("elsa-studio-workspace-style", "linen");
  }, mode);
  const scopedPage = await scoped.newPage();
  const scopedJourney = new TriggerJourney(scopedPage, j.info);
  const scopedErrors: string[] = []; scopedPage.on("pageerror", error => scopedErrors.push(error.message));
  let peerBase: string | undefined, peerTrigger: TriggerView | undefined;
  async function peerApi<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    if (!peerBase) throw new Error("The runner must start its owned scope peer");
    const response = await page.request.fetch(`${peerBase}${path}`, { method, data });
    const result = await response.json(); expect(response.ok(), JSON.stringify(result)).toBe(true);
    j.requests.push({ kind: "actual-isolated-peer-api", backendUrl: peerBase, method, path, status: response.status(), result });
    return result as T;
  }
  async function switchProfile(name: "Default backend" | "Owned alternate connection", id: "legacy" | "acceptance-alternate") {
    await page.getByRole("button", { name: /^Select backend\. Current:/ }).click();
    await page.getByLabel("Backend connections", { exact: true }).getByRole("button", { name: new RegExp(name) }).click();
    await expect(page).toHaveURL(new RegExp(`backend=${id}`));
  }
  async function signIn(username: string) {
    await scopedPage.goto(triggerPath(trigger.id));
    const form = scopedPage.locator('form[action="/_elsa/identity/login"]');
    await form.getByLabel(/username/i).fill(username); await form.getByLabel(/password/i).fill("Password123!");
    await form.getByRole("button", { name: /sign in/i }).click();
  }
  try {
    const anonymous = await scoped.request.get(`${j.base}/runtime/standalone-triggers/${trigger.id}`, { maxRedirects: 0 });
    expect([401, 403]).toContain(anonymous.status()); expect(await anonymous.text()).not.toContain(trigger.name);
    j.requests.push({ kind: "actual-anonymous-denial", path: `/runtime/standalone-triggers/${trigger.id}`, status: anonymous.status(), resourceDisclosed: false });
    await signIn("acceptance-reader");
    await expect(scopedPage.getByRole("heading", { name: trigger.name, exact: true })).toBeVisible();
    expect(await scopedJourney.api("/runtime/standalone-triggers/identity")).toMatchObject({ canRead: true, canManage: false, canOperate: false });
    await expect(scopedPage.getByRole("button", { name: "Edit configuration", exact: true })).toBeDisabled();
    await expect(scopedPage.getByRole("button", { name: "Enable trigger", exact: true })).toBeDisabled();
    await scopedJourney.capture("triggers-read-only");
    const refused = await scoped.request.post(`${j.base}/runtime/standalone-triggers/${trigger.id}/changes/preview`, { data: { expectedRevision: trigger.revision, command: { kind: "PauseTrigger" } } });
    expect(refused.status()).toBe(403);
    j.requests.push({ kind: "actual-reader-mutation-refused", path: `/runtime/standalone-triggers/${trigger.id}/changes/preview`, status: refused.status() });
    await scopedPage.getByRole("button", { name: /^Account:/ }).click();
    await scopedPage.getByRole("menuitem", { name: "Sign out", exact: true }).click();
    await expect(scopedPage.locator('form[action="/_elsa/identity/login"]')).toBeVisible();
    await expect(scopedPage.getByText(trigger.name, { exact: true })).toHaveCount(0);
    await signIn("acceptance-denied");
    await expect(scopedPage.getByRole("heading", { name: "Trigger access unavailable", exact: true })).toBeVisible();
    const deniedActor = await scoped.request.get(`${j.base}/runtime/standalone-triggers/identity`);
    expect(deniedActor.status()).toBe(403);
    j.requests.push({ kind: "actual-signed-in-denied-actor", path: "/runtime/standalone-triggers/identity", status: deniedActor.status() });
    for (const value of [trigger.id, trigger.name, sourceOwnedId]) await expect(scopedPage.getByText(value, { exact: true })).toHaveCount(0);
    const hidden = await scoped.request.get(`${j.base}/runtime/standalone-triggers/${trigger.id}`);
    expect([403, 404]).toContain(hidden.status()); expect(await hidden.text()).not.toContain(trigger.name);
    j.requests.push({ kind: "actual-signed-in-denial", path: `/runtime/standalone-triggers/${trigger.id}`, status: hidden.status(), resourceDisclosed: false });
    await scopedJourney.capture("triggers-denied");
    // This tenant is selected by the normal Identity password API because the login page has no tenant selector.
    const tenantLogin = await scoped.request.post(`${j.base}/_elsa/identity/login`, { data: { username: "acceptance-other-tenant", password: "Password123!", tenantId: "ux-other" } });
    expect(tenantLogin.ok()).toBe(true);
    await scopedPage.goto("/workflows/triggers?backend=legacy");
    await expect(scopedPage.getByRole("heading", { name: "No triggers yet", exact: true })).toBeVisible();
    await expect(scopedPage.getByText(trigger.name, { exact: true })).toHaveCount(0);
    const otherTenant = await scoped.request.get(`${j.base}/runtime/standalone-triggers/${trigger.id}`);
    expect(otherTenant.status()).toBe(404);
    j.requests.push({ kind: "actual-tenant-scope-denial", tenantId: "ux-other", path: `/runtime/standalone-triggers/${trigger.id}`, status: otherTenant.status(), resourceDisclosed: false });
    await scopedJourney.capture("triggers-other-tenant");
    await j.open(sourceOwnedId);
    await expect(page.getByText("acceptance-source-provider · Read-only", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Edit configuration", exact: true })).toBeDisabled();
    await j.capture("trigger-source-owned");
    const peerStarted = await j.host("start-scope-peer"); peerBase = peerStarted.scopePeer!.url;
    expect(peerStarted.scopePeer).toMatchObject({ runtimeAndDesignStoresIsolated: true, identityStoreSharedWithOwnedPrimary: true, sourceSha: process.env.UX_SOURCE_SHA });
    const peerLogin = await page.request.post(`${peerBase}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } }); expect(peerLogin.ok()).toBe(true);
    peerTrigger = await peerApi<TriggerView>("/runtime/standalone-triggers", "POST", { requestId: randomUUID(), name: `Only isolated backend ${j.stamp}`,
      typeKey: "event", schemaVersion: "1", configuration: { eventName: `isolated.${j.stamp}` } });
    await j.host("independent-backend-auth");
    await j.open(trigger.id); await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
    await page.getByLabel("Trigger name", { exact: true }).fill(`Unsaved previous connection ${j.stamp}`);
    await switchProfile("Owned alternate connection", "acceptance-alternate");
    await expect(page.getByLabel("Trigger name", { exact: true })).toHaveCount(0);
    await expect(page.getByText(`Unsaved previous connection ${j.stamp}`, { exact: true })).toHaveCount(0);
    await page.goto(triggerPath(peerTrigger.id).replace("backend=legacy", "backend=acceptance-alternate"));
    await expect(page.getByRole("heading", { name: peerTrigger.name, exact: true })).toBeVisible();
    await expect(page.getByText(trigger.name, { exact: true })).toHaveCount(0);
    await j.capture("trigger-backend-scope-switch");
    await switchProfile("Default backend", "legacy");
    expect((await j.read(trigger.id)).name).toBe(trigger.name);
    await page.goto("/workflows/triggers?backend=legacy");
    await expect(page.getByRole("link", { name: trigger.name, exact: true })).toBeVisible();
    const oldDocument = await page.locator("html").elementHandle();
    const pendingRead = page.waitForRequest(request => request.method() === "GET" && request.url().startsWith(`${j.base}/runtime/standalone-triggers?`));
    await j.host("pause"); await page.getByRole("button", { name: "Refresh", exact: true }).click();
    const obsoleteRead = await pendingRead; const obsoleteResponse = obsoleteRead.response();
    await expect(page.getByLabel("Loading triggers", { exact: true })).toBeVisible();
    await switchProfile("Owned alternate connection", "acceptance-alternate");
    await page.goto("/workflows/triggers?backend=acceptance-alternate");
    await expect(page.getByRole("link", { name: peerTrigger.name, exact: true })).toBeVisible();
    await expect(page.getByText(trigger.name, { exact: true })).toHaveCount(0);
    let documentReplaced = false;
    try { documentReplaced = !await oldDocument!.evaluate(node => node.isConnected); } catch { documentReplaced = true; }
    expect(documentReplaced).toBe(true);
    await j.host("resume");
    const lateResponse = await obsoleteResponse;
    if (lateResponse) await lateResponse.finished();
    await expect(page.getByRole("link", { name: peerTrigger.name, exact: true })).toBeVisible();
    await expect(page.getByText(trigger.name, { exact: true })).toHaveCount(0);
    expect((await peerApi<TriggerView>(`/runtime/standalone-triggers/${peerTrigger.id}`)).name).toBe(peerTrigger.name);
    const absentOnPeer = await page.request.get(`${peerBase}/runtime/standalone-triggers/${trigger.id}`); expect(absentOnPeer.status()).toBe(404);
    j.requests.push({ kind: "actual-scope-change-during-inflight-read", previousBackendUrl: j.base, selectedBackendUrl: peerBase,
      previousDocumentReplaced: documentReplaced, previousRequestUrl: obsoleteRead.url(), previousRequestFailure: obsoleteRead.failure()?.errorText ?? null,
      previousResponseStatus: lateResponse?.status() ?? null, mechanism: obsoleteRead.failure() ? "request cancellation and document replacement" : "document replacement",
      peerResourceId: peerTrigger.id, previousResourceStatusOnPeer: absentOnPeer.status(), studioHostAuthEnabled: false, backendProfileAuthenticationEnabled: true,
      identityStoreSharedWithOwnedPrimary: true, runtimeAndDesignStoresIsolated: true });
    await j.capture("triggers-scope-response-isolated");
    await switchProfile("Default backend", "legacy"); await j.host("restore-studio-features");
    await page.goto("/workflows/triggers/new?backend=legacy");
    await page.getByLabel("Trigger type", { exact: true }).selectOption("acceptance-event:1");
    await page.getByLabel("Trigger name", { exact: true }).fill(`Independent editor ${j.stamp}`);
    await page.getByLabel("Acme topic", { exact: true }).fill("saved through independently built editor");
    await j.capture("trigger-optional-editor-installed");
    const createdEditorResource = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/runtime/standalone-triggers");
    await page.getByRole("button", { name: "Create trigger", exact: true }).click();
    const editorResourceResponse = await createdEditorResource; expect(editorResourceResponse.status()).toBe(201);
    const editorResource = await editorResourceResponse.json() as { id: string; configuration: { topic: string } }; j.triggers.push(editorResource.id);
    await j.open(editorResource.id); await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
    await expect(page.getByLabel("Acme topic", { exact: true })).toHaveValue(editorResource.configuration.topic);
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await j.host("without-optional-editor"); await page.reload();
    await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
    await expect(page.getByText("The optional configuration editor is unavailable. Your saved values are preserved; use the standard fields below or install the matching editor module.", { exact: true })).toBeVisible();
    await expect(page.getByLabel("Acme topic", { exact: true })).toHaveCount(0);
    await expect(page.getByLabel("Topic", { exact: true })).toHaveValue(editorResource.configuration.topic);
    await page.getByLabel("Topic", { exact: true }).fill("corrected through metadata fallback"); await j.capture("trigger-optional-editor-unavailable");
    await page.getByRole("button", { name: "Review changes", exact: true }).click(); await j.review();
    expect((await j.read(editorResource.id)).configuration.topic).toBe("corrected through metadata fallback");
    await j.host("restore-studio-features"); await page.reload();
    await page.getByRole("button", { name: "Edit configuration", exact: true }).click();
    await expect(page.getByLabel("Acme topic", { exact: true })).toHaveValue("corrected through metadata fallback");
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    const timer = await j.create("timer", "Preserved missing module", { interval: "PT1H" });
    await j.host("without-schedule-module"); await page.reload();
    await expect(page.getByText("The configuration module for timer version 1 is unavailable. Saved configuration is preserved; install a compatible module to edit it.", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Edit configuration", exact: true })).toBeDisabled();
    expect((await j.read(timer.id)).configuration).toEqual(timer.configuration); await j.capture("trigger-module-unavailable");
    await j.host("without-trigger-capability"); await page.reload();
    await expect(page.getByRole("status").filter({ hasText: /standalone-triggers/ })).toBeVisible();
    await expect(page.getByText(timer.name, { exact: true })).toHaveCount(0); await j.capture("trigger-capability-unavailable");
    await j.host("unsupported-trigger-contract"); await page.reload();
    await expect(page.getByRole("status").filter({ hasText: /standalone-triggers requires contract 1; this backend advertises 2/ })).toBeVisible();
    const unsupported = await j.api<{ capabilities: { id: string; contractVersion: string }[] }>("/capabilities");
    expect(unsupported.capabilities.find(item => item.id === "elsa.api.standalone-triggers")?.contractVersion).toBe("2");
    const noVersionTwoEndpoint = await page.request.get(`${j.base}/runtime/standalone-triggers/identity`);
    expect(noVersionTwoEndpoint.status()).toBe(404);
    j.requests.push({ kind: "actual-contract-advertisement-fixture", advertisedMajor: 2, implementedVersionTwoApi: false,
      realVersionOneFeatureInstalled: false, directIdentityEndpointStatus: noVersionTwoEndpoint.status() });
    await j.capture("trigger-contract-incompatible");
    await j.host("restore-features"); await page.reload();
    expect((await j.read(timer.id)).configuration).toEqual(timer.configuration);
    await page.getByRole("button", { name: "Back to triggers", exact: true }).click();
    await expect(page.getByRole("region", { name: "Triggers table", exact: true })).toBeVisible();
    await j.host("pause");
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await expect(page.getByLabel("Loading triggers", { exact: true })).toBeVisible(); await j.capture("triggers-loading");
    await j.host("stop");
    await expect(page.getByRole("heading", { name: "Could not load triggers", exact: true })).toBeVisible(); await j.capture("triggers-refresh-error");
    await j.host("start"); await page.getByRole("button", { name: "Retry", exact: true }).click();
    await expect(page.getByRole("region", { name: "Triggers table", exact: true })).toBeVisible();
    await j.host("stop");
    await page.getByRole("link", { name: trigger.name, exact: true }).click();
    await expect(page.getByRole("heading", { name: "Could not load triggers", exact: true })).toBeVisible();
    await expect(page.getByText(trigger.name, { exact: true })).toHaveCount(0); await j.capture("triggers-load-error");
    await j.host("start"); await page.getByRole("button", { name: "Retry", exact: true }).click();
    await expect(page.getByRole("heading", { name: trigger.name, exact: true })).toBeVisible(); await j.capture("triggers-recovered");
    j.observed.push("Separate real operator, reader and signed-in denied identities; direct API mutation and read refusals", "UI sign-out cleared the reader resource before denied sign-in; actual tenant-scoped login could not read the first tenant", "Provider-owned configuration was persisted through the real management committer and displayed read-only", "Normal backend-profile navigation cleared an unsaved edit; isolated Runtime/Design stores showed distinct data while sharing the owned Identity/session authority", "With Studio host authentication temporarily disabled and normal per-backend authentication enabled, an actual primary read was held by SIGSTOP; supported full-navigation switching replaced the old document, then primary resume could not overwrite the isolated peer's resource data. Request cancellation is recorded only when actually observed", "Independently built source-consumer modules supplied backend authoring and a public SDK configuration editor; actual Studio feature removal preserved metadata editing, and restoring the module retained corrected values", "Normal feature removal/restart preserved saved configuration and demonstrated missing family and missing capability", "SIGSTOP established actual loading; stopped backend caused separate refresh and initial-route failures; a new owned process and Retry recovered the same resource");
    j.observed.push("A normally composed contract-advertisement fixture advertised major 2 with the real v1 API removed; Studio rejected it before resource access. This fixture implements no v2 endpoint or v2 runtime semantics.");
  } finally {
    j.captures.push(...scopedJourney.captures); j.requests.push(...scopedJourney.requests);
    await scoped.close();
    if (peerTrigger) {
      const current = await peerApi<TriggerView>(`/runtime/standalone-triggers/${peerTrigger.id}`);
      const preview = await peerApi<{ previewId: string; canApply: boolean }>(`/runtime/standalone-triggers/${peerTrigger.id}/changes/preview`, "POST", { expectedRevision: current.revision, command: { kind: "DeleteTrigger" } });
      expect(preview.canApply).toBe(true);
      await peerApi(`/runtime/standalone-triggers/${peerTrigger.id}/changes/apply`, "POST", { requestId: randomUUID(), previewId: preview.previewId, expectedRevision: current.revision, command: { kind: "DeleteTrigger" } });
    }
    await j.host("stop-scope-peer");
    await j.host("restore-studio-features"); await j.host("restore-features"); await j.host("cleanup-access");
  }
  expect(scopedErrors, "Uncaught browser errors in scoped identities").toEqual([]);
});
