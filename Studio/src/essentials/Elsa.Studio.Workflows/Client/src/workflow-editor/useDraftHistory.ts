import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { WorkflowDraft } from "../workflowTypes";
import { canRedo, canUndo, createHistory, pushSnapshot, redo as redoHistory, undo as undoHistory, type HistoryState } from "../workflowHistory";
import { historyCaptureDelayMs } from "./constants";
import { cloneWorkflowDraftForUndo, getDraftSignature } from "./editorHelpers";

interface DraftHistoryParams {
  draft: WorkflowDraft | null;
  // Restores a whole-draft snapshot. The host preserves the nearest surviving scope; this hook only
  // decides which snapshot to restore.
  restoreDraft(snapshot: WorkflowDraft): void;
}

/**
 * One designer-history position. `lineage` is the set of Weaver batch ids applied in this entry's line: an entry
 * inherits its predecessor's set, and the entry a batch produces adds that batch's id. `origin` is the batch that
 * produced exactly this entry, if any. Keying the autosave hold to the line, not to an index, keeps it correct when
 * the 50-entry cap drops the oldest entries.
 */
interface HistoryEntry {
  draft: WorkflowDraft;
  lineage: ReadonlySet<string>;
  origin: string | null;
}

/** Whether the review card's whole-change Undo may act now, and why not when it may not. */
export interface WorkflowBatchUndoState {
  status: "available" | "unavailable";
  reason?: string;
  /** The user's explicit save covered this batch; null when this history did not apply it, so it cannot know. */
  persisted: boolean | null;
}

/** The designer-history seam a reviewed Operation Batch uses (spec 1193 FR-009). */
export interface WorkflowBatchHistory {
  /** Captures a pending (debounced) edit from the latest committed draft, so the batch becomes its own entry. */
  flushFromLatest(): void;
  /** Records an applied batch as exactly one history entry whose line carries the batch id. */
  recordBatch(batchId: string, applied: WorkflowDraft): void;
  undoState(batchId: string): WorkflowBatchUndoState;
  /** Designer-history Undo of the batch entry, only while it is the current position with no later edit. */
  undoBatch(batchId: string): boolean;
  subscribe(listener: () => void): () => void;
}

/** The autosave hold keyed to history lineage (spec 1193 FR-010). */
export interface WorkflowLineageHold {
  /** Held while the current position's line contains a batch the user has not explicitly saved. */
  held: boolean;
  isHeld(): boolean;
  lineage(): ReadonlySet<string>;
  /** An explicit save covered these batches. */
  markSaved(batchIds: Iterable<string>): void;
}

