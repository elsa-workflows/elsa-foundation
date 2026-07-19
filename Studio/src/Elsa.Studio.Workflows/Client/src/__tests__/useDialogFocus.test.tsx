import React, { useRef } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useDialogFocus } from "../workflow-editor/useDialogFocus";

afterEach(() => vi.restoreAllMocks());

describe("dialog focus management", () => {
  it("focuses the first control, contains Shift+Tab from the dialog itself, handles Escape, and restores the opener", () => {
    vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
      callback(0);
      return 1;
    });
    vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
    const opener = document.createElement("button");
    const container = document.createElement("div");
    document.body.append(opener, container);
    opener.focus();
    const onEscape = vi.fn();
    const root = createRoot(container);

    flushSync(() => root.render(<Dialog onEscape={onEscape} />));
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const buttons = container.querySelectorAll("button");
    expect(document.activeElement).toBe(buttons[0]);

    dialog.focus();
    dialog.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(buttons[1]);

    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(onEscape).toHaveBeenCalledTimes(1);

    flushSync(() => root.unmount());
    expect(document.activeElement).toBe(opener);
    opener.remove();
    container.remove();
  });
});

function Dialog({ onEscape }: { onEscape(): void }) {
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, onEscape);
  return <section ref={ref} role="dialog" tabIndex={-1}><button type="button">Publish</button><button type="button">Cancel</button></section>;
}
