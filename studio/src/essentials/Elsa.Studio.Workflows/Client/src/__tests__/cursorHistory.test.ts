import { describe, expect, it } from "vitest";
import {
  createCursorHistory,
  getPreviousCursor,
  moveCursorHistoryNext,
  moveCursorHistoryPrevious,
  reconcileCursorHistory,
  type CursorHistoryScope
} from "../workflow-editor/cursorHistory";

describe("workflow cursor history", () => {
  it("returns to the first page's null cursor and supports Next, Previous, Next", () => {
    const query = scope("all-runs");
    let history = createCursorHistory(query, null);

    expect(getPreviousCursor(history, query, null)).toEqual({ available: false, cursor: null });

    const next = moveCursorHistoryNext(history, query, null, "page-2");
    history = next.history;
    expect(next.destination).toEqual({ available: true, cursor: "page-2" });
    expect(getPreviousCursor(history, query, "page-2")).toEqual({ available: true, cursor: null });

    const previous = moveCursorHistoryPrevious(history, query, "page-2");
    history = previous.history;
    expect(previous.destination).toEqual({ available: true, cursor: null });
    expect(getPreviousCursor(history, query, null)).toEqual({ available: false, cursor: null });

    const nextAgain = moveCursorHistoryNext(history, query, null, "page-2");
    expect(nextAgain.history.cursors).toEqual([null, "page-2"]);
    expect(nextAgain.history.index).toBe(1);
    expect(nextAgain.destination).toEqual({ available: true, cursor: "page-2" });
  });

  it("reconciles retained browser history and treats an unknown deep link as a fresh start", () => {
    const query = scope("all-runs");
    const history = moveCursorHistoryNext(
      moveCursorHistoryNext(createCursorHistory(query, null), query, null, "page-2").history,
      query,
      "page-2",
      "page-3"
    ).history;

    const restored = reconcileCursorHistory(history, query, "page-2");
    expect(getPreviousCursor(restored, query, "page-2")).toEqual({ available: true, cursor: null });

    const deepLink = reconcileCursorHistory(history, query, "external-cursor");
    expect(deepLink.cursors).toEqual(["external-cursor"]);
    expect(getPreviousCursor(deepLink, query, "external-cursor")).toEqual({ available: false, cursor: null });
  });

  it("resets history when server filters, page size, or backend context changes", () => {
    const query = scope("status=Running&pageSize=10");
    const pageTwo = moveCursorHistoryNext(createCursorHistory(query, null), query, null, "page-2").history;
    const changedQuery = scope("status=Faulted&pageSize=10");
    const changedBackend = scope(query.query, {});

    for (const nextScope of [changedQuery, changedBackend]) {
      const reset = reconcileCursorHistory(pageTwo, nextScope, null);
      expect(reset.cursors).toEqual([null]);
      expect(getPreviousCursor(reset, nextScope, null)).toEqual({ available: false, cursor: null });
    }
  });

  it("truncates a stale forward branch when the next cursor changes", () => {
    const query = scope("all-runs");
    const pageThree = moveCursorHistoryNext(
      moveCursorHistoryNext(createCursorHistory(query, null), query, null, "page-2").history,
      query,
      "page-2",
      "old-page-3"
    ).history;
    const pageTwo = reconcileCursorHistory(pageThree, query, "page-2");

    const refreshedNext = moveCursorHistoryNext(pageTwo, query, "page-2", "new-page-3");
    expect(refreshedNext.history.cursors).toEqual([null, "page-2", "new-page-3"]);
    expect(refreshedNext.history.index).toBe(2);
  });

  it("honors an advertised server previous cursor, including the null first-page cursor", () => {
    const query = scope("all-runs");
    const externalPage = createCursorHistory(query, "page-2");
    const previous = moveCursorHistoryPrevious(externalPage, query, "page-2", {
      hasPrevious: true,
      previousCursor: null
    });

    expect(previous.destination).toEqual({ available: true, cursor: null });
    expect(previous.history.cursors).toEqual([null]);
  });
});

function scope(query: string, backend: object = {}): CursorHistoryScope {
  return { backend, query };
}
