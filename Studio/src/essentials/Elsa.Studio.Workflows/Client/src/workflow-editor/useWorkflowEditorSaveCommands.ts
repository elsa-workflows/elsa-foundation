import { useCallback, useState } from "react";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import type { ActivityDefinitionVersionView, RecommendedActivityDefinition } from "../activityDefinitionTypes";
import type { ActivityVersionChangeApplyRequest } from "./ActivityVersionChangeDialog";
import { activityVersionChangePinnedReason, findActivityOccurrence, planActivityVersionChange } from "./activityVersionChangeModel";

export interface WorkflowVersionChangeState {
  occurrence: ActivityNode;
  current: ActivityDefinitionVersionView;
  recommendation?: RecommendedActivityDefinition | null;
}

type SaveOptions = { lineage?: Iterable<string> };

/**
 * The editor's save commands whose written snapshot is fixed when the author acts: the toolbar Save, the template
 * Back (save then leave) and the reviewed activity version change. Each captures the history line together with that
 * snapshot, before any await, so a batch autosave hold stays exact even when the author undoes or redoes meanwhile
 * (spec 1193 FR-010). The version change is refused while a pinned test-run snapshot is shown.
 */
export function useWorkflowEditorSaveCommands({ draft, canEdit, lineage, saveDraft, loadDraft, save, flushDefinitionMeta, onBack,
  setAutosavePaused, setError, setStatus, setNavigationPending, recommendation }: {
  draft: WorkflowDraft | null;
  canEdit(): boolean;
  lineage(): Iterable<string>;
  saveDraft(draft: WorkflowDraft, savedStatus: string, options?: SaveOptions): Promise<WorkflowDraft>;
  loadDraft(draft: WorkflowDraft): void;
  save(options?: SaveOptions): Promise<void>;
  flushDefinitionMeta(): Promise<boolean>;
  onBack(): void;
  setAutosavePaused(paused: boolean): void;
  setError(value: string): void;
  setStatus(value: string): void;
  setNavigationPending(pending: boolean): void;
  recommendation?: RecommendedActivityDefinition | null;
}) {
  const [versionChange, setVersionChange] = useState<WorkflowVersionChangeState | null>(null);

  const openVersionChange = useCallback((occurrence: ActivityNode, current: ActivityDefinitionVersionView) => {
    if (!canEdit()) { setError(activityVersionChangePinnedReason); return; }
    setError("");
    setAutosavePaused(true);
    setVersionChange({ occurrence, current, recommendation });
  }, [canEdit, recommendation, setAutosavePaused, setError]);

  const cancelVersionChange = useCallback(() => {
    setVersionChange(null);
    setAutosavePaused(false);
  }, [setAutosavePaused]);

  const applyVersionChange = useCallback(async (request: ActivityVersionChangeApplyRequest) => {
    const next = planActivityVersionChange(draft, request, canEdit());
    // `next` derives from the live editable draft, so it carries the live history line, captured with it.
    const line = [...lineage()];
    const saved = await saveDraft(next, "Activity version changed", { lineage: line });
    const savedOccurrence = findActivityOccurrence(saved.state.rootActivity, request.precondition.occurrenceId);
    if (!savedOccurrence || savedOccurrence.activityVersionId !== request.targetVersionId) {
      throw new Error("The server did not confirm the reviewed exact version change.");
    }
    loadDraft(saved);
    setVersionChange(null);
    setAutosavePaused(false);
  }, [canEdit, draft, lineage, loadDraft, saveDraft, setAutosavePaused]);

  // The toolbar Save writes the draft of the click (this render's `save`), so the line is captured at the click too.
  const saveFromToolbar = useCallback(() => {
    const line = [...lineage()];
    void flushDefinitionMeta().then(saved => { if (saved) void save({ lineage: line }); });
  }, [flushDefinitionMeta, lineage, save]);

  // A template's Back saves the click-time draft before leaving; its line is captured with it.
  const saveAndGoBack = useCallback((snapshot: WorkflowDraft) => {
    const line = [...lineage()];
    setNavigationPending(true);
    setStatus("Saving…");
    void flushDefinitionMeta().then(async saved => {
      if (saved) { await saveDraft(snapshot, "Saved", { lineage: line }); onBack(); }
    }).catch(() => undefined).finally(() => setNavigationPending(false));
  }, [flushDefinitionMeta, lineage, onBack, saveDraft, setNavigationPending, setStatus]);

  return { versionChange, openVersionChange, cancelVersionChange, applyVersionChange, saveFromToolbar, saveAndGoBack };
}
