import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateSecretDialog, RotateSecretDialog } from "../SecretDialogs";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "../secretTypes";

type SecretSaveRequest = CreateSecretRequest | RotateSecretRequest;

const descriptors: SecretDescriptorsResponse = {
  types: ["text", "json"].map(name => ({ name, displayName: name, supportedStoreNames: ["encrypted"] })),
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }]
};
const secret: SecretMetadata = {
  id: "test", name: "test-key", displayName: "Test key", typeName: "text", storeName: "encrypted",
  tags: [], status: "Active", createdAt: "2026-10-17T00:00:00Z"
};
const cleanup = new Set<() => void>();
beforeEach(() => {
  vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
    callback(0);
    return 1;
  });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
});
afterEach(() => {
  for (const dispose of cleanup) dispose();
  cleanup.clear();
  vi.restoreAllMocks();
});

describe("entered secret visibility", () => {
  it("explicitly associates the create description label with its textarea", () => {
    const { container } = renderDialog("create");
    const description = container.querySelector<HTMLTextAreaElement>(".secrets-modal textarea")!;
    expect(description.id).toBeTruthy();
    expect(description.labels?.[0]?.htmlFor).toBe(description.id);
    expect(description.labels?.[0]?.textContent).toBe("Description");
    expect(description.labels?.[0]?.contains(description)).toBe(false);
  });

  it("submits a custom scope without changing the authored value", async () => {
    const { container, onSave } = renderDialog("create");
    const scopeSelect = container.querySelectorAll<HTMLSelectElement>("select")[2];
    selectOption(scopeSelect, "Custom…");
    change(container.querySelector<HTMLInputElement>("input[id^='scope-custom-']")!, " Payments:Production ");
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "replacement-value");

    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ scope: " Payments:Production " }));
  });

  it.each(["create", "rotate"] as const)("masks by default, toggles without submitting, and resets when reopening %s", mode => {
    const { container, onSave, render } = renderDialog(mode);
    const input = container.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    change(input, "dummy-pasted-value");
    expect(input.type).toBe("password");
    expect(input.labels?.[0].textContent).toBe(mode === "create" ? "Value" : "New value");

    const toggle = container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!;
    expect(toggle.getAttribute("aria-controls")).toBe(input.id);
    expect(toggle.getAttribute("aria-pressed")).toBe("false");
    flushSync(() => toggle.click());
    expect(input.type).toBe("text");
    expect(input.value).toBe("dummy-pasted-value");
    expect(toggle.getAttribute("aria-label")).toBe("Hide value");
    expect(toggle.getAttribute("aria-pressed")).toBe("true");
    expect(onSave).not.toHaveBeenCalled();

    flushSync(() => toggle.click());
    expect(input.type).toBe("password");
    expect(input.value).toBe("dummy-pasted-value");
    flushSync(() => toggle.click());
    render(null);
    render(mode);
    expect(container.querySelector<HTMLInputElement>(".secrets-value-input input")?.type).toBe("password");
  });

  it("remasks when the selected secret type changes without losing entered text", () => {
    const { container } = renderDialog("create");
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "dummy-pasted-value");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    const type = container.querySelector<HTMLSelectElement>("select")!;
    flushSync(() => {
      type.value = "json";
      type.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const input = container.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    expect(input.type).toBe("password");
    expect(input.value).toBe("dummy-pasted-value");
  });
});

describe("secret dialog keyboard focus", () => {
  it.each(["create", "rotate"] as const)("focuses, contains, and restores focus for %s", mode => {
    const { container, opener } = renderDialogWithTrigger(mode);
    flushSync(() => {
      opener.focus();
      opener.click();
    });

    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const focusable = [...dialog.querySelectorAll<HTMLElement>("button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)")];
    expect(document.activeElement).toBe(dialog.querySelector("[data-dialog-initial-focus]") ?? focusable[0]);

    focusable[0].focus();
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true })));
    expect(document.activeElement).toBe(focusable.at(-1));

    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(container.querySelector("[role='dialog']")).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it.each(["create", "rotate"] as const)("does not dismiss %s while saving", async mode => {
    let finishSave!: () => void;
    const onSave = vi.fn<(request: SecretSaveRequest) => Promise<void>>(() => new Promise<void>(resolve => { finishSave = resolve; }));
    const { container, opener } = renderDialogWithTrigger(mode, onSave);
    flushSync(() => {
      opener.focus();
      opener.click();
    });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const input = dialog.querySelector<HTMLInputElement>(mode === "create" ? "input" : ".secrets-value-input input")!;
    change(input, mode === "create" ? "test-key" : "replacement-value");
    if (mode === "create") change(dialog.querySelectorAll<HTMLInputElement>(".secrets-value-input input")[0], "replacement-value");
    flushSync(() => dialog.querySelector<HTMLButtonElement>("button[type='submit']")!.click());
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));

    const cancel = [...dialog.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    expect(cancel.disabled).toBe(true);
    flushSync(() => cancel.click());
    expect(container.querySelector("[role='dialog']")).toBe(dialog);
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    expect(onSave).toHaveBeenCalledTimes(1);

    const escape = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
    window.dispatchEvent(escape);
    expect(escape.defaultPrevented).toBe(true);
    expect(container.querySelector("[role='dialog']")).toBe(dialog);

    finishSave();
    await waitFor(() => {
      expect(container.querySelector("[role='dialog']")).toBeNull();
      expect(document.activeElement).toBe(opener);
    });
  });
});

function renderDialog(mode: "create" | "rotate") {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onSave = vi.fn().mockResolvedValue(undefined);
  const onCancel = vi.fn();
  const render = (next: typeof mode | null) => flushSync(() => root.render(next === null ? null : next === "create"
    ? <CreateSecretDialog descriptors={descriptors} onSave={onSave} onCancel={onCancel} />
    : <RotateSecretDialog secret={secret} descriptors={descriptors} onSave={onSave} onCancel={onCancel} />));
  cleanup.add(() => { flushSync(() => root.unmount()); container.remove(); });
  render(mode);
  if (mode === "create") change(container.querySelector<HTMLInputElement>("input")!, "test-key");
  return { container, onSave, render };
}

function renderDialogWithTrigger(mode: "create" | "rotate", onSave: (request: SecretSaveRequest) => Promise<void> = vi.fn(async () => undefined)) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<DialogHarness mode={mode} onSave={onSave} />));
  cleanup.add(() => { flushSync(() => root.unmount()); container.remove(); });
  const opener = container.querySelector<HTMLButtonElement>("button")!;
  return { container, opener };
}

function DialogHarness({ mode, onSave }: { mode: "create" | "rotate"; onSave(request: SecretSaveRequest): Promise<void> }) {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" onClick={() => setOpen(true)}>Open dialog</button>
    {open ? mode === "create"
      ? <CreateSecretDialog descriptors={descriptors} onSave={onSave} onCancel={() => setOpen(false)} />
      : <RotateSecretDialog secret={secret} descriptors={descriptors} onSave={onSave} onCancel={() => setOpen(false)} />
      : null}
  </>;
}

async function waitFor(assertion: () => void) {
  for (let attempt = 0; attempt < 10; attempt++) {
    try {
      assertion();
      return;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  assertion();
}

function change(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function selectOption(select: HTMLSelectElement, label: string) {
  const option = [...select.options].find(item => item.textContent === label);
  if (!option) throw new Error(`Option not found: ${label}`);
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, option.value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
