import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityInputDescriptor, StudioActivityPropertyEditorContribution } from "@elsa-workflows/studio-sdk";
import { DictionaryValueEditor } from "../DictionaryValueEditor";
import { clearDictionaryEditorSession, clearDictionaryEditorSessionScope } from "../dictionaryEditorSession";

let root: Root;
let container: HTMLDivElement;
let testId = 0;
let sessionKey = "";

beforeEach(() => {
  testId++;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  clearDictionaryEditorSession(sessionKey);
  vi.restoreAllMocks();
});

describe("DictionaryValueEditor", () => {
  it("keeps a newly added blank row as a private draft", () => {
    const onChange = vi.fn();
    renderEditor({ value: {}, onChange });

    click(button("Add entry"));

    expect(container.querySelector(".wf-dictionary-empty")).toBeNull();
    expect(keyInputs()).toHaveLength(1);
    expect(keyInputs()[0].getAttribute("aria-invalid")).toBe("true");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("emits a plain object when a string row is completed", () => {
    const onChange = vi.fn();
    renderEditor({ value: {}, onChange });
    click(button("Add entry"));

    fill(keyInputs()[0], "Content-Type");
    fill(valueInputs()[0], "application/json");

    expect(onChange).toHaveBeenLastCalledWith({ "Content-Type": "application/json" });
  });

  it("does not emit a case-insensitive duplicate-key edit", () => {
    const onChange = vi.fn();
    renderEditor({
      value: { "Content-Type": "application/json", Accept: "text/plain" },
      keyComparison: "ordinalIgnoreCase",
      onChange
    });

    fill(keyInputs()[1], "content-type");

    expect(onChange).not.toHaveBeenCalled();
    expect(container.querySelectorAll(".wf-dictionary-row.invalid")).toHaveLength(2);
  });

  it("limits committed inline rows to five and reports the remainder", () => {
    renderEditor({ value: { one: "1", two: "2", three: "3", four: "4", five: "5", six: "6" } });

    expect(container.querySelectorAll(".wf-dictionary-row")).toHaveLength(5);
    expect(container.querySelector(".wf-dictionary-more")?.textContent).toBe("1 more entry");
  });

  it("removes an entry and Undo restores its value and position", () => {
    const onChange = vi.fn();
    renderEditor({ value: { first: "1", second: "2" }, onChange });

    click(container.querySelector<HTMLButtonElement>("button[aria-label='Remove first']")!);
    expect(onChange).toHaveBeenLastCalledWith({ second: "2" });
    click(button("Undo"));

    expect(onChange).toHaveBeenLastCalledWith({ first: "1", second: "2" });
    expect(keyInputs().map(input => input.value)).toEqual(["first", "second"]);
  });

  it("filters expanded rows by key or displayed scalar value", () => {
    renderEditor({ value: { Alpha: "first", Beta: "second" }, variant: "expanded" });
    const filter = container.querySelector<HTMLInputElement>("input[aria-label='Filter Headers entries']")!;

    fill(filter, "second");

    expect(keyInputs().map(input => input.value)).toEqual(["Beta"]);
  });

  it("commits valid JSON but preserves the last valid value for malformed or duplicate JSON", () => {
    const onChange = vi.fn();
    renderEditor({ value: { initial: "value" }, variant: "expanded", onChange });
    click(tab("JSON"));
    const editor = jsonEditor();

    fill(editor, '{"updated":"yes"}');
    expect(onChange).toHaveBeenLastCalledWith({ updated: "yes" });
    const validCallCount = onChange.mock.calls.length;

    fill(editor, '{"updated":');
    expect(onChange).toHaveBeenCalledTimes(validCallCount);
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("Invalid JSON");

    fill(editor, '{"same":"first","same":"second"}');
    expect(onChange).toHaveBeenCalledTimes(validCallCount);
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("duplicated");
  });

  it("dismisses a stale removal Undo when raw JSON editing begins", () => {
    renderEditor({ value: { first: "1", second: "2" }, variant: "expanded" });
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Remove first']")!);
    expect(button("Undo")).toBeDefined();
    click(tab("JSON"));
    fill(jsonEditor(), '{"replacement":"value"}');
    expect([...container.querySelectorAll("button")].some(candidate => candidate.textContent === "Undo")).toBe(false);
  });

  it("formats valid JSON explicitly and keeps read-only mutation controls disabled", () => {
    renderEditor({ value: { first: "1" }, variant: "expanded" });
    click(tab("JSON"));
    fill(jsonEditor(), '{"first":"1","second":"2"}');
    click(button("Format JSON"));
    expect(jsonEditor().value).toBe('{\n  "first": "1",\n  "second": "2"\n}');

    unmountAndRemount();
    renderEditor({ value: { first: "1" }, variant: "expanded", disabled: true });
    expect(button("Add entry").disabled).toBe(true);
    expect(container.querySelector<HTMLButtonElement>("button[aria-label='Remove first']")?.disabled).toBe(true);
    expect(keyInputs()[0].disabled).toBe(true);
    click(tab("JSON"));
    expect(jsonEditor().readOnly).toBe(true);
    expect(button("Copy JSON").disabled).toBe(false);
  });

  it("retains incomplete table and invalid JSON drafts across editor surfaces", () => {
    const onChange = vi.fn();
    renderEditor({ value: { Existing: "value" }, onChange });
    click(button("Add entry"));
    fill(keyInputs()[1], "Existing");
    unmountAndRemount(false);

    renderEditor({ value: { Existing: "value" }, variant: "expanded", onChange });
    expect(keyInputs().map(input => input.value)).toEqual(["Existing", "Existing"]);
    click(tab("JSON"));
    click(button("Discard draft"));
    fill(jsonEditor(), '{"invalid":');
    unmountAndRemount(false);

    renderEditor({ value: { Existing: "value" }, variant: "expanded", onChange });
    expect(tab("JSON").getAttribute("aria-selected")).toBe("true");
    expect(jsonEditor().value).toBe('{"invalid":');
    expect(onChange).not.toHaveBeenCalled();
  });

  it("delegates typed values and expands complex values without a nested dialog", () => {
    const onChange = vi.fn();
    const booleanEditor: StudioActivityPropertyEditorContribution = {
      id: "test.boolean",
      supports: (descriptor, context) => descriptor.typeName === "System.Boolean" && context.scope === "element",
      component: ({ value, onChange: change }) => (
        <button type="button" onClick={() => change(!value)}>Toggle typed value</button>
      )
    };
    renderEditor({ value: { enabled: true }, valueTypeName: "System.Boolean", editors: [booleanEditor], onChange });
    click(button("Toggle typed value"));
    expect(onChange).toHaveBeenLastCalledWith({ enabled: false });

    unmountAndRemount();
    renderEditor({ value: { payload: { count: 1 } }, valueTypeName: "Company.Payload", variant: "expanded", onChange });
    click(button("Edit value JSON"));
    let complexEditor = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Value 1 JSON']")!;
    expect(complexEditor).not.toBeNull();
    fill(complexEditor, "not json");
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("INVALID_VALUE_JSON");
    click(tab("JSON"));
    expect(container.querySelector(".wf-dictionary-tab-guard")).not.toBeNull();
    expect(tab("Table").getAttribute("aria-selected")).toBe("true");

    unmountAndRemount(false);
    renderEditor({ value: { payload: { count: 1 } }, valueTypeName: "Company.Payload", variant: "expanded", onChange });
    click(button("Edit value JSON"));
    complexEditor = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Value 1 JSON']")!;
    expect(complexEditor.value).toBe("not json");
    fill(complexEditor, '{"count":2}');
    expect(onChange).toHaveBeenLastCalledWith({ payload: { count: 2 } });
    expect(container.querySelectorAll("[role='dialog']")).toHaveLength(0);
  });

  it("isolates private drafts by workflow session scope and clears a completed scope", () => {
    renderEditor({ value: {}, sessionScope: "workflow-a" });
    click(button("Add entry"));
    unmountAndRemount(false);

    renderEditor({ value: {}, sessionScope: "workflow-b" });
    expect(keyInputs()).toHaveLength(0);
    clearDictionaryEditorSessionScope("workflow-a");
    unmountAndRemount(false);

    renderEditor({ value: {}, sessionScope: "workflow-a" });
    expect(keyInputs()).toHaveLength(0);
  });
});

function renderEditor({
  value,
  variant = "inline",
  disabled = false,
  keyComparison = "ordinal",
  valueTypeName = "System.String",
  editors = [],
  sessionScope = "dictionary-test-scope",
  onChange = vi.fn()
}: {
  value: unknown;
  variant?: "inline" | "expanded";
  disabled?: boolean;
  keyComparison?: "ordinal" | "ordinalIgnoreCase";
  valueTypeName?: string;
  editors?: StudioActivityPropertyEditorContribution[];
  sessionScope?: string;
  onChange?: (value: unknown) => void;
}) {
  const nodeId = `dictionary-test-${testId}`;
  sessionKey = `${sessionScope}:${nodeId}:Headers`;
  const input: StudioActivityInputDescriptor = {
    name: "Headers",
    displayName: "Headers",
    typeName: `System.Collections.Generic.IDictionary\`2[System.String,${valueTypeName}]`,
    uiSpecifications: { dictionary: { keyComparison } }
  };
  flushSync(() => root.render(
    <DictionaryValueEditor
      input={input}
      sessionScopeKey={sessionScope}
      valueTypeName={valueTypeName}
      value={value}
      editors={editors}
      context={{ activity: { nodeId }, expressionDescriptors: [], readOnly: disabled }}
      disabled={disabled}
      variant={variant}
      onChange={onChange}
    />
  ));
}

function unmountAndRemount(clearSession = true) {
  flushSync(() => root.unmount());
  if (clearSession) clearDictionaryEditorSession(sessionKey);
  container.remove();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
}

function keyInputs() {
  return [...container.querySelectorAll<HTMLInputElement>(".wf-dictionary-cell.key input")];
}

function valueInputs() {
  return [...container.querySelectorAll<HTMLInputElement>(".wf-dictionary-cell.value input")];
}

function jsonEditor() {
  return container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Headers dictionary JSON']")!;
}

function button(label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(candidate => candidate.textContent?.includes(label))!;
}

function tab(label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("[role='tab']")]
    .find(candidate => candidate.textContent?.startsWith(label))!;
}

function click(element: HTMLElement) {
  flushSync(() => element.click());
}

function fill(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
