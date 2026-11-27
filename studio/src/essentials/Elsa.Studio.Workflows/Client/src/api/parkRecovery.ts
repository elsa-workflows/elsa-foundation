import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

const recoveryLink = "workflow-instance-recovery";

export type WorkflowParkRecovery =
  | { status: "Available"; parkedAt: string; token: string }
  | { status: "Unavailable"; parkedAt: string }
  | { status: "NotParked" };

export async function resolveWorkflowParkRecovery(context: StudioEndpointContext, workflowExecutionId: string) {
  return await hasCapabilityLink(context, capabilityIds.runtime, recoveryLink)
    ? resolveCapabilityLink(context, capabilityIds.runtime, recoveryLink, { workflowExecutionId })
    : null;
}

export async function readWorkflowParkRecovery(context: StudioEndpointContext, path: string, signal: AbortSignal) {
  try {
    return await context.http.getJson<WorkflowParkRecovery>(path, { signal });
  } catch (error) {
    if (error instanceof StudioHttpError && [401, 403, 404].includes(error.status)) return null;
    throw error;
  }
}

export async function requestWorkflowParkRecovery(context: StudioEndpointContext, path: string, token: string, signal: AbortSignal) {
  await context.http.postJson(path, { token }, { signal });
}
