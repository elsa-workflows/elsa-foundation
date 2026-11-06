import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { VariablesEditor } from "../WorkflowPropertiesView";

let mounted: { root: Root; container: HTMLElement } | null = null;
afterEach(() => {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

function render(editor: string, initial: string | null, alias = "String", kind = "List", editors?: Record<string, string>) {
  const changed = vi.fn();
  function Harness() {
    const [items, setItems] = useState<Record<string, unknown>[]>([{
      referenceKey: "items", name: "Items", type: { alias, collectionKind: kind },
      storageDriverType: null, default: initial === null ? null : { expressionType: "Literal", value: initial }
    }]);
    return <VariablesEditor items={items} typeOptions={Object.keys(editors ?? { [alias]: editor }).map(value => ({ value, label: value }))}
      storageOptions={null} editorForAlias={selected => editors?.[selected] ?? editor} onChange={next => {
        changed(next);
        setItems(next as Record<string, unknown>[]);
      }} />;
  }
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(<Harness />));
  return { container, changed };
}

function edit(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const prototype = input.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function click(button: Element) {
  flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function item(container: HTMLElement, index: number) {
  return container.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[aria-label='Variable default value item ${index}']`)!;
}

function saved(changed: ReturnType<typeof vi.fn>) {
  return changed.mock.calls.at(-1)![0][0].default;
}

describe("collection variable starting values", () => {
  it.each(["Array", "List", "HashSet"])("edits %s string items without interpreting commas or quotes", kind => {
    const { container, changed } = render("text", JSON.stringify(["a,b", 'quoted "value"']), "String", kind);
    expect(item(container, 1).value).toBe("a,b");
    expect(item(container, 2).value).toBe('quoted "value"');
    expect(changed).not.toHaveBeenCalled();

    edit(item(container, 1), "first,second");

    expect(saved(changed)).toEqual({ expressionType: "Literal", value: JSON.stringify(["first,second", 'quoted "value"']) });
    expect(item(container, 1).value).toBe("first,second");
  });

  it("adds items and preserves an explicitly emptied list", () => {
    const { container, changed } = render("text", null);
    expect(container.textContent).toContain("No items yet.");
    click(container.querySelector(".wf-collection-add")!);
    edit(item(container, 1), "hello");
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: '["hello"]' });
    click(container.querySelector("[aria-label='Remove Variable default value item 1']")!);
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: "[]" });
  });

  it("uses the numeric element editor and writes JSON numbers", () => {
    const { container, changed } = render("number", "[1,2]", "Int32");
    expect(item(container, 1).getAttribute("type")).toBe("number");
    edit(item(container, 2), "3");
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: "[1,3]" });
  });

  it("uses Boolean element controls", () => {
    const { container, changed } = render("checkbox", "[true,false]", "Boolean");
    expect(item(container, 2).getAttribute("type")).toBe("checkbox");
    click(item(container, 2));
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: "[true,true]" });
  });

  it.each([
    ["checkbox", "Boolean", '["true"]', '"true"', "JSON boolean"],
    ["number", "Int32", '["bad"]', '"bad"', "JSON number"],
    ["text", "String", "[1]", "1", "JSON string"]
  ])("shows loaded %s item type mismatches as their exact JSON token", (editor, alias, initial, token, expected) => {
    const { container, changed } = render(editor, initial, alias);
    const input = item(container, 1);
    expect(input.tagName).toBe("TEXTAREA");
    expect(input.value).toBe(token);
    expect(input.getAttribute("aria-invalid")).toBe("true");
    const error = container.querySelector("[role='alert']")!;
    expect(error.textContent).toContain(expected);
    expect(input.getAttribute("aria-describedby")).toBe(error.id);
    expect(changed).not.toHaveBeenCalled();
  });

  it("recovers a loaded Boolean string without coercing or replacing its focused raw editor", () => {
    const { container, changed } = render("checkbox", '["true"]', "Boolean");
    const raw = item(container, 1);
    raw.focus();
    edit(raw, "true");
    expect(item(container, 1)).toBe(raw);
    expect(document.activeElement).toBe(raw);
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: "[true]" });
    expect(container.querySelector("[role='alert']")).toBeNull();
    flushSync(() => raw.dispatchEvent(new FocusEvent("focusout", { bubbles: true })));
    expect(item(container, 1).getAttribute("type")).toBe("checkbox");
    expect((item(container, 1) as HTMLInputElement).checked).toBe(true);
  });

  it("rechecks an edited token against the newly selected element type", () => {
    const { container, changed } = render("text", '["start"]', "String", "List", { String: "text", Boolean: "checkbox" });
    edit(item(container, 1), "true");
    const type = container.querySelector<HTMLSelectElement>("select[aria-label='Variable type']")!;
    flushSync(() => {
      type.value = "Boolean";
      type.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(item(container, 1).tagName).toBe("TEXTAREA");
    expect(item(container, 1).value).toBe('"true"');
    expect(item(container, 1).getAttribute("aria-invalid")).toBe("true");
    expect(container.querySelector("[role='alert']")?.textContent).toContain("JSON boolean");
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: '["true"]' });
  });

  it("keeps an incomplete numeric edit focused and exposes the stored token until recovery", () => {
    const { container, changed } = render("number", "[1,2]", "Int32");
    const input = item(container, 2);
    input.focus();
    edit(input, "");
    expect(item(container, 2)).toBe(input);
    expect(document.activeElement).toBe(input);
    expect(input.getAttribute("type")).toBe("number");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(container.querySelector("[role='alert']")?.textContent).toContain('Stored JSON: ""');
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: '[1,""]' });
    edit(input, "3");
    expect(item(container, 2)).toBe(input);
    expect(document.activeElement).toBe(input);
    expect(input.getAttribute("aria-invalid")).toBeNull();
    expect(container.querySelector("[role='alert']")).toBeNull();
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: "[1,3]" });
  });

  it.each(["2026-99-99", "2026-02-30", "2025-02-29"])("keeps an impossible date %s visible as raw JSON with an associated error", value => {
    const { container, changed } = render("date", JSON.stringify([value]), "DateTime");
    const raw = item(container, 1);
    expect(raw.tagName).toBe("TEXTAREA");
    expect(raw.value).toBe(JSON.stringify(value));
    expect(raw.getAttribute("aria-invalid")).toBe("true");
    const error = container.querySelector("[role='alert']")!;
    expect(error.textContent).toContain("valid calendar date");
    expect(raw.getAttribute("aria-describedby")).toBe(error.id);
    expect(changed).not.toHaveBeenCalled();
  });

  it("retains focus and the corrected date token before returning to a native control", () => {
    const { container, changed } = render("date", '["2025-02-29"]', "DateTime");
    const raw = item(container, 1);
    raw.focus();
    edit(raw, '"2024-02-29"');
    expect(item(container, 1)).toBe(raw);
    expect(document.activeElement).toBe(raw);
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: '["2024-02-29"]' });
    expect(container.querySelector("[role='alert']")).toBeNull();
    flushSync(() => raw.dispatchEvent(new FocusEvent("focusout", { bubbles: true })));
    expect(item(container, 1).getAttribute("type")).toBe("date");
    expect(item(container, 1).value).toBe("2024-02-29");
  });

  it("loads a valid leap date in the native control without changing it", () => {
    const { container, changed } = render("date", '["2024-02-29"]', "DateTime");
    expect(item(container, 1).getAttribute("type")).toBe("date");
    expect(item(container, 1).value).toBe("2024-02-29");
    expect(container.querySelector("[role='alert']")).toBeNull();
    expect(changed).not.toHaveBeenCalled();
  });

  it("keeps the shared scalar date fallback visible for an impossible calendar date", () => {
    const { container, changed } = render("date", "2026-02-30", "DateTime", "Single");
    const input = container.querySelector<HTMLInputElement>("[aria-label='Variable default value']")!;
    expect(input.type).toBe("text");
    expect(input.value).toBe("2026-02-30");
    expect(changed).not.toHaveBeenCalled();
  });

  it("keeps invalid legacy text visible until it is corrected to an array", () => {
    const { container, changed } = render("text", "a, b");
    const raw = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Variable default value']")!;
    expect(raw.value).toBe("a, b");
    expect(raw.getAttribute("aria-invalid")).toBe("true");
    expect(container.querySelector("[role='alert']")?.textContent).toContain("JSON array");
    expect(changed).not.toHaveBeenCalled();
    edit(raw, '["a,b"]');
    expect(item(container, 1).value).toBe("a,b");
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: '["a,b"]' });
  });

  it("keeps JSON item syntax and exact numeric tokens through edits", () => {
    const first = '{"Id":9007199254740993,"amount":1234567890.123456789012345}';
    const { container, changed } = render("json", `[${first},9007199254740995]`, "System.Text.Json.JsonElement");
    expect(item(container, 1).value).toBe(first);
    edit(item(container, 2), "9007199254740997");
    expect(saved(changed)).toEqual({ expressionType: "Literal", value: `[${first},9007199254740997]` });
    edit(item(container, 1), "{");
    expect(item(container, 1).value).toBe("{");
    expect(item(container, 2).value).toBe("9007199254740997");
    expect(container.querySelector("[role='alert']")?.textContent).toContain("JSON");
  });
});
