import { useCallback, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft, WorkflowExecutionInputs, WorkflowInput } from "../workflowTypes";
import { getWorkflowDefinitionVersion, promoteDraft } from "../api/workflowDesign";
import {
  getPublicationPolicy,
  listPublicationSlots,
  preflightPublication,
  publishVersion,
  startWorkflowDraftTestRun,
  type PublicationIntent
} from "../api/publishing";
import { buildExportPayload, downloadWorkflowJson } from "../workflowSerialization";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
import { createDraftSnapshotId, getDraftSignature, isRejectedTestRun } from "./editorHelpers";
import type { WorkflowEditorOperation, WorkflowTestRunState } from "./editorTypes";
import { createPublicationReview, type PublicationReviewState } from "./publicationReview";

interface WorkflowOperationsParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  details: WorkflowDefinitionDetails | null;
  catalog: ActivityCatalogItem[];
  busy: boolean;
  saveDraft(draft: WorkflowDraft, savedStatus: string): Promise<WorkflowDraft>;
  reload(): Promise<void>;
  startTestRun(testRun: WorkflowTestRunState): void;
  clearTestRun(): void;
  setPublishedArtifact(id: string | null): void;
  setOperation(operation: WorkflowEditorOperation): void;
  setStatus(value: string): void;
  setError(value: string): void;
  setActiveRightPanelId(id: string): void;
  setInspectorCollapsed(collapsed: boolean): void;
  setAutosavePaused(paused: boolean): void;
}

