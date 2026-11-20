import type { HTMLAttributes } from "react";

const embeddedControl = "button, a, input, select, textarea, label, summary, [contenteditable]:not([contenteditable='false']), [role='button'], [role='link'], [role='checkbox'], [role='switch'], [role='combobox']";

/** Activation for a table row whose default action opens or selects its details. */
export function studioRowInteraction<T extends HTMLElement>(activate?: () => void): HTMLAttributes<T> & { "data-studio-row-interactive"?: string } {
  if (!activate) return {};
  return {
    "data-studio-row-interactive": "",
    tabIndex: 0,
    onClick: event => {
      const row = event.currentTarget;
      const target = event.target;
      // React portals bubble through their owner even when outside the row's DOM.
      if (event.defaultPrevented || !(target instanceof Element) || !row.contains(target)) return;
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const control = target.closest(embeddedControl);
      if (control && control !== row) return;
      const selection = row.ownerDocument.getSelection();
      if (selection?.toString()) {
        for (let index = 0; index < selection.rangeCount; index++) {
          if (selection.getRangeAt(index).intersectsNode(row)) return;
        }
      }
      activate();
    },
    onKeyDown: event => {
      if (event.defaultPrevented || event.target !== event.currentTarget || event.repeat) return;
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      activate();
    }
  };
}
