import React, { useRef, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useDialogFocus } from "../workflow-editor/useDialogFocus";

let opener: HTMLButtonElement;
let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
    callback(0);
    return 1;
  });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
  opener = document.createElement("button");
  container = document.createElement("div");
  document.body.append(opener, container);
  opener.focus();
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  opener.remove();
  container.remove();
  vi.restoreAllMocks();
});

describe("dialog focus management", () => {
  it("focuses the first control, contains Shift+Tab from the dialog itself, handles Escape, and restores the opener", () => {
    const onEscape = vi.fn();

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
  });

  it("wraps Tab from a contenteditable editor to the first control", () => {
    flushSync(() => root.render(<Dialog onEscape={vi.fn()}><div contentEditable aria-label="Request body" /></Dialog>));
    const editor = container.querySelector<HTMLElement>("[contenteditable]")!;
    editor.focus();
    editor.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(container.querySelector("button"));
  });

  it.each([-1, -2])("excludes tabindex %s, hidden and disabled controls when wrapping backwards", tabIndex => {
    flushSync(() => root.render(<Dialog onEscape={vi.fn()}>
      <button tabIndex={tabIndex}>Suggestions</button>
      <button tabIndex={0} disabled>Disabled</button>
      <div hidden><button>Hidden</button></div>
    </Dialog>));
    const buttons = container.querySelectorAll("button");
    buttons[0].focus();
    buttons[0].dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(buttons[1]);
  });

  it("preserves editor-handled Tab and Escape", () => {
    const onEscape = vi.fn();
    flushSync(() => root.render(<Dialog onEscape={onEscape}>
      <div contentEditable onKeyDown={event => event.preventDefault()} />
    </Dialog>));
    const editor = container.querySelector<HTMLElement>("[contenteditable]")!;
    editor.focus();
    for (const key of ["Tab", "Escape"]) {
      editor.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }));
      expect(document.activeElement).toBe(editor);
    }
    expect(onEscape).not.toHaveBeenCalled();
  });

  it("does not replace focus already placed inside before initial focus runs", () => {
    let runInitialFocus: FrameRequestCallback | undefined;
    vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
      runInitialFocus = callback;
      return 1;
    });
    flushSync(() => root.render(<Dialog onEscape={vi.fn()} />));
    const buttons = container.querySelectorAll("button");
    buttons[1].focus();
    runInitialFocus?.(0);
    expect(document.activeElement).toBe(buttons[1]);
  });
});

function Dialog({ onEscape, children }: { onEscape(): void; children?: ReactNode }) {
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, onEscape);
  return <section ref={ref} role="dialog" tabIndex={-1}><button type="button">Publish</button><button type="button">Cancel</button>{children}</section>;
}
