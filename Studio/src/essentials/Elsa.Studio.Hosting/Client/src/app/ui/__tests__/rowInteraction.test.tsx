import React from "react";
import { createPortal, flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";
import { studioRowInteraction } from "../data-grid/rowInteraction";

let dispose: (() => void) | undefined;
afterEach(() => {
  document.getSelection()?.removeAllRanges();
  dispose?.();
});

function render(node: React.ReactNode) {
  const container = document.body.appendChild(document.createElement("div"));
  const root = createRoot(container);
  flushSync(() => root.render(node));
  dispose = () => { flushSync(() => root.unmount()); container.remove(); };
  return container;
}

it("opens from plain cells and direct Enter/Space without stealing nested control activation", () => {
  const activate = vi.fn();
  const host = render(<div role="row" {...studioRowInteraction<HTMLDivElement>(activate)}>
    <span role="cell">Name</span>
    <button>Copy</button><a href="#source">Source</a><input type="checkbox" />
    <select><option>Version</option></select><textarea defaultValue="Notes" />
    <details><summary>Details</summary>More</details>
    <div contentEditable suppressContentEditableWarning>Editable</div>
    <span role="button" tabIndex={0}>Action</span>
  </div>);
  const row = host.querySelector<HTMLElement>('[role="row"]')!;
  host.querySelector<HTMLElement>('[role="cell"]')!.click();
  for (const key of ["Enter", " "]) row.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true }));
  expect(activate).toHaveBeenCalledTimes(3);
  for (const control of host.querySelectorAll<HTMLElement>("button, a, input, select, textarea, summary, [contenteditable], [role='button']")) {
    control.click();
    control.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
  }
  expect(activate).toHaveBeenCalledTimes(3);
});

it("preserves text selection within the row but still opens when unrelated text is selected", () => {
  const activate = vi.fn();
  const host = render(<><p>Unrelated text</p><div {...studioRowInteraction<HTMLDivElement>(activate)}><span>Identifier to copy</span></div></>);
  const cell = host.querySelector("span")!;
  const selection = document.getSelection()!;
  const range = document.createRange();
  range.selectNodeContents(cell);
  selection.addRange(range);
  cell.click();
  expect(activate).not.toHaveBeenCalled();
  selection.removeAllRanges();
  range.selectNodeContents(host.querySelector("p")!);
  selection.addRange(range);
  cell.click();
  expect(activate).toHaveBeenCalledOnce();
});

it("ignores portal padding, prevented events, modified clicks and repeated keys", () => {
  const activate = vi.fn();
  const host = render(<div {...studioRowInteraction<HTMLDivElement>(activate)}>
    <span onClick={event => event.preventDefault()}>Prevented</span>
    {createPortal(<aside data-row-portal="">Popover padding</aside>, document.body)}
  </div>);
  document.querySelector<HTMLElement>("[data-row-portal]")!.click();
  host.querySelector("span")!.click();
  const row = host.firstElementChild!;
  for (const modifier of ["metaKey", "ctrlKey", "shiftKey", "altKey"]) row.dispatchEvent(new MouseEvent("click", { bubbles: true, [modifier]: true }));
  row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", repeat: true, bubbles: true }));
  expect(activate).not.toHaveBeenCalled();
});
