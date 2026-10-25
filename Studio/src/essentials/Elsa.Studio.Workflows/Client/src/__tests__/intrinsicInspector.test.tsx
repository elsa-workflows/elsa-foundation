import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { IntrinsicInspector } from "../IntrinsicInspector";
import { createActivityNode } from "../workflowAdapter";
import { readIntrinsicDescriptor } from "../intrinsicActivities";
import type { ActivityNode, VisibleVariableView } from "../workflowTypes";
import { setOutputActivity, setVariableActivity } from "./fixtures";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

const literalDescriptor: StudioExpressionDescriptor = { type: "Literal", displayName: "Literal", editingMode: "literal" };

const visibleVariables: VisibleVariableView[] = [
  { referenceKey: "counter", name: "Counter", scopeId: "workflow", isWorkflowScope: true },
  { referenceKey: "row", name: "Row", scopeId: "container-1", isWorkflowScope: false }
];

function descriptorFor(activity: typeof setVariableActivity): StudioActivityDescriptor {
  return {
    typeName: activity.activityTypeKey,
    name: activity.activityTypeKey.split(".").at(-1) ?? activity.activityTypeKey,
    displayName: activity.displayName,
    inputs: activity.inputs as StudioActivityDescriptor["inputs"],
    outputs: [],
    ports: []
  };
}

function inspector(activity: typeof setVariableActivity, node: ActivityNode, onChange = vi.fn(), variables = visibleVariables) {
  return (
    <IntrinsicInspector
      intrinsic={readIntrinsicDescriptor(activity)!}
      activity={node}
      descriptor={descriptorFor(activity)}
      editors={[]}
      expressionEditors={[]}
      expressionDescriptors={[literalDescriptor]}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      visibleVariables={variables}
      scopeStatus="ready"
      onChange={onChange}
    />
  );
}

function renderInspector(activity: typeof setVariableActivity, node: ActivityNode, onChange = vi.fn()) {
  const container = render(
    inspector(activity, node, onChange)
  );
  return { container, onChange };
}

describe("IntrinsicInspector", () => {
  it("renders the selected variable's current type and updates it after declaration or target changes", () => {
    const node: ActivityNode = {
      ...createActivityNode(setVariableActivity, "set-typed"),
      intrinsic: { kind: "Set", valueType: { alias: "Elsa.Any", collectionKind: "Single" }, variable: { referenceKey: "counter", declaringScopeId: "workflow" } }
    };
    const variables = visibleVariables.map(variable => ({ ...variable, type: { alias: "Int32", collectionKind: "Single" as const } }));
    const container = render(inspector(setVariableActivity, node, vi.fn(), variables));
    const valueType = () => container.querySelector(".wf-property-row-header-meta span")?.textContent;
    expect(valueType()).toBe("Int32");

    const changed = variables.map(variable => ({ ...variable, type: { alias: variable.scopeId === "workflow" ? "Boolean" : "String", collectionKind: "Single" as const } }));
    flushSync(() => active!.root.render(inspector(setVariableActivity, node, vi.fn(), changed)));
    expect(valueType()).toBe("Boolean");

    const retargeted: ActivityNode = { ...node, intrinsic: { ...node.intrinsic!, variable: { referenceKey: "row", declaringScopeId: "container-1" } } };
    flushSync(() => active!.root.render(inspector(setVariableActivity, retargeted, vi.fn(), changed)));
    expect(valueType()).toBe("String");
  });

  it("matches the declaration by scope when keys and names are shadowed", () => {
    const node: ActivityNode = {
      ...createActivityNode(setVariableActivity, "set-scoped"),
      intrinsic: { kind: "Set", valueType: { alias: "Elsa.Any", collectionKind: "Single" }, variable: { referenceKey: "counter", declaringScopeId: "container-1" } }
    };
    const variables = [
      { ...visibleVariables[0], type: { alias: "Int32", collectionKind: "Single" as const } },
      { ...visibleVariables[0], scopeId: "container-1", isWorkflowScope: false, type: { alias: "Boolean", collectionKind: "Single" as const } }
    ];
    const container = render(inspector(setVariableActivity, node, vi.fn(), variables));

    expect(container.querySelector(".wf-property-row-header-meta span")?.textContent).toBe("Boolean");
  });

  it("renders a variable-target picker and hides the variable pseudo-input from the properties panel", () => {
    const node = createActivityNode(setVariableActivity, "set-1");
    const { container } = renderInspector(setVariableActivity, node);

    const picker = container.querySelector<HTMLSelectElement>(".wf-intrinsic-variable select");
    expect(picker).not.toBeNull();
    expect([...picker!.options].map(option => option.textContent)).toEqual([
      "Select a variable…",
      "Counter · workflow",
      "Row · container"
    ]);

    // The value input renders in the properties panel; the "variable" pseudo-input does NOT (it is
    // authored by the picker instead), so "Variable" appears exactly once — as the picker label.
    const labels = [...container.querySelectorAll("label")].map(label => label.textContent);
    expect(labels.filter(label => label === "Variable")).toHaveLength(1);
    expect(labels).toContain("Value");
  });

  it("writes the selected variable onto the intrinsic block", () => {
    const node = createActivityNode(setVariableActivity, "set-1");
    const { container, onChange } = renderInspector(setVariableActivity, node);

    const picker = container.querySelector<HTMLSelectElement>(".wf-intrinsic-variable select")!;
    picker.value = "container-1::row";
    flushSync(() => picker.dispatchEvent(new Event("change", { bubbles: true })));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      nodeId: "set-1",
      intrinsic: expect.objectContaining({
        kind: "Set",
        variable: { referenceKey: "row", declaringScopeId: "container-1" }
      })
    }));
  });

  it("reflects the currently authored variable target in the picker", () => {
    const node: ActivityNode = {
      ...createActivityNode(setVariableActivity, "set-1"),
      intrinsic: {
        kind: "Set",
        valueType: { alias: "Elsa.Any", collectionKind: "Single" },
        variable: { referenceKey: "counter", declaringScopeId: "workflow" }
      }
    };
    const { container } = renderInspector(setVariableActivity, node);
    expect(container.querySelector<HTMLSelectElement>(".wf-intrinsic-variable select")!.value).toBe("workflow::counter");
  });

  it("shows no variable picker for Set Output and renders its name + value inputs", () => {
    const node = createActivityNode(setOutputActivity, "out-1");
    const { container } = renderInspector(setOutputActivity, node);

    expect(container.querySelector(".wf-intrinsic-variable")).toBeNull();
    const labels = [...container.querySelectorAll("label")].map(label => label.textContent);
    expect(labels).toContain("Output Name");
    expect(labels).toContain("Value");
  });
});
