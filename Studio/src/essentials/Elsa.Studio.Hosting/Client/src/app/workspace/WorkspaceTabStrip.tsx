import React, { useEffect, useId, useRef, useState } from "react";
import { ChevronDown, Pin, Plus, X } from "lucide-react";
import { AnchoredPopover } from "../ui/overlay/AnchoredPopover";
import { enabledMenuIndexes, focusIndexedItem, stepEnabledIndex } from "../ui/disclosure/menuNavigation";
import "../ui/disclosure/disclosure.css";
import "./workspace.css";

export interface WorkspaceTabView {
  id: string;
  title: string;
  detail?: string;
  icon: React.ReactNode;
  pinned: boolean;
  /** The tab went to the background holding unsaved or pending work. */
  guarded: boolean;
}

export interface WorkspaceTabStripProps {
  tabs: WorkspaceTabView[];
  activeId: string;
  notice: string | null;
  onDismissNotice(): void;
  onActivate(id: string): void;
  onClose(id: string): void;
  onCloseOthers(id: string): void;
  onCloseAll(): void;
  onNewTab(): void;
  onPin(id: string, pinned: boolean): void;
  onMove(id: string, toIndex: number): void;
}

interface MenuItem {
  id: string;
  label: string;
  hint?: string;
  checked?: boolean;
  disabled?: boolean;
  onSelect(): void;
}

export const workspaceShortcuts = {
  newTab: "Alt+Shift+T",
  closeTab: "Alt+Shift+W",
  nextTab: "Alt+Shift+]",
  previousTab: "Alt+Shift+[",
  pinTab: "Alt+Shift+P"
} as const;

export function workspaceTabDomId(baseId: string, tabId: string) {
  return `${baseId}-tab-${tabId}`;
}

export function workspacePanelDomId(baseId: string, tabId: string) {
  return `${baseId}-panel-${tabId}`;
}

function tabLabel(tab: WorkspaceTabView) {
  return tab.detail ? `${tab.title} · ${tab.detail}` : tab.title;
}

function scrollTabIntoView(tab: HTMLElement) {
  const tablist = tab.closest(".studio-workspace-tablist");
  if (!(tablist instanceof HTMLElement)) return;
  const tabBox = tab.getBoundingClientRect();
  const listBox = tablist.getBoundingClientRect();
  if (tabBox.left < listBox.left) tablist.scrollLeft += tabBox.left - listBox.left;
  else if (tabBox.right > listBox.right) tablist.scrollLeft += Math.ceil(tabBox.right - listBox.right);
}

