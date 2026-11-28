import React, { useRef } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, onTestFinished, vi } from "vitest";
import { useDialogFocus } from "../workflow-editor/useDialogFocus";

it("includes disclosure summaries in the focus loop and skips their collapsed contents", () => {
  vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => { callback(0); return 1; });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  onTestFinished(() => {
    flushSync(() => root.unmount());
    container.remove();
    vi.restoreAllMocks();
  });
  flushSync(() => root.render(<DisclosureDialog />));
  const summaries = container.querySelectorAll<HTMLElement>("summary");
  const first = summaries[0];
  const last = summaries[2];
  expect(document.activeElement).toBe(first);

  pressTab(first, true);
  expect(document.activeElement).toBe(last);
  pressTab(last);
  expect(document.activeElement).toBe(first);

  container.querySelectorAll("details")[2].open = true;
  pressTab(first, true);
  expect(document.activeElement?.textContent).toBe("Expanded action");
});

function pressTab(element: HTMLElement, shiftKey = false) {
  element.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey, bubbles: true }));
}

function DisclosureDialog() {
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, () => undefined);
  return <section ref={ref} role="dialog" tabIndex={-1}>
    <details>
      <summary>Publishing options</summary>
      <input aria-label="Hidden channel" />
      <details open>
        <summary>Hidden nested options</summary>
        <button type="button">Hidden nested action</button>
      </details>
    </details>
    <button type="button">Publish</button>
    <details>
      <summary>Review changes</summary>
      <button type="button">Expanded action</button>
    </details>
  </section>;
}