// User-facing undo/redo. Snapshots are whole drafts; nodes/edges rebuild from them. Signature-driven
// capture records every committed mutation (palette add, drag, property edit, auto-layout, code apply)
// without touching each handler. A Weaver batch is recorded explicitly as its own entry.
export function useDraftHistory({ draft, restoreDraft }: DraftHistoryParams) {
  const historyRef = useRef<HistoryState<HistoryEntry>>(createHistory());
  const lastCapturedDraftRef = useRef<WorkflowDraft | null>(null);
  const lastCapturedSignatureRef = useRef("");
  const restoringRef = useRef(false);
  // The latest committed draft. A flush reads this, never a render-time closure, so an edit committed after the
  // caller rendered is still captured before a batch.
  const latestDraftRef = useRef<WorkflowDraft | null>(draft);
  latestDraftRef.current = draft;
  const lineageRef = useRef<ReadonlySet<string>>(new Set());
  const originRef = useRef<string | null>(null);
  const savedBatchesRef = useRef(new Set<string>());
  const knownBatchesRef = useRef(new Set<string>());
  const listenersRef = useRef(new Set<() => void>());
  const [historyVersion, setHistoryVersion] = useState(0);
  const [lineageHeld, setLineageHeld] = useState(false);

  const isLineageHeld = useCallback(() => [...lineageRef.current].some(id => !savedBatchesRef.current.has(id)), []);
  const changed = useCallback(() => {
    setHistoryVersion(version => version + 1);
    setLineageHeld(isLineageHeld());
    for (const listener of [...listenersRef.current]) listener();
  }, [isLineageHeld]);

  // Re-seeds history when a new draft loads (or after a promote). Returns nothing; callers invoke it in
  // their load() flow so a fresh definition starts with an empty, consistent undo stack.
  const resetHistory = useCallback((nextDraft: WorkflowDraft | null) => {
    historyRef.current = createHistory();
    lastCapturedDraftRef.current = nextDraft ? cloneWorkflowDraftForUndo(nextDraft) : null;
    lastCapturedSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    restoringRef.current = false;
    lineageRef.current = new Set();
    originRef.current = null;
    savedBatchesRef.current = new Set();
    knownBatchesRef.current = new Set();
    setHistoryVersion(0);
    setLineageHeld(false);
    for (const listener of [...listenersRef.current]) listener();
  }, []);

  // Pushes the captured position as an entry and makes `next` the captured position: an ordinary edit, so the new
  // position keeps the line it inherited and has no batch origin.
  const capture = useCallback((next: WorkflowDraft) => {
    const previous = lastCapturedDraftRef.current;
    if (previous) historyRef.current = pushSnapshot(historyRef.current, { draft: previous, lineage: lineageRef.current, origin: originRef.current });
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(next);
    lastCapturedSignatureRef.current = getDraftSignature(next);
    originRef.current = null;
    changed();
  }, [changed]);

  useEffect(() => {
    if (!draft) return;
    if (restoringRef.current) {
      restoringRef.current = false;
      return;
    }
    const signature = getDraftSignature(draft);
    if (signature === lastCapturedSignatureRef.current) return;

    const timeoutId = window.setTimeout(() => {
      // Checked again when the timer fires: a flush or a recorded batch may already have captured this state.
      if (getDraftSignature(draft) === lastCapturedSignatureRef.current) return;
      capture(draft);
    }, historyCaptureDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [capture, draft]);

  // Collapses any pending (debounced-but-not-yet-captured) edit into the history stack so
  // undo/redo always operate on a consistent baseline.
  const flushPendingHistory = useCallback(() => {
    const latest = latestDraftRef.current;
    if (!latest || getDraftSignature(latest) === lastCapturedSignatureRef.current) return;
    capture(latest);
  }, [capture]);

  const restoreDraftSnapshot = useCallback((entry: HistoryEntry) => {
    restoringRef.current = true;
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(entry.draft);
    lastCapturedSignatureRef.current = getDraftSignature(entry.draft);
    lineageRef.current = entry.lineage;
    originRef.current = entry.origin;
    restoreDraft(entry.draft);
    changed();
  }, [changed, restoreDraft]);

  const currentPosition = useCallback((): HistoryEntry | null => {
    const latest = latestDraftRef.current;
    return latest ? { draft: latest, lineage: lineageRef.current, origin: originRef.current } : null;
  }, []);

  const undo = useCallback(() => {
    flushPendingHistory();
    const position = currentPosition();
    if (!position) return;
    const step = undoHistory(historyRef.current, position);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [currentPosition, flushPendingHistory, restoreDraftSnapshot]);

  const redo = useCallback(() => {
    flushPendingHistory();
    const position = currentPosition();
    if (!position) return;
    const step = redoHistory(historyRef.current, position);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [currentPosition, flushPendingHistory, restoreDraftSnapshot]);

  const { canUndoNow, canRedoNow } = useMemo(() => {
    void historyVersion; // re-evaluate when the history stack mutates
    const hasPendingEdit = !!draft && !!lastCapturedDraftRef.current && getDraftSignature(draft) !== lastCapturedSignatureRef.current;
    return {
      canUndoNow: canUndo(historyRef.current) || hasPendingEdit,
      canRedoNow: canRedo(historyRef.current) && !hasPendingEdit
    };
  }, [draft, historyVersion]);

  const batchHistory = useMemo<WorkflowBatchHistory>(() => {
    const undoState = (batchId: string): WorkflowBatchUndoState => {
      if (!knownBatchesRef.current.has(batchId))
        return { status: "unavailable", persisted: null, reason: "This change was applied in an editor whose history is no longer open here. Use the designer's own history." };
      const persisted = savedBatchesRef.current.has(batchId);
      const latest = latestDraftRef.current;
      const laterEdit = !latest || getDraftSignature(latest) !== lastCapturedSignatureRef.current;
      if (originRef.current === batchId && !laterEdit && canUndo(historyRef.current)) return { status: "available", persisted };
      if (lineageRef.current.has(batchId))
        return { status: "unavailable", persisted, reason: "A later edit exists. Use the designer's Undo, which reverts edits in order." };
      return { status: "unavailable", persisted, reason: "This change is undone. The designer's Redo restores the whole change." };
    };
    return {
      flushFromLatest: flushPendingHistory,
      recordBatch(batchId, applied) {
        // The captured position is the batch's base: the caller flushed pending capture before dispatching it.
        const previous = lastCapturedDraftRef.current;
        if (previous) historyRef.current = pushSnapshot(historyRef.current, { draft: previous, lineage: lineageRef.current, origin: originRef.current });
        lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(applied);
        lastCapturedSignatureRef.current = getDraftSignature(applied);
        lineageRef.current = new Set([...lineageRef.current, batchId]);
        originRef.current = batchId;
        knownBatchesRef.current.add(batchId);
        changed();
      },
      undoState,
      undoBatch(batchId) {
        if (undoState(batchId).status !== "available") return false;
        undo();
        return true;
      },
      subscribe(listener) {
        listenersRef.current.add(listener);
        return () => { listenersRef.current.delete(listener); };
      }
    };
  }, [changed, flushPendingHistory, undo]);

  const lineageHold = useMemo<WorkflowLineageHold>(() => ({
    held: lineageHeld,
    isHeld: isLineageHeld,
    lineage: () => lineageRef.current,
    markSaved(batchIds) {
      let added = false;
      for (const id of batchIds) if (!savedBatchesRef.current.has(id)) { savedBatchesRef.current.add(id); added = true; }
      if (added) changed();
    }
  }), [changed, isLineageHeld, lineageHeld]);

  return { resetHistory, undo, redo, canUndoNow, canRedoNow, flushPendingHistory, batchHistory, lineageHold };
}
