import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioActivityInputDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { toActivityDescriptor } from "../workflow-editor/useWorkflowEditorData";
import type { ActivityCatalogItem, ActivityNode, VisibleVariableView } from "../workflowTypes";

// Spec 1231 FR-036: variable-reference inputs bind a declared variable in scope; the panel shows the node's
// may-write set and the marker for an activity that may write any visible variable.

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  const mounted = active;
  flushSync(() => mounted.root.unmount());
  mounted.container.remove();
  active = null;
});

const expressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" }
];

const visibleVariables: VisibleVariableView[] = [
  { referenceKey: "counter-key", name: "Counter", scopeId: "workflow", isWorkflowScope: true },
  { referenceKey: "total-key", name: "Total", scopeId: "sequence-1", isWorkflowScope: false }
];

function referenceInput(name: string, variableAccess: "Read" | "ReadWrite"): StudioActivityInputDescriptor {
  return {
    name,
    displayName: name,
    referenceKey: name,
    typeName: "Int32",
    isWrapped: true,
    isRequired: true,
    uiHint: "variable-picker",
    defaultSyntax: "Variable",
    variableAccess
  };
}

function bound(referenceKey: string, declaringScopeId: string) {
  return { typeName: "Int32", expression: { type: "Variable", value: { referenceKey, declaringScopeId } } };
}

function node(overrides: Record<string, unknown> = {}): ActivityNode {
  return { nodeId: "node-1", activityVersionId: "activity-1", inputs: [], outputs: [], ...overrides };
}

function renderPanel(
  descriptor: Partial<StudioActivityDescriptor>,
  options: { activity?: ActivityNode; onChange?(activity: ActivityNode): void; variables?: VisibleVariableView[] } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <ActivityPropertiesPanel
      activity={options.activity ?? node()}
      descriptor={{ typeName: "Counter", inputs: [], outputs: [], ports: [], ...descriptor }}
      editors={[]}
      expressionEditors={[]}
      expressionDescriptors={expressionDescriptors}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      visibleVariables={options.variables ?? visibleVariables}
      scopeStatus="ready"
      onChange={options.onChange ?? (() => undefined)}
    />
  ));
  active = { root, container };
  return container;
}

function select(container: HTMLElement, option: string) {
  const element = container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!;
  element.value = option;
  flushSync(() => element.dispatchEvent(new Event("change", { bubbles: true })));
}

describe("variable-reference inputs", () => {
  it("offers only the declared variables visible at the node, with no other syntax", () => {
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] });

    const options = [...container.querySelectorAll(".wf-variable-picker option")].map(option => option.textContent);
    expect(options).toEqual(["Select a variable…", "Counter · workflow", "Total · container"]);
    expect(container.querySelector('[aria-label$="expression syntax"]')).toBeNull();
    expect(container.querySelector("textarea, input[type='text']")).toBeNull();
  });

  it("writes the Variable binding for the selected variable", () => {
    const onChange = vi.fn();
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] }, { onChange });

    select(container, "sequence-1::total-key");

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].Target).toEqual({
      typeName: "Int32",
      expression: { type: "Variable", value: { referenceKey: "total-key", declaringScopeId: "sequence-1" } }
    });
  });

  it("shows the value type and labels the direction", () => {
    const container = renderPanel({ inputs: [referenceInput("Source", "Read"), referenceInput("Target", "ReadWrite")] });

    const metas = [...container.querySelectorAll(".wf-property-row-header-meta")].map(meta => meta.textContent);
    expect(metas).toEqual(["Int32reads", "Int32reads and writes"]);
  });
});

describe("may-write set", () => {
  it("lists the variables the bound ReadWrite references name, not the Read ones", () => {
    const container = renderPanel(
      { inputs: [referenceInput("Source", "Read"), referenceInput("Target", "ReadWrite")] },
      { activity: node({ Source: bound("counter-key", "workflow"), Target: bound("total-key", "sequence-1") }) }
    );

    expect(container.querySelector(".wf-variable-writes")?.textContent).toBe("May write: Total");
  });

  it("shows nothing when no ReadWrite reference is bound", () => {
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] });

    expect(container.querySelector(".wf-variable-writes")).toBeNull();
  });

  it("marks an activity that accesses undeclared variables, even without inputs", () => {
    const container = renderPanel({ accessesUndeclaredVariables: true });

    expect(container.querySelector(".wf-variable-writes")?.textContent).toBe("May write any visible variable");
  });
});

describe("catalog descriptor", () => {
  const catalogItem = (overrides: Partial<ActivityCatalogItem>): ActivityCatalogItem => ({
    activityVersionId: "v1",
    activityTypeKey: "Acme.Counter",
    version: "1.0.0",
    category: "Test",
    displayName: "Counter",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ...overrides
  });

  it("carries the marker from the descriptor flag or the variable-access design facet", () => {
    expect(toActivityDescriptor(catalogItem({ accessesUndeclaredVariables: true })).accessesUndeclaredVariables).toBe(true);
    expect(toActivityDescriptor(catalogItem({
      designFacets: [{ kind: "elsa.variable-access", schemaVersion: "1", payload: { accessesUndeclaredVariables: true } }]
    })).accessesUndeclaredVariables).toBe(true);
    expect(toActivityDescriptor(catalogItem({})).accessesUndeclaredVariables).toBeUndefined();
  });
});
