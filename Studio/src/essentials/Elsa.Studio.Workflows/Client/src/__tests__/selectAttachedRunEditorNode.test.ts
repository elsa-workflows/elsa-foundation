import { afterEach, describe, expect, it, vi } from "vitest";
import {
  selectAttachedRunEditorNode,
  selectAttachedRunEditorNodeOnCanvasPointerDown,
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
    node.className = "react-flow__node";
    node.setAttribute("role", "button");
    node.setAttribute("data-id", "write");
    const label = document.createElement("span");
    const control = document.createElement("button");
    const controlLabel = document.createElement("span");
    control.append(controlLabel);
    node.append(label, control);
    document.body.append(node);
    return { node, label, control, controlLabel };
  }

  it("accepts a press on a .react-flow__node with role=button as a node press", () => {
    const { node } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: node })).toBe(true);
  });

  it("accepts a primary-button press on the node label", () => {
    const { label } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: label })).toBe(true);
  });

  it("ignores a secondary-button press", () => {
    const { node, label } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 2, target: node })).toBe(false);
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 2, target: label })).toBe(false);
  });

  it("ignores a press that starts on an inner control", () => {
    const { control, controlLabel } = nodeWithInnerControl();
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: control })).toBe(false);
    expect(shouldSelectAttachedRunEditorNodeOnPointerDown({ button: 0, target: controlLabel })).toBe(false);
  });
});

describe("selectAttachedRunEditorNodeOnCanvasPointerDown", () => {
  afterEach(() => {
    document.body.replaceChildren();
  });

  function flowNode() {
    const node = document.createElement("div");
    node.className = "react-flow__node";
    node.setAttribute("role", "button");
    node.setAttribute("data-id", "write");
    document.body.append(node);
    return node;
  }

  it("selects from data-id when a run is attached", () => {
    const onSelect = vi.fn();
    expect(selectAttachedRunEditorNodeOnCanvasPointerDown({ button: 0, target: flowNode() }, true, onSelect)).toBe(true);
    expect(onSelect).toHaveBeenCalledWith("write");
  });

  it("does not select when no run is attached", () => {
    const onSelect = vi.fn();
    expect(selectAttachedRunEditorNodeOnCanvasPointerDown({ button: 0, target: flowNode() }, false, onSelect)).toBe(false);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("does not select on a secondary-button press", () => {
    const onSelect = vi.fn();
    expect(selectAttachedRunEditorNodeOnCanvasPointerDown({ button: 2, target: flowNode() }, true, onSelect)).toBe(false);
    expect(onSelect).not.toHaveBeenCalled();
  });
});
