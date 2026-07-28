import { describe, expect, it } from "vitest";
import {
  canRedoActivityGraph,
  canUndoActivityGraph,
  createActivityGraphHistory,
  recordActivityGraphEdit,
  redoActivityGraph,
  resetActivityGraphHistory,
  undoActivityGraph
} from "../activityGraphHistory";

describe("Activity Graph history", () => {
  it("restores whole graph documents and supports redo", () => {
    const first = { payload: { rootActivity: { nodeId: "root", children: [] } }, layout: [] };
    const second = { payload: { rootActivity: { nodeId: "root", children: ["a"] } }, layout: [{ nodeId: "a" }] };
    const history = recordActivityGraphEdit(createActivityGraphHistory<typeof first>("draft-1"), first);

    const undone = undoActivityGraph(history, second);
    expect(undone?.document).toEqual(first);
    expect(canRedoActivityGraph(undone!.state)).toBe(true);

    const redone = redoActivityGraph(undone!.state, undone!.document);
    expect(redone?.document).toEqual(second);
    expect(canUndoActivityGraph(redone!.state)).toBe(true);
  });

  it("resets incompatible snapshots only when the host reset boundary changes", () => {
    const document = { payload: { edit: 1 }, layout: [] };
    const recorded = recordActivityGraphEdit(createActivityGraphHistory<typeof document>("active"), document);

    expect(resetActivityGraphHistory(recorded, "active")).toBe(recorded);
    expect(resetActivityGraphHistory(recorded, "conflict-4")).toEqual({
      resetKey: "conflict-4",
      snapshots: { past: [], future: [] }
    });
  });

  it("clones snapshots so later provider mutations cannot rewrite history", () => {
    const document = { payload: { children: ["a"] }, layout: [] as unknown[] };
    const recorded = recordActivityGraphEdit(createActivityGraphHistory<typeof document>("draft"), document);
    document.payload.children.push("b");

    expect(undoActivityGraph(recorded, document)?.document.payload.children).toEqual(["a"]);
  });
});
