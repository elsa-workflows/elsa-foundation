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
