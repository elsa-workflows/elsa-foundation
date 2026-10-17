import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CreateSecretDialog, RotateSecretDialog } from "../SecretDialogs";
import type { SecretDescriptorsResponse, SecretMetadata } from "../secretTypes";

const descriptors: SecretDescriptorsResponse = {
  types: ["text", "json"].map(name => ({ name, displayName: name, supportedStoreNames: ["encrypted"] })),
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }]
};
const secret: SecretMetadata = {
  id: "test", name: "test-key", displayName: "Test key", typeName: "text", storeName: "encrypted",
  tags: [], status: "Active", createdAt: "2026-10-17T00:00:00Z"
};
const cleanup = new Set<() => void>();
afterEach(() => {
  for (const dispose of cleanup) dispose();
  cleanup.clear();
});

describe("entered secret visibility", () => {
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

function change(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
