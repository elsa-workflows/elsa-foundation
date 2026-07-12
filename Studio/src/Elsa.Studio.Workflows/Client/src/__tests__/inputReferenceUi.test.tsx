import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { inputReferenceContribution } from "../inputReferenceContribution";
import { InspectorPanel } from "../workflow-editor/InspectorPanel";
import type { ActivityNode, WorkflowInput } from "../workflowTypes";

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

const descriptor: StudioActivityDescriptor = {
  typeName: "MyActivity",
  inputs: [{ name: "Source", displayName: "Source", typeName: "System.Int32", isWrapped: true }],
  outputs: [],
  ports: []
};

const inputs: WorkflowInput[] = [
  workflowInput("count", "Count", "Int32"),
  workflowInput("message", "Message", "String"),
  workflowInput("sales-order", "Sales order", "Contoso.Sales.Order"),
  workflowInput("shipping-order", "Shipping order", "Fabrikam.Shipping.Order"),
  workflowInput("counts", "Counts", "Int32", "Array")
];

describe("workflow Input reference Contribution", () => {
  it("initializes an empty Input expression through the complete Activity Property Editor Slot", () => {
    const { container, onChange } = renderPicker({ syntax: "Literal", value: "" });

    openAndSelect("Input");

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      source: expect.objectContaining({ expression: { type: "Input", value: null } })
    }));
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")?.value).toBe("");
  });

  it("orders compatible Inputs before visible disabled incompatible Inputs with their types", () => {
    const { container } = renderPicker();
    const groups = [...container.querySelectorAll("optgroup")];

    expect(groups.map(group => group.label)).toEqual(["Compatible inputs", "Other inputs"]);
    expect(groups[0].textContent).toContain("Count");
    const incompatible = groups[1].querySelector<HTMLOptionElement>("option");
    expect(incompatible?.textContent).toContain("Message");
    expect(incompatible?.textContent).toContain("String");
    expect(incompatible?.disabled).toBe(true);
  });

  it("keeps fully-qualified aliases incompatible when their short names match", () => {
    const { container } = renderPicker({ typeName: "Contoso.Sales.Order" });
    const options = [...container.querySelectorAll<HTMLOptionElement>("optgroup option")];

    expect(options.find(option => option.textContent?.includes("Sales order"))?.disabled).toBe(false);
    expect(options.find(option => option.textContent?.includes("Shipping order"))?.disabled).toBe(true);
  });

  it("requires collection-valued properties to bind collection Inputs", () => {
    const { container } = renderPicker({
      typeName: "System.Collections.Generic.ICollection`1[System.Int32]"
    });
    const options = [...container.querySelectorAll<HTMLOptionElement>("optgroup option")];

    expect(options.find(option => option.textContent?.includes("Counts"))?.disabled).toBe(false);
    expect(options.find(option => option.textContent?.startsWith("Count ·"))?.disabled).toBe(true);
  });

  it("writes a structured stable-key reference when a compatible Input is selected", () => {
    const { container, onChange } = renderPicker({ value: null });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")!;

    select.value = "count";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      source: expect.objectContaining({ expression: { type: "Input", value: { referenceKey: "count" } } })
    }));
  });

  it("renders an existing missing reference without mutating it and allows repair", () => {
    const { container, onChange } = renderPicker({ value: { referenceKey: "removed" } });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")!;

    expect(select.value).toBe("removed");
    expect(select.selectedOptions[0].textContent).toContain("not available");
    expect(onChange).not.toHaveBeenCalled();

    select.value = "count";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      source: expect.objectContaining({ expression: { type: "Input", value: { referenceKey: "count" } } })
    }));
  });

  it("preserves the current reference and disables selection while Inputs load", () => {
    const { container, onChange } = renderPicker({ status: "loading", workflowInputs: [] });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")!;

    expect(select.disabled).toBe(true);
    expect(select.value).toBe("count");
    expect(container.textContent).toContain("Loading workflow inputs");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("preserves the current reference and retries a failed definition load without mutating the draft", () => {
    const retry = vi.fn();
    const { container, onChange } = renderPicker({ status: "error", workflowInputs: [], retry });

    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")?.value).toBe("count");
    expect(container.textContent).toContain("Workflow inputs could not be loaded");
    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry")?.click());
    expect(retry).toHaveBeenCalledTimes(1);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("wires definition load failure and Retry through the production Inspector into the Input Contribution", () => {
    const retry = vi.fn();
    const onChange = vi.fn();
    const container = render(
      <InspectorPanel
        context={{} as StudioEndpointContext}
        workflowState={{ inputs }}
        selectedNode={activity("Input", { referenceKey: "count" })}
        selectedNodeLabel="My activity"
        selectedActivityType="MyActivity"
        selectedDescriptor={descriptor}
        selectedNodeAvailability={null}
        selectedSlots={[]}
        catalog={[]}
        selectedSupportsScopedVariables={false}
        propertyEditors={[]}
        expressionEditors={[inputReferenceContribution]}
        expressionDescriptors={[{ type: "Input", displayName: "Input", editingMode: "reference" }]}
        descriptorStatus="ready"
        definitionStatus="error"
        onRetryDefinition={retry}
        scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "unavailable" }}
        onSelectedActivityChange={onChange}
        onEnterSlot={() => undefined}
        onReplaceSlotActivity={() => undefined}
      />
    );

    expect(container.textContent).toContain("Workflow inputs could not be loaded");
    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry")?.click());
    expect(retry).toHaveBeenCalledTimes(1);
    expect(onChange).not.toHaveBeenCalled();
  });
});

function renderPicker(options: {
  syntax?: string;
  value?: unknown;
  typeName?: string;
  status?: "loading" | "ready" | "error";
  workflowInputs?: WorkflowInput[];
  retry?: () => void;
} = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onChange = vi.fn();
  const initial = activity(options.syntax ?? "Input", options.value === undefined ? { referenceKey: "count" } : options.value);

  function Harness() {
    const [current, setCurrent] = React.useState(initial);
    return (
      <ActivityPropertiesPanel
        workflowState={{ inputs: options.workflowInputs ?? inputs }}
        activity={current}
        descriptor={options.typeName ? {
          ...descriptor,
          inputs: [{ ...descriptor.inputs[0], typeName: options.typeName }]
        } : descriptor}
        editors={[]}
        expressionEditors={[inputReferenceContribution]}
        expressionDescriptors={[
          { type: "Literal", displayName: "Literal", editingMode: "literal" },
          { type: "Input", displayName: "Input", editingMode: "reference" }
        ]}
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        inputStatus={options.status ?? "ready"}
        inputRetry={options.retry}
        onChange={next => {
          onChange(next);
          setCurrent(next);
        }}
      />
    );
  }

  flushSync(() => root.render(<Harness />));
  active = { root, container };
  return { container, onChange };
}

function openAndSelect(label: string) {
  flushSync(() => active?.container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
  const option = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent?.includes(label));
  flushSync(() => option?.click());
}

function activity(syntax: string, value: unknown): ActivityNode {
  return {
    nodeId: "n1",
    activityVersionId: "v1",
    inputs: [],
    outputs: [],
    source: { typeName: "System.Object", expression: { type: syntax, value } }
  };
}

function workflowInput(
  referenceKey: string,
  displayName: string,
  alias: string,
  collectionKind: WorkflowInput["type"]["collectionKind"] = "Single"
): WorkflowInput {
  return {
    referenceKey,
    name: displayName.replaceAll(" ", ""),
    displayName,
    type: { alias, collectionKind },
    description: "",
    category: "",
    uiHint: "singleline",
    storageDriverType: null
  };
}
