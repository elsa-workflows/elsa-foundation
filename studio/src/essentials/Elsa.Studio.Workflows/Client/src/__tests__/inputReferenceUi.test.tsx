import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioEndpointContext,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { canonicalizeStateForWire } from "../activityInputWire";
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
  it("lets any activity select workflow inputs for a nullable JSON input", () => {
    const { container } = renderPicker({ typeName: "System.Text.Json.JsonElement?" });
    const options = [...container.querySelectorAll<HTMLOptionElement>("optgroup option")];
    expect(options.length).toBeGreaterThan(0);
    expect(options.every(option => !option.disabled)).toBe(true);
  });

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

  it("derives Inputs from the live draft without exposing a transport Retry", () => {
    const { container, onChange } = renderPicker({ workflowInputs: [] });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")!;

    expect(select.disabled).toBe(false);
    expect(select.value).toBe("count");
    expect(container.textContent).toContain("No workflow inputs are available");
    expect([...container.querySelectorAll("button")].some(button => button.textContent === "Retry")).toBe(false);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("keeps production Input authoring on the current draft and has no definition-reload control", () => {
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
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        onRetryExpressionDescriptors={() => undefined}
        scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "unavailable" }}
        activeTabId="inputs"
        onActiveTabChange={() => undefined}
        onSelectedActivityChange={onChange}
        onEnterSlot={() => undefined}
        onReplaceSlotActivity={() => undefined}
      />
    );

    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")?.disabled).toBe(false);
    expect(container.textContent).toContain("Count");
    expect([...container.querySelectorAll("button")].some(button => button.textContent === "Retry")).toBe(false);
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("Activity Property Panel reference clears", () => {
  it("unbinds a cleared Connection reference on the save wire and supports repicking without losing other inputs", () => {
    const { container, getActivity } = renderPicker({
      input: {
        name: "Connection",
        displayName: "Connection",
        referenceKey: "Connection",
        typeName: "ConnectionReference",
        uiHint: "connection-picker",
        connectionType: "http",
        isWrapped: true,
        defaultSyntax: "Connection",
        isRequired: false,
        tier: "essential"
      },
      syntax: "Connection",
      value: null,
      expressionEditors: [nullableReferencePickerContribution],
      expressionDescriptors: [{ type: "Connection", displayName: "Connection", editingMode: "reference" }],
      otherInputs: {
        untouched: { typeName: "System.String", expression: { type: "Literal", value: "keep me" } }
      }
    });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Connection reference']")!;

    select.value = "existing.connection";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));

    let wire = canonicalizeStateForWire({ rootActivity: getActivity() }).rootActivity!;
    expect(wire.inputs).toContainEqual({
      referenceKey: "Connection",
      value: { value: { connection: "existing.connection", type: "http" }, expressionType: "Connection" }
    });

    select.value = "";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));

    expect(getActivity()).not.toHaveProperty("Connection");
    wire = canonicalizeStateForWire({ rootActivity: getActivity() }).rootActivity!;
    expect(wire.inputs).toEqual([{
      referenceKey: "untouched",
      value: { value: "keep me", expressionType: "Literal" }
    }]);

    const repickSelect = container.querySelector<HTMLSelectElement>("select[aria-label='Connection reference']")!;
    repickSelect.value = "existing.connection";
    flushSync(() => repickSelect.dispatchEvent(new Event("change", { bubbles: true })));

    wire = canonicalizeStateForWire({ rootActivity: getActivity() }).rootActivity!;
    expect(wire.inputs).toContainEqual({
      referenceKey: "Connection",
      value: { value: { connection: "existing.connection", type: "http" }, expressionType: "Connection" }
    });
    expect(wire.inputs).toContainEqual({
      referenceKey: "untouched",
      value: { value: "keep me", expressionType: "Literal" }
    });
  });

  it("keeps an explicit null payload for a non-Connection reference syntax", () => {
    const { container, getActivity } = renderPicker({
      input: {
        name: "Input",
        displayName: "Input",
        referenceKey: "workflowInput",
        typeName: "System.String",
        isWrapped: true
      },
      syntax: "Input",
      value: { referenceKey: "count" },
      expressionEditors: [nullableReferencePickerContribution]
    });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']")!;

    select.value = "";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));

    expect(getActivity().workflowInput).toEqual({
      typeName: "System.String",
      expression: { type: "Input", value: null }
    });
    expect(canonicalizeStateForWire({ rootActivity: getActivity() }).rootActivity!.inputs).toEqual([{
      referenceKey: "workflowInput",
      value: { value: null, expressionType: "Input" }
    }]);
  });
});

function renderPicker(options: {
  syntax?: string;
  value?: unknown;
  typeName?: string;
  workflowInputs?: WorkflowInput[];
  input?: StudioActivityInputDescriptor;
  expressionEditors?: StudioExpressionEditorContribution[];
  expressionDescriptors?: { type: string; displayName: string; editingMode: "literal" | "reference" }[];
  otherInputs?: Record<string, unknown>;
} = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onChange = vi.fn();
  const input = options.input ?? {
    ...descriptor.inputs[0],
    ...(options.typeName ? { typeName: options.typeName } : {})
  };
  const propertyName = input.referenceKey ?? "source";
  const initial: ActivityNode = {
    nodeId: "n1",
    activityVersionId: "v1",
    inputs: [],
    outputs: [],
    structure: null,
    ...options.otherInputs,
    [propertyName]: {
      typeName: input.typeName,
      expression: {
        type: options.syntax ?? "Input",
        value: options.value === undefined ? { referenceKey: "count" } : options.value
      }
    }
  };
  let currentActivity = initial;

  function Harness() {
    const [current, setCurrent] = React.useState(initial);
    return (
      <ActivityPropertiesPanel
        workflowState={{ inputs: options.workflowInputs ?? inputs }}
        activity={current}
        descriptor={{ ...descriptor, inputs: [input] }}
        editors={[]}
        expressionEditors={options.expressionEditors ?? [inputReferenceContribution]}
        expressionDescriptors={options.expressionDescriptors ?? [
          { type: "Literal", displayName: "Literal", editingMode: "literal" },
          { type: "Input", displayName: "Input", editingMode: "reference" }
        ]}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        onChange={next => {
          onChange(next);
          currentActivity = next;
          setCurrent(next);
        }}
      />
    );
  }

  flushSync(() => root.render(<Harness />));
  active = { root, container };
  return { container, onChange, getActivity: () => currentActivity };
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
    storageDriverType: null,
    isNullable: false
  };
}

const nullableReferencePickerContribution: StudioExpressionEditorContribution = {
  id: "test.nullable-reference-picker",
  order: 1,
  supports: context => context.syntax === "Connection" || context.syntax === "Input",
  surfaces: {
    inline: ({ syntax, value, onChange }: StudioExpressionEditorProps) => {
      const isConnection = syntax === "Connection";
      const field = isConnection ? "connection" : "referenceKey";
      const current = value && typeof value === "object" && !Array.isArray(value)
        ? (value as Record<string, unknown>)[field]
        : "";
      const option = isConnection ? "existing.connection" : "count";
      return (
        <select
          aria-label={isConnection ? "Connection reference" : "Input reference"}
          value={typeof current === "string" ? current : ""}
          onChange={event => {
            const next = event.target.value
              ? isConnection
                ? { connection: event.target.value, type: "http" }
                : { referenceKey: event.target.value }
              : null;
            onChange(next);
          }}
        >
          <option value="">Select reference</option>
          <option value={option}>{option}</option>
        </select>
      );
    }
  },
  createDefaultValue: () => null
};
