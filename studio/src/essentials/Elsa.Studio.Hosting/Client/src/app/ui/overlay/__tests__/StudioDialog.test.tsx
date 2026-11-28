import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioDialog, StudioDialogFooter } from "../../shared";

let unmount: (() => void) | undefined;
afterEach(() => { unmount?.(); });

function mount(description?: string) {
  const host = document.createElement("div");
  document.body.append(host);
  const root = createRoot(host);
  unmount = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  function Harness() {
    const [open, setOpen] = useState(false);
    const close = () => setOpen(false);
    return <>
      <button onClick={() => setOpen(true)}>Open</button>
      <StudioDialog open={open} onClose={close} title="Preferences" description={description}
        footer={<StudioDialogFooter onCancel={close} confirmLabel="Save" confirmType="button" />}>
        <input aria-label="Name" />
      </StudioDialog>
    </>;
  }
  flushSync(() => root.render(<Harness />));
  const opener = host.querySelector("button")!;
  opener.focus();
  flushSync(() => opener.click());
  const dialog = document.querySelector<HTMLElement>('[role="dialog"]')!;
  return { dialog, opener };
}

describe("StudioDialog", () => {
  it("labels the dialog, renders the footer, traps focus and returns it after closing", async () => {
    const { dialog, opener } = mount("Choose how to display the workspace.");
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    expect(document.getElementById(dialog.getAttribute("aria-labelledby")!)?.textContent).toBe("Preferences");
    expect(document.getElementById(dialog.getAttribute("aria-describedby")!)?.textContent).toBe("Choose how to display the workspace.");
    expect([...dialog.querySelectorAll("footer button")].map(button => button.textContent)).toEqual(["Cancel", "Save"]);
    const close = dialog.querySelector<HTMLButtonElement>('button[aria-label="Close dialog"]')!;
    expect(document.activeElement).toBe(close);
    const last = dialog.querySelector<HTMLButtonElement>('footer button:last-child')!;
    last.focus();
    last.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(close);
    flushSync(() => close.click());
    expect(dialog.isConnected).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));
  });

  it("does not reference a missing description", () => {
    const { dialog } = mount();
    expect(dialog.hasAttribute("aria-describedby")).toBe(false);
  });
});