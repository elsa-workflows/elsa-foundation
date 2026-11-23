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
  const opener = document.createElement("button");
  opener.textContent = "Open dialog";
  document.body.append(opener, host);
  opener.focus();
  const root = createRoot(host);
  flushSync(() => root.render(<DialogHost />));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
    opener.remove();
  };
  return opener;
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
  it("resolves confirm to true when the confirm action is chosen and restores its opener", async () => {
    const opener = mount();
    const result = dialogs.confirm({ message: "Delete it?", confirmLabel: "Delete", tone: "danger" });

    await waitFor(() => dialog()?.textContent?.includes("Delete it?") === true, "Dialog did not open.");
    clickButton("Delete");

    expect(await result).toBe(true);
    await waitFor(() => dialog() === null, "Dialog did not close.");
    await waitFor(() => document.activeElement === opener, "Focus did not return to the opener.");
  });

  it("resolves confirm to false when cancelled and restores its opener", async () => {
    const opener = mount();
    const result = dialogs.confirm({ message: "Delete it?" });

    await waitFor(() => dialog() !== null, "Dialog did not open.");
    clickButton("Cancel");

    expect(await result).toBe(false);
    await waitFor(() => dialog() === null, "Dialog did not close.");
    await waitFor(() => document.activeElement === opener, "Focus did not return to the opener.");
  });

  it("resolves confirm to false when dismissed with Escape and restores its opener", async () => {
    const opener = mount();
    const result = dialogs.confirm({ message: "Delete it?" });

    await waitFor(() => dialog() !== null, "Dialog did not open.");
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(await result).toBe(false);
    await waitFor(() => dialog() === null, "Dialog did not close.");
    await waitFor(() => document.activeElement === opener, "Focus did not return to the opener.");
  });

  it("keeps focus in a queued prompt and restores the original opener after the queue closes", async () => {
    const opener = mount();
    const first = dialogs.confirm({ message: "First confirmation" });
    const second = dialogs.prompt({ title: "Queued prompt", message: "Enter a value" });
    await waitFor(() => dialog()?.textContent?.includes("First confirmation") === true, "First dialog did not open.");
    clickButton("Confirm");
    expect(await first).toBe(true);
    await waitFor(() => dialog()?.querySelector("input") != null, "Queued prompt did not open.");
    await waitFor(() => dialog()?.contains(document.activeElement) === true, "Focus left the active queued dialog.");
    clickButton("Cancel");
    expect(await second).toBeNull();
    await waitFor(() => dialog() === null, "Dialog queue did not close.");
    await waitFor(() => document.activeElement === opener, "Focus did not return to the original opener.");
  });

  it("preserves focus explicitly moved by the caller after confirmation", async () => {
    mount();
    const nextField = document.createElement("input");
    document.body.appendChild(nextField);
    const dispose = cleanup!;
    cleanup = () => { dispose(); nextField.remove(); };
    const result = dialogs.confirm({ message: "Continue to the next field?" });
    await waitFor(() => dialog() !== null, "Dialog did not open.");
    clickButton("Confirm");
    expect(await result).toBe(true);
    nextField.focus();
    await waitFor(() => dialog() === null, "Dialog did not close.");
    await flushPromises();
    await flushPromises();
    expect(document.activeElement).toBe(nextField);
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
