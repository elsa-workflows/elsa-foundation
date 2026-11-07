import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { TriggerJourney, configured, definitionPath, literal, type WorkflowState } from "./standaloneTriggersSupport";
import type { DeliveryView, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

/** Real embedded publications contend with the same physical reservation as standalone HTTP resources. */
export async function verifyEmbeddedHttpRouteConflicts(j: TriggerJourney, standaloneOwner: TriggerView, subscriberDefinitionId: string) {
  async function embedded(name: string, route: string) {
    const state: WorkflowState = { inputs: [], outputs: [], variables: [], rootActivity: {
      nodeId: "embedded-endpoint", activityVersionId: await j.version("Elsa.Activities.Http.Activities.HttpEndpoint"), outputs: [],
      inputs: [literal("Path", `/${route.replace(/^\/+/, "")}`),
        { referenceKey: "SupportedMethods", value: { expressionType: "Object", value: '["POST"]' } },
        literal("CanStartWorkflow", true), literal("Authorize", false), literal("ResponseMode", "Async")]
    } };
    return j.submit(name, state);
  }

  const embeddedRoute = `acceptance/embedded-first/${j.stamp}`;
  const first = await embedded("Embedded endpoint owns first", embeddedRoute); const firstPublication = await j.publish(first);
  const firstSlot = await j.api<{ activeActivationId: string }>(`/runtime/workflows/activation-slots/${first.definition.id}/default`);
  expect(firstSlot.activeActivationId).toBeTruthy();
  const refusedStandalone = await j.create("http", "Refused behind embedded owner", { route: embeddedRoute });
  await j.add(refusedStandalone.id, subscriberDefinitionId, [configured("message", "embedded collision"), configured("count", 7)]);
  await j.open(refusedStandalone.id); await j.page.getByRole("button", { name: "Enable trigger", exact: true }).click();
  await expect(j.page.getByRole("button", { name: "Apply reviewed change", exact: true })).toBeDisabled();
  await expect(j.page.getByText(/published workflow.*owns|(?:endpoint|route).*(?:conflict|reserved|owns)/i).first()).toBeVisible();
  expect((await j.read(refusedStandalone.id)).desiredState).toBe("Paused");
  expect((await j.api<typeof firstSlot>(`/runtime/workflows/activation-slots/${first.definition.id}/default`)).activeActivationId).toBe(firstSlot.activeActivationId);
  await j.capture("http-embedded-owner-conflict");
  const embeddedRequest = await j.page.request.post(`${j.base}/workflows/http/${embeddedRoute}`, { data: { actual: "original embedded owner remains routable" } });
  expect(embeddedRequest.status()).toBe(202);
  j.requests.push({ kind: "actual-embedded-owner-survives-standalone-conflict", embeddedDefinitionId: first.definition.id,
    publishedArtifactId: firstPublication.artifactId, activeActivationId: firstSlot.activeActivationId, route: embeddedRoute, requestStatus: embeddedRequest.status() });

  const ownerBefore = await j.read(standaloneOwner.id);
  const second = await embedded("Embedded publication refused behind standalone", String(standaloneOwner.configuration.path));
  const publicationPath = `/publishing/workflows/${second.version.id}/publish`;
  const refusedPublication = await j.page.request.post(`${j.base}${publicationPath}`, { data: {} });
  const refusedBody = await refusedPublication.json(); expect(refusedPublication.status(), JSON.stringify(refusedBody)).toBe(409);
  j.requests.push({ kind: "actual-standalone-owner-refuses-embedded-publication", method: "POST", path: publicationPath, status: refusedPublication.status(), result: refusedBody });
  const ownerAfter = await j.read(standaloneOwner.id);
  expect(ownerAfter).toMatchObject({ revision: ownerBefore.revision, desiredState: "Enabled", configuration: ownerBefore.configuration, http: { reservationState: "Reserved" } });

  // Use the actual publication UI as well as the direct protected request; its recovery message must stay visible.
  await j.page.goto(definitionPath(second.definition.id));
  await j.page.getByRole("button", { name: "Publish options", exact: true }).click();
  await j.page.getByRole("menuitemradio", { name: "Review & publish", exact: true }).click();
  const review = j.page.getByRole("dialog", { name: "Publish workflow", exact: true });
  await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
  const uiPublication = j.page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/publish"));
  await review.getByRole("button", { name: "Publish", exact: true }).click();
  expect((await uiPublication).status()).toBe(409);
  const failure = j.page.getByRole("dialog", { name: "Couldn’t publish", exact: true });
  await expect(failure).toContainText(/endpoint|route|reservation|exclusive/i);
  await expect(failure.getByRole("button", { name: "Retry publication", exact: true })).toBeVisible();
  await j.capture("http-standalone-owner-conflict");
  await failure.getByRole("button", { name: "Close", exact: true }).click();
  expect((await j.read(standaloneOwner.id)).revision).toBe(ownerBefore.revision);
  j.observed.push("Actual embedded-first publication refused standalone activation and retained its routable owner", "Actual standalone-first reservation refused embedded publication with409 through direct API and publication UI; recovery remained visible and the live standalone resource was unchanged");
}

type RetainedRun = { instance: { workflowExecutionId: string; status: string; artifactId: string; definitionVersionId: string } };

/** A real durable Delay keeps the business execution alive beyond its original HTTP connection. */
export async function verifyHttpTimeoutAndDisconnect(j: TriggerJourney) {
  const state: WorkflowState = { inputs: [], outputs: [], variables: [], rootActivity: {
    nodeId: "two-minute-response-delay", activityVersionId: await j.version("Elsa.Activities.Scheduling.Activities.Delay"),
    inputs: [literal("Duration", "00:02:00")], outputs: []
  } };
  const delayed = await j.submit("Genuine delayed response owner", state); const published = await j.publish(delayed);
  const trigger = await j.create("http", "Connection lifetime differs from accepted work", { mode: "WorkflowResponse" });
  await j.add(trigger.id, delayed.definition.id, []); await j.enable(trigger.id);
  const route = `/workflows/http/${String(trigger.configuration.path).replace(/^\/+/, "")}`;
  const timeoutKey = randomUUID(); const startedAt = Date.now();
  // The production server owns its normal30-second deadline. This new request allows its response to arrive.
  const timeout = await j.page.request.post(`${j.base}${route}`, { timeout: 45_000, data: { actual: "wait for the real server response deadline" }, headers: { "Idempotency-Key": timeoutKey } });
  const timeoutBody = await timeout.json(); expect(timeout.status(), JSON.stringify(timeoutBody)).toBe(504);
  expect(timeoutBody.code).toBe("ResponseTimedOut");
  let timedOut: DeliveryView | undefined;
  await expect.poll(async () => { timedOut = (await j.deliveries(trigger.id)).find(item => item.sourceOccurrenceId === timeoutKey); return timedOut?.transport?.state; }, { timeout: 45_000 }).toBe("TimedOut");
  expect(timedOut).toMatchObject({ admission: "Accepted", acceptedCount: 1, transport: { state: "TimedOut", statusCode: 504 } });
  const timedOutRun = await j.started(timedOut!); const timedOutId = timedOutRun.recipients[0].executionReference!.id;
  const liveAfterTimeout = await j.api<RetainedRun>(`/runtime/workflows/instances/${timedOutId}`);
  expect(["Running", "Suspended"]).toContain(liveAfterTimeout.instance.status); expect(liveAfterTimeout.instance.artifactId).toBe(published.artifactId);
  j.requests.push({ kind: "actual-workflow-response-timeout-with-retained-acceptance", method: "POST", path: route, sourceKey: timeoutKey,
    status: timeout.status(), result: timeoutBody, elapsedMilliseconds: Date.now() - startedAt, delivery: timedOut, retainedRun: liveAfterTimeout.instance });
  await j.inspectDelivery(await j.api(`/runtime/standalone-trigger-deliveries/${timedOut!.id}`), "http-timeout-retained");

  const disconnectKey = randomUUID();
  const exchange = j.page.evaluate(async ({ url, key }) => {
    const controller = new AbortController();
    const abort = (event: Event) => { if ((event as CustomEvent<string>).detail === key) controller.abort(); };
    window.addEventListener("elsa-ux-abort-owned-http", abort);
    try {
      const response = await fetch(url, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "Idempotency-Key": key },
        body: JSON.stringify({ actual: "abort only after observing physical admission" }), signal: controller.signal });
      return { kind: "response", status: response.status, errorName: null as string | null };
    } catch (error) { return { kind: "client-abort", status: null, errorName: error instanceof Error ? error.name : String(error) }; }
    finally { window.removeEventListener("elsa-ux-abort-owned-http", abort); }
  }, { url: `${j.base}${route}`, key: disconnectKey });
  let admitted: DeliveryView | undefined;
  try {
    await expect.poll(async () => { admitted = (await j.deliveries(trigger.id)).find(item => item.sourceOccurrenceId === disconnectKey); return admitted?.transport?.state; }, { timeout: 45_000 }).toBe("AwaitingResponse");
    expect(admitted).toMatchObject({ admission: "Accepted", acceptedCount: 1 });
    const beforeAbort = await j.started(admitted!); const executionId = beforeAbort.recipients[0].executionReference!.id;
    await j.page.evaluate(key => window.dispatchEvent(new CustomEvent("elsa-ux-abort-owned-http", { detail: key })), disconnectKey);
    const client = await exchange; expect(client).toMatchObject({ kind: "client-abort", status: null, errorName: "AbortError" });
    let disconnected: DeliveryView | undefined;
    await expect.poll(async () => { disconnected = await j.api(`/runtime/standalone-trigger-deliveries/${admitted!.id}`); return disconnected?.transport?.state; }, { timeout: 45_000 }).toBe("ClientDisconnected");
    expect(disconnected).toMatchObject({ admission: "Accepted", acceptedCount: 1 });
    expect(disconnected!.recipients[0].executionReference?.id).toBe(executionId);
    const liveAfterDisconnect = await j.api<RetainedRun>(`/runtime/workflows/instances/${executionId}`);
    expect(["Running", "Suspended"]).toContain(liveAfterDisconnect.instance.status); expect(liveAfterDisconnect.instance.artifactId).toBe(published.artifactId);
    j.requests.push({ kind: "actual-browser-abort-after-physical-admission", method: "POST", path: route, sourceKey: disconnectKey,
      beforeAbort, clientResult: client, afterDisconnect: disconnected, retainedRun: liveAfterDisconnect.instance });
    await j.inspectDelivery(disconnected!, "http-client-disconnected");
  } finally {
    await j.page.evaluate(key => window.dispatchEvent(new CustomEvent("elsa-ux-abort-owned-http", { detail: key })), disconnectKey);
    await exchange;
  }
  j.observed.push("A genuine two-minute Delay produced the normal server504; the accepted execution remained Running/Suspended on its original artifact", "A real browser AbortController cancelled its connection only after actual physical admission and execution were observed; Studio/API recorded ClientDisconnected while the same accepted execution remained live");
}
