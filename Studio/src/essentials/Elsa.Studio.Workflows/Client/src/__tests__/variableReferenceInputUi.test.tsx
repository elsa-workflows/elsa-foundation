import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { toActivityDescriptor } from "../workflow-editor/useWorkflowEditorData";
import type { ActivityCatalogItem, ActivityNode, VisibleVariableView, WorkflowDefinitionState } from "../workflowTypes";

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
  options: {
    activity?: ActivityNode;
    onChange?(activity: ActivityNode): void;
    variables?: VisibleVariableView[];
    readOnly?: boolean;
    scopeStatus?: "ready" | "loading" | "error" | "unavailable";
    editors?: StudioActivityPropertyEditorContribution[];
    workflowState?: WorkflowDefinitionState;
  } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <ActivityPropertiesPanel
      activity={options.activity ?? node()}
      descriptor={{ typeName: "Counter", inputs: [], outputs: [], ports: [], ...descriptor }}
      readOnly={options.readOnly}
      workflowState={options.workflowState}
      editors={options.editors ?? []}
      expressionEditors={[]}
      expressionDescriptors={expressionDescriptors}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      visibleVariables={options.variables ?? visibleVariables}
      scopeStatus={options.scopeStatus ?? "ready"}
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

  it("writes the Variable binding with the container node id and the declared name", () => {
    const onChange = vi.fn();
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] }, { onChange });

    select(container, "sequence-1::total-key");

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].Target).toEqual({
      typeName: "Int32",
      expression: { type: "Variable", value: { referenceKey: "total-key", declaringScopeId: "sequence-1", name: "Total" } }
    });
  });

  it("writes declaringScopeId 'workflow' and the name for a workflow-scope variable", () => {
    const onChange = vi.fn();
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] }, { onChange });

    select(container, "workflow::counter-key");

    expect(onChange.mock.calls[0][0].Target.expression).toEqual({
      type: "Variable",
      value: { referenceKey: "counter-key", declaringScopeId: "workflow", name: "Counter" }
    });
  });

  it("clears the binding when the empty option is selected", () => {
    const onChange = vi.fn();
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      { activity: node({ Target: bound("counter-key", "workflow") }), onChange }
    );
    expect(container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!.value).toBe("workflow::counter-key");

    select(container, "");

    expect(onChange.mock.calls[0][0].Target).toEqual({ typeName: "Int32", expression: { type: "Variable", value: null } });
  });

  it("disables the select in a read-only panel", () => {
    const onChange = vi.fn();
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] }, { readOnly: true, onChange });

    expect(container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!.disabled).toBe(true);
    select(container, "workflow::counter-key");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("notes a stored binding of another syntax and replaces it with the picked variable", () => {
    const onChange = vi.fn();
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      { activity: node({ Target: { typeName: "Int32", expression: { type: "JavaScript", value: "getCounter()" } } }), onChange }
    );

    expect(container.querySelector('.wf-property-row [role="note"]')?.textContent)
      .toBe("Bound with JavaScript; this input accepts only a declared variable. Pick one to replace it.");
    const picker = container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!;
    expect(picker.value).toBe("");
    // The note is part of the select's accessible description, so a screen reader hears it with the select.
    const noteId = container.querySelector('.wf-property-row [role="note"]')!.id;
    expect(picker.getAttribute("aria-describedby")?.split(" ")).toContain(noteId);

    select(container, "workflow::counter-key");
    expect(onChange.mock.calls[0][0].Target.expression.type).toBe("Variable");
  });

  it("shows no syntax note for an unbound input or an empty value of another syntax", () => {
    const container = renderPanel(
      { inputs: [referenceInput("Source", "Read"), referenceInput("Target", "ReadWrite")] },
      { activity: node({ Target: { typeName: "Int32", expression: { type: "Literal", value: "" } } }) }
    );

    expect(container.querySelector('.wf-property-row [role="note"]')).toBeNull();
  });

  it("describes the select with its direction", () => {
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] });

    const element = container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!;
    const describedBy = element.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)?.textContent).toBe("reads and writes");
  });

  it("offers only variables whose type fits the input's value type", () => {
    const declare = (referenceKey: string, name: string, alias: string) =>
      ({ referenceKey, name, type: { alias, collectionKind: "Single" as const }, storageDriverType: null, default: null });
    const workflowState: WorkflowDefinitionState = {
      variables: [declare("counter-key", "Counter", "Int32"), declare("label-key", "Label", "String")],
      rootActivity: node()
    };
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      {
        workflowState,
        variables: [
          { referenceKey: "counter-key", name: "Counter", scopeId: "workflow", isWorkflowScope: true },
          { referenceKey: "label-key", name: "Label", scopeId: "workflow", isWorkflowScope: true }
        ]
      }
    );

    const offered = [...container.querySelectorAll<HTMLOptionElement>(".wf-variable-picker select option")]
      .map(option => option.value)
      .filter(value => value !== "");
    expect(offered).toEqual(["workflow::counter-key"]);
  });

  it("names a bound variable whose type no longer fits, instead of calling it out of scope", () => {
    const declare = (referenceKey: string, name: string, alias: string) =>
      ({ referenceKey, name, type: { alias, collectionKind: "Single" as const }, storageDriverType: null, default: null });
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      {
        activity: node({ Target: bound("counter-key", "workflow") }),
        workflowState: { variables: [declare("counter-key", "Counter", "String")], rootActivity: node() },
        variables: [{ referenceKey: "counter-key", name: "Counter", scopeId: "workflow", isWorkflowScope: true }]
      }
    );

    const picker = container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!;
    expect(picker.selectedOptions[0].textContent).toBe("Counter (type does not fit)");
    const note = [...container.querySelectorAll('.wf-property-row [role="note"]')].find(item => item.textContent?.includes("does not fit"))!;
    expect(note.textContent).toContain("Counter");
    expect(note.textContent).toContain("Pick a compatible variable.");
    expect(picker.getAttribute("aria-describedby")?.split(" ")).toContain(note.id);
  });

  it("names a deleted bound variable by its stored name in the picker", () => {
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      {
        activity: node({
          Target: { typeName: "Int32", expression: { type: "Variable", value: { referenceKey: "gone-key", declaringScopeId: "workflow", name: "Gone" } } }
        })
      }
    );

    expect(container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!.selectedOptions[0].textContent)
      .toBe("Gone (not visible from this scope)");
  });

  it("offers only the variables the scope analysis reports visible, not ones declared in a sibling scope", () => {
    const declare = (referenceKey: string, name: string) =>
      ({ referenceKey, name, type: { alias: "Int32", collectionKind: "Single" as const }, storageDriverType: null, default: null });
    const sequence = (nodeId: string, variables: unknown[], activities: ActivityNode[]): ActivityNode => ({
      nodeId,
      activityVersionId: "sequence-v1",
      inputs: [],
      outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { variables, activities } }
    });
    const target = node();
    const workflowState: WorkflowDefinitionState = {
      variables: [declare("counter-key", "Counter")],
      rootActivity: sequence("root", [], [
        sequence("sequence-1", [declare("total-key", "Total")], [target]),
        sequence("sibling", [declare("sibling-key", "Sibling")], [])
      ])
    };

    // Visibility is the server's scoped-variable analysis for the selected node (useScopedVariableAnalysis);
    // the panel renders exactly that set, never every declaration in the workflow state.
    const container = renderPanel({ inputs: [referenceInput("Target", "ReadWrite")] }, { activity: target, workflowState });

    const options = [...container.querySelectorAll(".wf-variable-picker option")].map(option => option.textContent);
    expect(options).toEqual(["Select a variable…", "Counter · workflow", "Total · container"]);
    expect(options.some(option => option?.includes("Sibling"))).toBe(false);
  });

  it("keeps reference inputs out of an editor contribution's bulk literal write and its input values", () => {
    const onChange = vi.fn();
    const seen: Array<Record<string, unknown> | undefined> = [];
    const editors: StudioActivityPropertyEditorContribution[] = [{
      id: "bulk",
      supportsValidationReporting: true,
      readsActivityContext: true,
      supports: descriptor => descriptor.name === "Amount",
      component: props => {
        seen.push(props.context.inputValues);
        return (
          <button type="button" aria-label="Write all" onClick={() => props.context.setLiteralInputs?.({ Amount: "5", Target: "oops" })}>
            write
          </button>
        );
      }
    }];
    const existing = bound("counter-key", "workflow");
    const container = renderPanel(
      {
        inputs: [
          { name: "Amount", displayName: "Amount", referenceKey: "Amount", typeName: "Int32", isWrapped: true },
          referenceInput("Target", "ReadWrite")
        ]
      },
      { activity: node({ Target: existing }), editors, onChange }
    );

    expect(seen.length).toBeGreaterThan(0);
    expect(seen.at(-1)).not.toHaveProperty("Target");
    flushSync(() => container.querySelector<HTMLButtonElement>('button[aria-label="Write all"]')!.click());

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].Amount.expression).toEqual({ type: "Literal", value: "5" });
    expect(onChange.mock.calls[0][0].Target).toEqual(existing);
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

  it("flags a bound variable that is no longer visible, by its stored name", () => {
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      {
        activity: node({
          Target: { typeName: "Int32", expression: { type: "Variable", value: { referenceKey: "gone-key", declaringScopeId: "workflow", name: "Gone" } } }
        })
      }
    );

    expect(container.querySelector(".wf-variable-writes")?.textContent).toBe("May write: Gone (not visible from this scope)");
  });

  it("does not flag a bound variable as out of scope while the scope analysis is loading", () => {
    const container = renderPanel(
      { inputs: [referenceInput("Target", "ReadWrite")] },
      {
        activity: node({
          Target: { typeName: "Int32", expression: { type: "Variable", value: { referenceKey: "counter-key", declaringScopeId: "workflow", name: "Counter" } } }
        }),
        variables: [],
        scopeStatus: "loading"
      }
    );

    expect(container.querySelector(".wf-variable-writes")?.textContent).toBe("May write: Counter");
  });

  it("disables the select for an input that is itself read-only", () => {
    const onChange = vi.fn();
    const container = renderPanel({ inputs: [{ ...referenceInput("Target", "ReadWrite"), isReadOnly: true }] }, { onChange });

    expect(container.querySelector<HTMLSelectElement>(".wf-variable-picker select")!.disabled).toBe(true);
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

  it("renders the marker in the panel for a catalog item that carries only the variable-access facet", () => {
    const descriptor = toActivityDescriptor(catalogItem({
      designFacets: [{ kind: "elsa.variable-access", schemaVersion: "1", payload: { accessesUndeclaredVariables: true } }]
    }));
    const container = renderPanel(descriptor);

    expect(container.querySelector(".wf-variable-writes")?.textContent).toBe("May write any visible variable");
  });
});
