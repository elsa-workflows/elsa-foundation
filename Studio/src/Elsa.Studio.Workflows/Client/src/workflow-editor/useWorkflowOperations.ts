import { useCallback, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft, WorkflowExecutionInputs, WorkflowInput } from "../workflowTypes";
import { promoteDraft } from "../api/workflowDesign";
import {
  preflightPublication,
  publishVersion,
  startWorkflowDraftTestRun,
  type PublicationIntent,
  type PublicationPreflight
} from "../api/publishing";
import { buildExportPayload, downloadWorkflowJson } from "../workflowSerialization";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
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
  const [publicationReview, setPublicationReview] = useState<{
    versionId: string;
    intent: PublicationIntent;
    preflight: PublicationPreflight;
  } | null>(null);
  const [runInputPrompt, setRunInputPrompt] = useState<{
    draft: WorkflowDraft;
    inputs: WorkflowInput[];
  } | null>(null);
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

  const preparePublication = useCallback(async () => {
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
      setOperation("publicationPreflight");
      setStatus("Checking publication changes...");
      const intent: PublicationIntent = {};
      const preflight = await preflightPublication(context, promoted.versionId, intent);
      setPublicationReview({ versionId: promoted.versionId, intent, preflight });
      setStatus("");
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  }, [draft, busy, context, saveDraft, setOperation, setStatus, setError]);

  const reviewPublication = useCallback(async (intent: PublicationIntent) => {
    if (!publicationReview) return;
    setOperation("publicationPreflight");
    setError("");
    try {
      const preflight = await preflightPublication(context, publicationReview.versionId, intent);
      setPublicationReview({ ...publicationReview, intent, preflight });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  }, [context, publicationReview, setError, setOperation]);

  const confirmPublication = useCallback(async () => {
    if (!publicationReview || !publicationReview.preflight.canActivate) return;
    if (publicationReview.intent.action === "sideBySide" && !publicationReview.intent.slotName?.trim()) {
      setError("Enter a meaningful slot name for side-by-side publication.");
      return;
    }
    setOperation("publishing");
    setError("");
    setStatus("Publishing...");
    try {
      const published = await publishVersion(context, publicationReview.versionId, publicationReview.intent);
      setPublishedArtifact(published.artifactId);
      setPublicationReview(null);
      setStatus(`Published to ${published.slotName}`);
      await reload();
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  }, [context, publicationReview, reload, setError, setOperation, setPublishedArtifact, setStatus]);

  const cancelPublication = useCallback(() => {
    setPublicationReview(null);
    setStatus("Publication cancelled; the promoted version remains available.");
  }, [setStatus]);

  const dispatchTestRun = useCallback(async (draftSnapshot: WorkflowDraft, inputs: WorkflowExecutionInputs) => {
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
        state: draftSnapshot.state,
        inputs
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
  }, [context, clearTestRun, startTestRun, setActiveRightPanelId, setInspectorCollapsed, setOperation, setStatus, setError]);

  const run = useCallback(async () => {
    if (!draft?.state.rootActivity || busy) return;
    const inputs = readWorkflowInputs(draft.state.inputs);
    if (inputs.length > 0) {
      setRunInputPrompt({ draft, inputs });
      return;
    }
    await dispatchTestRun(draft, {});
  }, [draft, busy, dispatchTestRun]);

  const confirmRunInputs = useCallback(async (inputs: WorkflowExecutionInputs) => {
    const pending = runInputPrompt;
    if (!pending) return;
    setRunInputPrompt(null);
    await dispatchTestRun(pending.draft, inputs);
  }, [dispatchTestRun, runInputPrompt]);

  const cancelRunInputs = useCallback(() => setRunInputPrompt(null), []);

  return {
    exportJson,
    save,
    preparePublication,
    publicationReview,
    reviewPublication,
    confirmPublication,
    cancelPublication,
    runInputPrompt,
    confirmRunInputs,
    cancelRunInputs,
    run
  };
}
