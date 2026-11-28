import { useQuery } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export interface ActivityVersionDiffIdentity {
  kind: string;
  definitionId: string;
  versionId?: string | null;
  draftId?: string | null;
  revision?: number | null;
  version?: string | null;
  templateHash?: string | null;
}

export interface ActivityVersionDiffChange {
  changeId: string;
  area: string;
  kind: string;
  subject: {
    memberKind?: string | null;
    referenceKey?: string | null;
    dependencyVersionId?: string | null;
    occurrenceId?: string | null;
  };
  before?: unknown;
  after?: unknown;
  impact: string;
  requiredBump: string;
  message: string;
}

export interface ActivityVersionDiffView {
  from: ActivityVersionDiffIdentity;
  to: ActivityVersionDiffIdentity;
  compatibility: string;
  requiredBump: string;
  behaviorChanged: boolean;
  provider: {
    fromKey?: string | null;
    fromSchemaVersion?: string | null;
    toKey?: string | null;
    toSchemaVersion?: string | null;
    changed: boolean;
  };
  summary: {
    breaking: number;
    additive: number;
    nonBehavioral: number;
    warnings: number;
  };
  changes: ActivityVersionDiffChange[];
  diagnostics: Array<{ code?: string; message?: string; severity?: string; [key: string]: unknown }>;
}

const activityVersionChangeKeys = {
  diff: (fromVersionId: string, toVersionId: string) =>
    ["activity-version-change", "diff", fromVersionId, toVersionId] as const
};

export function useActivityVersionDiff(
  context: StudioEndpointContext,
  fromVersionId: string,
  toVersionId: string | null
) {
  return useQuery({
    queryKey: activityVersionChangeKeys.diff(fromVersionId, toVersionId ?? ""),
    queryFn: ({ signal }) => getActivityVersionDiff(context, fromVersionId, toVersionId!, signal),
    enabled: Boolean(fromVersionId && toVersionId),
    staleTime: Infinity,
    retry: false
  });
}

export async function getActivityVersionDiff(
  context: StudioEndpointContext,
  fromVersionId: string,
  toVersionId: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-version-diff", {
    fromVersionId,
    toVersionId
  });
  return context.http.getJson<ActivityVersionDiffView>(path, { signal });
}
