import { useCallback } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { promoteDraft, publishVersion, startWorkflowDraftTestRun } from "../api/workflows";
import { buildExportPayload, downloadWorkflowJson } from "../workflowSerialization";
import { createDraftSnapshotId, getDraftSignature, isRejectedTestRun } from "./editorHelpers";
import type { WorkflowEditorOperation, WorkflowTestRunState } from "./editorTypes";

interface WorkflowOperationsParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  details: WorkflowDefinitionDetails | null;
  busy: boolean;
  saveDraft(draft: WorkflowDraft, savedStatus: string): Promise<unknown>;
  reload(): Promise<void>;
  startTestRun(testRun: WorkflowTestRunState): void;
  clearTestRun(): void;
  setPublishedArtifact(id: string | null): void;
  setOperation(operation: WorkflowEditorOperation): void;
  setStatus(value: string): void;
  setError(value: string): void;
  setActiveRightPanelId(id: string): void;
  setInspectorCollapsed(collapsed: boolean): void;
}

// The editor's async command handlers: export JSON, save, promote+publish, and dispatch a transient test
// run. Each guards on `busy`, drives the shared `operation` flag, and reports progress via status/error.
export function useWorkflowOperations({
  context,
  draft,
  details,
  busy,
  saveDraft,
  reload,
  startTestRun,
  clearTestRun,
  setPublishedArtifact,
  setOperation,
  setStatus,
  setError,
  setActiveRightPanelId,
  setInspectorCollapsed
}: WorkflowOperationsParams) {
  const exportJson = useCallback(() => {
    if (!draft) return;
    const name = details?.definition.name;
    downloadWorkflowJson(buildExportPayload(draft, name), name);
    setStatus("Exported workflow as JSON.");
  }, [draft, details, setStatus]);

  const save = useCallback(async () => {
    if (!draft || busy) return;
    setOperation("saving");
    setStatus("Saving...");
    try {
      await saveDraft(draft, "Saved");
    } catch {
      // saveDraft surfaces the error in the editor alert.
    } finally {
      setOperation("idle");
    }
  }, [draft, busy, saveDraft, setOperation, setStatus]);

  const promoteAndPublish = useCallback(async () => {
    if (!draft || busy) return;
    setOperation("promoting");
    setStatus("Saving...");
    try {
      // Persist in-flight edits first: promotion snapshots the persisted draft, so without
      // this save the new version — and the post-promote reload — would revert to the last
      // stored state and lose unsaved changes.
      await saveDraft(draft, "Saved");
      setStatus("Promoting...");
      const promoted = await promoteDraft(context, draft.id);
      const published = await publishVersion(context, promoted.versionId);
      setPublishedArtifact(published.artifactId);
      setStatus(`Published ${published.artifactVersion}`);
      await reload();
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  }, [draft, busy, context, saveDraft, reload, setPublishedArtifact, setOperation, setStatus, setError]);

  const run = useCallback(async () => {
    if (!draft?.state.rootActivity || busy) return;
    const draftSnapshot = draft;
    const draftSignature = getDraftSignature(draftSnapshot);
    clearTestRun();
    setStatus("Preparing test run...");
    try {
      setOperation("testRunPreparing");
      setStatus("Preparing test run...");
      const snapshotId = createDraftSnapshotId(draftSnapshot);

      setOperation("testRunStarting");
      setStatus("Starting test run...");
      const nextTestRun = await startWorkflowDraftTestRun(context, {
        definitionId: draftSnapshot.definitionId,
        snapshotId,
        state: draftSnapshot.state
      });
      startTestRun({ draftSignature, view: nextTestRun });
      setActiveRightPanelId("runtime");
      setInspectorCollapsed(false);
      setStatus(isRejectedTestRun(nextTestRun) ? "Test run rejected" : "Test run dispatched");
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  }, [draft, busy, context, clearTestRun, startTestRun, setActiveRightPanelId, setInspectorCollapsed, setOperation, setStatus, setError]);

  return { exportJson, save, promoteAndPublish, run };
}
