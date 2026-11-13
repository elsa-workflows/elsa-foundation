import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import axe from "axe-core";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WorkspaceTabStrip, type WorkspaceTabStripProps, type WorkspaceTabView } from "../app/workspace/WorkspaceTabStrip";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

const tabs: WorkspaceTabView[] = [
  { id: "a", title: "Settings", icon: null, pinned: true, guarded: false },
  { id: "b", title: "Order flow", detail: "Draft", icon: null, pinned: false, guarded: true },
  { id: "c", title: "Runs", icon: null, pinned: false, guarded: false }
];

function render(overrides: Partial<WorkspaceTabStripProps> = {}) {
  const props: WorkspaceTabStripProps = {
    tabs,
    activeId: "c",
    notice: null,
    onDismissNotice: vi.fn(),
    onActivate: vi.fn(),
    onClose: vi.fn(),
    onCloseOthers: vi.fn(),
    onCloseAll: vi.fn(),
    onNewTab: vi.fn(),
    onPin: vi.fn(),
    onMove: vi.fn(),
    ...overrides
  };
  flushSync(() => root.render(<WorkspaceTabStrip baseId="ws" {...props} />));
  return props;
}

const tabButtons = () => [...container.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
const key = (element: Element, init: KeyboardEventInit) => flushSync(() => { element.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, ...init })); });

describe("workspace tab strip", () => {
  it("names tabs with their state and keeps only the active tab in the tab order", () => {
    render();
    expect(tabButtons().map(tab => tab.getAttribute("aria-label"))).toEqual(["Settings, pinned", "Order flow · Draft, unsaved changes", "Runs"]);
    expect(tabButtons().map(tab => tab.tabIndex)).toEqual([-1, -1, 0]);
    expect(container.querySelectorAll(".studio-workspace-tab-close")).toHaveLength(2);
  });

  it("moves focus with arrows but activates only on Enter or click, and closes with Delete", () => {
    const props = render();
    tabButtons()[2].focus();
    key(tabButtons()[2], { key: "ArrowLeft" });
    expect(document.activeElement).toBe(tabButtons()[1]);
    expect(props.onActivate).not.toHaveBeenCalled();
    flushSync(() => tabButtons()[1].click());
    expect(props.onActivate).toHaveBeenCalledWith("b");
    key(tabButtons()[1], { key: "Delete" });
    expect(props.onClose).toHaveBeenCalledWith("b");
    key(tabButtons()[1], { key: "ArrowRight", altKey: true, shiftKey: true });
    expect(props.onMove).toHaveBeenCalledWith("b", 2);
  });

  it("opens the tab menu from the keyboard with pin and bulk-close actions", () => {
    const props = render();
    key(tabButtons()[1], { key: "F10", shiftKey: true });
    const items = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
    expect(items.map(item => item.firstChild?.textContent)).toEqual(["Pin tab", "Move left", "Move right", "Close tab", "Close other tabs", "Close all tabs"]);
    // Order flow leads the unpinned group, so it cannot move further left.
    expect(items[1].disabled).toBe(true);
    flushSync(() => items[0].click());
    expect(props.onPin).toHaveBeenCalledWith("b", true);
  });

  it("lists every tab in the All tabs menu for overflow", () => {
    const props = render();
    flushSync(() => container.querySelector<HTMLButtonElement>('[aria-label="All tabs (3)"]')!.click());
    const radios = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')];
    expect(radios.map(item => [item.firstChild?.textContent, item.getAttribute("aria-checked")])).toEqual([["Settings", "false"], ["Order flow · Draft", "false"], ["Runs", "true"]]);
    flushSync(() => radios[1].click());
    expect(props.onActivate).toHaveBeenCalledWith("b");
  });

  it("keeps the full name on title and the accessible name when the active tab is long", () => {
    const title = "Picker-created policy narrow-dark-1731234567890";
    render({
      tabs: [
        { id: "workflow", title: "Decision picker narrow-dark-1731234567890", icon: null, pinned: false, guarded: true },
        { id: "policy", title, icon: null, pinned: false, guarded: false }
      ],
      activeId: "policy"
    });
    const active = tabButtons()[1];
    expect(active.getAttribute("title")).toBe(title);
    expect(active.getAttribute("aria-label")).toBe(title);
    expect(container.querySelector('[data-active="true"] .studio-workspace-tab-title')?.textContent).toBe(title);
    expect(container.querySelector('[data-active="true"] .studio-workspace-tab-close')).not.toBeNull();
  });

  it("has no axe violations, including with a notice", async () => {
    render({ notice: "\"Order flow\" has unsaved changes or a save in progress." });
    const panels = tabs.map(tab => {
      const panel = document.createElement("div");
      panel.id = `ws-panel-${tab.id}`;
      panel.setAttribute("role", "tabpanel");
      panel.setAttribute("aria-labelledby", `ws-tab-${tab.id}`);
      panel.hidden = tab.id !== "c";
      container.appendChild(panel);
      return panel;
    });
    const result = await axe.run(container, { runOnly: ["wcag2a", "wcag2aa"], rules: { "color-contrast": { enabled: false } } });
    panels.forEach(panel => panel.remove());
    expect(result.violations.map(violation => violation.id)).toEqual([]);
  });
});
