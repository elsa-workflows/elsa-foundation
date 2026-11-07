import type { WorkflowEditorDocumentAdapter } from "./WorkflowEditorDocumentAdapter";
import { useCallback, useEffect, useRef, useState } from "react";
import { studioNavigationRequestedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { updateDraft } from "../api/workflowDesign";
import { autosaveDelayMs } from "./constants";
import { describeWorkflowError, getDraftSignature } from "./editorHelpers";
import type { WorkflowErrorInput } from "./editorTypes";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import type { WorkflowLineageHold } from "./useDraftHistory";

export interface PreparedWorkflowSave {
  readonly draft: WorkflowDraft;
  /** Omitted means keep the line captured by the caller; null explicitly writes an unknown line. */
  readonly lineage?: readonly string[] | null;
  /** Check resource/authority again after waiting behind earlier HTTP saves. */
  assertCurrent?(): void;
}

export interface WorkflowSaveCheckpoint {
  prepare(request: { snapshot: WorkflowDraft; intent: "save" | "autosave"; capture: "current" | "snapshot"; lineage: readonly string[] | null }): Promise<PreparedWorkflowSave>;
  /** Synchronous only: navigation must retain unsaved native work without starting serialization. */
  probe(): boolean;
  readonly revision: number;
}

interface WorkflowPersistenceParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  documentAdapter?: WorkflowEditorDocumentAdapter;
  autosaveEnabledByDefault?: boolean;
  editDraft(recipe: WorkflowDraftRecipe): void;
  /** Reconcile an acknowledged live edit with history before adopting the server's normalized representation. */
  onSaveReconciled?(requested: WorkflowDraft, saved: WorkflowDraft): void;
  setStatus(value: string): void;
  setError(value: WorkflowErrorInput): void;
  /**
   * The designer-history autosave hold for reviewed Operation Batches (spec 1193 FR-010): autosave is skipped while the
   * current history position's line holds a batch the user has not explicitly saved, and an explicit save that covers
   * the live draft marks the batches of that line as saved.
   */
  lineageHold?: WorkflowLineageHold;
  authoringCheckpoint?: WorkflowSaveCheckpoint;
}

