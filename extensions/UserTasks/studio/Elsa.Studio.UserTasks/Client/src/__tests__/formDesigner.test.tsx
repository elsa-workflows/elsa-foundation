// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { FormDesigner } from "../FormDesigner";
import { taskFieldKinds, validateTaskFields } from "../formDesignerModel";
import type { TaskField } from "../taskApi";

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

  it("enforces the 100-field limit in the palette and duplicate controls", async () => {
    const fields = Array.from({ length: 100 }, (_, index) => ({ key: `key${index}`, label: `Field ${index + 1}`, kind: "text", required: false, readOnly: false }));
    await render(fields);
    expect(container.querySelector(".user-task-designer__palette > small")?.textContent).toBe("100 fields in form · limit reached");
    expect([...container.querySelectorAll(".user-task-designer__palette-items button")].every(button => (button as HTMLButtonElement).disabled)).toBe(true);
    expect(container.querySelector<HTMLButtonElement>('button[aria-label="Duplicate Field 1"]')?.disabled).toBe(true);
  });

  it("shows a clear field count and only field-specific advanced options", async () => {
    await render();
    expect(container.querySelector(".user-task-designer__palette > small")?.textContent).toBe("0 fields in form · maximum 100");
    await click("Text input");
    expect(container.querySelector(".user-task-designer__palette > small")?.textContent).toBe("1 field in form · maximum 100");
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
    expect(container.querySelector(".user-task-designer__advanced")).toBeNull();
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
});