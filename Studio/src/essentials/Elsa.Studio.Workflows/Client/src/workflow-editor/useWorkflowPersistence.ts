import type { WorkflowEditorDocumentAdapter } from "./WorkflowEditorDocumentAdapter";
import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { updateDraft } from "../api/workflowDesign";
import { autosaveDelayMs } from "./constants";
import { describeWorkflowError, getDraftSignature } from "./editorHelpers";
import type { WorkflowErrorInput } from "./editorTypes";
import type { WorkflowDraftRecipe } from "./workflowDocument";

interface WorkflowPersistenceParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  documentAdapter?: WorkflowEditorDocumentAdapter;
  autosaveEnabledByDefault?: boolean;
  editDraft(recipe: WorkflowDraftRecipe): void;
  setStatus(value: string): void;
  setError(value: WorkflowErrorInput): void;
}

// Owns draft persistence: the serialised save queue (so concurrent saves apply in order), the
// last-saved signature that gates autosave, and the debounced autosave effect. `saveDraft` reconciles
// the server copy back into the live draft via the document reducer; `markSaved` lets the loader seed
// the baseline so a freshly loaded draft doesn't immediately autosave.
export function useWorkflowPersistence({ context, draft, documentAdapter, autosaveEnabledByDefault = true, editDraft, setStatus, setError }: WorkflowPersistenceParams) {
  const [autosaveEnabled, setAutosaveEnabled] = useState(autosaveEnabledByDefault);
  const [autosavePaused, setAutosavePaused] = useState(false);
  const reviewHoldRef = useRef(false);
  const reviewHoldGenerationRef = useRef(0);
  const [reviewHeld, setReviewHeld] = useState(false);
  const holdForReview = useCallback((hold: boolean) => { const previous = reviewHoldRef.current; reviewHoldGenerationRef.current++; reviewHoldRef.current = hold; setReviewHeld(hold); return previous; }, []);
  const [saving, setSaving] = useState(false);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const inFlightSavesRef = useRef(0);
  // Keep the latest draft addressable so flushPendingSave can persist unsaved edits without a stale closure.
  const latestDraftRef = useRef<WorkflowDraft | null>(draft);
  latestDraftRef.current = draft;

  const markSaved = useCallback((savedDraft: WorkflowDraft | null) => {
    reviewHoldGenerationRef.current++;
    reviewHoldRef.current = false; setReviewHeld(false);
    lastSavedDraftSignatureRef.current = savedDraft ? getDraftSignature(savedDraft) : "";
  }, []);

  const saveDraft = useCallback(async (draftSnapshot: WorkflowDraft, savedStatus: string) => {
    // Capture when requested, before waiting in the queue. An old save cannot release a newer
    // reviewed edit, even when that old request only starts after the new hold was acquired.
    const requestedReviewGeneration = reviewHoldGenerationRef.current;
    const requestedSignature = getDraftSignature(draftSnapshot);
    const queuedSave = async () => {
      if (savedStatus === "Autosaved" && reviewHoldRef.current) return draftSnapshot;
      const requestId = ++saveRequestIdRef.current;
      setError("");
      inFlightSavesRef.current += 1;
      setSaving(true);
      try {
        const saved = await (documentAdapter?.save ?? updateDraft)(context, draftSnapshot);
        const savedSignature = getDraftSignature(saved);
        lastSavedDraftSignatureRef.current = savedSignature;
        const live = latestDraftRef.current;
        const coversLiveDraft = live?.id === draftSnapshot.id && live.definitionId === draftSnapshot.definitionId && getDraftSignature(live) === requestedSignature;
        const coversReviewGeneration = requestedReviewGeneration === reviewHoldGenerationRef.current;
        if (savedStatus !== "Autosaved" && coversLiveDraft && coversReviewGeneration) { reviewHoldRef.current = false; setReviewHeld(false); }
        // Reconcile the just-persisted draft: if the live draft still matches what we sent, adopt the
        // server copy wholesale; otherwise the author kept editing, so only fold in the fresh validation.
        editDraft(({ draft: current }) => {
          if (!current || current.id !== saved.id) return null;
          return getDraftSignature(current) === requestedSignature
            ? saved
            : { ...current, validationErrors: saved.validationErrors };
        });
        if (requestId === saveRequestIdRef.current && coversLiveDraft && coversReviewGeneration) setStatus(savedStatus);
        return saved;
      } catch (e) {
        if (requestId === saveRequestIdRef.current) {
          setStatus("");
          setError(describeWorkflowError(e, "Could not save the workflow draft."));
        }
        throw e;
      } finally {
        inFlightSavesRef.current -= 1;
        if (inFlightSavesRef.current === 0) setSaving(false);
      }
    };
    const queued = saveQueueRef.current.then(queuedSave, queuedSave);
    saveQueueRef.current = queued.catch(() => undefined);
    return queued;
  }, [context, documentAdapter, editDraft, setStatus, setError]);

  // Settles pending persistence before a publish review: persists the latest draft when it has unsaved
  // edits (so the review captures the same state that will be saved), then awaits the whole save queue.
  // Guarantees "Review & publish" acts on a consistent, saved snapshot instead of silently no-opping.
  const flushPendingSave = useCallback(async () => {
    const current = latestDraftRef.current;
    if (current && getDraftSignature(current) !== lastSavedDraftSignatureRef.current) {
      await saveDraft(current, "Saved");
    }
    await saveQueueRef.current.catch(() => undefined);
  }, [saveDraft]);

  useEffect(() => {
    if (!autosaveEnabled || autosavePaused || reviewHeld || !draft) return;

    const draftSignature = getDraftSignature(draft);
    if (draftSignature === lastSavedDraftSignatureRef.current) return;

    setStatus("Autosaving...");
    const timeoutId = window.setTimeout(() => {
      if (reviewHoldRef.current) return;
      void saveDraft(draft, "Autosaved").catch(() => undefined);
    }, autosaveDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [autosaveEnabled, autosavePaused, reviewHeld, draft, saveDraft, setStatus]);

  return { holdForReview, reviewHeld, saveDraft, flushPendingSave, saving, autosaveEnabled, setAutosaveEnabled, autosavePaused, setAutosavePaused, markSaved };
}
