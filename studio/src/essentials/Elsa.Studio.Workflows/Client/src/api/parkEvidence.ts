import { type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

const parkLink = "workflow-instance-park";

export interface WorkflowParkEvidence {
  parkedAt: string;
  faultSummary: string;
  deliveries: {
    handle: string;
    kind: string;
    state: "Consumed" | "Arrived";
    dueAt: string | null;
    attempts: number;
  }[];
  next: string | null;
}

export async function resolveWorkflowParkEvidence(context: StudioEndpointContext, workflowExecutionId: string) {
  return await hasCapabilityLink(context, capabilityIds.runtime, parkLink)
    ? resolveCapabilityLink(context, capabilityIds.runtime, parkLink, { workflowExecutionId })
    : null;
}

export function readWorkflowParkEvidence(context: StudioEndpointContext, path: string, cursor: string | null, signal: AbortSignal) {
  const query = cursor === null ? "" : `?${new URLSearchParams({ cursor })}`;
  return context.http.getJson<WorkflowParkEvidence>(path + query, { signal });
}
