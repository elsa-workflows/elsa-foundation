import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionRecommendationMutationView,
  ActivityDefinitionRevocationEvidence,
  ActivityDefinitionVersionLifecycleAction,
  ActivityDefinitionVersionLifecycleMutationView,
  ActivityDefinitionVersionManagementView,
  ChangeActivityDefinitionVersionLifecycleRequest,
  SetActivityDefinitionRecommendationRequest
} from "../activityDefinitionTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";
import { listActivityDefinitionVersions } from "./activityDesign";
import { getActivityDefinitionDependencyPage } from "./activityDefinitionDependencies";

export async function setActivityDefinitionRecommendation(
  context: StudioEndpointContext,
  definitionId: string,
  request: SetActivityDefinitionRecommendationRequest
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-definition-recommendation",
    { definitionId }
  );
  return context.http.putJson<ActivityDefinitionRecommendationMutationView>(path, request);
}

export async function changeActivityDefinitionVersionLifecycle(
  context: StudioEndpointContext,
  versionId: string,
  action: Exclude<ActivityDefinitionVersionLifecycleAction, "recommend">,
  request: ChangeActivityDefinitionVersionLifecycleRequest
) {
  const versionPath = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-definition-version",
    { versionId }
  );
  return context.http.postJson<ActivityDefinitionVersionLifecycleMutationView>(`${versionPath}/${action}`, request);
}

export async function listAllActivityDefinitionVersions(
  context: StudioEndpointContext,
  definitionId: string,
  signal?: AbortSignal
) {
  const items: ActivityDefinitionVersionManagementView[] = [];
  let cursor: string | null = null;
  do {
    const page = await listActivityDefinitionVersions(context, { definitionId, limit: 100, cursor }, signal);
    items.push(...page.items);
    if (!page.hasMore || !page.continuation) break;
    if (page.continuation === cursor) throw new Error("Activity Definition version pagination did not advance.");
    cursor = page.continuation;
  } while (!signal?.aborted);
  return items;
}

export async function getActivityDefinitionRevocationEvidence(
  context: StudioEndpointContext,
  versionId: string,
  signal?: AbortSignal
): Promise<ActivityDefinitionRevocationEvidence> {
  const [directDependencies, inboundUsage] = await Promise.all([
    getActivityDefinitionDependencyEvidence(context, {
      versionId,
      direction: "outbound",
      transitive: false,
      include: ["versions"]
    }, signal),
    getActivityDefinitionDependencyEvidence(context, {
      versionId,
      direction: "inbound",
      transitive: true,
      include: ["versions", "drafts"]
    }, signal)
  ]);
  return { directDependencies, inboundUsage };
}

async function getActivityDefinitionDependencyEvidence(
  context: StudioEndpointContext,
  query: {
    versionId: string;
    direction: "outbound" | "inbound";
    transitive: boolean;
    include: Array<"versions" | "drafts">;
  },
  signal?: AbortSignal
) {
  const items: ActivityDefinitionRevocationEvidence["directDependencies"]["items"] = [];
  let cursor: string | null = null;
  let evidence: ActivityDefinitionRevocationEvidence["directDependencies"] | null = null;
  do {
    const page = await getActivityDefinitionDependencyPage(context, { ...query, cursor, limit: 500 }, signal);
    if (evidence && (
      evidence.consistency.asOf !== page.consistency.asOf ||
      evidence.consistency.asOfSequence !== page.consistency.asOfSequence ||
      evidence.consistency.rebuildId !== page.consistency.rebuildId
    )) throw new Error("Activity Definition usage evidence changed while it was being reviewed.");
    evidence ??= page;
    items.push(...page.items);
    if (!page.nextCursor) break;
    if (page.nextCursor === cursor) throw new Error("Activity Definition usage pagination did not advance.");
    cursor = page.nextCursor;
  } while (!signal?.aborted);
  if (!evidence) throw new Error("Activity Definition usage evidence was unavailable.");
  return { ...evidence, items, nextCursor: null };
}
