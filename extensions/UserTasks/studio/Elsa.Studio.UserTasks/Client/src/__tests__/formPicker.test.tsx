// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorGroupInput } from "@elsa-workflows/studio-sdk";
import { FormPicker } from "../FormPicker";
import type { FormVersion } from "../taskApi";

type PinValue = { syntax: string; value: unknown };
type PinValues = Record<string, PinValue>;

function version(id: string, name: string, value: string): FormVersion {
  return {
    tenantId: "tenant",
    form: { id, name, version: value, provider: "elsa.basic-forms/1", fields: [] },
    createdAt: `2026-11-${value.padStart(2, "0")}T00:00:00Z`,
    createdBy: "author"
  };
}

function createApi(getJson: (path: string) => Promise<unknown>) {
  return {
    backend: { http: { getJson: vi.fn(getJson) } },
    backendSession: { capabilities: { capabilities: [{
      id: "elsa.api.user-tasks", contractVersion: "1", links: [{ rel: "forms", href: "runtime/user-task-forms" }]
    }] } }
  } as unknown as ElsaStudioModuleApi;
}

function makeInput(key: string, state: PinValue, readOnly: boolean): StudioActivityPropertyEditorGroupInput {
  return {
    descriptor: { name: key, referenceKey: key, displayName: key, isWrapped: true } as StudioActivityPropertyEditorGroupInput["descriptor"],
    value: state.value,
    syntax: state.syntax,
    expressionValue: state.value,
    readOnly
  };
}

function Harness({ api, initial, disabled = false, atomic = true, onLiteralInputs }: {
  api: ElsaStudioModuleApi;
  initial: PinValues;
  disabled?: boolean;
  atomic?: boolean;
  onLiteralInputs?: (values: Record<string, unknown>) => void;
}) {
  const [values, setValues] = React.useState(initial);
  const inputs = [makeInput("FormId", values.FormId!, disabled), makeInput("FormVersion", values.FormVersion!, disabled)];
  const setLiteralInputs = React.useCallback((next: Record<string, unknown>) => {
    onLiteralInputs?.(next);
    setValues(current => ({ ...current, ...Object.fromEntries(Object.entries(next).map(([key, value]) => [key, { syntax: "Literal", value }])) }));
  }, [onLiteralInputs]);
  return <>
    <FormPicker
      api={api}
      inputs={inputs}
      context={{ activity: {}, expressionDescriptors: [], inputValues: Object.fromEntries(Object.entries(values).map(([key, value]) => [key, value.value])), readOnly: disabled, ...(atomic ? { setLiteralInputs } : {}) }}
      disabled={disabled}
      renderExpressionEditor={key => <textarea aria-label={`Host expression editor ${key}`} />}
      onChange={(key, value) => setValues(current => ({ ...current, [key]: { syntax: "Literal", value } }))}
    />
    <output data-testid="pin-values">{JSON.stringify(values)}</output>
  </>;
}

async function act(action: () => unknown) {
  let result: unknown;
  flushSync(() => { result = action(); });
  await result;
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(() => root.unmount());
  container.remove();
});

async function render(element: React.ReactNode) {
  await act(() => root.render(element));
}

function summary() {
  return container.querySelector<HTMLElement>(".user-task-form-picker__disclosure > summary")!;
}

async function openPicker() {
  await act(() => summary().click());
  await act(() => new Promise(resolve => setTimeout(resolve, 0)));
}

