import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionEditorContext
} from "@elsa-workflows/studio-sdk";
import { createObjectExpressionEditorContribution } from "../objectExpressionEditor";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

describe("Object Expression Editor Contribution", () => {
  it("owns Object inline and expanded surfaces and creates type-aware defaults", () => {
    const contribution = createObjectExpressionEditorContribution(() => []);

    expect(contribution.supports(context(descriptor("Payload", "System.Object")))).toBe(true);
    expect(contribution.supports({ ...context(descriptor("Payload", "System.Object")), syntax: "Record" })).toBe(false);
    expect(contribution.surfaces.inline).toBeDefined();
    expect(contribution.surfaces.expanded).toBeDefined();
    expect(contribution.createDefaultValue?.(context(descriptor("Payload", "System.Object")))).toEqual({});
    expect(contribution.createDefaultValue?.(context(descriptor(
      "Items",
      "System.Collections.Generic.ICollection`1[System.String]"
    )))).toEqual([]);
  });

  it("uses the existing collection-scoped Activity Property Editor Contribution", () => {
    const collectionEditor: StudioActivityPropertyEditorContribution = {
      id: "test.collection-options",
      supports: (_descriptor, editorContext) => editorContext.scope === "collection",
      component: ({ value, onChange }) => (
        <button type="button" onClick={() => onChange([...(value as unknown[]), "selected"])}>
          Select collection option
        </button>
      )
    };
    const contribution = createObjectExpressionEditorContribution(() => [collectionEditor]);
    const Inline = contribution.surfaces.inline!;
    const onChange = vi.fn();
    const input = descriptor("Items", "System.Collections.Generic.ICollection`1[System.String]");
    const container = render(
      <Inline
        descriptor={input}
        syntax="Object"
        value={["existing"]}
        context={context(input)}
        onChange={onChange}
      />
    );

    flushSync(() => container.querySelector<HTMLButtonElement>("button")?.click());

    expect(onChange).toHaveBeenCalledWith(["existing", "selected"]);
    expect(container.querySelector(".wf-collection-editor")).toBeNull();
  });

  it("preserves collection repeater editing, add, reorder, and remove behavior", () => {
    const textEditor: StudioActivityPropertyEditorContribution = {
      id: "test.text",
      supports: (_descriptor, editorContext) => editorContext.scope === "element",
      component: ({ value, onChange }) => (
        <input aria-label="Collection item value" value={String(value ?? "")} onChange={event => onChange(event.target.value)} />
      )
    };
    const contribution = createObjectExpressionEditorContribution(() => [textEditor]);
    const Inline = contribution.surfaces.inline!;
    const input = descriptor("Items", "System.Collections.Generic.ICollection`1[System.String]");
    const changes: unknown[][] = [];

    function Harness() {
      const [value, setValue] = React.useState<unknown[]>(["A", "B"]);
      return (
        <Inline
          descriptor={input}
          syntax="Object"
          value={value}
          context={context(input)}
          onChange={next => {
            changes.push(next as unknown[]);
            setValue(next as unknown[]);
          }}
        />
      );
    }

    const container = render(<Harness />);
    flushSync(() => buttonByText(container, "Add item")?.click());
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Move Items item 3 up']")?.click());
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Remove Items item 2']")?.click());
    changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Collection item value']")!, "Edited");

    expect(changes).toEqual([
      ["A", "B", ""],
      ["A", "", "B"],
      ["A", "B"],
      ["Edited", "B"]
    ]);
  });

  it("keeps invalid JSON visible with diagnostics and preserves the last valid structured value", () => {
    const contribution = createObjectExpressionEditorContribution(() => []);
    const Expanded = contribution.surfaces.expanded!;
    const onChange = vi.fn();
    const input = descriptor("Payload", "System.Object");
    const container = render(
      <Expanded
        descriptor={input}
        syntax="Object"
        value={{ enabled: true }}
        context={{ ...context(input), surface: "expanded" }}
        onChange={onChange}
      />
    );
    const editor = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload JSON value']")!;

    change(editor, '{"enabled":');

    expect(editor.value).toBe('{"enabled":');
    expect(container.querySelector("[role='status']")?.textContent).toContain("Invalid JSON");
    expect(onChange).not.toHaveBeenCalled();

    change(editor, '{"enabled":false,"count":2}');

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith({ enabled: false, count: 2 });
    expect(container.querySelector("[role='status']")).toBeNull();
  });

  it("rejects primitive JSON roots that cannot satisfy the backend Object wire contract", () => {
    const contribution = createObjectExpressionEditorContribution(() => []);
    const Expanded = contribution.surfaces.expanded!;
    const onChange = vi.fn();
    const input = descriptor("Payload", "System.Object");
    const container = render(
      <Expanded
        descriptor={input}
        syntax="Object"
        value={{}}
        context={{ ...context(input), surface: "expanded" }}
        onChange={onChange}
      />
    );
    const editor = container.querySelector<HTMLTextAreaElement>("textarea")!;

    change(editor, '"hello"');
    expect(container.querySelector("[role='status']")?.textContent).toContain("Top-level JSON must be an object or array");
    expect(onChange).not.toHaveBeenCalled();

    change(editor, "null");
    expect(container.querySelector("[role='status']")?.textContent).toContain("Top-level JSON must be an object or array");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("decodes backend Object JSON strings and keeps malformed stored JSON editable", async () => {
    const contribution = createObjectExpressionEditorContribution(() => []);
    const Expanded = contribution.surfaces.expanded!;
    const input = descriptor("Payload", "System.Object");
    const container = render(
      <Expanded
        descriptor={input}
        syntax="Object"
        value={'{"enabled":true}'}
        context={{ ...context(input), surface: "expanded" }}
        onChange={() => {}}
      />
    );

    expect(container.querySelector<HTMLTextAreaElement>("textarea")?.value).toContain('"enabled": true');

    flushSync(() => active!.root.render(
      <Expanded
        descriptor={input}
        syntax="Object"
        value={'{"enabled":'}
        context={{ ...context(input), surface: "expanded" }}
        onChange={() => {}}
      />
    ));
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));

    expect(container.querySelector<HTMLTextAreaElement>("textarea")?.value).toBe('{"enabled":');
    expect(container.querySelector("[role='status']")?.textContent).toContain("Invalid JSON");
  });
});

function descriptor(name: string, typeName: string): StudioActivityInputDescriptor {
  return { name, displayName: name, typeName, isWrapped: true };
}

function context(input: StudioActivityInputDescriptor): StudioExpressionEditorContext {
  return {
    syntax: "Object",
    surface: "inline",
    descriptor: input,
    activity: {},
    expressionDescriptors: [],
    readOnly: false
  };
}

function render(node: React.ReactNode) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(node));
  active = { root, container };
  return container;
}

function change(input: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function changeInput(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function buttonByText(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes(label));
}
