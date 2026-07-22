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

function renderTypedEditor(id: string, descriptor: StudioActivityInputDescriptor, props: Partial<StudioActivityPropertyEditorProps> = {}) {
  const contribution = builtInPropertyEditors.find(editor => editor.id === id);
  if (!contribution) throw new Error(`Built-in property editor '${id}' was not registered.`);
  const Editor = contribution.component;
  flushSync(() => root.render(
    <Editor
      descriptor={descriptor}
      value={undefined}
      context={{ activity: {}, expressionDescriptors: [] }}
      onChange={() => {}}
      {...props}
    />
  ));
  return container.querySelector<HTMLInputElement>("input")!;
}

function resolves(id: string, descriptor: StudioActivityInputDescriptor, scope: "element" | "collection" = "element") {
  const contribution = builtInPropertyEditors.find(editor => editor.id === id)!;
  return contribution.supports(descriptor, { activity: {}, expressionDescriptors: [], scope });
}

// React installs a value tracker on controlled inputs; assigning `.value` directly updates the tracker
// so React skips onChange. Drive changes through the native setter to fire a genuine change event.
function typeInto(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")!.set!;
  flushSync(() => {
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

describe("numeric activity property editor", () => {
  const intDescriptor: StudioActivityInputDescriptor = { name: "ExpectedStatus", displayName: "Expected status", typeName: "System.Int32" };

  it("claims numeric CLR types in element scope but not string or option-backed inputs", () => {
    expect(resolves("studio.property.number", intDescriptor)).toBe(true);
    expect(resolves("studio.property.number", { name: "x", typeName: "System.Double" })).toBe(true);
    expect(resolves("studio.property.number", { name: "x", typeName: "System.String" })).toBe(false);
    expect(resolves("studio.property.number", { ...intDescriptor, uiSpecifications: { options: [{ label: "A", value: 1 }] } })).toBe(false);
  });

  it("emits a parsed number and labels the input accessibly", () => {
    const onChange = vi.fn();
    const input = renderTypedEditor("studio.property.number", intDescriptor, { onChange });
    expect(input.getAttribute("aria-label")).toBe("Expected status");

    typeInto(input, "404");
    expect(onChange).toHaveBeenLastCalledWith(404);
  });

  it("rejects non-numeric text with an inline error and does not emit it", () => {
    const onChange = vi.fn();
    const input = renderTypedEditor("studio.property.number", intDescriptor, { onChange });

    typeInto(input, "20a");
    expect(onChange).not.toHaveBeenCalled();
    expect(container.querySelector(".studio-property-error")?.textContent).toMatch(/valid/i);
    expect(input.getAttribute("aria-invalid")).toBe("true");
  });

  it("rejects a fractional value for an integer type", () => {
    const onChange = vi.fn();
    const input = renderTypedEditor("studio.property.number", intDescriptor, { onChange });
    typeInto(input, "1.5");
    expect(onChange).not.toHaveBeenCalled();
    expect(container.querySelector(".studio-property-error")?.textContent).toMatch(/whole/i);
  });
});

describe("TimeSpan activity property editor", () => {
  const descriptor: StudioActivityInputDescriptor = { name: "Timeout", displayName: "Timeout", typeName: "System.TimeSpan" };

  it("claims TimeSpan inputs and shows a format hint", () => {
    expect(resolves("studio.property.timespan", descriptor)).toBe(true);
    const input = renderTypedEditor("studio.property.timespan", descriptor);
    expect(input.getAttribute("placeholder")).toBe("hh:mm:ss");
    expect(container.querySelector(".studio-property-hint")?.textContent).toMatch(/hh:mm:ss/);
  });

  it("accepts a valid duration and flags a malformed one", () => {
    const onChange = vi.fn();
    const input = renderTypedEditor("studio.property.timespan", descriptor, { onChange });

    typeInto(input, "00:00:30");
    expect(onChange).toHaveBeenLastCalledWith("00:00:30");
    expect(container.querySelector(".studio-property-error")).toBeNull();

    typeInto(input, "30 seconds");
    expect(container.querySelector(".studio-property-error")?.textContent).toMatch(/hh:mm:ss/);
  });
});

describe("enum text fallback", () => {
  it("adds a title hint when an enum input has no options payload", () => {
    const input = renderTypedEditor("studio.property.singleline", {
      name: "ResponseMode",
      displayName: "Response mode",
      typeName: "Elsa.Http.ResponseMode",
      uiHint: "enum"
    });
    expect(input.getAttribute("title")).toMatch(/enum member/i);
    expect(input.getAttribute("aria-label")).toBe("Response mode");
  });

  it("does not add the enum hint once options are present (a real select is used instead)", () => {
    const enumWithOptions: StudioActivityInputDescriptor = {
      name: "ResponseMode",
      typeName: "Elsa.Http.ResponseMode",
      uiHint: "enum",
      uiSpecifications: { options: [{ label: "Default", value: "Default" }] }
    };
    // With options the dropdown editor wins; the singleline fallback should not add its enum hint.
    expect(resolves("studio.property.dropdown", enumWithOptions)).toBe(true);
  });
});
