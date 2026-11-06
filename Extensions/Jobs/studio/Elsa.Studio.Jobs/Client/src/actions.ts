import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { newOperation } from "./jobsApi";
import { gateLabel } from "./Shared";
import { permissions, type Gate, type Hold, type Job, type Schedule } from "./types";
import type { useJobsMutation } from "./useJobs";

type Mutation = ReturnType<typeof useJobsMutation>;
async function reason(api: ElsaStudioModuleApi, title: string, message: string, confirmLabel: string) {
  const value = await api.dialogs.prompt({ title, message: `${message} Enter a reason (1–256 characters).`, confirmLabel, cancelLabel: "Cancel" });
  if (value === null) return null;
  if (!value.trim() || value.length > 256) { await api.dialogs.alert({ title: "Reason required", message: "Enter a nonblank reason of at most 256 characters." }); return null; }
  return newOperation(value);
}
export async function pause(api: ElsaStudioModuleApi, mutation: Mutation, gate: Gate) {
  if (mutation.blocked) return;
  const operation = await reason(api, "Pause jobs", `${gateLabel(gate)}. Pause blocks later durable start permissions and schedule materialization. Work already granted permission may finish. Other holds remain independent.`, "Add hold");
  if (operation) await mutation.run({ method: "POST", relation: "holds", segments: [], permission: permissions.pause, title: "Hold added",
    body: { ...operation, gate, holdId: crypto.randomUUID() } });
}
export async function release(api: ElsaStudioModuleApi, mutation: Mutation, hold: Hold) {
  if (mutation.blocked) return;
  const operation = await reason(api, "Release this hold", `${gateLabel(hold.gate)}. Release only “${hold.reason}” by ${hold.actor}. Other holds or capacity limits may continue to prevent work.`, "Release hold");
  if (operation) await mutation.run({ method: "POST", relation: "holds", segments: [hold.holdId, "release"], permission: permissions.pause,
    title: "Hold released; other restrictions still apply", body: { ...operation, gate: hold.gate, expectedRevision: hold.revision } });
}
export async function cancel(api: ElsaStudioModuleApi, mutation: Mutation, job: Job) {
  if (mutation.blocked) return;
  const operation = await reason(api, "Request job cancellation", "This requests cooperative cancellation. Already running code may continue until it exits. Saved history and results are retained.", "Request cancellation");
  if (operation) await mutation.run({ method: "POST", relation: "jobs", segments: [job.jobId, "cancel"], permission: permissions.cancel, title: "Cancellation requested", body: operation });
}
export async function retry(api: ElsaStudioModuleApi, mutation: Mutation, job: Job) {
  if (mutation.blocked) return;
  const operation = await reason(api, "Retry as new run", "Create a new linked JobId using the retained invocation. The old result, attempts and parent wait remain unchanged. This does not resume or resurrect the parent.", "Create new run");
  if (operation) await mutation.run({ method: "POST", relation: "jobs", segments: [job.jobId, "retry"], permission: permissions.retry, title: "New linked job accepted",
    body: { ...operation, expectedRevision: job.revision, idempotencyKey: crypto.randomUUID() } });
}
export async function enableSchedule(api: ElsaStudioModuleApi, mutation: Mutation, schedule: Schedule) {
  if (mutation.blocked) return;
  const enabled = !schedule.enabled;
  const operation = await reason(api, enabled ? "Enable schedule" : "Disable schedule", "Accepted historical jobs and owed schedule responsibility remain durable. This changes future schedule eligibility.", enabled ? "Enable" : "Disable");
  if (operation) await mutation.run({ method: "POST", relation: "schedules", segments: [schedule.scheduleId, "enabled"], permission: permissions.schedules,
    title: enabled ? "Schedule enabled" : "Schedule disabled", body: { ...operation, enabled, expectedRevision: schedule.revision } });
}
