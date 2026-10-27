import React, { useRef } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useDialogFocus } from "../sdk/useDialogFocus";

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

describe("Studio SDK dialog focus", () => {
  it("prefers the marked control, contains Tab, consumes Escape while busy, and restores the opener", () => {
    flushSync(() => root.render(<Dialog onEscape={null} />));
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const first = container.querySelector<HTMLButtonElement>("button")!;
    const preferred = container.querySelector<HTMLInputElement>("[data-dialog-initial-focus]")!;
    expect(document.activeElement).toBe(preferred);
    expect(document.body.style.overflow).toBe("hidden");

    const last = container.querySelector<HTMLButtonElement>("button:last-of-type")!;
    last.focus();
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true })));
    expect(document.activeElement).toBe(first);

    const escape = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
    window.dispatchEvent(escape);
    expect(escape.defaultPrevented).toBe(true);
    expect(container.contains(dialog)).toBe(true);
    flushSync(() => root.render(null));
    expect(document.activeElement).toBe(opener);
  });

  it("keeps focus inside the dialog when StrictMode replays layout effects", () => {
    const scheduledFrames: FrameRequestCallback[] = [];
    vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
      scheduledFrames.push(callback);
      return scheduledFrames.length;
    });

    flushSync(() => root.render(<React.StrictMode><Dialog onEscape={null} /></React.StrictMode>));
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(scheduledFrames).toHaveLength(0);

    flushSync(() => root.render(null));
    expect(document.activeElement).toBe(opener);
  });

  it("uses the fallback when the opener is removed while the dialog is open", () => {
    const fallback = document.createElement("button");
    document.body.append(fallback);
    const fallbackFocus = vi.fn(() => fallback.focus());
    flushSync(() => root.render(<Dialog onEscape={() => undefined} onFallback={fallbackFocus} />));
    opener.remove();

    flushSync(() => root.render(null));

    expect(fallbackFocus).toHaveBeenCalledOnce();
    expect(document.activeElement).toBe(fallback);
    fallback.remove();
  });
});

function Dialog({ onEscape, onFallback }: { onEscape: (() => void) | null; onFallback?(): void }) {
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, onEscape, true, onFallback);
  return <section ref={ref} role="dialog" tabIndex={-1}><button type="button">Close</button><input autoFocus data-dialog-initial-focus /><button type="button">Save</button></section>;
}
