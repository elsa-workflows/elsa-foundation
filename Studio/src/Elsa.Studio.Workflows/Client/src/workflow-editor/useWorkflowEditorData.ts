import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnostics, ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import {
  getDefinition,
  listActivities,
  listActivityAvailabilityDiagnostics,
  listActivityDescriptors,
  listExpressionDescriptors
} from "../api/workflows";

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
// details, the activity catalog, and the descriptor/availability/expression metadata. The draft itself
// is handed to the document reducer via loadDraft; the rest is returned as local state.
export function useWorkflowEditorData({ context, definitionId, resetHistory, loadDraft, markSaved, setError }: WorkflowEditorDataParams) {
  const [details, setDetails] = useState<WorkflowDefinitionDetails | null>(null);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
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
    const [nextDetails, nextCatalog, nextDescriptors, nextAvailability] = await Promise.all([
      getDefinition(context, definitionId),
      listActivities(context),
      listActivityDescriptors(context).then(
        descriptors => ({ ok: true as const, descriptors }),
        () => ({ ok: false as const, descriptors: [] as StudioActivityDescriptor[] })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      listActivityAvailabilityDiagnostics(context).then(
        diagnostics => diagnostics,
        () => null
      ),
      reloadExpressionDescriptors()
    ]);
    const nextDraft = nextDetails.draft ?? null;
    setDetails(nextDetails);
    markSaved(nextDraft);
    resetHistory(nextDraft);
    loadDraft(nextDraft);
    setCatalog(nextCatalog.activities ?? []);
    setActivityDescriptors(nextDescriptors.descriptors);
    setAvailabilityDiagnostics(nextAvailability);
    setDescriptorStatus(nextDescriptors.ok ? "ready" : "failed");
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
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors: expressionStateMatchesAuthority ? expressionDescriptorState.descriptors : [],
    expressionDescriptorStatus: expressionStateMatchesAuthority ? expressionDescriptorState.status : "loading",
    descriptorStatus,
    reload,
    reloadExpressionDescriptors
  };
}
