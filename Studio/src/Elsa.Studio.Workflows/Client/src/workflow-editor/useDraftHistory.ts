import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { WorkflowDraft } from "../workflowTypes";
import { canRedo, canUndo, createHistory, pushSnapshot, redo as redoHistory, undo as undoHistory, type HistoryState } from "../workflowHistory";
import { historyCaptureDelayMs } from "./constants";
import { cloneWorkflowDraftForUndo, getDraftSignature } from "./editorHelpers";

interface DraftHistoryParams {
  draft: WorkflowDraft | null;
  // Restores a whole-draft snapshot into the editor document (resets scope + selection). The document
  // reducer owns the cascade; the hook only decides *which* snapshot to restore.
  restoreDraft(snapshot: WorkflowDraft): void;
}

// User-facing undo/redo. Snapshots are whole drafts; nodes/edges rebuild from them. Signature-driven
// capture records every committed mutation (palette add, drag, property edit, auto-layout, code apply,
// Weaver batch) without touching each handler. Lifted out of WorkflowEditor along with the associated
// refs and the debounced capture effect.
export function useDraftHistory({ draft, restoreDraft }: DraftHistoryParams) {
  const historyRef = useRef<HistoryState<WorkflowDraft>>(createHistory());
  const lastCapturedDraftRef = useRef<WorkflowDraft | null>(null);
  const lastCapturedSignatureRef = useRef("");
  const restoringRef = useRef(false);
  const [historyVersion, setHistoryVersion] = useState(0);

  // Re-seeds history when a new draft loads (or after a promote). Returns nothing; callers invoke it in
  // their load() flow so a fresh definition starts with an empty, consistent undo stack.
  const resetHistory = useCallback((nextDraft: WorkflowDraft | null) => {
    historyRef.current = createHistory();
    lastCapturedDraftRef.current = nextDraft ? cloneWorkflowDraftForUndo(nextDraft) : null;
    lastCapturedSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    restoringRef.current = false;
    setHistoryVersion(0);
  }, []);

  useEffect(() => {
    if (!draft) return;
    if (restoringRef.current) {
      restoringRef.current = false;
      return;
    }
    const signature = getDraftSignature(draft);
    if (signature === lastCapturedSignatureRef.current) return;

    const timeoutId = window.setTimeout(() => {
      const previous = lastCapturedDraftRef.current;
      if (previous) {
        historyRef.current = pushSnapshot(historyRef.current, previous);
        setHistoryVersion(version => version + 1);
      }
      lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(draft);
      lastCapturedSignatureRef.current = signature;
    }, historyCaptureDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [draft]);

  // Collapses any pending (debounced-but-not-yet-captured) edit into the history stack so
  // undo/redo always operate on a consistent baseline.
  const flushPendingHistory = useCallback(() => {
    if (!draft) return;
    const signature = getDraftSignature(draft);
    if (signature === lastCapturedSignatureRef.current) return;
    const previous = lastCapturedDraftRef.current;
    if (previous) historyRef.current = pushSnapshot(historyRef.current, previous);
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(draft);
    lastCapturedSignatureRef.current = signature;
  }, [draft]);

  const restoreDraftSnapshot = useCallback((snapshot: WorkflowDraft) => {
    restoringRef.current = true;
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(snapshot);
    lastCapturedSignatureRef.current = getDraftSignature(snapshot);
    restoreDraft(snapshot);
    setHistoryVersion(version => version + 1);
  }, [restoreDraft]);

  const undo = useCallback(() => {
    if (!draft) return;
    flushPendingHistory();
    const step = undoHistory(historyRef.current, draft);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [draft, flushPendingHistory, restoreDraftSnapshot]);

  const redo = useCallback(() => {
    if (!draft) return;
    flushPendingHistory();
    const step = redoHistory(historyRef.current, draft);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [draft, flushPendingHistory, restoreDraftSnapshot]);

  const { canUndoNow, canRedoNow } = useMemo(() => {
    void historyVersion; // re-evaluate when the history stack mutates
    const hasPendingEdit = !!draft && !!lastCapturedDraftRef.current && getDraftSignature(draft) !== lastCapturedSignatureRef.current;
    return {
      canUndoNow: canUndo(historyRef.current) || hasPendingEdit,
      canRedoNow: canRedo(historyRef.current) && !hasPendingEdit
    };
  }, [draft, historyVersion]);

  return { resetHistory, undo, redo, canUndoNow, canRedoNow };
}
