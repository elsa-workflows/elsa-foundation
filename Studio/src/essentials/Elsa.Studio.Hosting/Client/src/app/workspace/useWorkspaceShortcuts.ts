import { useEffect, useRef } from "react";
import type { WorkspaceTabsController } from "./useWorkspaceTabs";

function isEditableTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable || target.closest("[contenteditable='true'], .cm-editor")) return true;
  return target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement
    || target instanceof HTMLInputElement && !["button", "checkbox", "radio", "submit", "reset", "range", "color", "file"].includes(target.type);
}

/**
 * Workspace shortcuts use Alt+Shift with physical key codes, so browser tab shortcuts (Ctrl/Cmd+T, W,
 * Tab) keep working and macOS Option characters do not change the key. They are ignored while typing.
 */
export function useWorkspaceShortcuts(workspace: WorkspaceTabsController | null) {
  const workspaceRef = useRef(workspace);
  workspaceRef.current = workspace;
  const enabled = workspace !== null;

  useEffect(() => {
    if (!enabled) return;
    const onKeyDown = (event: KeyboardEvent) => {
      const current = workspaceRef.current;
      if (!current || event.defaultPrevented || !event.altKey || !event.shiftKey || event.ctrlKey || event.metaKey) return;
      if (isEditableTarget(event.target)) return;
      const { tabs, activeId } = current.state;
      const index = tabs.findIndex(tab => tab.id === activeId);
      const active = tabs[index];
      let handled = true;
      if (event.code === "KeyT") current.open("/", { newTab: true });
      else if (event.code === "KeyW") current.close(activeId);
      else if (event.code === "KeyP" && active) current.setPinned(activeId, !active.pinned);
      else if (event.code === "BracketRight") current.activate(tabs[(index + 1) % tabs.length].id);
      else if (event.code === "BracketLeft") current.activate(tabs[(index - 1 + tabs.length) % tabs.length].id);
      else if (/^Digit[1-9]$/.test(event.code)) {
        const position = Number(event.code.slice(5));
        const target = position === 9 ? tabs[tabs.length - 1] : tabs[position - 1];
        if (target) current.activate(target.id);
      } else handled = false;
      if (handled) event.preventDefault();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [enabled]);
}
