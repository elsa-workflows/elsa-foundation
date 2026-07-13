import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionDescriptor,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { createObjectExpressionEditorContribution } from "../objectExpressionEditor";
import type { ActivityNode } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

const backendExpressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Liquid", displayName: "Liquid", editingMode: "text" },
  { type: "Object", displayName: "Object", editingMode: "structured" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" },
  { type: "Input", displayName: "Input", editingMode: "reference" }
];

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

function renderPanel(
  inputs: StudioActivityInputDescriptor[],
  options: {
    customProperties?: Record<string, unknown>;
    activity?: ActivityNode;
    editors?: StudioActivityPropertyEditorContribution[];
    expressionEditors?: StudioExpressionEditorContribution[];
    expressionDescriptors?: StudioExpressionDescriptor[];
    expressionDescriptorStatus?: "loading" | "ready" | "failed";
    onRetryDescriptors?(): void;
    onChange?(activity: ActivityNode): void;
  } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const descriptor: StudioActivityDescriptor = {
    typeName: "TestActivity",
    inputs,
    outputs: [],
    ports: [],
    customProperties: options.customProperties
  };
  const initialActivity = options.activity ?? activity();

  function Harness() {
    const [currentActivity, setCurrentActivity] = React.useState(initialActivity);
    const handleChange = (nextActivity: ActivityNode) => {
      options.onChange?.(nextActivity);
      setCurrentActivity(nextActivity);
    };
    return (
      <ActivityPropertiesPanel
        activity={currentActivity}
        descriptor={descriptor}
        editors={options.editors ?? []}
        expressionEditors={options.expressionEditors ?? []}
        expressionDescriptors={options.expressionDescriptors ?? backendExpressionDescriptors}
        expressionDescriptorStatus={options.expressionDescriptorStatus ?? "ready"}
        onRetryDescriptors={options.onRetryDescriptors}
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        onChange={handleChange}
      />
    );
  }

  flushSync(() => root.render(<Harness />));
  active = { root, container };
  return container;
}

function activity(overrides: Record<string, unknown> = {}): ActivityNode {
  return {
    nodeId: "node-1",
    activityVersionId: "activity-1",
    inputs: [],
    outputs: [],
    ...overrides
  };
}

function input(name: string, overrides: Partial<StudioActivityInputDescriptor> = {}): StudioActivityInputDescriptor {
  return { name, displayName: name, typeName: "System.String", isWrapped: false, ...overrides };
}

function propertyLabels(container: HTMLElement) {
  return [...container.querySelectorAll(".wf-property-row-header label")].map(label => label.textContent);
}

