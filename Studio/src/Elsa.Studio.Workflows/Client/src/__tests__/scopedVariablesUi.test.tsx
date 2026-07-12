import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { VariablesEditor } from "../WorkflowPropertiesView";
import { variableReferenceContribution } from "../variableReferenceContribution";
import type { ActivityNode, VisibleVariableView } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
  setter.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function clickButton(button: HTMLButtonElement) {
  flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

const visibleVariables: VisibleVariableView[] = [
  { referenceKey: "k1", name: "Count", scopeId: "workflow", isWorkflowScope: true },
  { referenceKey: "k2", name: "Inner", scopeId: "node-c", isWorkflowScope: false }
];

const descriptor: StudioActivityDescriptor = {
  typeName: "MyActivity",
  inputs: [{ name: "Source", displayName: "Source", typeName: "System.Int32", isWrapped: true, defaultSyntax: "Variable" }],
  outputs: [],
  ports: []
};

function variableActivity(reference: unknown): ActivityNode {
  return {
    nodeId: "n1",
    activityVersionId: "v1",
    inputs: [],
    outputs: [],
    source: { typeName: "System.Object", expression: { type: "Variable", value: reference } }
  };
}

describe("variable picker", () => {
  function renderPicker(options: {
    reference?: unknown;
    variables?: VisibleVariableView[];
    typeName?: string;
    status?: "loading" | "ready" | "unavailable" | "error";
    retry?: () => void;
    onChange?: ReturnType<typeof vi.fn>;
  } = {}) {
    const onChange = options.onChange ?? vi.fn();
    const container = render(
      <ActivityPropertiesPanel
        workflowState={{
          variables: [
            { referenceKey: "k1", name: "Count", type: { alias: "Int32", collectionKind: "Single" }, storageDriverType: null, default: null },
            { referenceKey: "k3", name: "Message", type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, default: null },
            { referenceKey: "k4", name: "Sales order", type: { alias: "Contoso.Sales.Order", collectionKind: "Single" }, storageDriverType: null, default: null },
            { referenceKey: "k5", name: "Shipping order", type: { alias: "Fabrikam.Shipping.Order", collectionKind: "Single" }, storageDriverType: null, default: null }
          ],
          rootActivity: {
            nodeId: "node-c",
            activityVersionId: "container-v1",
            inputs: [],
            outputs: [],
            structure: {
              kind: "elsa.sequence.structure",
              schemaVersion: "1.0.0",
              payload: {
                variables: [
                  { referenceKey: "k2", name: "Inner", type: { alias: "Int32", collectionKind: "Single" }, storageDriverType: null, default: null }
                ],
                activities: []
              }
            }
          }
        }}
        activity={variableActivity(options.reference ?? { referenceKey: "k1", declaringScopeId: "workflow" })}
        descriptor={options.typeName ? {
          ...descriptor,
          inputs: [{ ...descriptor.inputs[0], typeName: options.typeName }]
        } : descriptor}
        editors={[]}
        expressionEditors={[variableReferenceContribution]}
        expressionDescriptors={[{ type: "Variable", displayName: "Variable", editingMode: "reference" }]}
        descriptorStatus="ready"
        visibleVariables={options.variables ?? [
          visibleVariables[0],
          { referenceKey: "k3", name: "Message", scopeId: "workflow", isWorkflowScope: true }
        ]}
        scopeStatus={options.status ?? "ready"}
        scopeRetry={options.retry}
        onChange={onChange}
      />
    );
    return { container, onChange };
  }

  it("uses the registered Contribution to rank compatible Variables before disabled incompatible Variables", () => {
    const { container } = renderPicker();
    const groups = [...container.querySelectorAll("optgroup")];
    expect(groups.map(group => group.label)).toEqual(["Compatible variables", "Other variables"]);
    expect(groups[0].textContent).toContain("Count");
    const incompatible = groups[1].querySelector("option");
    expect(incompatible?.textContent).toContain("Message");
    expect(incompatible?.textContent).toContain("String");
    expect(incompatible?.disabled).toBe(true);
  });

  it("keeps distinct fully-qualified aliases incompatible even when their short names match", () => {
    const { container } = renderPicker({
      typeName: "Contoso.Sales.Order",
      variables: [
        { referenceKey: "k4", name: "Sales order", scopeId: "workflow", isWorkflowScope: true },
        { referenceKey: "k5", name: "Shipping order", scopeId: "workflow", isWorkflowScope: true }
      ]
    });
    const options = [...container.querySelectorAll<HTMLOptionElement>("optgroup option")];

    expect(options.find(option => option.textContent?.includes("Sales order"))?.disabled).toBe(false);
    expect(options.find(option => option.textContent?.includes("Shipping order"))?.disabled).toBe(true);
  });

  it("writes a structured scope-aware reference when a compatible Variable is selected", () => {
    const variables = [
      { referenceKey: "k2", name: "Inner", scopeId: "node-c", isWorkflowScope: false },
      visibleVariables[0]
    ];
    const { container, onChange } = renderPicker({ reference: null, variables });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;

    select.value = "node-c::k2";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      source: expect.objectContaining({
        expression: { type: "Variable", value: { referenceKey: "k2", declaringScopeId: "node-c" } }
      })
    }));
  });

  it("preserves an unresolved reference as a selected repair option", () => {
    const { container, onChange } = renderPicker({
      reference: { referenceKey: "missing", declaringScopeId: "old-scope" }
    });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;

    expect(select.value).toBe("old-scope::missing");
    expect(select.selectedOptions[0].textContent).toContain("not visible from this scope");
    expect(select.selectedOptions[0].disabled).toBe(true);
    expect(select.disabled).toBe(false);
    expect(onChange).not.toHaveBeenCalled();

    select.value = "workflow::k1";
    flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      source: expect.objectContaining({
        expression: { type: "Variable", value: { referenceKey: "k1", declaringScopeId: "workflow" } }
      })
    }));
  });

  it("disables changes while loading without erasing the current reference", () => {
    const { container } = renderPicker({ status: "loading", variables: [] });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;

    expect(select.disabled).toBe(true);
    expect(select.value).toBe("workflow::k1");
    expect(container.textContent).toContain("Loading visible variables");
  });

  it("preserves the current reference and offers Retry after analysis fails", () => {
    const retry = vi.fn();
    const { container, onChange } = renderPicker({ status: "error", variables: [], retry });

    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")?.value).toBe("workflow::k1");
    expect(container.textContent).toContain("Variable scope information could not be loaded");
    clickButton([...container.querySelectorAll("button")].find(button => button.textContent === "Retry")!);
    expect(retry).toHaveBeenCalledTimes(1);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("preserves an existing Variable reference as read-only until its Contribution is available", () => {
    const onChange = vi.fn();
    const container = render(
      <ActivityPropertiesPanel
        activity={variableActivity({ referenceKey: "k1", declaringScopeId: "workflow" })}
        descriptor={descriptor}
        editors={[]}
        expressionEditors={[]}
        expressionDescriptors={[]}
        descriptorStatus="ready"
        visibleVariables={visibleVariables}
        scopeStatus="ready"
        onChange={onChange}
      />
    );

    expect(container.textContent).toContain("current value is preserved and read-only");
    expect(container.querySelector("select[aria-label='Variable reference']")).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("variable declaration editor", () => {
  it("adds a canonical VariableDefinition", () => {
    const onChange = vi.fn();
    const container = render(<VariablesEditor items={[]} typeOptions={null} storageOptions={null} editorForAlias={() => "text"} onChange={onChange} />);

    clickButton([...container.querySelectorAll("button")].find(button => button.textContent?.includes("Add variable"))! as HTMLButtonElement);

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next).toHaveLength(1);
    expect(next[0]).toMatchObject({ name: "Variable1", type: { alias: "String", collectionKind: "Single" } });
    expect(next[0].referenceKey).toBeTypeOf("string");
  });

  it("edits a variable name without losing its reference key", () => {
    const onChange = vi.fn();
    const items = [{ referenceKey: "ref-1", name: "Old", type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, default: null }];
    const container = render(<VariablesEditor items={items} typeOptions={null} storageOptions={null} editorForAlias={() => "text"} onChange={onChange} />);

    setInputValue(container.querySelector<HTMLInputElement>("input[aria-label='Variable name']")!, "Renamed");

    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next[0]).toMatchObject({ referenceKey: "ref-1", name: "Renamed" });
  });

  it("removes a variable", () => {
    const onChange = vi.fn();
    const items = [{ referenceKey: "ref-1", name: "Old", type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, default: null }];
    const container = render(<VariablesEditor items={items} typeOptions={null} storageOptions={null} editorForAlias={() => "text"} onChange={onChange} />);

    clickButton(container.querySelector<HTMLButtonElement>("button[aria-label^='Remove variable']")!);

    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("shows a non-blocking shadowing advisory for a flagged declaration", () => {
    const items = [{ referenceKey: "ref-1", name: "Count", type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, default: null }];
    const container = render(
      <VariablesEditor
        items={items}
        typeOptions={null}
        storageOptions={null} editorForAlias={() => "text"}
        warnings={new Map([["ref-1", 'Shadows "Count" declared in an outer scope.']])}
        onChange={vi.fn()}
      />
    );

    expect(container.textContent).toContain('Shadows "Count" declared in an outer scope.');
  });
});
