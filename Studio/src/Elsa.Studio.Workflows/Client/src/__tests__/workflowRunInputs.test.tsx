import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { WorkflowInput } from "../workflowTypes";
import { parseWorkflowRunInputs } from "../workflowRunInputs";
import { WorkflowRunInputDialog } from "../workflow-editor/WorkflowRunInputDialog";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

describe("workflow run inputs", () => {
  it("blocks a missing required value and serializes valid scalar values by input name", () => {
    const inputs = [
      workflowInput("greeting", "Greeting", "String", true),
      workflowInput("attempts", "Attempts", "Int32", false)
    ];

    expect(parseWorkflowRunInputs(inputs, {})).toEqual({
      values: {},
      errors: { greeting: "Greeting is required." }
    });

    expect(parseWorkflowRunInputs(inputs, { greeting: "Hello", attempts: "3" })).toEqual({
      values: { Greeting: "Hello", Attempts: 3 },
      errors: {}
    });
  });

  it("validates booleans, declared complex values, and every collection kind", () => {
    const inputs = [
      workflowInput("enabled", "Enabled", "Boolean", true),
      workflowInput("payload", "Payload", "Contoso.Order", false),
      workflowInput("array", "Array", "Int32", false, "Array"),
      workflowInput("list", "List", "String", false, "List"),
      workflowInput("set", "Set", "Boolean", false, "HashSet")
    ];

    expect(parseWorkflowRunInputs(inputs, {
      enabled: "yes",
      payload: "not json",
      array: "[1, \"two\"]",
      list: "{}",
      set: "[true, false]"
    }).errors).toEqual({
      enabled: "Choose true or false.",
      payload: "Enter valid JSON.",
      array: "Item 2 must be a whole number.",
      list: "Enter a JSON array.",
    });

    expect(parseWorkflowRunInputs(inputs, {
      enabled: "false",
      payload: "{\"id\":42}",
      array: "[1, 2]",
      list: "[\"one\", \"two\"]",
      set: "[true, false]"
    })).toEqual({
      values: {
        Enabled: false,
        Payload: { id: 42 },
        Array: [1, 2],
        List: ["one", "two"],
        Set: [true, false]
      },
      errors: {}
    });
  });

  it("omits blank optional values and reports declared scalar type errors", () => {
    const inputs = [
      workflowInput("optional", "Optional", "String", false),
      workflowInput("whole", "Whole", "Int32", false),
      workflowInput("when", "When", "DateTime", false),
      workflowInput("id", "ID", "Guid", false)
    ];

    expect(parseWorkflowRunInputs(inputs, {
      optional: "",
      whole: "3.5",
      when: "yesterday-ish",
      id: "not-a-guid"
    })).toEqual({
      values: {},
      errors: {
        whole: "Enter a whole number.",
        when: "Enter a valid date and time.",
        id: "Enter a valid GUID."
      }
    });
  });

  it("does not treat JSON null as a supplied required complex value", () => {
    expect(parseWorkflowRunInputs(
      [workflowInput("payload", "Payload", "Contoso.Order", true)],
      { payload: "null" }
    )).toEqual({ values: {}, errors: { payload: "Payload is required." } });
  });

  it("keeps required and type errors in the dialog until the form is valid", () => {
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[
          workflowInput("greeting", "Greeting", "String", true),
          workflowInput("attempts", "Attempts", "Int32", false)
        ]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    submit(container);
    expect(container.textContent).toContain("Greeting is required.");
    expect(onSubmit).not.toHaveBeenCalled();

    fill(container.querySelector("input[aria-label='Greeting']"), "Hello");
    fill(container.querySelector("input[aria-label='Attempts']"), "3.5");
    submit(container);
    expect(container.textContent).toContain("Enter a whole number.");
    expect(onSubmit).not.toHaveBeenCalled();

    fill(container.querySelector("input[aria-label='Attempts']"), "3");
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Greeting: "Hello", Attempts: 3 });
  });

  it("cancels without submitting input values", () => {
    const onSubmit = vi.fn();
    const onCancel = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[workflowInput("greeting", "Greeting", "String", true)]}
        onSubmit={onSubmit}
        onCancel={onCancel}
      />
    );

    fill(container.querySelector("input[aria-label='Greeting']"), "Unsaved");
    button(container, "Cancel").click();

    expect(onCancel).toHaveBeenCalledOnce();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("renders collection and boolean controls and omits an untouched optional input", () => {
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[
          workflowInput("enabled", "Enabled", "Boolean", true),
          workflowInput("tags", "Tags", "String", true, "HashSet"),
          workflowInput("note", "Note", "String", false)
        ]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    fill(container.querySelector("select[aria-label='Enabled']"), "false");
    fill(container.querySelector("textarea[aria-label='Tags']"), "not-json");
    submit(container);
    expect(container.textContent).toContain("Enter a valid JSON array.");

    fill(container.querySelector("textarea[aria-label='Tags']"), "[\"qa\", \"studio\"]");
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Enabled: false, Tags: ["qa", "studio"] });
  });

  it("focuses the first field, traps focus, and supports Escape cancellation", () => {
    const onCancel = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[workflowInput("greeting", "Greeting", "String", true)]}
        onSubmit={vi.fn()}
        onCancel={onCancel}
      />
    );
    const first = container.querySelector<HTMLInputElement>("input[aria-label='Greeting']")!;
    const submitButton = button(container, "Run workflow");

    expect(document.activeElement).toBe(first);
    submitButton.focus();
    submitButton.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function fill(element: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null, value: string) {
  if (!element) throw new Error("Input not found");
  const prototype = element instanceof HTMLTextAreaElement
    ? HTMLTextAreaElement.prototype
    : element instanceof HTMLSelectElement
      ? HTMLSelectElement.prototype
      : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
  flushSync(() => element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })));
}

function submit(container: HTMLElement) {
  flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}

function button(container: HTMLElement, label: string) {
  const result = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.trim() === label);
  if (!result) throw new Error(`Button ${label} not found`);
  return result;
}

function workflowInput(
  referenceKey: string,
  name: string,
  alias: string,
  isRequired: boolean,
  collectionKind: WorkflowInput["type"]["collectionKind"] = "Single"
): WorkflowInput {
  return {
    referenceKey,
    name,
    displayName: name,
    description: "",
    category: "",
    uiHint: "",
    storageDriverType: null,
    type: { alias, collectionKind },
    isRequired
  };
}
