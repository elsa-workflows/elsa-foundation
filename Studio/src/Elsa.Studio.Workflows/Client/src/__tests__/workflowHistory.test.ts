import { describe, expect, it } from "vitest";
import { canRedo, canUndo, createHistory, pushSnapshot, redo, undo } from "../workflowHistory";

describe("workflow history stack", () => {
  it("starts empty", () => {
    const history = createHistory<string>();
    expect(canUndo(history)).toBe(false);
    expect(canRedo(history)).toBe(false);
  });

  it("records snapshots and clears redo on a new push", () => {
    let history = createHistory<string>();
    history = pushSnapshot(history, "v0");
    expect(canUndo(history)).toBe(true);

    const undone = undo(history, "v1");
    expect(undone?.snapshot).toBe("v0");
    expect(canRedo(undone!.history)).toBe(true);

    // A fresh edit after an undo discards the redo branch.
    const afterEdit = pushSnapshot(undone!.history, "v0");
    expect(canRedo(afterEdit)).toBe(false);
  });

  it("round-trips undo then redo", () => {
    let history = createHistory<string>();
    history = pushSnapshot(history, "v0"); // baseline before editing to v1

    const undone = undo(history, "v1");
    expect(undone?.snapshot).toBe("v0");

    const redone = redo(undone!.history, undone!.snapshot);
    expect(redone?.snapshot).toBe("v1");
  });

  it("returns null when there is nothing to undo or redo", () => {
    const history = createHistory<string>();
    expect(undo(history, "current")).toBeNull();
    expect(redo(history, "current")).toBeNull();
  });

  it("clamps the stack to the max size", () => {
    let history = createHistory<number>();
    for (let i = 0; i < 60; i += 1) history = pushSnapshot(history, i, 50);
    expect(history.past.length).toBe(50);
    expect(history.past[0]).toBe(10); // oldest 10 dropped
    expect(history.past.at(-1)).toBe(59);
  });

  it("does not mutate the input history", () => {
    const history = pushSnapshot(createHistory<string>(), "v0");
    const before = JSON.stringify(history);
    undo(history, "v1");
    expect(JSON.stringify(history)).toBe(before);
  });
});