export function WorkspaceTabStrip({ baseId, tabs, activeId, notice, onDismissNotice, onActivate, onClose, onCloseOthers, onCloseAll, onNewTab, onPin, onMove }: WorkspaceTabStripProps & { baseId: string }) {
  const [focusedId, setFocusedId] = useState(activeId);
  const [menu, setMenu] = useState<{ tabId: string } | { all: true } | null>(null);
  const [dragId, setDragId] = useState<string | null>(null);
  const tabRefs = useRef(new Map<string, HTMLElement>());
  const allTabsRef = useRef<HTMLButtonElement>(null);
  const menuAnchorRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    setFocusedId(activeId);
    const tab = tabRefs.current.get(activeId);
    if (!tab) return;
    let cancelled = false;
    const run = () => { if (!cancelled && tab.isConnected) scrollTabIntoView(tab); };
    run();
    const frame = requestAnimationFrame(run);
    const tablist = tab.closest(".studio-workspace-tablist");
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(run);
    observer?.observe(tab);
    if (tablist instanceof Element) observer?.observe(tablist);
    void document.fonts?.ready?.then(run);
    return () => {
      cancelled = true;
      cancelAnimationFrame(frame);
      observer?.disconnect();
    };
  }, [activeId]);

  useEffect(() => {
    if (!tabs.some(tab => tab.id === focusedId)) setFocusedId(activeId);
  }, [activeId, focusedId, tabs]);

  function focusTab(id: string) {
    setFocusedId(id);
    tabRefs.current.get(id)?.querySelector<HTMLElement>('[role="tab"]')?.focus();
  }

  function openTabMenu(id: string) {
    menuAnchorRef.current = tabRefs.current.get(id) ?? null;
    setMenu({ tabId: id });
  }

  function onTabKeyDown(event: React.KeyboardEvent<HTMLButtonElement>, tab: WorkspaceTabView, index: number) {
    const ids = tabs.map(candidate => candidate.id);
    if (event.altKey && event.shiftKey && (event.key === "ArrowLeft" || event.key === "ArrowRight")) {
      event.preventDefault();
      onMove(tab.id, index + (event.key === "ArrowLeft" ? -1 : 1));
      return;
    }
    switch (event.key) {
      case "ArrowRight":
        event.preventDefault();
        focusTab(ids[(index + 1) % ids.length]);
        return;
      case "ArrowLeft":
        event.preventDefault();
        focusTab(ids[(index - 1 + ids.length) % ids.length]);
        return;
      case "Home":
        event.preventDefault();
        focusTab(ids[0]);
        return;
      case "End":
        event.preventDefault();
        focusTab(ids[ids.length - 1]);
        return;
      case "Delete":
        event.preventDefault();
        onClose(tab.id);
        return;
      case "ContextMenu":
        event.preventDefault();
        openTabMenu(tab.id);
        return;
      case "F10":
        if (event.shiftKey) {
          event.preventDefault();
          openTabMenu(tab.id);
        }
        return;
    }
  }

  const menuTab = menu && "tabId" in menu ? tabs.find(tab => tab.id === menu.tabId) : undefined;
  const menuTabIndex = menuTab ? tabs.indexOf(menuTab) : -1;
  const unpinnedCount = tabs.filter(tab => !tab.pinned).length;
  const groupBounds = menuTab ? tabs.filter(tab => tab.pinned === menuTab.pinned) : [];
  const menuItems: MenuItem[] = menuTab ? [
    { id: "pin", label: menuTab.pinned ? "Unpin tab" : "Pin tab", hint: workspaceShortcuts.pinTab, onSelect: () => onPin(menuTab.id, !menuTab.pinned) },
    { id: "move-left", label: "Move left", hint: "Alt+Shift+←", disabled: groupBounds[0]?.id === menuTab.id, onSelect: () => onMove(menuTab.id, menuTabIndex - 1) },
    { id: "move-right", label: "Move right", hint: "Alt+Shift+→", disabled: groupBounds[groupBounds.length - 1]?.id === menuTab.id, onSelect: () => onMove(menuTab.id, menuTabIndex + 1) },
    { id: "close", label: "Close tab", hint: workspaceShortcuts.closeTab, onSelect: () => onClose(menuTab.id) },
    { id: "close-others", label: "Close other tabs", disabled: tabs.every(tab => tab.id === menuTab.id || tab.pinned), onSelect: () => onCloseOthers(menuTab.id) },
    { id: "close-all", label: "Close all tabs", disabled: unpinnedCount === 0, onSelect: onCloseAll }
  ] : menu ? [
    ...tabs.map(tab => ({ id: `tab-${tab.id}`, label: tabLabel(tab), checked: tab.id === activeId, onSelect: () => onActivate(tab.id) })),
    { id: "new", label: "New tab", hint: workspaceShortcuts.newTab, onSelect: onNewTab },
    { id: "close-others", label: "Close other tabs", disabled: tabs.every(tab => tab.id === activeId || tab.pinned), onSelect: () => onCloseOthers(activeId) },
    { id: "close-all", label: "Close all tabs", disabled: unpinnedCount === 0, onSelect: onCloseAll }
  ] : [];

  return (
    <div className="studio-workspace-tabs">
      <div className="studio-workspace-tablist" role="tablist" aria-label="Open screens">
        {tabs.map((tab, index) => {
          const active = tab.id === activeId;
          const label = tabLabel(tab);
          return (
            <div
              key={tab.id}
              ref={element => { if (element) tabRefs.current.set(tab.id, element); else tabRefs.current.delete(tab.id); }}
              role="presentation"
              className="studio-workspace-tab"
              data-active={active ? "true" : undefined}
              data-pinned={tab.pinned ? "true" : undefined}
              data-dragging={dragId === tab.id ? "true" : undefined}
              draggable
              onDragStart={event => {
                setDragId(tab.id);
                event.dataTransfer.effectAllowed = "move";
                event.dataTransfer.setData("text/plain", label);
              }}
              onDragEnd={() => setDragId(null)}
              onDragOver={event => {
                if (dragId && dragId !== tab.id) event.preventDefault();
              }}
              onDrop={event => {
                event.preventDefault();
                if (dragId && dragId !== tab.id) onMove(dragId, index);
                setDragId(null);
              }}
              onContextMenu={event => {
                event.preventDefault();
                openTabMenu(tab.id);
              }}
            >
              <button
                type="button"
                role="tab"
                id={workspaceTabDomId(baseId, tab.id)}
                data-tab-id={tab.id}
                aria-selected={active}
                aria-controls={workspacePanelDomId(baseId, tab.id)}
                aria-label={`${label}${tab.pinned ? ", pinned" : ""}${tab.guarded ? ", unsaved changes" : ""}`}
                aria-describedby={`${baseId}-tab-help`}
                title={label}
                tabIndex={tab.id === focusedId ? 0 : -1}
                className="studio-workspace-tab-button"
                onClick={() => onActivate(tab.id)}
                onFocus={() => setFocusedId(tab.id)}
                onAuxClick={event => {
                  if (event.button !== 1 || tab.pinned) return;
                  event.preventDefault();
                  onClose(tab.id);
                }}
                onKeyDown={event => onTabKeyDown(event, tab, index)}
              >
                <span className="studio-workspace-tab-icon" aria-hidden="true">{tab.icon}</span>
                <span className="studio-workspace-tab-title">{tab.title}</span>
                {tab.detail ? <span className="studio-workspace-tab-detail">{tab.detail}</span> : null}
                {tab.guarded ? <span className="studio-workspace-tab-unsaved" aria-hidden="true" /> : null}
                {tab.pinned ? <Pin className="studio-workspace-tab-pin" size={12} aria-hidden="true" /> : null}
              </button>
              {tab.pinned ? null : (
                // Pointer shortcut only: a tablist may contain only tabs, so keyboard and screen-reader
                // users close a tab with Delete or from its menu (Shift+F10), as its description says.
                <button
                  type="button"
                  className="studio-workspace-tab-close"
                  tabIndex={-1}
                  aria-hidden="true"
                  aria-label={`Close ${label}`}
                  title={`Close (${workspaceShortcuts.closeTab})`}
                  onClick={() => onClose(tab.id)}
                >
                  <X size={13} aria-hidden="true" />
                </button>
              )}
            </div>
          );
        })}
      </div>
      <span id={`${baseId}-tab-help`} hidden>Delete closes the tab. Shift+F10 opens tab actions.</span>
      <div className="studio-workspace-tab-actions">
        <button type="button" className="studio-workspace-tab-action" aria-label="New tab" title={`New tab (${workspaceShortcuts.newTab}). Alt-click a link to open it in a tab.`} onClick={onNewTab}>
          <Plus size={15} aria-hidden="true" />
        </button>
        <button
          ref={allTabsRef}
          type="button"
          className="studio-workspace-tab-action"
          aria-label={`All tabs (${tabs.length})`}
          title="All tabs"
          aria-haspopup="menu"
          aria-expanded={menu !== null && "all" in menu}
          onClick={() => {
            menuAnchorRef.current = allTabsRef.current;
            setMenu(current => current && "all" in current ? null : { all: true });
          }}
        >
          <span className="studio-workspace-tab-count" aria-hidden="true">{tabs.length}</span>
          <ChevronDown size={14} aria-hidden="true" />
        </button>
      </div>
      {notice ? (
        <p className="studio-workspace-notice" role="status">
          <span>{notice}</span>
          <button type="button" onClick={onDismissNotice} aria-label="Dismiss">
            <X size={13} aria-hidden="true" />
          </button>
        </p>
      ) : null}
      <WorkspaceMenu
        anchorRef={menuAnchorRef}
        open={menu !== null}
        label={menuTab ? `Actions for ${tabLabel(menuTab)}` : "All tabs"}
        items={menuItems}
        onClose={() => {
          const returnTo = menu && "tabId" in menu ? menu.tabId : null;
          setMenu(null);
          if (returnTo) focusTab(returnTo);
          else allTabsRef.current?.focus();
        }}
      />
    </div>
  );
}

