// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { FormDesigner } from "../FormDesigner";
import { parseTaskFormSource, serializeTaskFormSource, taskFieldKinds, validateTaskFields } from "../formDesignerModel";
import type { TaskField, TaskForm } from "../taskApi";

let container: HTMLDivElement;
let root: Root;

async function act(action: () => unknown) {
  let result: unknown;
  flushSync(() => { result = action(); });
  await result;
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}

beforeEach(() => {
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(() => root.unmount());
  container.remove();
});

async function render(fields: TaskField[] = [], onFieldsChange = vi.fn()) {
  await act(() => root.render(<FormDesigner fields={fields} onFieldsChange={onFieldsChange} />));
  return onFieldsChange;
}

async function click(label: string) {
  const button = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === label);
  expect(button, `button ${label}`).toBeTruthy();
  await act(() => button!.click());
}

async function clickAria(label: string) {
  const button = container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  expect(button, `button ${label}`).toBeTruthy();
  await act(() => button!.click());
}

describe("native form designer", () => {
  it("offers all six field kinds and creates collision-free keys on add and duplicate", async () => {
    const changed = await render();
    for (const kind of taskFieldKinds) await click(kind.label);
    expect(changed.mock.calls.at(-1)?.[0].map((field: TaskField) => field.kind)).toEqual(taskFieldKinds.map(item => item.kind));

    await act(() => root.render(null));
    await act(() => root.render(<FormDesigner fields={[]} onFieldsChange={changed} />));
    await click("Text input"); await click("Text input"); await clickAria("Duplicate Text field 2");
    const fields = changed.mock.calls.at(-1)?.[0] as TaskField[];
    expect(fields.map(field => field.key)).toEqual(["text1", "text2", "text2_copy"]);
    expect(new Set(fields.map(field => field.key)).size).toBe(fields.length);
  });

  it("keeps selection through reordering and selects a surviving field after deletion", async () => {
    const changed = await render([
      { key: "alpha", label: "Alpha", kind: "text", required: false, readOnly: false },
      { key: "bravo", label: "Bravo", kind: "text", required: false, readOnly: false },
      { key: "charlie", label: "Charlie", kind: "text", required: false, readOnly: false }
    ]);
    await clickAria("Select Bravo"); await clickAria("Move Bravo up");
    expect(changed.mock.calls.at(-1)?.[0].map((field: TaskField) => field.key)).toEqual(["bravo", "alpha", "charlie"]);
    expect(container.querySelector('button[aria-label="Select Bravo"]')?.getAttribute("aria-pressed")).toBe("true");
    await clickAria("Remove Bravo");
    expect(changed.mock.calls.at(-1)?.[0].map((field: TaskField) => field.key)).toEqual(["alpha", "charlie"]);
    expect(container.querySelector('button[aria-label="Select Alpha"]')?.getAttribute("aria-pressed")).toBe("true");
  });

  it("opens form settings first and switches to the selected field properties", async () => {
    await act(() => root.render(<FormDesigner
      form={{ id: "contact", version: "3", name: "Contact form" }}
      onFormChange={vi.fn()}
      fields={[{ key: "email", label: "Email", kind: "text", required: true, readOnly: false }]}
      onFieldsChange={vi.fn()}
    />));
    expect(container.querySelector(".user-task-designer__form-settings")?.textContent).toContain("Form settings");
    expect(container.querySelector<HTMLInputElement>(".user-task-designer__form-settings input")?.value).toBe("contact");

    await clickAria("Select Email");
    expect(container.querySelector(".user-task-designer__properties h4")?.textContent).toBe("Field properties");
    expect(container.querySelector(".user-task-designer__form-settings")).toBeNull();
    await click("Form settings");
    expect(container.querySelector<HTMLInputElement>(".user-task-designer__form-settings input")?.value).toBe("contact");
    expect(container.querySelector(".user-task-designer__form-settings output")?.textContent).toBe("3");
  });

  it("reports empty and duplicate keys, invalid choices, and inverted numeric bounds", () => {
    const errors = validateTaskFields([
      { key: " ", label: "Missing key", kind: "choice", choices: ["", "A", "A"], required: false, readOnly: false },
      { key: "amount", label: "Amount", kind: "number", minimum: 10, maximum: 1, required: false, readOnly: false },
      { key: "amount", label: "Duplicate key", kind: "text", required: false, readOnly: false }
    ]);
    expect(errors[0]).toEqual(expect.arrayContaining(["Field key is required.", "Choice values cannot be blank.", "Choice values must be unique."]));
    expect(errors[1]).toContain("Minimum cannot exceed maximum.");
    expect(errors[1]).toContain("Field keys must be unique.");
    expect(errors[2]).toContain("Field keys must be unique.");
  });

  it("round-trips form JSON while keeping identity and system-owned fields immutable", () => {
    const form: TaskForm = {
      id: "contact", version: "4", name: "Contact", provider: "elsa.basic-forms/1",
      fields: [
        { key: "email", label: "Email", kind: "text", required: true, readOnly: false, help: "Work address", maxLength: 80 },
        { key: "accepted", label: "Accept terms", kind: "boolean", required: true, readOnly: false, mustBeTrue: true, presentation: { display: "radio", trueLabel: "I agree", falseLabel: "I do not agree", unansweredLabel: "No answer" } }
      ]
    };
    const source = serializeTaskFormSource(form);
    expect(parseTaskFormSource(source, form)).toEqual(form);

    const changed = { ...form, name: "Contact updated", fields: [{ ...form.fields[0], label: "Email address" }] };
    expect(parseTaskFormSource(JSON.stringify(changed), form)).toEqual(changed);
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, id: "other" }), form)).toThrow("identifier cannot be changed");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, version: "5" }), form)).toThrow("system-managed");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, provider: "other" }), form)).toThrow("system-managed");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, fields: [{ ...form.fields[1], presentation: { display: "checkbox", trueLabel: 42 } }] }), form)).toThrow("trueLabel must be a string");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, fields: [{ ...form.fields[0], presentation: { display: "radio" } }] }), form)).toThrow("supported by this field type");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, fields: [{ ...form.fields[0], mustBeTrue: true }] }), form)).toThrow("only supported for Boolean");
  });

  it("rejects malformed, unsafe, oversized, and wrongly typed source", () => {
    const form: TaskForm = { id: "contact", version: "1", name: "Contact", provider: "elsa.basic-forms/1", fields: [] };
    expect(() => parseTaskFormSource("{", form)).toThrow("valid JSON");
    expect(() => parseTaskFormSource("[]", form)).toThrow("form object");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, unexpected: true }), form)).toThrow("Unsupported property");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, fields: [{ key: "x", label: "X", kind: "text", required: "yes", readOnly: false }] }), form)).toThrow("must be booleans");
    expect(() => parseTaskFormSource(JSON.stringify({ ...form, fields: Array.from({ length: 101 }, (_, index) => ({ key: `field${index}`, label: "Field", kind: "text", required: false, readOnly: false })) }), form)).toThrow("at most 100");
  });

  it("enforces the 100-field limit in the palette and duplicate controls", async () => {
    const fields = Array.from({ length: 100 }, (_, index) => ({ key: `key${index}`, label: `Field ${index + 1}`, kind: "text", required: false, readOnly: false }));
    await render(fields);
    expect(container.querySelector(".user-task-designer__palette .user-task-designer__panel-content > small")?.textContent).toBe("100 fields in form · limit reached");
    expect([...container.querySelectorAll(".user-task-designer__palette-items button")].every(button => (button as HTMLButtonElement).disabled)).toBe(true);
    expect(container.querySelector<HTMLButtonElement>('button[aria-label="Duplicate Field 1"]')?.disabled).toBe(true);
  });

  it("shows a clear field count and only field-specific advanced options", async () => {
    await render();
    expect(container.querySelector(".user-task-designer__palette .user-task-designer__panel-content > small")?.textContent).toBe("0 fields in form · maximum 100");
    await click("Text input");
    expect(container.querySelector(".user-task-designer__palette .user-task-designer__panel-content > small")?.textContent).toBe("1 field in form · maximum 100");
    expect(container.querySelector(".user-task-designer__advanced summary")?.textContent).toBe("Validation");

    const selectType = async (kind: string) => {
      const select = container.querySelector<HTMLSelectElement>(".user-task-designer__properties select");
      expect(select).toBeTruthy();
      await act(() => {
        select!.value = kind;
        select!.dispatchEvent(new Event("change", { bubbles: true }));
      });
    };

    const advanced = container.querySelector<HTMLDetailsElement>(".user-task-designer__advanced");
    expect(advanced).toBeTruthy();
    advanced!.open = true;
    await selectType("boolean");
    expect(container.querySelector(".user-task-designer__advanced summary")?.textContent).toBe("Validation");
    expect(container.textContent).toContain("Must be checked");
    await selectType("date");
    expect(container.querySelector(".user-task-designer__advanced")).toBeNull();
    await selectType("number");
    expect(container.querySelector(".user-task-designer__advanced summary")?.textContent).toBe("Validation");
    expect(container.querySelectorAll('.user-task-designer__advanced input[type="number"]')).toHaveLength(2);
    await selectType("multiline");
    expect(container.querySelector(".user-task-designer__advanced summary")?.textContent).toBe("Validation");
    await selectType("choice");
    expect(container.querySelector(".user-task-designer__advanced summary")?.textContent).toBe("Choices");
    expect(container.querySelector(".user-task-designer__advanced textarea")?.getAttribute("rows")).toBe("4");
  });

  it("offers only supported presentation settings and clears incompatible settings on type changes", async () => {
    const changed = await render([{ key: "answer", label: "Accept terms", kind: "boolean", required: false, readOnly: false, mustBeTrue: true, presentation: { display: "radio", trueLabel: "Agree" } }]);
    const select = (label: string, value: string) => {
      const element = [...container.querySelectorAll("select")].find(item => item.getAttribute("aria-label") === label || item.closest(".studio-field")?.textContent?.includes(label));
      expect(element).toBeTruthy();
      flushSync(() => { element!.value = value; element!.dispatchEvent(new Event("change", { bubbles: true })); });
    };
    expect(container.textContent).toContain("Must be checked");
    expect(container.textContent).toContain("Answer labels");
    select("Display", "checkbox");
    expect((changed.mock.calls.at(-1)?.[0] as TaskField[])[0].presentation).toEqual({ display: "checkbox", trueLabel: "Agree" });
    select("Type", "text");
    const field = (changed.mock.calls.at(-1)?.[0] as TaskField[])[0];
    expect(field.presentation).toBeUndefined();
    expect(field.mustBeTrue).toBeUndefined();
  });
});