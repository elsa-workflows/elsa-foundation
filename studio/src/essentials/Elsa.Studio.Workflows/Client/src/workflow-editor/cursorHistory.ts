export type WorkflowCursor = string | null;

export interface CursorHistoryScope {
  /** The stable endpoint context object, which also carries the active backend/auth context. */
  backend: object;
  /** A stable serialization of the server-side filters and page size. */
  query: string;
}

export interface CursorHistoryState {
  scope: CursorHistoryScope;
  cursors: readonly WorkflowCursor[];
  index: number;
}

export interface CursorHistoryDestination {
  available: boolean;
  cursor: WorkflowCursor;
}

export interface ServerPreviousPage {
  hasPrevious: boolean;
  previousCursor: WorkflowCursor;
}

export function createCursorHistory(scope: CursorHistoryScope, cursor: WorkflowCursor): CursorHistoryState {
  return { scope, cursors: [cursor], index: 0 };
}

/** Selects a retained location on browser Back/Forward, or drops history for an unknown deep link. */
export function reconcileCursorHistory(
  history: CursorHistoryState,
  scope: CursorHistoryScope,
  cursor: WorkflowCursor
): CursorHistoryState {
  if (!sameScope(history.scope, scope)) return createCursorHistory(scope, cursor);

  const index = history.cursors.indexOf(cursor);
  return index < 0 ? createCursorHistory(scope, cursor) : { ...history, index };
}

export function getPreviousCursor(
  history: CursorHistoryState,
  scope: CursorHistoryScope,
  cursor: WorkflowCursor,
  serverPage?: ServerPreviousPage
): CursorHistoryDestination {
  if (serverPage?.hasPrevious) return { available: true, cursor: serverPage.previousCursor };

  const current = reconcileCursorHistory(history, scope, cursor);
  if (current.index === 0) return { available: false, cursor: null };
  return { available: true, cursor: current.cursors[current.index - 1] };
}

export function moveCursorHistoryNext(
  history: CursorHistoryState,
  scope: CursorHistoryScope,
  currentCursor: WorkflowCursor,
  nextCursor: string | null
): { history: CursorHistoryState; destination: CursorHistoryDestination } {
  const current = reconcileCursorHistory(history, scope, currentCursor);
  if (!nextCursor || nextCursor === currentCursor) {
    return { history: current, destination: { available: false, cursor: null } };
  }

  const retainedNext = current.cursors[current.index + 1];
  const next = retainedNext === nextCursor
    ? { ...current, index: current.index + 1 }
    : {
      scope,
      cursors: [...current.cursors.slice(0, current.index + 1), nextCursor],
      index: current.index + 1
    };
  return { history: next, destination: { available: true, cursor: nextCursor } };
}

export function moveCursorHistoryPrevious(
  history: CursorHistoryState,
  scope: CursorHistoryScope,
  currentCursor: WorkflowCursor,
  serverPage?: ServerPreviousPage
): { history: CursorHistoryState; destination: CursorHistoryDestination } {
  const current = reconcileCursorHistory(history, scope, currentCursor);
  const destination = getPreviousCursor(current, scope, currentCursor, serverPage);
  if (!destination.available) return { history: current, destination };

  return {
    history: reconcileCursorHistory(current, scope, destination.cursor),
    destination
  };
}

function sameScope(left: CursorHistoryScope, right: CursorHistoryScope) {
  return left.backend === right.backend && left.query === right.query;
}
