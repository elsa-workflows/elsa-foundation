import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, onTestFinished, vi } from "vitest";
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
// Replaces the rendered activity from outside the panel, as an undo or selecting another activity does.
const noRenderedPanel = () => {
  throw new Error("No panel is rendered.");
};
let replaceActivity: (next: ActivityNode) => void = noRenderedPanel;

const backendExpressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Liquid", displayName: "Liquid", editingMode: "text" },
  { type: "Object", displayName: "Object", editingMode: "structured" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" },
  { type: "Input", displayName: "Input", editingMode: "reference" }
];

afterEach(() => {
  replaceActivity = noRenderedPanel;
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
    draftId?: string;
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
    replaceActivity = setCurrentActivity;
    const handleChange = (nextActivity: ActivityNode) => {
      options.onChange?.(nextActivity);
      setCurrentActivity(nextActivity);
    };
    return (
      <ActivityPropertiesPanel
        draftId={options.draftId}
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

  it("orders properties by input order, keeping delivery order for ties (never alphabetical)", () => {
    const container = renderPanel([
      input("Last", { order: 20 }),
      input("Beta", { order: 10 }),
      input("Alpha", { order: 10 })
    ]);

    // Ties keep catalog delivery order (Beta before Alpha), not an alphabetical re-sort (issue #452).
    expect(propertyLabels(container)).toEqual(["Beta", "Alpha", "Last"]);
  });

  it("preserves catalog delivery order when the backend supplies no explicit order (issue #452)", () => {
    const container = renderPanel([
      input("Url"),
      input("Content"),
      input("Method")
    ]);

    // The primary field (Url) is delivered first and must stay first — alphabetical sorting pushed it last.
    expect(propertyLabels(container)).toEqual(["Url", "Content", "Method"]);
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

  it("uses inline syntax chrome for URI inputs and a compact toolbar for dictionaries", () => {
    const container = renderPanel([
      input("Url", { typeName: "System.Uri", isWrapped: true }),
      input("RequestHeaders", { collectionKind: "Dictionary", isWrapped: true })
    ], {
      activity: activity({
        url: { typeName: "System.Uri", expression: { type: "Literal", value: "https://example.com" } },
        requestHeaders: { typeName: "System.String", expression: { type: "Literal", value: {} } }
      })
    });

    const rows = [...container.querySelectorAll<HTMLElement>(".wf-property-row")];
    expect(rows[0]?.querySelector(".wf-expression-field .wf-syntax-picker.inline")).not.toBeNull();
    expect(rows[0]?.querySelector(":scope > .wf-syntax-picker:not(.inline)")).toBeNull();
    expect(rows[1]?.querySelector(".wf-dictionary-expression-toolbar .wf-syntax-picker.inline")).not.toBeNull();
    expect(rows[1]?.querySelector(":scope > .wf-syntax-picker:not(.inline)")).toBeNull();
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

  it("keeps one stable expression document across compact and expanded contributed surfaces", () => {
    const contexts: Array<React.ComponentProps<
      NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>
    >["context"]> = [];
    const contribution: StudioExpressionEditorContribution = {
      id: "javascript.rich",
      supports: context => context.syntax === "JavaScript",
      surfaces: {
        inline: props => {
          contexts.push(props.context);
          return <button type="button" aria-label="Expand from editor" onClick={props.onExpand}>Compact editor</button>;
        },
        expanded: props => {
          contexts.push(props.context);
          return <div aria-label="Expanded rich editor">{String(props.value)}</div>;
        }
      }
    };
    const container = renderPanel([input("Template", { isWrapped: true })], {
      draftId: "draft-42",
      expressionEditors: [contribution],
      activity: activity({
        template: { typeName: "System.String", expression: { type: "JavaScript", value: "input.name" } }
      })
    });

    const inlineContext = contexts.at(-1)!;
    expect(inlineContext).toMatchObject({ syntax: "JavaScript", surface: "inline" });
    expect(inlineContext.document).toMatchObject({
      draftId: "draft-42",
      activityId: "node-1",
      propertyKey: "Template",
      expressionType: "JavaScript",
      source: "input.name",
      sourceVersion: 0
    });

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Expand from editor']")!.click());

    expect(container.querySelector("[aria-label='Expanded rich editor']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Expand from editor']")).toBeNull();
    const expandedContext = contexts.at(-1)!;
    expect(expandedContext.surface).toBe("expanded");
    expect(expandedContext.document?.id).toBe(inlineContext.document?.id);
    expect(expandedContext.document?.source).toBe(inlineContext.document?.source);
  });

  // Regression: ticking a checklist box saved fine, but autosave promoted the structured Literal to an
  // "Object" expression on the wire and nothing reversed it, so the row reopened with the picker reading
  // "Object" and the checklist replaced by the raw JSON summary.
  it("reopens a saved checklist input as Literal with its checklist intact, not the Object JSON summary", () => {
    const checklistEditor: StudioActivityPropertyEditorContribution = {
      id: "test.multiselect",
      supports: (_descriptor, editorContext) => editorContext.scope === "collection",
      component: ({ value }) => <div className="test-checklist">{(value as unknown[]).join(",")}</div>
    };
    const container = renderPanel([
      input("SupportedMethods", { typeName: "System.String", collectionKind: "List", isWrapped: true })
    ], {
      editors: [checklistEditor],
      expressionEditors: [createObjectExpressionEditorContribution(() => [checklistEditor])],
      // Exactly what a save round-trip leaves behind: expressionType "Object", value a JSON string.
      activity: activity({
        supportedMethods: { typeName: "", expression: { type: "Object", value: '["GET"]' } }
      })
    });

    expect(container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.textContent).toContain("Literal");
    expect(container.querySelector(".test-checklist")?.textContent).toBe("GET");
    expect(container.textContent).not.toContain("Open the expanded editor to edit JSON.");
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

const secretDescriptor: StudioExpressionDescriptor = { type: "Secret", displayName: "Secret", editingMode: "reference" };
const secretPicker: StudioExpressionEditorContribution = {
  id: "secret.picker",
  supports: context => context.syntax === "Secret",
  surfaces: { inline: SecretPickerStub },
  createDefaultValue: () => null
};

function clickButton(scope: ParentNode, label: string) {
  const button = scope.querySelector<HTMLButtonElement>(`button[aria-label='${label}']`);
  expect(button, `button "${label}"`).not.toBeNull();
  flushSync(() => button!.click());
}

describe("secret-only inputs", () => {
  const literalEditor: StudioActivityPropertyEditorContribution = {
    id: "studio.property.singleline",
    supports: () => true,
    component: ({ value, onChange }) => <input type="text" aria-label="Literal editor" value={String(value ?? "")} onChange={event => onChange(event.target.value)} />
  };
  const authorization = input("Authorization", { isWrapped: true, isSensitive: true, isCredential: true, defaultSyntax: "Literal" });
  const legacyLiteral = "plain-old-words";

  const renderSecretOnly = (options: Parameters<typeof renderPanel>[1] = {}, overrides: Partial<StudioActivityInputDescriptor> = {}) => renderPanel([{ ...authorization, ...overrides }, input("Url", { isWrapped: true })], {
    editors: [literalEditor],
    expressionEditors: [secretPicker],
    expressionDescriptors: [...backendExpressionDescriptors, secretDescriptor],
    ...options
  });
  const rowOf = (container: HTMLElement, label: string) =>
    [...container.querySelectorAll<HTMLElement>(".wf-property-row")].find(row => row.querySelector("label")?.textContent === label)!;
  const syntaxOptions = (row: HTMLElement) => {
    flushSync(() => row.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    return [...document.querySelectorAll("[role='option']")].map(option => option.textContent);
  };

  it("defaults to the secret picker ahead of the descriptor's default syntax and offers no literal editor", () => {
    const row = rowOf(renderSecretOnly(), "Authorization");

    expect(row.querySelector(".wf-syntax-picker-trigger")?.textContent).toBe("Secret");
    expect(row.querySelector("[aria-label='Secret picker']")).not.toBeNull();
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
  });

  it("offers only the Secret syntax in the picker, while other inputs keep every syntax", () => {
    const container = renderSecretOnly();

    expect(syntaxOptions(rowOf(container, "Authorization"))).toEqual(["Secret"]);
    flushSync(() => document.body.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(syntaxOptions(rowOf(container, "Url"))).toEqual(expect.arrayContaining(["Literal", "JavaScript", "Secret"]));
    expect(rowOf(container, "Url").querySelector("[aria-label='Literal editor']")).not.toBeNull();
  });

  // Each layout has its own syntax picker. A dictionary layout is reached only when the Secret descriptor
  // reports literal mode, since a secret-only input's syntax is always Secret.
  const layouts: Array<[string, string, Partial<StudioActivityInputDescriptor>, StudioExpressionDescriptor["editingMode"]]> = [
    ["inline", ".wf-expression-field .wf-syntax-picker-trigger", {}, "reference"],
    ["block", ".wf-syntax-picker-trigger:not(.inline)", { uiHint: "multiline" }, "reference"],
    ["dictionary", ".wf-dictionary-expression-toolbar .wf-syntax-picker-trigger", { typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]" }, "literal"]
  ];

  it.each(layouts)("offers only the Secret syntax in the %s layout", (_layout, picker, overrides, secretMode) => {
    const container = renderSecretOnly({ expressionDescriptors: [...backendExpressionDescriptors, { ...secretDescriptor, editingMode: secretMode }] }, overrides);
    const row = rowOf(container, "Authorization");

    expect(row.querySelectorAll(".wf-syntax-picker-trigger")).toHaveLength(1);
    expect(row.querySelector(picker)).not.toBeNull();
    expect(syntaxOptions(row)).toEqual(["Secret"]);
  });

  it("does not display a value stored before the rule as text", () => {
    const container = renderSecretOnly({
      activity: activity({ authorization: { typeName: "System.String", expression: { type: "Literal", value: legacyLiteral } } })
    });
    const row = rowOf(container, "Authorization");

    expect(container.innerHTML).not.toContain(legacyLiteral);
    expect(row.querySelector(".wf-syntax-picker-trigger")?.textContent).toBe("Secret");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("[aria-label='Secret picker']")?.getAttribute("data-value")).toBe("null");
  });

  it("shows the existing unavailable state, never a literal editor, when the Secret descriptor is absent", () => {
    const container = renderSecretOnly({ expressionEditors: [], expressionDescriptors: backendExpressionDescriptors });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
  });

  it("keeps the secret picker, with its own states, when the backend lists no Secret syntax", () => {
    const row = rowOf(renderSecretOnly({ expressionDescriptors: backendExpressionDescriptors }), "Authorization");

    expect(row.querySelector("[aria-label='Secret picker']")).not.toBeNull();
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it("shows the existing unavailable state when the Secret descriptor exists but no secret picker is registered", () => {
    const container = renderSecretOnly({ expressionEditors: [] });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it.each(["literal", "text"] as const)("never falls back to a literal, text or expanded editor when the Secret descriptor reports %s mode", mode => {
    const container = renderSecretOnly({
      expressionEditors: [],
      expressionDescriptors: [...backendExpressionDescriptors, { ...secretDescriptor, editingMode: mode }]
    });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
    expect(row.querySelector("button[aria-label='Open expanded Authorization editor']")).toBeNull();
  });

  it("shows no literal editor for a secret-only input the backend marks unwrapped", () => {
    const container = renderPanel([input("Authorization", { isCredential: true })], { editors: [literalEditor] });

    expect(container.textContent).toContain("No editor is available for Secret.");
    expect(container.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it("offers no conversion or expand control, writes a picked Secret under the Secret syntax and unbinds the input when it is cleared", () => {
    const changes: ActivityNode[] = [];
    const url = { typeName: "System.String", expression: { type: "Literal", value: "https://example.test" } };
    const container = renderSecretOnly({
      onChange: next => changes.push(next),
      activity: activity({
        authorization: { typeName: "System.String", expression: { type: "Literal", value: legacyLiteral }, conversion: { mode: "json" } },
        url
      })
    });
    const row = rowOf(container, "Authorization");

    // An authored conversion would otherwise show its chip, and the toggle shows on every wrapped input.
    expect(row.querySelector(".wf-conversion-toggle, .wf-conversion-chip, .wf-conversion-control")).toBeNull();
    expect(row.querySelector("button[aria-label='Open expanded Authorization editor']")).toBeNull();
    // A secret picker that asks to expand does not open the expanded editor, which carries its own conversion control.
    clickButton(row, "Expand secret");
    expect(document.querySelector("[role='dialog'], .wf-conversion-control")).toBeNull();
    expect(changes).toEqual([]);

    clickButton(row, "Pick secret");
    expect(changes).toHaveLength(1);
    expect(changes[0]?.authorization).toEqual({ typeName: "System.String", expression: { type: "Secret", value: { name: "tokens" } }, conversion: { mode: "json" } });

    clickButton(row, "Clear secret");
    expect(changes).toHaveLength(2);
    expect(changes[1]).not.toHaveProperty("authorization");
    expect(changes[1]?.url).toEqual(url);
    expect(JSON.stringify(changes[1])).not.toContain(legacyLiteral);
    expect(document.body.innerHTML).not.toContain(legacyLiteral);
  });
});

describe("property editor context", () => {
  it("gives a property editor the node id of the activity being edited", () => {
    const identityEditor: StudioActivityPropertyEditorContribution = {
      id: "test.identity",
      supports: () => true,
      component: ({ context }) => <span data-activity-id={context.activityId ?? ""} />
    };

    const container = renderPanel([input("Message")], { editors: [identityEditor] });

    expect(container.querySelector("[data-activity-id]")?.getAttribute("data-activity-id")).toBe("node-1");
  });
});

describe("masked inputs", () => {
  const storedValue = "stored-value-words";
  const maskedNotice = "This value is masked. It is preserved and read-only here.";
  // The panel asks nothing of the masked editor but its id; this one renders nothing of the value.
  const maskedEditor: StudioActivityPropertyEditorContribution = {
    id: "studio.property.password",
    supports: () => true,
    component: () => <span>Value set</span>
  };
  // Shows everything it is given, as any editor but the masked one would.
  const revealingEditor: StudioActivityPropertyEditorContribution = {
    id: "test.revealing",
    supports: () => true,
    component: ({ value }) => <input type="text" aria-label="Revealing editor" value={JSON.stringify(value)} readOnly />
  };
  // The real Object editor: its expanded surface prints the stored JSON.
  const objectEditor = createObjectExpressionEditorContribution(() => [revealingEditor]);
  const sensitive: Partial<StudioActivityInputDescriptor> = { isSensitive: true };
  const token = (type: string, value: unknown) => ({ typeName: "System.String", expression: { type, value } });
  const renderToken = (
    stored: ReturnType<typeof token>,
    overrides: Partial<StudioActivityInputDescriptor>,
    options: Parameters<typeof renderPanel>[1] = {}
  ) => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([input("ApiToken", { isWrapped: true, ...overrides })], {
      editors: [maskedEditor],
      expressionEditors: [secretPicker, objectEditor],
      expressionDescriptors: [...backendExpressionDescriptors, secretDescriptor],
      onChange: next => changes.push(next),
      activity: activity({ apiToken: stored }),
      ...options
    });
    return { container, changes };
  };
  const switchToJavaScript = (overrides: Partial<StudioActivityInputDescriptor>, stored: unknown = storedValue) => {
    const rendered = renderToken(token("Literal", stored), overrides);
    openAndSelect(rendered.container, "JavaScript");
    return rendered;
  };
  const openExpandedEditor = (container: HTMLElement) => clickButton(container, "Open expanded ApiToken editor");
  const expandedEditor = () => document.querySelector<HTMLElement>("[role='dialog']");
  const expandAffordance = (container: HTMLElement) =>
    container.querySelector(".wf-expression-expand-button, .wf-property-expand-row, .wf-dictionary-open-expanded");
  const rowPicker = (container: HTMLElement) => container.querySelector(".wf-syntax-picker-trigger");
  const inlineSource = (container: HTMLElement) => container.querySelector<HTMLInputElement>("input[aria-label='ApiToken expression']")!.value;
  const replaceButton = () => [...document.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
    .find(button => button.textContent === "Replace value") ?? null;
  const clearTextFields = () => document.querySelectorAll("textarea, input[type='text']");
  // A React-controlled field's value is not in innerHTML, so each field's value is checked as well.
  const isShown = (text: string) => document.body.innerHTML.includes(text)
    || [...document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>("input, textarea")].some(field => field.value.includes(text));
  const expectNowhere = (text: string) => expect(isShown(text), `"${text}" is shown`).toBe(false);
  const masked: Array<[string, Partial<StudioActivityInputDescriptor>]> = [
    ["a sensitive input", sensitive],
    ["a password-hinted input", { uiHint: "password" }]
  ];
  // Masked inputs whose Object syntax holds the stored value itself: Object JSON is data deserialized into the input.
  const objectShapes = [
    { shape: "scalar", declared: sensitive, objectValue: { token: storedValue }, objectDefault: {}, literalDefault: "" },
    { shape: "POCO-typed", declared: { ...sensitive, typeName: "Contoso.Settings" }, objectValue: { token: storedValue }, objectDefault: {}, literalDefault: "" },
    { shape: "json-hinted", declared: { ...sensitive, uiHint: "json", typeName: "String", collectionKind: "List" }, objectValue: [storedValue], objectDefault: [], literalDefault: [] },
    { shape: "malformed-collection", declared: { ...sensitive, typeName: "String", collectionKind: "List" }, objectValue: { header: storedValue }, objectDefault: [], literalDefault: [] }
  ];

  it("never offers to expand a sensitive multiline input into a clear-text editor", () => {
    const { container } = renderToken(token("Literal", storedValue), { ...sensitive, uiHint: "multiline" });

    expect(container.textContent).toContain("Value set");
    expect(expandAffordance(container)).toBeNull();
  });

  it.each(masked)("does not carry the stored literal of %s into a text syntax, inline or expanded", (_label, overrides) => {
    const { container, changes } = switchToJavaScript(overrides);
    // Discarding a stored value asks first, like any other lossy switch.
    expect(changes).toEqual([]);
    flushSync(() => replaceButton()!.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", ""));
    expect(inlineSource(container)).toBe("");
    expectNowhere(storedValue);

    openExpandedEditor(container);
    expect(container.querySelector<HTMLTextAreaElement>("textarea[aria-label='ApiToken expanded value']")!.value).toBe("");
    expectNowhere(storedValue);
  });

  it.each(masked)("switches %s with nothing stored straight to a text syntax", (_label, overrides) => {
    const { container, changes } = switchToJavaScript(overrides, "");

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", ""));
  });

  it("still carries an ordinary input's literal into a text syntax", () => {
    const { container, changes } = switchToJavaScript({});

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", storedValue));
    expect(inlineSource(container)).toBe(storedValue);
  });

  // Every syntax switch a masked input can make, from the row and from the expanded editor (which opens only over
  // a text syntax), with a registry whose only editor shows everything. The value stored under Literal or Object
  // must be kept, yet shown nowhere, and no clear-text field may be left editing it. An expression source or a
  // Secret Reference name may show; only those syntaxes offer the expanded editor.
  const surfaces = {
    row: { open: (_container: HTMLElement) => {}, scope: (container: HTMLElement): ParentNode => container },
    "expanded editor": { open: openExpandedEditor, scope: (_container: HTMLElement): ParentNode => expandedEditor()! }
  };
  type Step = {
    shape: string;
    declared: Partial<StudioActivityInputDescriptor>;
    surface: keyof typeof surfaces;
    from: string;
    authored: unknown;
    to: string;
    expandableBefore: boolean;
    shownBefore: boolean;
    asksFirst: boolean;
    storedAfter: unknown;
    expandableAfter: boolean;
    clearTextFieldsAfter: number;
  };
  const scalarSteps: Step[] = [
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "JavaScript", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "JavaScript", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "Liquid", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Liquid", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "Secret", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Secret", authored: { name: storedValue }, to: "Literal", expandableBefore: false, shownBefore: true, asksFirst: true, storedAfter: "", expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "Liquid", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Secret", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "Liquid", authored: storedValue, to: "Secret", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 }
  ];
  // A malformed collection's well-formed default reads back as Literal, so it never offers expansion.
  const objectSteps: Step[] = objectShapes.flatMap(({ shape, declared, objectValue, objectDefault, literalDefault }) => [
    { shape, declared, surface: "row", from: "Literal", authored: storedValue, to: "Object", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "row", from: "Object", authored: objectValue, to: "Literal", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: literalDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "row", from: "Object", authored: objectValue, to: "JavaScript", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape, declared, surface: "row", from: "JavaScript", authored: storedValue, to: "Object", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Object", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 }
  ]);

  it.each([...scalarSteps, ...objectSteps])("$shape: after switching $from to $to from the $surface, the stored value is nowhere in sight and no clear-text field edits it", step => {
    const { container, changes } = renderToken(token(step.from, step.authored), step.declared, { editors: [revealingEditor] });
    expect(expandAffordance(container) !== null).toBe(step.expandableBefore);
    surfaces[step.surface].open(container);
    expect(isShown(storedValue)).toBe(step.shownBefore);

    openAndSelect(surfaces[step.surface].scope(container), step.to);
    expect(replaceButton() !== null).toBe(step.asksFirst);
    flushSync(() => replaceButton()?.click());

    expect(changes.at(-1)?.apiToken).toEqual(token(step.to, step.storedAfter));
    expectNowhere(storedValue);
    expect(expandedEditor()).toBeNull();
    expect(expandAffordance(container) !== null).toBe(step.expandableAfter);
    expect(clearTextFields()).toHaveLength(step.clearTextFieldsAfter);
  });

  it.each([
    { change: "an undo", nodeId: "node-1", shape: "scalar", declared: sensitive, lands: token("Literal", storedValue) },
    { change: "selecting another activity of the same type", nodeId: "node-2", shape: "scalar", declared: sensitive, lands: token("Literal", storedValue) },
    ...objectShapes.map(({ shape, declared, objectValue }) => ({ change: "an undo", nodeId: "node-1", shape, declared, lands: token("Object", objectValue) }))
  ])("closes the expanded editor when $change lands a $shape input on its stored $lands.expression.type value", ({ nodeId, declared, lands }) => {
    const { container } = renderToken(token("JavaScript", "author-code"), declared, { editors: [revealingEditor] });
    openExpandedEditor(container);
    expect(expandedEditor()).not.toBeNull();

    flushSync(() => replaceActivity(activity({ nodeId, apiToken: lands })));

    expectNowhere(storedValue);
    expect(expandedEditor()).toBeNull();
    expect(clearTextFields()).toHaveLength(0);
  });

  it("returns focus to the row when a switch inside the expanded editor closes it", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);

    openAndSelect(expandedEditor()!, "Literal");
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(rowPicker(container));
  });

  it("returns focus to the row when an undo closes the expanded editor, and does not reopen it by itself", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);

    flushSync(() => replaceActivity(activity({ apiToken: token("Literal", storedValue) })));
    await nextFrame();
    expect(document.activeElement).toBe(rowPicker(container));

    openAndSelect(container, "JavaScript");
    flushSync(() => replaceButton()!.click());
    expect(container.querySelector<HTMLInputElement>("input[aria-label='ApiToken expression']")).not.toBeNull();
    expect(expandedEditor()).toBeNull();
  });

  it("moves focus from elsewhere in the row to its syntax picker when the expanded editor closes under it", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);
    // In the row but outside the expanded editor when the editor closes.
    container.querySelector<HTMLButtonElement>(".wf-conversion-toggle")!.focus();

    flushSync(() => replaceActivity(activity({ apiToken: token("Literal", storedValue) })));
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(rowPicker(container));
  });

  it("leaves focus on another activity selected while the expanded editor is open", async () => {
    const canvasNode = document.body.appendChild(document.createElement("button"));
    onTestFinished(() => canvasNode.remove());
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);
    canvasNode.focus();

    flushSync(() => replaceActivity(activity({ nodeId: "node-2", apiToken: token("Literal", storedValue) })));
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(canvasNode);
  });

  // The expanded editor's open state follows the row's expandability for every input, masked or not.
  it("closes an ordinary input's expanded editor when a switch inside it leaves the row unexpandable", () => {
    const { container, changes } = renderToken(token("JavaScript", "author-code"), { uiHint: "dropdown" }, { editors: [revealingEditor] });
    openExpandedEditor(container);

    openAndSelect(expandedEditor()!, "Literal");

    expect(changes.at(-1)?.apiToken).toEqual(token("Literal", "author-code"));
    expect(expandedEditor()).toBeNull();
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Revealing editor']")?.value).toBe('"author-code"');
  });

  it.each([
    { label: "a password-hinted number", declared: { uiHint: "password", typeName: "Int32" }, stored: token("Literal", 731904), shown: "731904", editors: [revealingEditor] },
    { label: "a sensitive text input another editor claims ahead of the masked one", declared: sensitive, stored: token("Literal", storedValue), shown: storedValue, editors: [revealingEditor, maskedEditor] },
    { label: "a sensitive list the masked editor claims", declared: { ...sensitive, typeName: "String", collectionKind: "List" }, stored: token("Literal", [storedValue]), shown: storedValue, editors: [maskedEditor, revealingEditor] },
    { label: "a sensitive dictionary the masked editor claims", declared: { ...sensitive, typeName: "String", collectionKind: "Dictionary" }, stored: token("Literal", { header: storedValue }), shown: storedValue, editors: [maskedEditor, revealingEditor] },
    { label: "a sensitive input under a syntax without a descriptor", declared: sensitive, stored: token("CSharp", storedValue), shown: storedValue, editors: [maskedEditor] },
    ...objectShapes.map(({ shape, declared, objectValue }) => ({ label: `a ${shape} input's Object value`, declared, stored: token("Object", objectValue), shown: storedValue, editors: [revealingEditor] }))
  ] as Array<{ label: string; declared: Partial<StudioActivityInputDescriptor>; stored: ReturnType<typeof token>; shown: string; editors: StudioActivityPropertyEditorContribution[] }>)(
    "masks the stored value of $label: the masked notice, no editor, no expanded editor and no write",
    ({ declared, stored, shown, editors }) => {
      const { container, changes } = renderToken(stored, declared, { editors });

      expectNowhere(shown);
      expect(container.textContent).toContain(maskedNotice);
      expect(container.textContent).not.toContain("Value set");
      expect(expandAffordance(container)).toBeNull();
      expect(changes).toEqual([]);
    }
  );

  const switchToJavaScriptAndReplace = (container: HTMLElement) => {
    openAndSelect(container, "JavaScript");
    expect(replaceButton()).not.toBeNull();
    flushSync(() => replaceButton()!.click());
  };

  const literalEditor: StudioExpressionEditorContribution = { id: "test.literal", supports: context => context.syntax === "Literal", surfaces: { inline: EchoExpressionEditor } };
  const withExpressionTypes = [...backendExpressionDescriptors, secretDescriptor];

  it.each([
    ["a Literal editor", literalEditor, withExpressionTypes, () => {}],
    ["a Literal editor while expression types are unavailable", literalEditor, [], () => {}],
    ["Literal diagnostics", { id: "test.diagnostics", supports: context => context.syntax === "Literal", surfaces: {}, diagnostics: (_context, value) => [{ message: String(value) }] }, withExpressionTypes, () => {}],
    ["a JavaScript default built from its document", { id: "test.javascript", supports: context => context.syntax === "JavaScript", surfaces: { inline: EchoExpressionEditor }, createDefaultValue: context => context.document?.source }, withExpressionTypes, switchToJavaScriptAndReplace]
  ] as Array<[string, StudioExpressionEditorContribution, StudioExpressionDescriptor[], (container: HTMLElement) => void]>)("gives nothing of a masked literal to %s", (_label, contribution, expressionDescriptors, act) => {
    const { container } = renderToken(token("Literal", storedValue), sensitive, { expressionEditors: [secretPicker, contribution], expressionDescriptors });

    act(container);

    expectNowhere(storedValue);
  });
});

function SecretPickerStub({ value, onChange, onExpand }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  return (
    <div aria-label="Secret picker" data-value={JSON.stringify(value)}>
      <button type="button" aria-label="Pick secret" onClick={() => onChange({ name: "tokens" })} />
      <button type="button" aria-label="Clear secret" onClick={() => onChange(null)} />
      <button type="button" aria-label="Expand secret" onClick={() => onExpand?.()} />
    </div>
  );
}

function EchoExpressionEditor({ value }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  return <output>{String(value)}</output>;
}

function TestReferenceEditor({ value, disabled, initialFocus }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  const ref = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (initialFocus) ref.current?.focus();
  }, [initialFocus]);
  return <input ref={ref} aria-label="Secret reference" value={JSON.stringify(value)} disabled={disabled} readOnly />;
}

function openAndSelect(scope: ParentNode, label: string) {
  const trigger = scope.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger");
  expect(trigger, "syntax picker").not.toBeNull();
  flushSync(() => trigger!.click());
  const option = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent === label);
  expect(option, `${label} option`).toBeDefined();
  flushSync(() => option!.click());
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
