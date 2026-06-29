import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { VariablesEditor } from "../WorkflowPropertiesView";
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

function setSelectValue(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!;
  setter.call(select, value);
  flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
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

const descriptor = {
  typeName: "MyActivity",
  inputs: [{ name: "Source", typeName: "System.Object", isWrapped: true, defaultSyntax: "Variable" }],
  outputs: [],
  ports: []
} as never;

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
  it("lists only the variables visible from the activity's scope, grouped by scope", () => {
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
        onChange={vi.fn()}
      />
    );

    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;
    expect(select).not.toBeNull();
    expect(select.value).toBe("workflow::k1");
    const optionText = [...select.options].map(option => option.textContent);
    expect(optionText).toContain("Count · workflow");
    expect(optionText).toContain("Inner · container");
  });

  it("writes a structured VariableReference when retargeting to another scope", () => {
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

    setSelectValue(container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!, "node-c::k2");

    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as ActivityNode;
    const expression = (updated.source as { expression: { type: string; value: unknown } }).expression;
    expect(expression.type).toBe("Variable");
    expect(expression.value).toEqual({ referenceKey: "k2", declaringScopeId: "node-c" });
  });

  it("surfaces an out-of-scope existing reference as a repairable option (invalid-reference repair)", () => {
    const container = render(
      <ActivityPropertiesPanel
        activity={variableActivity({ referenceKey: "ghost", declaringScopeId: "removed-scope" })}
        descriptor={descriptor}
        editors={[]}
        expressionEditors={[]}
        expressionDescriptors={[]}
        descriptorStatus="ready"
        visibleVariables={visibleVariables}
        scopeStatus="ready"
        onChange={vi.fn()}
      />
    );

    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;
    expect([...select.options].map(option => option.textContent)).toContain("ghost (not visible from this scope)");
  });

  it("does not surface a leftover literal value as a phantom reference after switching to Variable syntax", () => {
    const container = render(
      <ActivityPropertiesPanel
        activity={variableActivity("some literal text")}
        descriptor={descriptor}
        editors={[]}
        expressionEditors={[]}
        expressionDescriptors={[]}
        descriptorStatus="ready"
        visibleVariables={visibleVariables}
        scopeStatus="ready"
        onChange={vi.fn()}
      />
    );

    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Variable reference']")!;
    expect(select.value).toBe("");
    expect([...select.options].some(option => option.textContent?.includes("not visible"))).toBe(false);
  });

  it("explains an unavailable analysis endpoint instead of showing an empty list", () => {
    const container = render(
      <ActivityPropertiesPanel
        activity={variableActivity({ referenceKey: "k1", declaringScopeId: "workflow" })}
        descriptor={descriptor}
        editors={[]}
        expressionEditors={[]}
        expressionDescriptors={[]}
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="unavailable"
        onChange={vi.fn()}
      />
    );

    expect(container.textContent).toContain("Variable scope information is unavailable");
  });
});

describe("variable declaration editor", () => {
  it("adds a canonical VariableDefinition", () => {
    const onChange = vi.fn();
    const container = render(<VariablesEditor items={[]} typeOptions={null} storageOptions={null} onChange={onChange} />);

    clickButton([...container.querySelectorAll("button")].find(button => button.textContent?.includes("Add variable"))! as HTMLButtonElement);

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next).toHaveLength(1);
    expect(next[0]).toMatchObject({ name: "Variable1", typeInformation: { typeName: "String" } });
    expect(next[0].referenceKey).toBeTypeOf("string");
  });

  it("edits a variable name without losing its reference key", () => {
    const onChange = vi.fn();
    const items = [{ referenceKey: "ref-1", name: "Old", typeInformation: { typeName: "String", namespace: "System", assemblyName: "", assemblyVersion: "" }, storageDriverType: null, default: null }];
    const container = render(<VariablesEditor items={items} typeOptions={null} storageOptions={null} onChange={onChange} />);

    setInputValue(container.querySelector<HTMLInputElement>("input[aria-label='Variable name']")!, "Renamed");

    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next[0]).toMatchObject({ referenceKey: "ref-1", name: "Renamed" });
  });

  it("removes a variable", () => {
    const onChange = vi.fn();
    const items = [{ referenceKey: "ref-1", name: "Old", typeInformation: { typeName: "String", namespace: "System", assemblyName: "", assemblyVersion: "" }, storageDriverType: null, default: null }];
    const container = render(<VariablesEditor items={items} typeOptions={null} storageOptions={null} onChange={onChange} />);

    clickButton(container.querySelector<HTMLButtonElement>("button[aria-label^='Remove variable']")!);

    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("shows a non-blocking shadowing advisory for a flagged declaration", () => {
    const items = [{ referenceKey: "ref-1", name: "Count", typeInformation: { typeName: "String", namespace: "System", assemblyName: "", assemblyVersion: "" }, storageDriverType: null, default: null }];
    const container = render(
      <VariablesEditor
        items={items}
        typeOptions={null}
        storageOptions={null}
        warnings={new Map([["ref-1", 'Shadows "Count" declared in an outer scope.']])}
        onChange={vi.fn()}
      />
    );

    expect(container.textContent).toContain('Shadows "Count" declared in an outer scope.');
  });
});
