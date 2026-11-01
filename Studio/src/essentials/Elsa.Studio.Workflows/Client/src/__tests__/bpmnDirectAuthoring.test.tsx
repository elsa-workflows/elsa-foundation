import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { BpmnElementInspector } from "../bpmn/BpmnElementInspector";
import { directBindingProperty, directHumanBinding, directHumanMode, directHumanPatch } from "../bpmn/bpmnDirectBinding";
import { createBpmnShapeNode, updateBpmnElement, readBpmnElements } from "../bpmn/bpmnAdapter";
import { bpmnStructureKind, type BpmnElement } from "../bpmn/bpmnTypes";
import type { ActivityNode } from "../workflowTypes";

function inspect(element: BpmnElement, readOnly = false, directHumanAvailable = true) {
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container); const change = vi.fn();
  flushSync(() => root.render(<BpmnElementInspector element={element} outboundFlows={[]} onChangeElement={change}
    onChangeFlow={vi.fn()} onSetDefaultFlow={vi.fn()} targetLabelFor={flow => flow.targetRef} readOnly={readOnly} directHumanAvailable={directHumanAvailable} />));
  return { container, change, dispose: () => { flushSync(() => root.unmount()); container.remove(); } };
}

describe("Direct human BPMN authoring", () => {
  it("recognizes standard unbound user tasks as human without rewriting imported content", () => {
    const element: BpmnElement = { elementId: "review", elementType: "userTask", properties: { vendor: "keep" } };
    const before = JSON.stringify(element); expect(directHumanMode(element)).toBe("human"); expect(JSON.stringify(element)).toBe(before);
  });
  it("changes a task explicitly and preserves unrelated properties", () => {
    const element: BpmnElement = { elementId: "review", elementType: "task", properties: { vendor: "keep" } };
    const patch = directHumanPatch(element, "human");
    expect(patch).toEqual({ elementType: "userTask", properties: { vendor: "keep", [directBindingProperty]: directHumanBinding } });
    expect(element.properties).toEqual({ vendor: "keep" });
    expect(directHumanMode({ ...element, ...patch })).toBe("human");
    expect(directHumanPatch({ ...element, ...patch }, "unbound")).toEqual({ elementType: "task", properties: { vendor: "keep" } });
  });
  it.each(["not-json", '{"formatVersion":99,"kind":"human"}', '{"formatVersion":1,"kind":"human","claimRequired":true}', '{"formatVersion":1,"kind":"human","unknown":true}'])("keeps unsupported binding metadata unchanged: %s", raw => {
    const element: BpmnElement = { elementId: "review", elementType: "userTask", properties: { [directBindingProperty]: raw } };
    expect(directHumanMode(element)).toBe("unsupported"); expect(directHumanPatch(element, "human")).toEqual({});
    expect(element.properties![directBindingProperty]).toBe(raw);
  });
  it("never removes an existing native child binding", () => {
    const element: BpmnElement = { elementId: "review", elementType: "userTask", childNodeId: "native-child" };
    expect(directHumanMode(element)).toBe("native"); expect(directHumanPatch(element, "human")).toEqual({}); expect(element.childNodeId).toBe("native-child");
  });
  it.each([false, true])("renders the human choice with readOnly=%s and makes no implicit edit", readOnly => {
    const element: BpmnElement = { elementId: "review", elementType: "task", properties: { vendor: "keep" } }; const view = inspect(element, readOnly);
    try {
      const select = view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")!;
      expect(select).not.toBeNull(); expect(select.disabled).toBe(readOnly); expect(view.change).not.toHaveBeenCalled();
      flushSync(() => { select.value = "human"; select.dispatchEvent(new Event("change", { bubbles: true })); });
      if (readOnly) expect(view.change).not.toHaveBeenCalled();
      else expect(view.change).toHaveBeenCalledWith("review", { elementType: "userTask", properties: { vendor: "keep", [directBindingProperty]: directHumanBinding } });
    } finally { view.dispose(); }
  });
  it("retains unsupported binding information in a disabled inspector", () => {
    const view = inspect({ elementId: "review", elementType: "userTask", properties: { [directBindingProperty]: '{"formatVersion":99,"kind":"human"}' } });
    try { expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.disabled).toBe(true); expect(view.container.textContent).toContain("kept unchanged"); expect(view.change).not.toHaveBeenCalled(); }
    finally { view.dispose(); }
  });
  it("does not add direct controls to an ordinary native editor", () => {
    const view = inspect({ elementId: "review", elementType: "task", childNodeId: "native-child" }, false, false);
    try { expect(view.container.querySelector("[aria-label='Task execution']")).toBeNull(); expect(view.change).not.toHaveBeenCalled(); }
    finally { view.dispose(); }
  });
  it("stamps only an explicitly chosen human palette shape and applies it through immutable draft editing", () => {
    const node = createBpmnShapeNode({ key: "humanWait", label: "Human wait", name: "Human wait", elementType: "userTask", properties: { [directBindingProperty]: directHumanBinding } }, { x: 100, y: 120 });
    expect(node.data.element.properties).toEqual({ [directBindingProperty]: directHumanBinding });
    const original: ActivityNode = { nodeId: "root", activityVersionId: "bpmn", inputs: [], outputs: [], structure: { kind: bpmnStructureKind, schemaVersion: "1.0.0", payload: { activities: [], elements: [node.data.element], sequenceFlows: [] } } };
    const before = JSON.stringify(original); const edited = updateBpmnElement(original, node.id, { name: "Review order" });
    expect(readBpmnElements(edited)[0].properties).toEqual({ [directBindingProperty]: directHumanBinding });
    expect(readBpmnElements(edited)[0].name).toBe("Review order"); expect(JSON.stringify(original)).toBe(before);
    const old = createBpmnShapeNode({ key: "task", label: "Task", elementType: "task" }, { x: 0, y: 0 });
    expect(old.data.element.properties).toBeUndefined();
  });
});
