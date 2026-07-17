import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnostics, ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { getDefinition } from "../api/workflowDesign";
import { listActivities, listRecommendedActivityDefinitions } from "../api/activityDesign";
import type { RecommendedActivityDefinition } from "../activityDefinitionTypes";
import { listExpressionDescriptors } from "../api/expressions";
import { observeReusableActivity } from "../reusableActivityObservability";

interface WorkflowEditorDataParams {
  context: StudioEndpointContext;
  definitionId: string;
  // Re-seed undo history and load the draft into the document reducer for a freshly fetched definition.
  resetHistory(draft: WorkflowDraft | null): void;
  loadDraft(draft: WorkflowDraft | null): void;
  // Seed the persistence baseline so a just-loaded draft doesn't immediately autosave.
  markSaved(draft: WorkflowDraft | null): void;
  setError(value: string): void;
}

// Loads (and reloads, after a promote) everything the editor renders around the draft: the definition
// details, the normalized authoring catalog, and expression metadata. Activity descriptors and
// availability are projected from that one catalog response. The draft itself
// is handed to the document reducer via loadDraft; the rest is returned as local state.
export function useWorkflowEditorData({ context, definitionId, resetHistory, loadDraft, markSaved, setError }: WorkflowEditorDataParams) {
  const [details, setDetails] = useState<WorkflowDefinitionDetails | null>(null);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [paletteCatalog, setPaletteCatalog] = useState<ActivityCatalogItem[]>([]);
  const [recommendedDefinitions, setRecommendedDefinitions] = useState<RecommendedActivityDefinition[]>([]);
  const [activityDescriptors, setActivityDescriptors] = useState<StudioActivityDescriptor[]>([]);
  const [availabilityDiagnostics, setAvailabilityDiagnostics] = useState<ActivityAvailabilityDiagnostics | null>(null);
  const [expressionDescriptorState, setExpressionDescriptorState] = useState<{
    context: StudioEndpointContext;
    definitionId: string;
    descriptors: StudioExpressionDescriptor[];
    status: "loading" | "ready" | "failed";
  }>({ context, definitionId, descriptors: [], status: "loading" });
  const expressionRequestGeneration = useRef(0);
  const [descriptorStatus, setDescriptorStatus] = useState<"loading" | "ready" | "failed">("loading");

  const reloadExpressionDescriptors = useCallback(async () => {
    const generation = ++expressionRequestGeneration.current;
    setExpressionDescriptorState(previous => {
      const sameAuthority = previous.context === context && previous.definitionId === definitionId;
      return {
        context,
        definitionId,
        descriptors: sameAuthority ? previous.descriptors : [],
        status: "loading"
      };
    });
    try {
      const descriptors = await listExpressionDescriptors(context);
      if (generation !== expressionRequestGeneration.current) return;
      setExpressionDescriptorState({ context, definitionId, descriptors, status: "ready" });
    } catch {
      if (generation !== expressionRequestGeneration.current) return;
      setExpressionDescriptorState(previous => ({
        context,
        definitionId,
        descriptors: previous.context === context && previous.definitionId === definitionId ? previous.descriptors : [],
        status: "failed"
      }));
    }
  }, [context, definitionId]);

  const reload = useCallback(async () => {
    setError("");
    const [nextDetails, nextCatalog, nextRecommendedDefinitions] = await Promise.all([
      getDefinition(context, definitionId),
      listActivities(context),
      listRecommendedActivityDefinitions(context).catch(error => {
        observeReusableActivity({
          event: "picker-load",
          surface: "workflow-designer",
          outcome: "failed"
        });
        throw error;
      }),
      reloadExpressionDescriptors()
    ]);
    const nextDraft = nextDetails.draft ?? null;
    setDetails(nextDetails);
    markSaved(nextDraft);
    resetHistory(nextDraft);
    loadDraft(nextDraft);
    const activities = decorateReusableCatalog(nextCatalog.activities ?? [], nextRecommendedDefinitions);
    setCatalog(activities);
    setRecommendedDefinitions(nextRecommendedDefinitions);
    const nextPaletteCatalog = projectRecommendedPalette(activities, nextRecommendedDefinitions);
    setPaletteCatalog(nextPaletteCatalog);
    observeReusableActivity({
      event: "picker-load",
      surface: "workflow-designer",
      outcome: nextPaletteCatalog.length > 0 ? "ready" : "empty"
    });
    setActivityDescriptors(activities.map(toActivityDescriptor));
    setAvailabilityDiagnostics(null);
    setDescriptorStatus("ready");
  }, [context, definitionId, resetHistory, loadDraft, markSaved, reloadExpressionDescriptors, setError]);

  useEffect(() => {
    void reload().catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, [reload, setError]);

  const expressionStateMatchesAuthority = expressionDescriptorState.context === context
    && expressionDescriptorState.definitionId === definitionId;

  return {
    details,
    setDetails,
    catalog,
    paletteCatalog,
    recommendedDefinitions,
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors: expressionStateMatchesAuthority ? expressionDescriptorState.descriptors : [],
    expressionDescriptorStatus: expressionStateMatchesAuthority ? expressionDescriptorState.status : "loading",
    descriptorStatus,
    reload,
    reloadExpressionDescriptors
  };
}

export function projectRecommendedPalette(
  catalog: ActivityCatalogItem[],
  recommendations: RecommendedActivityDefinition[]
): ActivityCatalogItem[] {
  const catalogByVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));
  return recommendations.reduce<ActivityCatalogItem[]>((result, recommendation) => {
    if (!recommendation.isAvailable) return result;
    const activity = catalogByVersion.get(recommendation.versionId);
    if (!activity || activity.activityTypeKey !== recommendation.activityTypeKey) return result;
    result.push({
      ...activity,
      activityDefinitionId: recommendation.definitionId,
      activityDefinitionVersionId: recommendation.versionId,
      activityDefinitionVersion: recommendation.version
    });
    return result;
  }, []);
}

export function decorateReusableCatalog(
  catalog: ActivityCatalogItem[],
  recommendations: RecommendedActivityDefinition[]
): ActivityCatalogItem[] {
  const recommendationByType = new Map(recommendations.map(recommendation => [recommendation.activityTypeKey, recommendation]));
  return catalog.map(activity => {
    const recommendation = recommendationByType.get(activity.activityTypeKey);
    return recommendation ? {
      ...activity,
      activityDefinitionId: recommendation.definitionId,
      activityDefinitionVersionId: activity.activityVersionId,
      activityDefinitionVersion: activity.version
    } : activity;
  });
}

function toActivityDescriptor(activity: ActivityCatalogItem): StudioActivityDescriptor {
  const name = activity.activityTypeKey.split(".").at(-1) ?? activity.activityTypeKey;
  const numericVersion = Number(activity.version);
  return {
    typeName: activity.activityTypeKey,
    name,
    version: Number.isFinite(numericVersion) ? numericVersion : undefined,
    category: activity.category,
    displayName: activity.displayName,
    description: activity.description,
    kind: activity.executionType as StudioActivityDescriptor["kind"],
    inputs: activity.inputs as StudioActivityDescriptor["inputs"],
    outputs: activity.outputs as StudioActivityDescriptor["outputs"],
    ports: (activity.ports ?? []) as StudioActivityDescriptor["ports"],
    isContainer: Boolean(activity.containerStructure),
    isBrowsable: activity.available !== false
  };
}
