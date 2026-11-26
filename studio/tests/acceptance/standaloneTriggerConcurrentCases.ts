import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { TriggerJourney, configured, type DispatchBoundary } from "./standaloneTriggersSupport";
import type { DeliveryView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

/** Two live normal hosts share the real source queue; no claim or clock is changed to make either worker win. */
export async function verifyConcurrentNodeClaims(j: TriggerJourney) {
  const target = await j.echo("Concurrent ordinary queue claimant");
  const trigger = await j.create("http", "Two live source queue workers");
  await j.add(trigger.id, target.authored.definition.id, [configured("message", "shared queue inputs"), configured("count", 23)]); await j.enable(trigger.id);
  let boundary: DispatchBoundary | undefined, released = false;
  try {
    await j.host("arm-dispatch-boundary", { triggerId: trigger.id });
    const firstResponse = await j.sourceRequest(trigger, { actual: "A keeps its real first source claim" }, 202);
    boundary = (await j.host("await-dispatch-boundary")).boundary as DispatchBoundary;
    expect(boundary).toMatchObject({ nodeIdentity: "A", phase: "DispatchCommitted", blockAfterFirstSeal: true, deliveryId: firstResponse.body.deliveryId });
    const peer = (await j.host("start-concurrent-node")).concurrent!;
    expect(peer).toMatchObject({ primaryAlive: true, peerAlive: true, runtimeAndDesignStoresShared: true, sourceSha: process.env.UX_SOURCE_SHA });
    expect(peer.primaryProcessId).toBe(boundary.processId); expect(peer.peerProcessId).not.toBe(boundary.processId);
    const path = `/workflows/http/${String(trigger.configuration.path).replace(/^\/+/, "")}`, sourceKey = randomUUID();
    const secondResponse = await j.page.request.post(`${peer.peerUrl}${path}`, { data: { actual: "B admits and claims a different source while A is live and held" }, headers: { "Idempotency-Key": sourceKey } });
    const secondBody = await secondResponse.json(); expect(secondResponse.status(), JSON.stringify(secondBody)).toBe(202);
    expect(secondBody).toMatchObject({ admission: "Accepted", acceptedCount: 1, refusedCount: 0 });
    j.requests.push({ kind: "actual-concurrent-peer-http-source", backendUrl: peer.peerUrl, method: "POST", path, sourceKey, status: secondResponse.status(), result: secondBody });
    const secondDelivery = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${secondBody.deliveryId}`);
    const secondStarted = await j.started(secondDelivery); const secondId = secondStarted.recipients[0].executionReference!.id;
    await j.outputs(secondId, { message: "shared queue inputs", count: 23 });
    const live = (await j.host("inspect-concurrent-node")).concurrent!;
    expect(live).toMatchObject({ primaryAlive: true, peerAlive: true, runtimeAndDesignStoresShared: true });
    expect(live.peerBoundary).toMatchObject({ nodeIdentity: "B", processId: live.peerProcessId, deliveryId: secondBody.deliveryId,
      plannedExecutionId: secondId, phase: "TargetAccepted", sourceRevision: process.env.UX_SOURCE_SHA });
    expect(live.peerBoundary!.claimOwnerId).toBeTruthy(); expect(live.peerBoundary!.claimOwnerId).not.toBe(boundary.claimOwnerId);
    expect(live.peerBoundary!.bootId).not.toBe(boundary.bootId); expect(live.peerBoundary!.acceptanceReceipt).toBeTruthy();
    const firstHeld = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${boundary.deliveryId}`);
    expect(firstHeld.recipients[0]).toMatchObject({ dispatch: "DispatchCommitted", plannedExecutionIdentity: boundary.plannedExecutionId });
    expect(firstHeld.recipients[0].executionReference).toBeFalsy();
    await j.inspectDelivery(firstHeld, "delivery-concurrent-claims");
    await j.inspectDelivery(await j.api(`/runtime/standalone-trigger-deliveries/${secondDelivery.id}`), "delivery-concurrent-peer-started");
    // The original real lease must still exclude the peer; expiry is never accelerated or rewritten.
    expect(Date.parse(boundary.claimExpiresAt!)).toBeGreaterThan(Date.now());
    await j.host("release-dispatch-boundary"); released = true;
    const firstStarted = await j.started(await j.api(`/runtime/standalone-trigger-deliveries/${firstHeld.id}`));
    expect(firstStarted.recipients[0].executionReference!.id).toBe(boundary.plannedExecutionId);
    expect(boundary.plannedExecutionId).not.toBe(secondId);
    await j.outputs(boundary.plannedExecutionId, { message: "shared queue inputs", count: 23 });
    const settled = (await j.host("inspect-concurrent-node")).concurrent!;
    expect(settled).toMatchObject({ primaryAlive: true, peerAlive: true });
    expect(settled.primaryBoundary).toMatchObject({ nodeIdentity: "A", processId: boundary.processId, deliveryId: boundary.deliveryId,
      plannedExecutionId: boundary.plannedExecutionId, sealFingerprint: boundary.sealFingerprint, phase: "TargetAccepted" });
    expect(settled.primaryBoundary!.acceptanceReceipt).toBeTruthy();
    for (const executionId of [boundary.plannedExecutionId, secondId]) {
      const run = await j.api<{ activities: { activityExecutionId: string; firstCheckpointId?: string; lastCheckpointId?: string; provenance: { parentActivityExecutionId?: string | null } }[] }>(`/runtime/workflows/instances/${executionId}`);
      const roots = run.activities.filter(item => !item.provenance.parentActivityExecutionId); expect(roots).toHaveLength(1);
      expect(roots[0].firstCheckpointId).toBeTruthy(); expect(roots[0].lastCheckpointId).toBeTruthy();
      const reread = await j.api<typeof run>(`/runtime/workflows/instances/${executionId}`);
      expect(reread.activities.filter(item => !item.provenance.parentActivityExecutionId)).toEqual(roots);
    }
    const deliveries = await j.deliveries(trigger.id); expect(deliveries).toHaveLength(2);
    j.requests.push({ kind: "actual-concurrent-normal-source-queue-workers", initialBoundary: boundary, concurrent: live, settled,
      distinctExecutions: [boundary.plannedExecutionId, secondId], originalLeaseMutated: false, clockChanged: false,
      scope: "Shared SQLite Runtime/Design stores with separate host content and Nuplane stores; not distributed placement qualification" });
    j.observed.push("Two live Workbench processes shared the actual source queue: A retained a live sealed claim while B admitted and completed a second source under a distinct physical claim owner", "Exact file release of A's inspected seal completed its original planned root; both captured inputs, distinct executions and single inspected root/checkpoint identities were retained without lease or clock changes");
  } finally {
    if (boundary && !released) await j.host("release-dispatch-boundary");
    await j.host("stop-concurrent-node"); await j.host("restore-features");
  }
}
