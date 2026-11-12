import { describe, expect, it, vi } from "vitest";
import { selectAttachedRunEditorNode } from "../workflow-editor/selectAttachedRunEditorNode";

describe("selectAttachedRunEditorNode", () => {
  it("reveals the run inspector when a node is selected while a snapshot is attached", () => {
    const select = vi.fn();
    const reveal = vi.fn();
    selectAttachedRunEditorNode("write", select, true, reveal);
    expect(select).toHaveBeenCalledWith("write");
    expect(reveal).toHaveBeenCalledOnce();
  });

  it("selects without revealing when no run overlay is attached", () => {
    const select = vi.fn();
    const reveal = vi.fn();
    selectAttachedRunEditorNode("write", select, false, reveal);
    expect(select).toHaveBeenCalledWith("write");
    expect(reveal).not.toHaveBeenCalled();
  });

  it("does not reveal the inspector when clearing selection", () => {
    const select = vi.fn();
    const reveal = vi.fn();
    selectAttachedRunEditorNode(null, select, true, reveal);
    expect(select).toHaveBeenCalledWith(null);
    expect(reveal).not.toHaveBeenCalled();
  });
});
