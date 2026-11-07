import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { configured, source, TriggerJourney, type TriggerFault } from "./standaloneTriggersSupport";
import type { DeliveryView, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

type BusinessOutput = { value: unknown; isRedacted: boolean };
function scalar(output: BusinessOutput): unknown {
  expect(output).toBeTruthy(); expect(output.isRedacted).toBe(false);
  const value = output.value;
  if (value && typeof value === "object") {
    const envelope = value as { kind?: string; preview?: unknown; value?: unknown };
    if (envelope.kind === "null") return null;
    if (Object.hasOwn(envelope, "preview")) return envelope.preview;
    if (Object.hasOwn(envelope, "value")) return envelope.value;
  }
  return value;
}
async function outputs(j: TriggerJourney, id: string, expected: Record<string, unknown>) {
  let run: { instance: { status: string }; outputs: Record<string, BusinessOutput> } | undefined;
  await expect.poll(async () => { run = await j.api(`/runtime/workflows/instances/${id}`); return run!.instance.status; }, { timeout: 45_000 }).toBe("Completed");
  for (const [name, value] of Object.entries(expected)) expect(scalar(run!.outputs[name])).toEqual(value);
  return run!;
}

async function observedFault(j: TriggerJourney, mode: TriggerFault["faultMode"]) {
  let marker: TriggerFault | undefined;
  await expect.poll(async () => { marker = (await j.host("inspect-trigger-fault")).fault; return marker?.faultMode; },
    { timeout: 45_000, message: `Actual owned ${mode} public-port refusal was observed` }).toBe(mode);
  expect(marker!.sourceRevision).toBe(process.env.UX_SOURCE_SHA);
  expect(marker!.controlledPublicPortFault).toBe(true);
  return marker!;
}

async function send(j: TriggerJourney, trigger: TriggerView, key: string, payload: unknown) {
  const path = `/workflows/http/${String(trigger.configuration.path).replace(/^\/+/, "")}`;
  const response = await j.page.request.post(`${j.base}${path}`, { data: payload, headers: { "Idempotency-Key": key } });
  const body = await response.json() as Record<string, unknown>;
  j.requests.push({ kind: "actual-http-source-under-controlled-public-port-fault", path, sourceKey: key, status: response.status(), result: body });
  return { response, body };
}

async function pauseThroughUi(j: TriggerJourney, id: string) {
  await j.open(id); await j.page.getByRole("button", { name: "Pause new starts", exact: true }).click(); await j.review();
  expect((await j.read(id)).desiredState).toBe("Paused");
}
async function inspectDelivery(j: TriggerJourney, delivery: DeliveryView) {
  await j.page.goto(`/workflows/triggers/deliveries/${encodeURIComponent(delivery.id)}?backend=legacy`);
  await expect(j.page.getByRole("heading", { name: "Trigger delivery", exact: true })).toBeVisible();
  await expect(j.page.getByText(delivery.id, { exact: true }).first()).toBeVisible();
}

export async function verifyHttpAdmissionAndQueueFaults(j: TriggerJourney) {
  const healthy = await j.echo("Healthy alongside controlled actor refusal");
  const owed = await j.echo("Owed after controlled actor refusal");
  const http = await j.create("http", "Controlled public boundary failures");
  const mappings = [source("message", ["payload", "message"]), configured("count", 29)];
  await j.add(http.id, healthy.authored.definition.id, mappings);
  const owedSubscription = await j.add(http.id, owed.authored.definition.id, mappings);
  await j.enable(http.id);
  try {
    await j.host("arm-trigger-fault", { triggerId: http.id, faultMode: "SourceWriteRefused" });
    const key = randomUUID(), payload = { message: "Same request after unavailable physical acceptance" };
    const unavailable = await send(j, http, key, payload);
    expect(unavailable.response.status()).toBe(503);
    expect(unavailable.body.code).toBe("SourceUnavailable");
    const refusal = await observedFault(j, "SourceWriteRefused");
    expect(refusal.triggerId).toBe(http.id); expect(refusal.physicalSourcePresent).toBe(false);
    expect(await j.deliveries(http.id)).toEqual([]);
    await j.open(http.id); await expect(j.page.getByRole("heading", { name: http.name, exact: true })).toBeVisible();
    await j.host("release-trigger-fault");
    const accepted = await send(j, http, key, payload);
    expect(accepted.response.status()).toBe(202); expect(accepted.body.deliveryId).toBe(refusal.deliveryId);
    const repaired = await j.started(await j.waitDelivery(http.id, new Set(), 2));
    for (const recipient of repaired.recipients) await outputs(j, recipient.executionReference!.id, { message: payload.message, count: 29 });

    await j.host("arm-trigger-fault", { triggerId: http.id, faultMode: "DispatchShed", subscriptionId: owedSubscription.id });
    const shedRequest = await send(j, http, randomUUID(), { message: "Durably accepted before actual actor refusal" });
    expect(shedRequest.response.status()).toBe(202); expect(shedRequest.body.acceptedCount).toBe(2);
    const shed = await observedFault(j, "DispatchShed");
    expect(shed.physicalSourcePresent).toBe(true); expect(shed.deliveryId).toBe(shedRequest.body.deliveryId);
    expect(shed.subscriptionId).toBe(owedSubscription.id); expect(shed.sealFingerprint).toMatch(/^[a-f0-9]{64}$/);
    let held: DeliveryView | undefined;
    await expect.poll(async () => { held = await j.api(`/runtime/standalone-trigger-deliveries/${shed.deliveryId}`);
      return held!.recipients.filter(item => item.executionReference).length; }, { timeout: 45_000 }).toBe(1);
    const heldOwed = held!.recipients.find(item => item.id === shed.recipientId)!;
    expect(heldOwed.dispatch).toBe("DispatchCommitted"); expect(heldOwed.executionReference).toBeFalsy();
    const successful = held!.recipients.find(item => item.id !== shed.recipientId)!;
    expect(successful.executionReference).toBeTruthy();
    const successfulId = successful.executionReference!.id;
    const successfulBefore = await outputs(j, successfulId, { message: "Durably accepted before actual actor refusal", count: 29 });
    await expect.poll(async () => (await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${shed.deliveryId}`)).transport?.state).toBe("WriteCompleted");
    await inspectDelivery(j, held!);
    await j.capture("http-accepted-subscriber-owed");
    await pauseThroughUi(j, http.id);
    await j.host("release-trigger-fault"); // Waits the actual normal claim expiry; it changes no clock or stored lease.
    const recovered = await j.started(held!);
    expect(recovered.recipients.find(item => item.id !== shed.recipientId)!.executionReference!.id).toBe(successfulId);
    const recoveredOwed = recovered.recipients.find(item => item.id === shed.recipientId)!;
    expect(recoveredOwed.executionReference!.id).toBe(shed.plannedExecutionId);
    await outputs(j, recoveredOwed.executionReference!.id, { message: "Durably accepted before actual actor refusal", count: 29 });
    const successfulAfter = await outputs(j, successfulId, { message: "Durably accepted before actual actor refusal", count: 29 });
    expect(successfulAfter.instance).toEqual(successfulBefore.instance);
    expect((await j.deliveries(http.id)).map(item => item.id).sort()).toEqual([refusal.deliveryId, shed.deliveryId].sort());
    await inspectDelivery(j, recovered);
    await j.capture("http-accepted-subscriber-recovered");
    j.requests.push({ kind: "controlled-public-port-faults-with-real-composed-source-and-ordinary-recovery", storeRefusal: refusal,
      acceptedAfterRepair: repaired, postacceptShed: shed, held, recovered, successfulExecutionBefore: successfulBefore.instance,
      successfulExecutionAfter: successfulAfter.instance, productionDiskFailureClaim: false, productionCapacityLoadClaim: false });
    j.observed.push("A controlled IOException at the selected real ingress write port produced actual HTTP503 and no accepted source; the same request key succeeded after repair",
      "Actual HTTP202 stayed accepted after the selected native actor enqueue returned Deferred with DispatchShed; one subscriber completed while its peer remained sealed and owed",
      "After pause and natural source-claim expiry, the original owed identity completed through the normal actor; the successful subscriber retained its exact completed execution");
  } finally { await j.host("restore-features"); }
}

export async function verifyUnreadableScheduleAdmission(j: TriggerJourney) {
  const target = await j.echo("Schedule source-store failure recovery");
  const timer = await j.create("timer", "Unavailable durable schedule admission", { interval: "PT15S" });
  await j.add(timer.id, target.authored.definition.id, [source("message", ["metadata", "dueAt"]), configured("count", 31)]);
  try {
    await j.host("arm-trigger-fault", { triggerId: timer.id, faultMode: "SourceWriteRefused" });
    await j.enable(timer.id); const armed = await j.read(timer.id);
    const refusal = await observedFault(j, "SourceWriteRefused");
    expect(refusal.physicalSourcePresent).toBe(false); expect(refusal.triggerId).toBe(timer.id);
    let failed: TriggerView | undefined;
    await expect.poll(async () => { failed = await j.read(timer.id); return (failed.schedule?.failureCount ?? 0) > 0 && failed.schedule?.failureReason === "AdmissionUnavailable"; }, { timeout: 45_000 }).toBe(true);
    const unavailableReason = failed!.findings.find(finding => finding.code === "ScheduleAdmissionUnavailable");
    const unavailableMessage = "Schedule admission is unavailable. The due occurrence is retained for retry.";
    expect(unavailableReason).toMatchObject({ code: "ScheduleAdmissionUnavailable", message: unavailableMessage });
    expect(failed!.findings.some(finding => finding.code === "ScheduleNoEligibleSubscribers")).toBe(false);
    expect(failed!.schedule?.nextDueUtc).toBe(armed.schedule?.nextDueUtc);
    expect(failed!.schedule?.generation).toBe(armed.schedule?.generation);
    expect(failed!.schedule?.nextRetryAt).toBeTruthy();
    expect(await j.deliveries(timer.id)).toEqual([]);
    await j.open(timer.id);
    const unavailableSchedule = j.page.getByRole("region", { name: "Schedule", exact: true });
    await expect(unavailableSchedule).toContainText(/overdue|retry|retained/i);
    await expect(unavailableSchedule).toContainText(unavailableMessage);
    await expect(unavailableSchedule).not.toContainText("Waiting for eligible subscribers.");
    await j.capture("schedule-admission-unavailable");
    await j.host("release-trigger-fault");
    const accepted = await j.started(await j.waitDelivery(timer.id));
    expect(accepted.id).toBe(refusal.deliveryId);
    const execution = await outputs(j, accepted.recipients[0].executionReference!.id, { count: 31 });
    expect(Date.parse(String(scalar(execution.outputs.message)))).toBe(Date.parse(armed.schedule!.nextDueUtc!));
    await pauseThroughUi(j, timer.id);
    expect((await j.deliveries(timer.id)).filter(item => item.acceptedCount > 0)).toHaveLength(1);
    j.requests.push({ kind: "actual-due-schedule-controlled-physical-write-unavailable-with-no-cursor-advance", armed: armed.schedule,
      refusal, failed: failed!.schedule, finding: unavailableReason, accepted, outcome: "The original due occurrence recovered through ordinary admission; no known-zero-eligibility substitution." });
    j.observed.push("A due schedule with a healthy target encountered a separate controlled physical-admission IOException, retained its due cursor with failed-attempt backoff and no accepted source, and explicitly reported admission unavailability in API and Schedule UI rather than waiting for eligible subscribers, then accepted the original due identity after repair");
  } finally { await j.host("restore-features"); }
}

export async function verifyHttpWriteFailure(j: TriggerJourney) {
  const target = await j.echo("Original HTTP response write failure retains accepted work");
  const http = await j.create("http", "Actual original response stream failure");
  await j.add(http.id, target.authored.definition.id, [configured("message", "Accepted despite failed response bytes"), configured("count", 37)]);
  await j.enable(http.id);
  const path = `/workflows/http/${String(http.configuration.path).replace(/^\/+/, "")}`;
  const key = randomUUID(), payload = { actual: "response body write fails after physical admission" };
  try {
    await j.host("arm-trigger-fault", { triggerId: http.id, faultMode: "ResponseWriteFailed", requestPath: path });
    let caller: unknown;
    let exchange: { status: number; body: string } | undefined;
    try {
      const response = await j.page.request.post(`${j.base}${path}`, { data: payload, headers: { "Idempotency-Key": key } });
      const body = await response.text();
      exchange = { status: response.status(), body }; caller = exchange;
    } catch (error) { caller = { transportException: error instanceof Error ? error.name : "UnknownTransportException" }; }
    if (exchange?.status === 202) expect(exchange.body, "A failed original body write must not fabricate the JSON acknowledgement").toBe("");
    else if (exchange) expect(exchange.status).toBe(503);
    const failure = await observedFault(j, "ResponseWriteFailed");
    expect(failure.physicalSourcePresent).toBe(true);
    let retained: DeliveryView | undefined;
    await expect.poll(async () => { retained = await j.api(`/runtime/standalone-trigger-deliveries/${failure.deliveryId}`); return retained!.transport?.state; },
      { timeout: 45_000 }).toBe("WriteFailed");
    expect(retained!.acceptedCount).toBe(1); expect(retained!.refusedCount).toBe(0);
    await inspectDelivery(j, retained!);
    await expect(j.page.getByText("HTTP transport", { exact: true }).locator("xpath=following-sibling::dd[1]")).toContainText("Write Failed");
    await j.capture("http-response-write-failed");
    await j.host("release-trigger-fault");
    const completed = await j.started(retained!);
    await outputs(j, completed.recipients[0].executionReference!.id, { message: "Accepted despite failed response bytes", count: 37 });
    const replay = await send(j, http, key, payload);
    expect(replay.response.status()).toBe(202); expect(replay.body.deliveryId).toBe(failure.deliveryId); expect(replay.body.replayed).toBe(true);
    expect((await j.deliveries(http.id)).map(item => item.id)).toEqual([failure.deliveryId]);
    j.requests.push({ kind: "actual-original-response-body-write-failure-after-persisted-awaiting-response", controlledBoundary: failure,
      caller, failedTransportWithAcceptedCohort: retained, completed, replay: replay.body });
    j.observed.push("An owner-selected response stream threw on actual202 body write only after verifying the real accepted AwaitingResponse source; ordinary middleware retained WriteFailed",
      "The accepted workflow completed through ordinary processing despite the failed original response, and exact-key replay reconciled the same delivery without another root");
  } finally { await j.host("restore-features"); }
}
