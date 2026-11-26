/**
 * Pure undo/redo stack. Kept free of React so it can be unit-tested in isolation and
 * reasoned about independently of the editor's effects.
 */

export const MAX_HISTORY = 50;

export interface HistoryState<T> {
  past: T[];
  future: T[];
}

export function createHistory<T>(): HistoryState<T> {
  return { past: [], future: [] };
}

export function canUndo<T>(history: HistoryState<T>): boolean {
  return history.past.length > 0;
}

export function canRedo<T>(history: HistoryState<T>): boolean {
  return history.future.length > 0;
}

/** Records a new snapshot, clearing the redo stack and clamping to `max` entries. */
export function pushSnapshot<T>(history: HistoryState<T>, snapshot: T, max: number = MAX_HISTORY): HistoryState<T> {
  const past = [...history.past, snapshot];
  if (past.length > max) past.splice(0, past.length - max);
  return { past, future: [] };
}

export interface HistoryStep<T> {
  history: HistoryState<T>;
  snapshot: T;
}

/** Moves one step back. `current` is pushed onto the redo stack. Returns null when empty. */
export function undo<T>(history: HistoryState<T>, current: T): HistoryStep<T> | null {
  if (history.past.length === 0) return null;
  const past = history.past.slice();
  const snapshot = past.pop() as T;
  return { history: { past, future: [...history.future, current] }, snapshot };
}

/** Moves one step forward. `current` is pushed onto the undo stack. Returns null when empty. */
export function redo<T>(history: HistoryState<T>, current: T): HistoryStep<T> | null {
  if (history.future.length === 0) return null;
  const future = history.future.slice();
  const snapshot = future.pop() as T;
  return { history: { past: [...history.past, current], future }, snapshot };
}