// Owns draft persistence: the serialised save queue (so concurrent saves apply in order), the
// last-saved signature that gates autosave, and the debounced autosave effect. `saveDraft` reconciles
// the server copy back into the live draft via the document reducer; `markSaved` lets the loader seed
// the baseline so a freshly loaded draft doesn't immediately autosave.
export function useWorkflowPersistence({ context, draft, documentAdapter, autosaveEnabledByDefault = true, editDraft, onSaveReconciled, setStatus, setError, lineageHold, authoringCheckpoint }: WorkflowPersistenceParams) {
  const checkpointRef = useRef(authoringCheckpoint);
  checkpointRef.current = authoringCheckpoint;
  const authoringRevision = authoringCheckpoint?.revision;
  const onSaveReconciledRef = useRef(onSaveReconciled);
  onSaveReconciledRef.current = onSaveReconciled;
  const lineageHoldRef = useRef(lineageHold);
  lineageHoldRef.current = lineageHold;
  const lineageHeld = lineageHold?.held ?? false;
  const [autosaveEnabled, setAutosaveEnabled] = useState(autosaveEnabledByDefault);
  const [autosavePaused, setAutosavePaused] = useState(false);
  const reviewHoldRef = useRef(false);
  const reviewHoldGenerationRef = useRef(0);
  const [reviewHeld, setReviewHeld] = useState(false);
  const holdForReview = useCallback((hold: boolean) => { const previous = reviewHoldRef.current; reviewHoldGenerationRef.current++; reviewHoldRef.current = hold; setReviewHeld(hold); return previous; }, []);
  const [saving, setSaving] = useState(false);
  const lastSavedDraftSignatureRef = useRef("");
  const [persistedDraft, setPersistedDraft] = useState<WorkflowDraft | null>(null);
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const inFlightSavesRef = useRef(0);
  const queuedSavesRef = useRef(0);
  // Keep the latest draft addressable so flushPendingSave can persist unsaved edits without a stale closure.
  const latestDraftRef = useRef<WorkflowDraft | null>(draft);
  latestDraftRef.current = draft;

  useEffect(() => {
    const preventNavigation = (event: Event) => {
      const current = latestDraftRef.current;
      if (queuedSavesRef.current > 0 || checkpointRef.current?.probe() || current && getDraftSignature(current) !== lastSavedDraftSignatureRef.current) event.preventDefault();
    };
    window.addEventListener(studioNavigationRequestedEvent, preventNavigation);
    window.addEventListener("beforeunload", preventNavigation);
    return () => {
      window.removeEventListener(studioNavigationRequestedEvent, preventNavigation);
      window.removeEventListener("beforeunload", preventNavigation);
    };
  }, []);

  const markSaved = useCallback((savedDraft: WorkflowDraft | null) => {
    reviewHoldGenerationRef.current++;
    reviewHoldRef.current = false; setReviewHeld(false);
    lastSavedDraftSignatureRef.current = savedDraft ? getDraftSignature(savedDraft) : "";
    setPersistedDraft(savedDraft);
  }, []);

  /**
   * Saves a snapshot. A caller that captured a snapshot of (or derived one from) the live draft declares the history line
   * it captured together with it in `options.lineage`; without one a save of anything but the live draft writes an
   * unknown line, which clears the batch saved set.
   */
  const saveDraft = useCallback(async (draftSnapshot: WorkflowDraft, savedStatus: string, options?: { lineage?: Iterable<string>; capture?: "current" }) => {
    // Capture when requested, before waiting in the queue. An old save cannot release a newer
    // reviewed edit, even when that old request only starts after the new hold was acquired.
    const requestedReviewGeneration = reviewHoldGenerationRef.current;
    const initialSignature = getDraftSignature(draftSnapshot);
    // The line of the state this save writes, captured when the save is requested: the caller's declared line for a
    // snapshot it captured together with that line, else the live line's when it saves the live draft, else not known
    // (null). The live match is by signature: edits that return to the line's content and are then saved explicitly
    // mark its batches saved without writing anything new, which is harmless, as the owner then holds content equal
    // to what the line represents.
    const requestedLive = latestDraftRef.current;
    const initialLineage = options?.lineage ? [...options.lineage]
      : requestedLive && getDraftSignature(requestedLive) === initialSignature ? [...(lineageHoldRef.current?.lineage() ?? [])] : null;
    type Prepared = { ok: true; value: PreparedWorkflowSave } | { ok: false; error: unknown };
    let settlePrepared!: (outcome: Prepared) => void;
    const prepared = new Promise<Prepared>(resolve => { settlePrepared = resolve; });
    const queuedSave = async () => {
      const requestId = ++saveRequestIdRef.current;
      setError("");
      inFlightSavesRef.current += 1;
      setSaving(true);
      try {
        const preparationResult = await prepared;
        if (!preparationResult.ok) throw preparationResult.error;
        const { value } = preparationResult;
        const requested = value.draft;
        const requestedSignature = getDraftSignature(requested);
        const line = value.lineage === undefined ? initialLineage : value.lineage;
        const writtenLineage = line === null ? null : [...line];
        if (savedStatus === "Autosaved" && (reviewHoldRef.current || lineageHoldRef.current?.isHeld())) return requested;
        value.assertCurrent?.();
        const saved = await (documentAdapter?.save ?? updateDraft)(context, requested);
        value.assertCurrent?.();
        const savedSignature = getDraftSignature(saved);
        lastSavedDraftSignatureRef.current = savedSignature;
        setPersistedDraft(saved);
        const live = latestDraftRef.current;
        const coversLiveDraft = live?.id === requested.id && live.definitionId === requested.definitionId && getDraftSignature(live) === requestedSignature;
        if (coversLiveDraft) onSaveReconciledRef.current?.(requested, saved);
        const coversReviewGeneration = requestedReviewGeneration === reviewHoldGenerationRef.current;
        if (savedStatus !== "Autosaved" && coversLiveDraft && coversReviewGeneration) { reviewHoldRef.current = false; setReviewHeld(false); }
        // An explicit save marks the batches of the line it wrote, even when the author kept editing while it was in flight:
        // those later edits stay unsaved on their own, and autosave as usual unless they bring a new batch.
        lineageHoldRef.current?.recordSave(writtenLineage, savedStatus !== "Autosaved");
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
    queuedSavesRef.current += 1;
    const queued = saveQueueRef.current.then(queuedSave, queuedSave).finally(() => { queuedSavesRef.current -= 1; });
    saveQueueRef.current = queued.catch(() => undefined);
    // Reserve the queue position before invoking a host that might synchronously request another
    // save. Preparation still starts in this call; its rejection is always observed immediately.
    try {
      const preparation: Promise<PreparedWorkflowSave> = checkpointRef.current?.prepare({ snapshot: draftSnapshot, intent: savedStatus === "Autosaved" ? "autosave" : "save",
        capture: options?.capture ?? "snapshot", lineage: initialLineage }) ?? Promise.resolve({ draft: draftSnapshot });
      void preparation.then(value => settlePrepared({ ok: true, value: {
        ...value, draft: structuredClone(value.draft), lineage: value.lineage === undefined || value.lineage === null ? value.lineage : [...value.lineage]
      } })).catch(error => settlePrepared({ ok: false, error }));
    } catch (error) { settlePrepared({ ok: false, error }); }
    return queued;
  }, [context, documentAdapter, editDraft, setStatus, setError]);

  // Settles pending persistence before a publish review: persists the latest draft when it has unsaved
  // edits (so the review captures the same state that will be saved), then awaits the whole save queue.
  // Guarantees "Review & publish" acts on a consistent, saved snapshot instead of silently no-opping.
  const flushPendingSave = useCallback(async () => {
    const current = latestDraftRef.current;
    if (current && (checkpointRef.current?.probe() || getDraftSignature(current) !== lastSavedDraftSignatureRef.current)) {
      await saveDraft(current, "Saved", { capture: "current" });
    }
    await saveQueueRef.current.catch(() => undefined);
  }, [saveDraft]);

  useEffect(() => {
    if (!autosaveEnabled || autosavePaused || reviewHeld || lineageHeld || !draft) return;

    const draftSignature = getDraftSignature(draft);
    if (draftSignature === lastSavedDraftSignatureRef.current && !checkpointRef.current?.probe()) return;

    setStatus("Autosaving...");
    const timeoutId = window.setTimeout(() => {
      if (reviewHoldRef.current || lineageHoldRef.current?.isHeld()) return;
      void saveDraft(draft, "Autosaved", { capture: "current" }).catch(() => undefined);
    }, autosaveDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [autosaveEnabled, autosavePaused, reviewHeld, lineageHeld, draft, saveDraft, setStatus, authoringRevision]);

  return { persistedDraft, holdForReview, reviewHeld, saveDraft, flushPendingSave, saving, autosaveEnabled, setAutosaveEnabled, autosavePaused, setAutosavePaused, markSaved };
}