function WorkspaceMenu({ anchorRef, open, label, items, onClose }: { anchorRef: React.RefObject<HTMLElement | null>; open: boolean; label: string; items: MenuItem[]; onClose(): void }) {
  const menuId = useId();
  const enabled = enabledMenuIndexes(items.map(item => Boolean(item.disabled)));
  const initial = Math.max(0, items.findIndex(item => item.checked && !item.disabled));
  const [activeIndex, setActiveIndex] = useState(enabled.includes(initial) ? initial : enabled[0] ?? 0);

  useEffect(() => {
    if (open) setActiveIndex(enabled.includes(initial) ? initial : enabled[0] ?? 0);
    // Reset the roving item only when the menu opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  function focusItem(index: number) {
    setActiveIndex(index);
    focusIndexedItem(menuId, index);
  }

  return (
    <AnchoredPopover id={menuId} anchorRef={anchorRef} open={open} className="studio-overflow-menu-popover studio-workspace-menu" minWidth={220} maxHeight={360} onDismiss={onClose} onPositioned={() => focusIndexedItem(menuId, activeIndex)}>
      <div
        className="studio-overflow-menu-list"
        role="menu"
        aria-label={label}
        onKeyDown={event => {
          if (event.key === "Escape" || event.key === "Tab") {
            event.preventDefault();
            onClose();
          } else if (event.key === "ArrowDown") {
            event.preventDefault();
            focusItem(stepEnabledIndex(enabled, activeIndex, 1));
          } else if (event.key === "ArrowUp") {
            event.preventDefault();
            focusItem(stepEnabledIndex(enabled, activeIndex, -1));
          } else if (event.key === "Home") {
            event.preventDefault();
            focusItem(enabled[0] ?? 0);
          } else if (event.key === "End") {
            event.preventDefault();
            focusItem(enabled[enabled.length - 1] ?? 0);
          }
        }}
      >
        {items.map((item, index) => (
          <button
            key={item.id}
            id={`${menuId}-item-${index}`}
            type="button"
            role={item.checked === undefined ? "menuitem" : "menuitemradio"}
            aria-checked={item.checked === undefined ? undefined : item.checked}
            className="studio-overflow-menu-item studio-workspace-menu-item"
            disabled={item.disabled}
            tabIndex={index === activeIndex ? 0 : -1}
            onClick={() => {
              if (item.disabled) return;
              onClose();
              item.onSelect();
            }}
          >
            <span>{item.label}</span>
            {item.hint ? <kbd>{item.hint}</kbd> : null}
          </button>
        ))}
      </div>
    </AnchoredPopover>
  );
}