describe("activity property organization", () => {
  it("does not invent expression types when the backend returns none", () => {
    const container = renderPanel([
      input("Message", { isWrapped: true, defaultSyntax: "Literal" })
    ], {
      expressionDescriptors: [],
      activity: activity({
        message: { typeName: "System.String", expression: { type: "Literal", value: "hello" } }
      })
    });

    expect(container.textContent).toContain("No expression types are available.");
    expect(container.textContent).toContain("No editor is available for Literal");
    expect(container.textContent).not.toContain("JavaScript");
    expect(container.textContent).not.toContain("Liquid");
  });

  it("reports loading and failed expression metadata without replacing a preserved backend snapshot", () => {
    const retry = vi.fn();
    const loading = renderPanel([input("Message", { isWrapped: true })], {
      expressionDescriptorStatus: "loading",
      expressionDescriptors: []
    });
    expect(loading.textContent).toContain("Loading expression types...");

    flushSync(() => active!.root.unmount());
    active!.container.remove();
    active = null;

    const failed = renderPanel([input("Message", { isWrapped: true })], {
      expressionDescriptorStatus: "failed",
      expressionDescriptors: [{ type: "Literal", displayName: "Literal", editingMode: "literal" }],
      onRetryDescriptors: retry
    });
    expect(failed.textContent).toContain("Expression types could not be refreshed. Using the last loaded metadata.");
    flushSync(() => failed.querySelector<HTMLButtonElement>("button.wf-expression-descriptors-retry")!.click());
    expect(retry).toHaveBeenCalledOnce();
  });

  it("orders properties by input order, then name", () => {
    const container = renderPanel([
      input("Last", { order: 20 }),
      input("Beta", { order: 10 }),
      input("Alpha", { order: 10 })
    ]);

    expect(propertyLabels(container)).toEqual(["Alpha", "Beta", "Last"]);
  });

  it("uses descriptor metadata to order and label category groups", () => {
    const container = renderPanel([
      input("RetryPolicy", { category: "Advanced" }),
      input("Path", { category: "Simple" })
    ], {
      customProperties: {
        propertyGroups: [
          { category: " ", name: "Advanced", label: " ", displayName: "Advanced settings", order: 20 },
          { category: "Simple", label: "Essentials", order: 10 }
        ]
      }
    });

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["Essentials", "Advanced settings"]);
    expect(propertyLabels(container)).toEqual(["Path", "RetryPolicy"]);
  });

  it("orders unconfigured groups by their first property order", () => {
    const container = renderPanel([
      input("RequestSizeLimit", { category: "Advanced", order: 110 }),
      input("Path", { category: "Simple", order: 10 }),
      input("Policy", { category: "Advanced", order: 100 })
    ]);

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["Simple", "Advanced"]);
    expect(propertyLabels(container)).toEqual(["Path", "Policy", "RequestSizeLimit"]);
  });

  it("keeps unconfigured and uncategorized properties visible in deterministic fallback groups", () => {
    const container = renderPanel([
      input("ZebraValue", { category: "Zebra", order: 200 }),
      input("GeneralValue", { category: null, order: 10 }),
      input("AdvancedValue", { category: "Advanced" })
    ], {
      customProperties: {
        propertyGroups: [{ category: "Advanced", label: "Advanced settings", order: 100 }]
      }
    });

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["General", "Advanced settings", "Zebra"]);
    expect(propertyLabels(container)).toEqual(["GeneralValue", "AdvancedValue", "ZebraValue"]);
  });

  it("places the syntax picker inline with the checkbox contribution", () => {
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input
          type="checkbox"
          checked={value === true}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
      )
    };
    const container = renderPanel([
      input("CanStartWorkflow", { typeName: "System.Boolean", isWrapped: true, defaultSyntax: "Literal" })
    ], {
      editors: [checkboxEditor],
      activity: activity({
        canStartWorkflow: { typeName: "System.Boolean", expression: { type: "Literal", value: false } }
      })
    });

    const expressionField = container.querySelector(".wf-expression-field");
    expect(expressionField?.classList.contains("wf-expression-field--toggle")).toBe(true);
    expect(expressionField?.querySelector("input[type='checkbox']")).not.toBeNull();
    expect(expressionField?.querySelector(".wf-syntax-picker.inline")).not.toBeNull();
    expect(container.querySelector(".wf-property-row > .wf-syntax-picker:not(.inline)")).toBeNull();
  });

  it("uses normal inline chrome for non-literal Boolean expressions", () => {
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input
          type="checkbox"
          checked={value === true}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
      )
    };
    const javaScriptEditor: StudioExpressionEditorContribution = {
      id: "javascript.inline",
      supports: context => context.syntax === "JavaScript",
      surfaces: { inline: ({ value }) => <textarea aria-label="JavaScript expression" value={String(value ?? "")} readOnly /> }
    };
    const container = renderPanel([
      input("JavaScriptValue", { typeName: "System.Boolean", isWrapped: true }),
      input("VariableValue", { typeName: "System.Boolean", isWrapped: true })
    ], {
      editors: [checkboxEditor],
      expressionEditors: [javaScriptEditor],
      activity: activity({
        javaScriptValue: { typeName: "System.Boolean", expression: { type: "JavaScript", value: "input.enabled" } },
        variableValue: { typeName: "System.Boolean", expression: { type: "Variable", value: null } }
      })
    });

    const fields = [...container.querySelectorAll(".wf-expression-field")];
    expect(fields).toHaveLength(2);
    expect(fields.every(field => !field.classList.contains("wf-expression-field--toggle"))).toBe(true);
    expect(fields[0]?.querySelector("textarea[aria-label='JavaScript expression']")).not.toBeNull();
    expect(fields[1]?.textContent).toContain("current value is preserved and read-only");
  });

  it("does not steal focus for pre-existing text expressions and still expands regardless of property type", () => {
    const container = renderPanel([
      input("Condition", { typeName: "System.Boolean", isWrapped: true }),
      input("Template", { isWrapped: true })
    ], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Python", displayName: "Python", editingMode: "text" }
      ],
      activity: activity({
        condition: { typeName: "System.Boolean", expression: { type: "Python", value: "value and" } },
        template: { typeName: "System.String", expression: { type: "Python", value: "message" } }
      })
    });

    const inlineEditors = [...container.querySelectorAll<HTMLInputElement>("input[aria-label$=' expression']")];
    expect(inlineEditors.map(editor => editor.value)).toEqual(["value and", "message"]);
    expect(inlineEditors).not.toContain(document.activeElement);
    expect(container.querySelector("input[type='checkbox']")).toBeNull();

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Condition editor']")?.click());
    const expanded = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Condition expanded value']");
    expect(expanded?.value).toBe("value and");
    expect(document.activeElement).toBe(expanded);
  });

  it("focuses the replacement textbox when selecting a text expression mode", () => {
    const container = renderPanel([
      input("Template", { isWrapped: true })
    ], {
      activity: activity({
        template: { typeName: "System.String", expression: { type: "Literal", value: "Hello" } }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const liquidOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent === "Liquid");
    flushSync(() => liquidOption?.click());

    const replacement = container.querySelector<HTMLInputElement>("input[aria-label='Template expression']");
    expect(replacement?.value).toBe("Hello");
    expect(document.activeElement).toBe(replacement);
  });

  it("preserves a primitive Literal as visible text when switching to a text expression", () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([
      input("Count", { typeName: "System.Int32", isWrapped: true })
    ], {
      onChange: next => changes.push(next),
      activity: activity({
        count: { typeName: "System.Int32", expression: { type: "Literal", value: 42 } }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const javaScriptOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent === "JavaScript");
    flushSync(() => javaScriptOption?.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.count).toEqual({
      typeName: "System.Int32",
      expression: { type: "JavaScript", value: "42" }
    });
  });

  it("prompts before replacing an incompatible value and mutates syntax and value atomically", async () => {
    const changes: ActivityNode[] = [];
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input type="checkbox" checked={value === true} disabled={disabled} onChange={event => onChange(event.target.checked)} />
      )
    };
    const container = renderPanel([
      input("Condition", { typeName: "System.Boolean", isWrapped: true })
    ], {
      editors: [checkboxEditor],
      onChange: next => changes.push(next),
      activity: activity({
        condition: { typeName: "System.Boolean", expression: { type: "JavaScript", value: "input.enabled" } }
      })
    });

    openAndSelect(container, "Literal");
    expect(changes).toHaveLength(0);
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Condition expression']")?.value).toBe("input.enabled");
    const confirmation = container.querySelector<HTMLElement>("[role='alertdialog']")!;
    const cancel = [...confirmation.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    await nextFrame();
    expect(document.activeElement).toBe(cancel);

    flushSync(() => cancel.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await nextFrame();
    expect(changes).toHaveLength(0);
    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(document.activeElement).toBe(container.querySelector(".wf-syntax-picker-trigger"));

    openAndSelect(container, "Literal");
    const replace = [...container.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
      .find(button => button.textContent === "Replace value")!;
    flushSync(() => replace.click());
    await nextFrame();

    expect(changes).toHaveLength(1);
    expect(changes[0]?.condition).toEqual({
      typeName: "System.Boolean",
      expression: { type: "Literal", value: false }
    });
    expect(document.activeElement).toBe(container.querySelector("input[type='checkbox']"));
  });

  it("uses the admitted inline Contribution's default when an empty value changes to reference mode", async () => {
    const changes: ActivityNode[] = [];
    const factoryOnly: StudioExpressionEditorContribution = {
      id: "factory-only",
      order: 1,
      supports: context => context.syntax === "Secret",
      surfaces: {},
      createDefaultValue: () => ({ name: "wrong-provider" })
    };
    const admitted: StudioExpressionEditorContribution = {
      id: "secret.inline",
      order: 2,
      supports: context => context.syntax === "Secret",
      surfaces: { inline: TestReferenceEditor },
      createDefaultValue: () => ({ name: "new-secret" })
    };
    const container = renderPanel([input("Credential", { isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Secret", displayName: "Secret", editingMode: "reference" }
      ],
      expressionEditors: [factoryOnly, admitted],
      onChange: next => changes.push(next),
      activity: activity({
        credential: { typeName: "System.String", expression: { type: "Literal", value: "" } }
      })
    });

    openAndSelect(container, "Secret");
    await nextFrame();

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes).toHaveLength(1);
    expect(changes[0]?.credential).toEqual({
      typeName: "System.String",
      expression: { type: "Secret", value: { name: "new-secret" } }
    });
    expect(document.activeElement).toBe(container.querySelector("input[aria-label='Secret reference']"));
  });

  it("disables structured and reference modes without an inline Contribution and default factory", () => {
    const container = renderPanel([input("Source", { isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Record", displayName: "Record", editingMode: "structured" },
        { type: "Variable", displayName: "Variable", editingMode: "reference" }
      ],
      expressionEditors: [{
        id: "record.inline-without-default",
        supports: context => context.syntax === "Record",
        surfaces: { inline: TestReferenceEditor }
      }]
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const options = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")];
    const record = options.find(option => option.textContent?.includes("Record"));
    const variable = options.find(option => option.textContent?.includes("Variable"));
    expect(record?.disabled).toBe(true);
    expect(record?.textContent).toContain("default value factory");
    expect(variable?.disabled).toBe(true);
    expect(variable?.textContent).toContain("inline editor Contribution");
  });

  it("keeps the Object collection Contribution selectable from text mode and defaults it after confirmation", () => {
    const changes: ActivityNode[] = [];
    const collectionType = "System.Collections.Generic.ICollection`1[System.String]";
    const container = renderPanel([input("Items", { typeName: collectionType, isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
        { type: "Object", displayName: "Object", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        items: { typeName: collectionType, expression: { type: "JavaScript", value: "input.items" } }
      })
    });

    openAndSelect(container, "Object");
    expect(container.querySelector("[role='alertdialog']")).not.toBeNull();
    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
      .find(button => button.textContent === "Replace value")?.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.items).toEqual({
      typeName: collectionType,
      expression: { type: "Object", value: [] }
    });
    expect(container.querySelector(".wf-collection-editor")).not.toBeNull();
  });

  it("keeps contribution diagnostics visible when text mode uses the generic inline fallback", () => {
    const liquidEditor: StudioExpressionEditorContribution = {
      id: "liquid.expanded",
      supports: context => context.syntax === "Liquid",
      surfaces: { expanded: () => <div>Enhanced Liquid editor</div> },
      diagnostics: (_context, value) => String(value).endsWith("{")
        ? [{ severity: "warning", code: "LIQUID_DRAFT", message: "Expression is incomplete." }]
        : []
    };
    const container = renderPanel([
      input("Template", { isWrapped: true })
    ], {
      expressionEditors: [liquidEditor],
      activity: activity({
        template: { typeName: "System.String", expression: { type: "Liquid", value: "{{ order.{" } }
      })
    });

    expect(container.querySelector<HTMLInputElement>("input[aria-label='Template expression']")?.value).toBe("{{ order.{");
    expect(container.querySelector(".wf-expression-editor-diagnostic.warning")?.textContent)
      .toContain("Expression is incomplete.");
  });

  it("uses text mode instead of a collection repeater for collection-typed expressions", () => {
    const container = renderPanel([
      input("Items", { typeName: "System.Collections.Generic.ICollection`1[System.String]", isWrapped: true })
    ], {
      activity: activity({
        items: { typeName: "System.Collections.Generic.ICollection`1[System.String]", expression: { type: "JavaScript", value: "input.items" } }
      })
    });

    expect(container.querySelector<HTMLInputElement>("input[aria-label='Items expression']")?.value).toBe("input.items");
    expect(container.querySelector(".wf-collection-editor")).toBeNull();
    expect(container.querySelector("button[aria-label='Open expanded Items editor']")).not.toBeNull();
  });

  it("requires an owning Contribution for arbitrary structured syntaxes while Object owns collection authoring", () => {
    const changes: ActivityNode[] = [];
    const collectionType = "System.Collections.Generic.ICollection`1[System.String]";
    const container = renderPanel([
      input("RecordItems", { typeName: collectionType, isWrapped: true }),
      input("ObjectItems", { typeName: collectionType, isWrapped: true })
    ], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Object", displayName: "Object", editingMode: "structured" },
        { type: "Record", displayName: "Record", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        recordItems: { typeName: collectionType, expression: { type: "Record", value: ["one"] } },
        objectItems: { typeName: collectionType, expression: { type: "Object", value: ["one"] } }
      })
    });

    expect(container.querySelectorAll(".wf-collection-editor")).toHaveLength(1);
    expect(container.textContent).toContain("No editor is available for Record.");
    expect(container.textContent).toContain("current value is preserved and read-only");
    expect(container.textContent).not.toContain("No editor is available for Object.");
    const recordRow = [...container.querySelectorAll<HTMLElement>(".wf-property-row")]
      .find(row => row.textContent?.includes("RecordItems"))!;
    flushSync(() => recordRow.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const recordOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent?.includes("Record"));
    expect(recordOption?.getAttribute("aria-selected")).toBe("true");
    expect(recordOption?.disabled).toBe(true);
    expect(changes).toHaveLength(0);
  });

  it("shows a compact scalar Object summary and preserves invalid JSON across expanded-editor reopening", () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([input("Payload", { typeName: "System.Object", isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Object", displayName: "Object", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        payload: { typeName: "System.Object", expression: { type: "Object", value: { first: 1, second: 2 } } }
      })
    });

    expect(container.querySelector(".wf-object-expression-summary")?.textContent).toContain("2 properties");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Payload editor']")?.click());

    const expanded = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload JSON value']");
    expect(expanded?.value).toContain('"first": 1');
    expect(container.querySelector("[role='dialog']")).not.toBeNull();

    changeTextArea(expanded!, '{"first":');
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("Invalid JSON");
    expect(changes).toHaveLength(0);
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Close Payload editor']")?.click());
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Payload editor']")?.click());

    expect(container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload JSON value']")?.value).toBe('{"first":');
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("Invalid JSON");
  });
});

function TestReferenceEditor({ value, disabled, initialFocus }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  const ref = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (initialFocus) ref.current?.focus();
  }, [initialFocus]);
  return <input ref={ref} aria-label="Secret reference" value={JSON.stringify(value)} disabled={disabled} readOnly />;
}

function openAndSelect(container: HTMLElement, label: string) {
  flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
  const option = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent === label);
  flushSync(() => option?.click());
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function changeTextArea(input: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