// The editor's async command handlers: export JSON, save, promote+publish, and dispatch a transient test
// run. Each guards on `busy`, drives the shared `operation` flag, and reports progress via status/error.
export function useWorkflowOperations({
  context,
  draft,
  details,
  catalog,
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
  setInspectorCollapsed,
  setAutosavePaused
}: WorkflowOperationsParams) {
  const [publicationReview, setPublicationReview] = useState<PublicationReviewState | null>(null);
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
    const draftSnapshot = structuredClone(draft);
    setAutosavePaused(true);
    setOperation("publicationPreflight");
    setError("");
    setStatus("Preparing publication review...");
    try {
      // Review preparation is deliberately read-only. The captured draft is not saved or promoted
      // until the author explicitly presses Publish.
      const [policy, slots] = await Promise.all([
        getPublicationPolicy(context, draftSnapshot.definitionId),
        listPublicationSlots(context, draftSnapshot.definitionId)
      ]);
      const occupiedSlots = slots.filter(slot => slot.activePublicationId || slot.publication);
      const incompleteSlot = occupiedSlots.find(slot => !slot.publication?.versionId);
      if (incompleteSlot) throw new Error(`Publication slot '${incompleteSlot.slotName}' did not include its active version.`);
      const versionsById = new Map<string, Awaited<ReturnType<typeof getWorkflowDefinitionVersion>>>();
      await Promise.all(occupiedSlots.map(async slot => {
        const versionId = slot.publication!.versionId;
        if (!versionsById.has(versionId)) versionsById.set(versionId, await getWorkflowDefinitionVersion(context, versionId));
      }));
      const slotVersions = Object.fromEntries(occupiedSlots.map(slot => [slot.slotName, versionsById.get(slot.publication!.versionId)!]));
      setPublicationReview(createPublicationReview({
        draft: draftSnapshot,
        details,
        policy,
        slots,
        slotVersions,
        catalog
      }));
      setStatus("");
    } catch (error) {
      setPublicationReview(null);
      setAutosavePaused(false);
      setStatus("");
      setError(`Could not prepare a trustworthy publication review. No changes were saved, promoted, or published. ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      setOperation("idle");
    }
  }, [draft, details, catalog, busy, context, setAutosavePaused, setError, setOperation, setStatus]);

  const confirmPublication = useCallback(async (intent: PublicationIntent) => {
    if (!publicationReview || publicationReview.phase === "success") return;
    if (intent.action === "sideBySide" && !intent.slotName?.trim()) {
      setError("Enter a meaningful slot name for side-by-side publication.");
      return;
    }
    if (intent.action === "sideBySide" && intent.slotName?.trim().toLowerCase() === "default") {
      setError("The default slot is reserved for replacement publication. Choose another named slot.");
      return;
    }
    if (publicationReview.validationErrors.length) {
      setPublicationReview(current => current ? { ...current, phase: "validationBlocked", intent } : current);
      setError("Resolve validation errors before publishing. No changes were saved or promoted.");
      return;
    }
    setOperation("publishing");
    setError("");
    let promotedVersionId = publicationReview.promotedVersionId;
    let savedDraft = publicationReview.savedDraft;
    try {
      if (!promotedVersionId) {
        if (!savedDraft) {
          setPublicationReview(current => current ? { ...current, phase: "publishing", progressStep: "saving", intent } : current);
          setStatus("Saving publication snapshot...");
          const justSaved = await saveDraft(publicationReview.draftSnapshot, "Publication snapshot saved");
          savedDraft = justSaved;
          setPublicationReview(current => current ? { ...current, savedDraft: justSaved, draftSnapshot: justSaved, intent } : current);
        }
        const persistedDraft = savedDraft;
        const validationErrors = persistedDraft.validationErrors
          .map(error => error.message?.trim())
          .filter((message): message is string => Boolean(message));
        if (!persistedDraft.state.rootActivity) validationErrors.unshift("Workflow has no root activity.");
        if (validationErrors.length) {
          setPublicationReview(current => current ? {
            ...current,
            phase: "savedFailure",
            progressStep: undefined,
            validationErrors,
            executableStatus: "blocked",
            savedDraft: persistedDraft,
            draftSnapshot: persistedDraft,
            failureMessage: "The reviewed draft was saved, but validation blocked promotion. No version or publication was created. Close the review, correct the validation errors, and try again.",
            intent
          } : current);
          setStatus("");
          setError("The reviewed draft was saved, but it failed validation and was not promoted or published.");
          return;
        }

        setPublicationReview(current => current ? { ...current, phase: "publishing", progressStep: "promoting", savedDraft: persistedDraft, draftSnapshot: persistedDraft, intent } : current);
        setStatus("Promoting publication version...");
        const promoted = await promoteDraft(context, persistedDraft.id);
        promotedVersionId = promoted.id;
        setPublicationReview(current => current ? { ...current, promotedVersionId, proposedVersion: promoted.version, progressStep: "preflight" } : current);
      } else {
        setPublicationReview(current => current ? { ...current, phase: "publishing", progressStep: "preflight", intent } : current);
      }

      setStatus("Checking server publication policy...");
      const preflight = await preflightPublication(context, promotedVersionId, intent);
      setPublicationReview(current => current ? { ...current, preflight, intent } : current);
      if (!preflight.canActivate) {
        setPublicationReview(current => current ? {
          ...current,
          phase: "partialFailure",
          progressStep: undefined,
          promotedVersionId,
          preflight,
          intent,
          failureMessage: "Server preflight blocked publication. The promoted version was retained. Resolve the conflicts or choose another slot, then retry Publish."
        } : current);
        setStatus("");
        return;
      }

      setPublicationReview(current => current ? { ...current, progressStep: "publishing", preflight, intent } : current);
      setStatus("Publishing executable reference...");
      const published = await publishVersion(context, promotedVersionId, intent);
      setPublishedArtifact(published.artifactId);
      setPublicationReview(current => current ? {
        ...current,
        phase: "success",
        progressStep: undefined,
        preflight,
        intent,
        promotedVersionId,
        published
      } : current);
      setStatus(`Published to ${published.slotName}`);
      try {
        await reload();
      } catch (reloadError) {
        setError(`Publication succeeded, but the editor could not refresh: ${reloadError instanceof Error ? reloadError.message : String(reloadError)}`);
      }
      setAutosavePaused(false);
    } catch (e) {
      setStatus("");
      const failureMessage = e instanceof Error ? e.message : String(e);
      if (promotedVersionId) {
        setPublicationReview(current => current ? {
          ...current,
          phase: "partialFailure",
          progressStep: undefined,
          promotedVersionId,
          intent,
          failureMessage: `Publication failed after promotion: ${failureMessage}. The promoted version was retained; retry Publish or close this review and publish it later.`
        } : current);
      } else if (savedDraft) {
        const failedSavedDraft = savedDraft;
        setPublicationReview(current => current ? {
          ...current,
          phase: "savedFailure",
          progressStep: undefined,
          savedDraft: failedSavedDraft,
          draftSnapshot: failedSavedDraft,
          intent,
          failureMessage: `The reviewed draft was saved, but promotion failed: ${failureMessage}. No version or publication was created. Retry Publish to promote the already-saved snapshot.`
        } : current);
      } else {
        setPublicationReview(current => current ? { ...current, phase: "review", progressStep: undefined, intent } : current);
      }
      setError(failureMessage);
    } finally {
      setOperation("idle");
    }
  }, [context, publicationReview, reload, saveDraft, setAutosavePaused, setError, setOperation, setPublishedArtifact, setStatus]);

  const cancelPublication = useCallback(() => {
    setPublicationReview(null);
    setAutosavePaused(false);
    setStatus(publicationReview?.promotedVersionId
      ? "Publication review closed; the promoted version remains available."
      : publicationReview?.savedDraft
        ? "Publication review closed; the draft was saved, but no version was promoted or published."
        : "Publication cancelled; no changes were saved, promoted, or published.");
  }, [publicationReview, setAutosavePaused, setStatus]);

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
    confirmPublication,
    cancelPublication,
    runInputPrompt,
    confirmRunInputs,
    cancelRunInputs,
    run
  };
}
