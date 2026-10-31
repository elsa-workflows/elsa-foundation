import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "./capabilities";

/** Keep discovery on the landing path; organization transports load with their owning surfaces. */
export async function workflowOrganizationSupport(context: StudioEndpointContext) {
  const relations = ["workflow-tags", "workflow-organization", "workflow-tags-bulk", "workflow-views", "workflow-favorites"] as const;
  const available = await Promise.all(relations.map(relation => hasCapabilityLink(context, capabilityIds.workflowDesign, relation)));
  return { tags: available[0], organization: available[1], bulk: available[2], views: available[3], favorites: available[4] };
}