describe("unified Task form picker", () => {
  it("pins the latest version discovered across catalog pages", async () => {
    const getJson = vi.fn(async (path: string) => path.includes("after=page-2")
      ? { items: [version("newsletter", "Newsletter", "2")], next: null }
      : { items: [version("newsletter", "Newsletter", "1")], next: "page-2" });
    const api = createApi(getJson);
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} initial={{ FormId: { syntax: "Literal", value: "" }, FormVersion: { syntax: "Literal", value: "" } }} onLiteralInputs={onLiteralInputs} />);
    await openPicker();

    expect(getJson).toHaveBeenCalledTimes(2);
    await act(() => container.querySelector<HTMLButtonElement>('[aria-label="Select Newsletter version 2"]')!.click());

    expect(onLiteralInputs).toHaveBeenCalledTimes(1);
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ FormId: "newsletter", FormVersion: "2" });
    expect(summary().getAttribute("aria-label")).toBe("Task form: Newsletter · Version 2");
    expect(summary()).toBe(document.activeElement);
  });

  it("does not change a saved pin while browsing or closing its history", async () => {
    const api = createApi(async () => ({ items: [version("newsletter", "Newsletter", "1"), version("newsletter", "Newsletter", "2")], next: null }));
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} initial={{ FormId: { syntax: "Literal", value: "newsletter" }, FormVersion: { syntax: "Literal", value: "1" } }} onLiteralInputs={onLiteralInputs} />);
    await openPicker();
    await act(() => container.querySelector<HTMLButtonElement>(".user-task-form-picker__form-row button[aria-expanded]")!.click());
    expect(container.querySelector('[aria-label="Select Newsletter version 1"]')?.getAttribute("aria-pressed")).toBe("true");
    await act(() => container.querySelector(".user-task-form-picker__disclosure")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));

    expect(onLiteralInputs).not.toHaveBeenCalled();
    expect(summary().getAttribute("aria-label")).toBe("Task form: Newsletter · Version 1");
    expect(summary()).toBe(document.activeElement);
  });

  it("changes to an older version only after explicit selection and clears both inputs atomically", async () => {
    const api = createApi(async () => ({ items: [version("newsletter", "Newsletter", "1"), version("newsletter", "Newsletter", "2")], next: null }));
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} initial={{ FormId: { syntax: "Literal", value: "newsletter" }, FormVersion: { syntax: "Literal", value: "2" } }} onLiteralInputs={onLiteralInputs} />);
    await openPicker();
    await act(() => container.querySelector<HTMLButtonElement>(".user-task-form-picker__form-row button[aria-expanded]")!.click());
    await act(() => container.querySelector<HTMLButtonElement>('[aria-label="Select Newsletter version 1"]')!.click());
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ FormId: "newsletter", FormVersion: "1" });
    expect(summary().getAttribute("aria-label")).toBe("Task form: Newsletter · Version 1");

    await openPicker();
    await act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Clear selection"]')!.click());
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ FormId: null, FormVersion: null });
    expect(JSON.parse(container.querySelector('[data-testid="pin-values"]')!.textContent!)).toMatchObject({
      FormId: { syntax: "Literal", value: null }, FormVersion: { syntax: "Literal", value: null }
    });
  });

  it("keeps an unavailable saved pin visible during a catalog failure and supports retry", async () => {
    const getJson = vi.fn().mockRejectedValueOnce(new Error("Forms are unavailable"))
      .mockResolvedValueOnce({ items: [version("newsletter", "Newsletter", "2")], next: null });
    const api = createApi(getJson);
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} initial={{ FormId: { syntax: "Literal", value: "newsletter" }, FormVersion: { syntax: "Literal", value: "1" } }} onLiteralInputs={onLiteralInputs} />);
    await openPicker();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Forms are unavailable");
    expect(summary().getAttribute("aria-label")).toBe("Task form: newsletter · Version 1");
    await act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Retry loading forms"]')!.click());
    await act(() => new Promise(resolve => setTimeout(resolve, 0)));

    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(summary().getAttribute("aria-label")).toBe("Task form: Newsletter · Version 1 (unavailable)");
    expect(onLiteralInputs).not.toHaveBeenCalled();
  });

  it("keeps picker actions read-only when the group is disabled", async () => {
    const api = createApi(async () => ({ items: [version("newsletter", "Newsletter", "1")], next: null }));
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} disabled initial={{ FormId: { syntax: "Literal", value: "" }, FormVersion: { syntax: "Literal", value: "" } }} onLiteralInputs={onLiteralInputs} />);
    await openPicker();

    expect(container.querySelector<HTMLButtonElement>('[aria-label="Select Newsletter version 1"]')?.disabled).toBe(true);
    expect(container.querySelector<HTMLButtonElement>('button[aria-label="Clear selection"]')?.disabled).toBe(true);
    expect(onLiteralInputs).not.toHaveBeenCalled();
  });

  it("preserves both inputs until a fixed form is explicitly selected from expression mode", async () => {
    const api = createApi(async () => ({ items: [version("newsletter", "Newsletter", "2")], next: null }));
    const onLiteralInputs = vi.fn();
    await render(<Harness api={api} initial={{ FormId: { syntax: "JavaScript", value: "args.formId" }, FormVersion: { syntax: "Literal", value: "1" } }} onLiteralInputs={onLiteralInputs} />);
    await act(() => new Promise(resolve => setTimeout(resolve, 0)));
    expect(container.querySelector('[aria-label="Host expression editor FormId"]')).not.toBeNull();
    const target = container.querySelector<HTMLSelectElement>('[aria-label="Expression returns"]')!;
    expect(target).not.toBe(document.activeElement);
    await act(() => {
      target.value = "FormVersion";
      target.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(container.querySelector('[aria-label="Host expression editor FormVersion"]')).not.toBeNull();
    await act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Use a fixed form"]')!.click());
    expect(onLiteralInputs).not.toHaveBeenCalled();
    expect(summary().getAttribute("aria-label")).toBe("Task form: Choose a published form (expression unchanged)");
    await openPicker();
    await act(() => container.querySelector<HTMLButtonElement>('[aria-label="Select Newsletter version 2"]')!.click());

    expect(onLiteralInputs).toHaveBeenCalledTimes(1);
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ FormId: "newsletter", FormVersion: "2" });
    expect(summary().getAttribute("aria-label")).toBe("Task form: Newsletter · Version 2");
  });
});