import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { InspectorPanel } from "../workflow-editor/InspectorPanel";
import type { ActivityNode, VisibleVariableView } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
  vi.unstubAllGlobals();
});

function stubResizeObserver() {
  vi.stubGlobal("ResizeObserver", class {
    observe() {}
    disconnect() {}
  });
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function change(el: HTMLSelectElement, value: string) {
  el.value = value;
  flushSync(() => el.dispatchEvent(new Event("change", { bubbles: true })));
}

function click(el: Element) {
  flushSync(() => el.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

const readLineNode: ActivityNode = {
  nodeId: "read-1",
  activityVersionId: "activity-read-line-v1",
  inputs: [],
  outputs: []
};

const visibleVariables: VisibleVariableView[] = [
  { referenceKey: "line", name: "Line", scopeId: "workflow", isWorkflowScope: true },
  { referenceKey: "row", name: "Row", scopeId: "container-1", isWorkflowScope: false }
];

function descriptor(overrides: Partial<StudioActivityDescriptor> = {}): StudioActivityDescriptor {
  return {
    typeName: "Elsa.Activities.Primitives.Activities.ReadLine",
    inputs: [],
    outputs: [
      { name: "Result", typeName: "System.String", description: "The line that was read." },
      { name: "Hidden", typeName: "System.Int32", isBrowsable: false }
    ],
    ports: [],
    ...overrides
  };
}

function panel(node: ActivityNode, onChange: ReturnType<typeof vi.fn>, overrides: Partial<React.ComponentProps<typeof InspectorPanel>> = {}) {
  return (
    <InspectorPanel
      context={{} as StudioEndpointContext}
      selectedNode={node}
      selectedNodeLabel="Read Line"
      selectedActivityType="Elsa.Activities.Primitives.Activities.ReadLine"
      selectedDescriptor={descriptor()}
      selectedNodeAvailability={null}
      selectedSlots={[]}
      catalog={[]}
      catalogByVersion={new Map()}
      selectedSupportsScopedVariables={false}
      propertyEditors={[]}
      expressionEditors={[]}
      expressionDescriptors={[]}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      onRetryExpressionDescriptors={() => undefined}
      scopedVariableAnalysis={{ visibleVariables, shadowingWarnings: [], status: "ready" }}
      onSelectedActivityChange={onChange}
      onEnterSlot={vi.fn()}
      onReplaceSlotActivity={vi.fn()}
      {...overrides}
    />
  );
}

function outputSelect(container: HTMLElement) {
  return container.querySelector<HTMLSelectElement>(".wf-outputs .wf-output-row .wf-variable-picker select")!;
}

describe("ActivityOutputsPanel capture editor", () => {
  it("renders a workflow-scope-only capture picker for each browsable output", () => {
    const container = render(panel(readLineNode, vi.fn()));

    expect(container.querySelectorAll(".wf-outputs .wf-output-row").length).toBe(1);
    expect(container.textContent).toContain("Result");
    expect(container.textContent).not.toContain("Hidden");

    // v1 offers workflow-scope variables only — the container-scoped "Row" is filtered out.
    const options = [...outputSelect(container).options].map(option => option.textContent);
    expect(options).toEqual(["Select a variable…", "Line · workflow"]);
  });

  it("writes a workflow-scope capture onto the node when a variable is selected", () => {
    const onChange = vi.fn();
    const container = render(panel(readLineNode, onChange));

    change(outputSelect(container), "workflow::line");

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      nodeId: "read-1",
      Result: { target: { referenceKey: "line", declaringScopeId: "workflow" } }
    }));
  });

  it("reflects an authored capture in the picker and clears it on empty selection", () => {
    const onChange = vi.fn();
    const node: ActivityNode = { ...readLineNode, Result: { target: { referenceKey: "line", declaringScopeId: "workflow" } } };
    const container = render(panel(node, onChange));

    expect(outputSelect(container).value).toBe("workflow::line");

    change(outputSelect(container), "");
    const written = onChange.mock.calls[0][0] as ActivityNode;
    expect("Result" in written).toBe(false);
  });

  it("hides the conversion control until a variable is captured", () => {
    const container = render(panel(readLineNode, vi.fn()));
    expect(container.querySelector(".wf-output-row .wf-conversion-toggle")).toBeNull();
  });

  it("shows the conversion control once a variable is captured and writes the chosen mode", async () => {
    stubResizeObserver();
    const onChange = vi.fn();
    const node: ActivityNode = { ...readLineNode, Result: { target: { referenceKey: "line", declaringScopeId: "workflow" } } };
    const container = render(panel(node, onChange));

    const toggle = container.querySelector<HTMLButtonElement>(".wf-output-row .wf-conversion-toggle")!;
    expect(toggle).toBeTruthy();
    click(toggle);

    const trigger = container.querySelector<HTMLButtonElement>(".wf-output-row .wf-conversion-control .wf-syntax-picker-trigger")!;
    expect(trigger).toBeTruthy();
    flushSync(() => trigger.click());
    await nextFrame();

    // The mode listbox portals to document.body.
    const jsonOption = [...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent?.startsWith("JSON"))!;
    expect(jsonOption).toBeDefined();
    flushSync(() => jsonOption.click());

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      Result: { target: { referenceKey: "line", declaringScopeId: "workflow" }, conversion: { mode: "json" } }
    }));
  });

  it("renders no capture picker when the activity exposes no outputs", () => {
    const container = render(panel(readLineNode, vi.fn(), { selectedDescriptor: descriptor({ outputs: [] }) }));
    expect(container.querySelector(".wf-outputs")).toBeNull();
  });
});
