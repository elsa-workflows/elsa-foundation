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
  autosaveEnabledByDefault?: boolean;
  editDraft(recipe: WorkflowDraftRecipe): void;
  setStatus(value: string): void;
  setError(value: WorkflowErrorInput): void;
}

// Owns draft persistence: the serialised save queue (so concurrent saves apply in order), the
// last-saved signature that gates autosave, and the debounced autosave effect. `saveDraft` reconciles
// the server copy back into the live draft via the document reducer; `markSaved` lets the loader seed
// the baseline so a freshly loaded draft doesn't immediately autosave.
export function useWorkflowPersistence({ context, draft, autosaveEnabledByDefault = true, editDraft, setStatus, setError }: WorkflowPersistenceParams) {
  const [autosaveEnabled, setAutosaveEnabled] = useState(autosaveEnabledByDefault);
  const [autosavePaused, setAutosavePaused] = useState(false);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());

  const markSaved = useCallback((savedDraft: WorkflowDraft | null) => {
    lastSavedDraftSignatureRef.current = savedDraft ? getDraftSignature(savedDraft) : "";
  }, []);

  const saveDraft = useCallback(async (draftSnapshot: WorkflowDraft, savedStatus: string) => {
    const queuedSave = async () => {
      const requestId = ++saveRequestIdRef.current;
      const requestedSignature = getDraftSignature(draftSnapshot);
      setError("");
      try {
        const saved = await updateDraft(context, draftSnapshot);
        const savedSignature = getDraftSignature(saved);
        lastSavedDraftSignatureRef.current = savedSignature;
        // Reconcile the just-persisted draft: if the live draft still matches what we sent, adopt the
        // server copy wholesale; otherwise the author kept editing, so only fold in the fresh validation.
        editDraft(({ draft: current }) => {
          if (!current || current.id !== saved.id) return null;
          return getDraftSignature(current) === requestedSignature
            ? saved
            : { ...current, validationErrors: saved.validationErrors };
        });
        if (requestId === saveRequestIdRef.current) setStatus(savedStatus);
        return saved;
      } catch (e) {
        if (requestId === saveRequestIdRef.current) {
          setStatus("");
          setError(describeWorkflowError(e, "Could not save the workflow draft."));
        }
        throw e;
      }
    };
    const queued = saveQueueRef.current.then(queuedSave, queuedSave);
    saveQueueRef.current = queued.catch(() => undefined);
    return queued;
  }, [context, editDraft, setStatus, setError]);

  useEffect(() => {
    if (!autosaveEnabled || autosavePaused || !draft) return;

    const draftSignature = getDraftSignature(draft);
    if (draftSignature === lastSavedDraftSignatureRef.current) return;

    setStatus("Autosaving...");
    const timeoutId = window.setTimeout(() => {
      void saveDraft(draft, "Autosaved").catch(() => undefined);
    }, autosaveDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [autosaveEnabled, autosavePaused, draft, saveDraft, setStatus]);

  return { saveDraft, autosaveEnabled, setAutosaveEnabled, autosavePaused, setAutosavePaused, markSaved };
}
