import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { DialogHost } from "../DialogHost";
import { dialogs } from "../../../dialogs";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
});

function mount() {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(<DialogHost />));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitFor(predicate: () => boolean, message: string) {
  for (let attempt = 0; attempt < 50; attempt++) {
    if (predicate()) return;
    await flushPromises();
  }
  throw new Error(message);
}

function dialog() {
  return document.body.querySelector<HTMLElement>(".studio-dialog");
}

function clickButton(text: string) {
  const button = Array.from(document.body.querySelectorAll("button")).find(item => item.textContent?.trim() === text);
  if (!button) throw new Error(`Button not found: ${text}`);
  button.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
}

function setInputValue(value: string) {
  const input = dialog()?.querySelector<HTMLInputElement>("input");
  if (!input) throw new Error("Prompt input not found.");
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
}

describe("DialogHost", () => {
  it("resolves confirm to true when the confirm action is chosen", async () => {
    mount();
    const result = dialogs.confirm({ message: "Delete it?", confirmLabel: "Delete", tone: "danger" });

    await waitFor(() => dialog()?.textContent?.includes("Delete it?") === true, "Dialog did not open.");
    clickButton("Delete");

    expect(await result).toBe(true);
    await waitFor(() => dialog() === null, "Dialog did not close.");
  });

  it("resolves confirm to false when cancelled", async () => {
    mount();
    const result = dialogs.confirm({ message: "Delete it?" });

    await waitFor(() => dialog() !== null, "Dialog did not open.");
    clickButton("Cancel");

    expect(await result).toBe(false);
  });

  it("resolves confirm to false when dismissed with Escape", async () => {
    mount();
    const result = dialogs.confirm({ message: "Delete it?" });

    await waitFor(() => dialog() !== null, "Dialog did not open.");
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(await result).toBe(false);
  });

  it("settles pending dialogs as cancelled when the host unmounts", async () => {
    mount();
    const confirmResult = dialogs.confirm({ message: "Delete it?" });
    const promptResult = dialogs.prompt({ message: "New path" });

    await waitFor(() => dialog() !== null, "Dialog did not open.");
    cleanup?.();
    cleanup = null;

    expect(await confirmResult).toBe(false);
    expect(await promptResult).toBeNull();
  });

  it("resolves prompt to the entered value and null when cancelled", async () => {
    mount();
    const accepted = dialogs.prompt({ title: "Rename", message: "New path", defaultValue: "a.cs" });

    await waitFor(() => dialog()?.querySelector("input") != null, "Prompt did not open.");
    setInputValue("b.cs");
    clickButton("Confirm");
    expect(await accepted).toBe("b.cs");

    const cancelled = dialogs.prompt({ message: "New path" });
    await waitFor(() => dialog()?.querySelector("input") != null, "Second prompt did not open.");
    clickButton("Cancel");
    expect(await cancelled).toBeNull();
  });
});
