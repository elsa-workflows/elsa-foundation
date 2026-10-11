import {
  canRedo,
  canUndo,
  createHistory,
  pushSnapshot,
  redo,
  undo,
  type HistoryState,
  type HistoryStep
} from "./workflowHistory";

export interface ActivityGraphHistory<TDocument> {
  resetKey: string;
  snapshots: HistoryState<TDocument>;
}

export function createActivityGraphHistory<TDocument>(resetKey: string): ActivityGraphHistory<TDocument> {
  return { resetKey, snapshots: createHistory<TDocument>() };
}

export function recordActivityGraphEdit<TDocument>(
  state: ActivityGraphHistory<TDocument>,
  current: TDocument
): ActivityGraphHistory<TDocument> {
  return { ...state, snapshots: pushSnapshot(state.snapshots, structuredClone(current)) };
}

export function resetActivityGraphHistory<TDocument>(
  state: ActivityGraphHistory<TDocument>,
  resetKey: string
): ActivityGraphHistory<TDocument> {
  return state.resetKey === resetKey
    ? state
    : createActivityGraphHistory<TDocument>(resetKey);
}

export function canUndoActivityGraph(state: ActivityGraphHistory<unknown>) {
  return canUndo(state.snapshots);
}

export function canRedoActivityGraph(state: ActivityGraphHistory<unknown>) {
  return canRedo(state.snapshots);
}

export function undoActivityGraph<TDocument>(
  state: ActivityGraphHistory<TDocument>,
  current: TDocument
): { state: ActivityGraphHistory<TDocument>; document: TDocument } | null {
  return mapStep(state, undo(state.snapshots, structuredClone(current)));
}

export function redoActivityGraph<TDocument>(
  state: ActivityGraphHistory<TDocument>,
  current: TDocument
): { state: ActivityGraphHistory<TDocument>; document: TDocument } | null {
  return mapStep(state, redo(state.snapshots, structuredClone(current)));
}

function mapStep<TDocument>(
  state: ActivityGraphHistory<TDocument>,
  step: HistoryStep<TDocument> | null
) {
  return step
    ? { state: { ...state, snapshots: step.history }, document: structuredClone(step.snapshot) }
    : null;
}
