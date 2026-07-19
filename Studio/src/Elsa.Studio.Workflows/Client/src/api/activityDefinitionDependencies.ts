import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionUsageEvidence } from "../activityDefinitionTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export interface ActivityDefinitionDependencyRequest {
  versionId: string;
  direction: "outbound" | "inbound";
  transitive: boolean;
  include: Array<"versions" | "drafts">;
  cursor?: string | null;
  limit?: number;
}

export async function getActivityDefinitionDependencyPage(
  context: StudioEndpointContext,
  request: ActivityDefinitionDependencyRequest,
  signal?: AbortSignal
) {
  const versionPath = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-definition-version",
    { versionId: request.versionId }
  );
  const parameters = new URLSearchParams({
    direction: request.direction,
    transitive: String(request.transitive),
    include: request.include.join(","),
    limit: String(request.limit ?? 100)
  });
  if (request.cursor) parameters.set("cursor", request.cursor);
  return context.http.getJson<ActivityDefinitionUsageEvidence>(
    `${versionPath}/dependencies?${parameters.toString()}`,
    { signal }
  );
}
