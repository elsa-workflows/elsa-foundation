import { useCallback, useEffect, useState } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnostics, ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import {
  fallbackExpressionDescriptors,
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
  const [expressionDescriptors, setExpressionDescriptors] = useState<StudioExpressionDescriptor[]>(fallbackExpressionDescriptors);
  const [descriptorStatus, setDescriptorStatus] = useState<"loading" | "ready" | "failed">("loading");
  const [definitionStatus, setDefinitionStatus] = useState<"loading" | "ready" | "error">("loading");

  const reload = useCallback(async () => {
    setError("");
    // Keep an already-rendered descriptor snapshot during refresh. The matching loaded draft remains
    // visible too, allowing reference editors to preserve their current value and expose Retry if the
    // definition transaction fails. Initial load still starts in descriptorStatus="loading".
    setDefinitionStatus("loading");
    try {
      const [nextDetails, nextCatalog, nextDescriptors, nextExpressions, nextAvailability] = await Promise.all([
        getDefinition(context, definitionId),
        listActivities(context),
        listActivityDescriptors(context).then(
          descriptors => ({ ok: true as const, descriptors }),
          () => ({ ok: false as const, descriptors: [] as StudioActivityDescriptor[] })
        ),
        listExpressionDescriptors(context).then(
          descriptors => ({ ok: true as const, descriptors }),
          () => ({ ok: false as const, descriptors: fallbackExpressionDescriptors })
        ),
        // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
        listActivityAvailabilityDiagnostics(context).then(
          diagnostics => diagnostics,
          () => null
        )
      ]);
      const nextDraft = nextDetails.draft ?? null;
      setDetails(nextDetails);
      markSaved(nextDraft);
      resetHistory(nextDraft);
      loadDraft(nextDraft);
      setCatalog(nextCatalog.activities ?? []);
      setActivityDescriptors(nextDescriptors.descriptors);
      setAvailabilityDiagnostics(nextAvailability);
      setExpressionDescriptors(nextExpressions.descriptors.length > 0 ? nextExpressions.descriptors : fallbackExpressionDescriptors);
      setDescriptorStatus(nextDescriptors.ok ? "ready" : "failed");
      setDefinitionStatus("ready");
    } catch (error) {
      setDefinitionStatus("error");
      throw error;
    }
  }, [context, definitionId, resetHistory, loadDraft, markSaved, setError]);

  useEffect(() => {
    void reload().catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, [reload, setError]);

  return {
    details,
    setDetails,
    catalog,
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors,
    descriptorStatus,
    definitionStatus,
    reload
  };
}
