import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { builtInPropertyEditors } from "../app/propertyEditors";
import type { StudioActivityInputDescriptor, StudioActivityPropertyEditorProps } from "../sdk";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

const descriptor: StudioActivityInputDescriptor = {
  name: "CanStartWorkflow",
  displayName: "Can start workflow",
  typeName: "System.Boolean"
};

function renderBooleanEditor(props: Partial<StudioActivityPropertyEditorProps> = {}) {
  const contribution = builtInPropertyEditors.find(editor => editor.id === "studio.property.checkbox");
  if (!contribution) throw new Error("Built-in boolean property editor was not registered.");
  const Editor = contribution.component;

  flushSync(() => root.render(
    <Editor
      descriptor={descriptor}
      value={false}
      context={{ activity: {}, expressionDescriptors: [] }}
      onChange={() => {}}
      {...props}
    />
  ));

  return container.querySelector<HTMLInputElement>('[role="switch"]')!;
}

function renderEditor(id: string, props: Partial<StudioActivityPropertyEditorProps> = {}) {
  const contribution = builtInPropertyEditors.find(editor => editor.id === id);
  if (!contribution) throw new Error(`Built-in property editor '${id}' was not registered.`);
  const Editor = contribution.component;
  flushSync(() => root.render(
    <Editor
      descriptor={{
        name: "Priority",
        displayName: "Priority",
        typeName: "System.Int32",
        uiSpecifications: { options: [{ label: "Low", value: 1 }, { label: "High", value: 10 }] }
      }}
      value={1}
      context={{ activity: {}, expressionDescriptors: [] }}
      onChange={() => {}}
      {...props}
    />
  ));
}

describe("built-in boolean activity property editor", () => {
  it("renders an accessible off switch with the property name", () => {
    const input = renderBooleanEditor();

    expect(input).not.toBeNull();
    expect(input.type).toBe("checkbox");
    expect(input.checked).toBe(false);
    expect(input.getAttribute("aria-label")).toBe("Can start workflow");
    expect(container.querySelector(".studio-property-switch-state")?.textContent).toBe("Disabled");
  });

  it("renders Boolean and persisted string true values as on", () => {
    expect(renderBooleanEditor({ value: true }).checked).toBe(true);
    expect(container.querySelector(".studio-property-switch-state")?.textContent).toBe("Enabled");

    expect(renderBooleanEditor({ value: "true" }).checked).toBe(true);
    expect(renderBooleanEditor({ value: "false" }).checked).toBe(false);
  });

  it("emits boolean values when toggled", () => {
    const onChange = vi.fn();
    const input = renderBooleanEditor({ onChange });

    flushSync(() => {
      input.click();
    });

    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("preserves the disabled state", () => {
    const onChange = vi.fn();
    const input = renderBooleanEditor({ disabled: true, onChange });

    expect(input.disabled).toBe(true);
    input.click();
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("built-in option activity property editors", () => {
  it("emits the original typed dropdown value", () => {
    const onChange = vi.fn();
    renderEditor("studio.property.dropdown", { onChange });
    const select = container.querySelector("select")!;

    flushSync(() => {
      select.value = Array.from(select.options).find(option => option.textContent === "High")!.value;
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });

    expect(onChange).toHaveBeenCalledWith(10);
  });

  it("renders a stale scalar value as an unavailable option without changing it", () => {
    const onChange = vi.fn();
    renderEditor("studio.property.dropdown", { value: 99, onChange });

    const selected = container.querySelector<HTMLOptionElement>("option:checked")!;
    expect(selected.textContent).toBe("99 (unavailable)");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("renders stale collection values as checked unavailable entries that can be removed", () => {
    const onChange = vi.fn();
    renderEditor("studio.property.multiselect", { value: [1, 99], onChange });

    const labels = Array.from(container.querySelectorAll("label")).map(label => label.textContent);
    expect(labels).toContain("99 (unavailable)");
    const stale = Array.from(container.querySelectorAll<HTMLInputElement>("input[type='checkbox']"))
      .find(input => input.parentElement?.textContent === "99 (unavailable)")!;
    expect(stale.checked).toBe(true);

    flushSync(() => stale.click());
    expect(onChange).toHaveBeenCalledWith([1]);
  });

  it("does not report an empty provider-backed checklist while its options are unresolved", () => {
    renderEditor("studio.property.multiselect", {
      descriptor: {
        name: "Methods",
        displayName: "Methods",
        typeName: "System.Collections.Generic.ICollection`1[System.String]",
        uiSpecifications: { optionsProvider: { key: "catalog.methods", dependsOn: [] } }
      },
      value: []
    });

    expect(container.textContent).not.toContain("No options available.");
  });
});
