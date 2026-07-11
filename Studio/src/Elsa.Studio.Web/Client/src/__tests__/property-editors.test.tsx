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

describe("built-in boolean activity property editor", () => {
  it("renders an accessible off switch with the property name", () => {
    const input = renderBooleanEditor();

    expect(input).not.toBeNull();
    expect(input.type).toBe("checkbox");
    expect(input.checked).toBe(false);
    expect(input.getAttribute("aria-label")).toBe("Can start workflow");
    expect(container.querySelector(".studio-property-switch-state")?.textContent).toBe("Disabled");
  });

  it("renders only the literal true value as on", () => {
    expect(renderBooleanEditor({ value: true }).checked).toBe(true);
    expect(container.querySelector(".studio-property-switch-state")?.textContent).toBe("Enabled");

    expect(renderBooleanEditor({ value: "true" }).checked).toBe(false);
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
