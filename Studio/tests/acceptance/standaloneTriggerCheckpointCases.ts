import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { configured, TriggerJourney, type CheckpointCut } from "./standaloneTriggersSupport";
import type { DeliveryView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

type Instance = { instance: { artifactId: string; definitionVersionId: string }; activities: {
  activityExecutionId: string; firstCheckpointId?: string; lastCheckpointId?: string;
  provenance: { parentActivityExecutionId?: string | null };
}[] };

export async function verifyInitialCheckpointCrashRecovery(j: TriggerJourney) {
  for (const cut of ["BeforeInitialCheckpoint", "AfterInitialCheckpointCommit"] as const) {
    const target = await j.echo(`Initial checkpoint crash: ${cut}`);
    const trigger = await j.create("http", `Initial checkpoint crash ${cut}`);
    const expected = { message: `Retained input through ${cut}`, count: 41 };
    await j.add(trigger.id, target.authored.definition.id, [configured("message", expected.message), configured("count", expected.count)]);
    await j.enable(trigger.id);
    const sourceKey = randomUUID(), payload = { actual: cut };
    try {
      await j.host("arm-checkpoint-crash", { triggerId: trigger.id, sourceKey, checkpointCut: cut });
      // The checkpoint writer may hold SQLite before the original HTTP transport finalizes.
      // Observe the real cut concurrently; owned process death may also lose that response.
      const intake = j.sourceRequest(trigger, payload, undefined, { "Idempotency-Key": sourceKey })
        .then(value => ({ value, transportUnconfirmed: false as const }), error => ({ transportUnconfirmed: true as const,
          rejectedAt: new Date().toISOString(), errorType: error instanceof Error ? error.name : typeof error,
          errorMessage: error instanceof Error ? error.message.split("\n")[0] : "Non-Error transport rejection" }));
      const recovery = await j.host("recover-checkpoint-crash");
      const witness = recovery.checkpoint!, boundary: CheckpointCut = witness.cut;
      expect(boundary).toMatchObject({ cut, sourceRevision: process.env.UX_SOURCE_SHA,
        checkpointTransactionCommitted: cut === "AfterInitialCheckpointCommit", acknowledgementReturned: false });
      const caller = await intake;
      if (!caller.transportUnconfirmed) {
        expect(caller.value.response.status()).toBe(202);
        expect(caller.value.body).toMatchObject({ acceptedCount: 1, refusedCount: 0, deliveryId: boundary.sourceOutboxItemId });
      }
      expect(boundary.processId).toBe(recovery.previousProcessId); expect(recovery.processId).not.toBe(boundary.processId);
      expect(boundary.durableQueueBeforeCheckpoint.some(item => item.commandKind === "Checkpoint")).toBe(true);
      expect(witness).toMatchObject({ stopSignal: "SIGKILL", naturalLeaseWaitSeconds: 61, leaseMutated: false, clockChanged: false });
      expect(witness.physicalAfterDeath!.stateCount).toBe(cut === "AfterInitialCheckpointCommit" ? 1 : 0);
      if (boundary.commitId) expect(witness.physicalAfterDeath!.commitIds).toContain(boundary.commitId);
      else expect(witness.physicalAfterDeath!.commitIds).toEqual([]);
      const delivery = await j.started(await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${boundary.sourceOutboxItemId}`));
      expect(delivery.recipients).toHaveLength(1);
      expect(delivery.recipients[0]).toMatchObject({ id: boundary.origin.recipientId,
        plannedExecutionIdentity: boundary.workflowExecutionId, executionReference: { id: boundary.workflowExecutionId } });
      await j.outputs(boundary.workflowExecutionId, expected);
      const path = `/runtime/workflows/instances/${boundary.workflowExecutionId}`;
      const instance = await j.api<Instance>(path);
      const roots = instance.activities.filter(item => !item.provenance.parentActivityExecutionId);
      expect(roots).toHaveLength(1); expect(roots[0].firstCheckpointId).toBeTruthy(); expect(roots[0].lastCheckpointId).toBeTruthy();
      const physical = (await j.host("inspect-checkpoint-recovery")).checkpoint!.physicalAfterRecovery!;
      expect(physical.stateCount).toBe(1); expect(physical.sourceExecutionIds).toEqual([boundary.workflowExecutionId]); expect(physical.origins).toEqual([boundary.origin]);
      if (boundary.commitId) expect(physical.commitIds).toContain(boundary.commitId);
      const replay = await j.sourceRequest(trigger, payload, 202, { "Idempotency-Key": sourceKey });
      expect(replay.body).toMatchObject({ deliveryId: boundary.sourceOutboxItemId, replayed: true });
      expect((await j.deliveries(trigger.id)).map(item => item.id)).toEqual([boundary.sourceOutboxItemId]);
      expect((await j.api<Instance>(path)).activities.filter(item => !item.provenance.parentActivityExecutionId)).toEqual(roots);
      await j.inspectDelivery(delivery, cut === "BeforeInitialCheckpoint" ? "delivery-before-initial-checkpoint-recovered" : "delivery-committed-checkpoint-ack-lost-recovered");
      j.requests.push({ kind: "actual-physical-initial-checkpoint-process-crash", cut, recovery, physical, root: roots[0],
        originalResponse: caller.transportUnconfirmed ? { outcome: "Original transport outcome unconfirmed", rejectedAt: caller.rejectedAt,
          errorType: caller.errorType, errorMessage: caller.errorMessage } : { status: caller.value.response.status(), body: caller.value.body }, replay: replay.body });
      j.observed.push(`${cut}: an actual queued native Start reached its initial checkpoint transaction; owned SIGKILL and a distinct normal process on the same SQLite recovered the original execution, immutable ingress origin, mapped business outputs and one root. Exact-key replay retained that delivery and root/checkpoint identities.`);
    } finally { await j.host("restore-features"); }
  }
}
