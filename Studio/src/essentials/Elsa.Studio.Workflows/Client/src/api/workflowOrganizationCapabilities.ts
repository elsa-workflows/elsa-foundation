import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "./capabilities";

/** Keep discovery on the landing path; organization transports load with their owning surfaces. */
export async function workflowOrganizationSupport(context: StudioEndpointContext) {
  const relations = ["workflow-tags", "workflow-organization", "workflow-tags-bulk", "workflow-views", "workflow-favorites", "workflow-folders", "workflow-definitions-page", "workflow-definitions-query"] as const;
  const available = await Promise.all(relations.map(relation => hasCapabilityLink(context, capabilityIds.workflowDesign, relation)));
  return { tags: available[0], organization: available[1], bulk: available[2], views: available[3], favorites: available[4], folders: available[5] && available[6], paging: available[6], queryOptions: available[6] && (available[7] || available[1]) };
}

/** The original library relation signals this grammar on older library-capable hosts. Base paging alone does not. */
export async function workflowQueryOptionsAvailable(context: StudioEndpointContext) {
  const support = await workflowOrganizationSupport(context);
  return support.queryOptions;
}
