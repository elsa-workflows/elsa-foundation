import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import type { ActivityNode } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

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

  flushSync(() => root.render(
    <ActivityPropertiesPanel
      activity={options.activity ?? activity()}
      descriptor={descriptor}
      editors={options.editors ?? []}
      expressionEditors={options.expressionEditors ?? []}
      expressionDescriptors={[]}
      descriptorStatus="ready"
      visibleVariables={[]}
      scopeStatus="ready"
      onChange={vi.fn()}
    />
  ));
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
    expect(fields[1]?.querySelector(".wf-variable-picker")).not.toBeNull();
  });
});
