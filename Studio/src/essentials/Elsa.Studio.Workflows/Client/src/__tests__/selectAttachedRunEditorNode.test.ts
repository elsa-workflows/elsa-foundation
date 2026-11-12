import { afterEach, describe, expect, it, vi } from "vitest";
import {
  selectAttachedRunEditorNode,
  shouldSelectAttachedRunEditorNodeOnPointerDown
} from "../workflow-editor/selectAttachedRunEditorNode";

describe("selectAttachedRunEditorNode", () => {
  it("reveals the run inspector when a node is selected while a run overlay is attached", () => {
    const select = vi.fn();
    const reveal = vi.fn();
    selectAttachedRunEditorNode("write", select, true, reveal);
    expect(select).toHaveBeenCalledWith("write");
    expect(reveal).toHaveBeenCalledOnce();
  });

  it("selects without revealing when a snapshot is pinned but no run overlay is attached", () => {
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

describe("shouldSelectAttachedRunEditorNodeOnPointerDown", () => {
  afterEach(() => {
    document.body.replaceChildren();
  });

  function nodeWithInnerControl() {
    const node = document.createElement("div");
    node.setAttribute("role", "button");
    const label = document.createElement("span");
    const control = document.createElement("button");
    const controlLabel = document.createElement("span");
    control.append(controlLabel);
    node.append(label, control);
    document.body.append(node);
    return { node, label, control, controlLabel };
  }

  it("accepts a primary-button press on the node", () => {
    const { node, label } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: node, currentTarget: node })).toBe(true);
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: label, currentTarget: node })).toBe(true);
  });

  it("ignores a secondary-button press", () => {
    const { node, label } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 2, target: node, currentTarget: node })).toBe(false);
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 2, target: label, currentTarget: node })).toBe(false);
  });

  it("ignores a press that starts on an inner control", () => {
    const { node, control, controlLabel } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: control, currentTarget: node })).toBe(false);
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: controlLabel, currentTarget: node })).toBe(false);
  });
});
