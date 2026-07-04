import React, { useCallback, useId } from "react";

export interface StudioTabItem {
  id: string;
  label: string;
}

/**
 * Shared WAI-ARIA tablist keyboard handler. Returns an `onKeyDown` handler that implements the
 * authoring-practices roving-focus pattern for a horizontal tablist:
 *   - ArrowLeft / ArrowRight move (and activate) the previous / next enabled tab, wrapping around.
 *   - Home / End jump to the first / last enabled tab.
 * Focus follows selection ("selection follows focus"), which matches this app's automatic-activation
 * tablists. Callers own selection state; the hook just tells them which tab id to select next and
 * moves DOM focus onto the corresponding roving-tabindex button.
 *
 * `tabIds` must be in visual (DOM) order. `onSelect` is invoked with the id to activate.
 */
export function useTablistKeyboard(tabIds: string[], activeId: string, onSelect: (id: string) => void) {
  return useCallback(
    (event: React.KeyboardEvent<HTMLElement>) => {
      if (tabIds.length === 0) {
        return;
      }

      let nextIndex: number | null = null;
      const currentIndex = Math.max(0, tabIds.indexOf(activeId));

      switch (event.key) {
        case "ArrowRight":
        case "ArrowDown":
          nextIndex = (currentIndex + 1) % tabIds.length;
          break;
        case "ArrowLeft":
        case "ArrowUp":
          nextIndex = (currentIndex - 1 + tabIds.length) % tabIds.length;
          break;
        case "Home":
          nextIndex = 0;
          break;
        case "End":
          nextIndex = tabIds.length - 1;
          break;
        default:
          return;
      }

      event.preventDefault();
      const nextId = tabIds[nextIndex];
      onSelect(nextId);

      // Move DOM focus onto the newly-selected tab so roving tabindex stays consistent with selection.
      const tablist = event.currentTarget.closest('[role="tablist"]') ?? event.currentTarget;
      const nextTab = tablist.querySelector<HTMLElement>(`[data-tab-id="${cssEscape(nextId)}"]`);
      nextTab?.focus();
    },
    [tabIds, activeId, onSelect]
  );
}

/** Build stable, linkable ids for a tab and its panel so `aria-controls` / `aria-labelledby` line up. */
export function tabElementIds(baseId: string, tabId: string) {
  const safe = tabId.replace(/[^a-zA-Z0-9_-]/g, "-");
  return { tabId: `${baseId}-tab-${safe}`, panelId: `${baseId}-panel-${safe}` };
}

function cssEscape(value: string) {
  const cssApi = typeof globalThis !== "undefined" ? (globalThis.CSS as { escape?: (v: string) => string } | undefined) : undefined;
  if (cssApi?.escape) {
    return cssApi.escape(value);
  }

  return value.replace(/["\\]/g, "\\$&");
}

export function StudioTabs({
  tabs,
  activeTab,
  onSelect,
  ariaLabel
}: {
  tabs: StudioTabItem[];
  activeTab: string;
  onSelect(tabId: string): void;
  ariaLabel?: string;
}) {
  const baseId = useId();
  const tabIds = tabs.map(tab => tab.id);
  const onKeyDown = useTablistKeyboard(tabIds, activeTab, onSelect);

  return (
    <div className="studio-tabs" role="tablist" aria-label={ariaLabel} onKeyDown={onKeyDown}>
      {tabs.map(tab => {
        const isActive = tab.id === activeTab;
        const ids = tabElementIds(baseId, tab.id);
        return (
          <button
            key={tab.id}
            id={ids.tabId}
            data-tab-id={tab.id}
            type="button"
            role="tab"
            aria-selected={isActive}
            aria-controls={ids.panelId}
            // Roving tabindex: only the active tab is in the sequential tab order; arrows reach the rest.
            tabIndex={isActive ? 0 : -1}
            className={isActive ? "active" : ""}
            onClick={() => onSelect(tab.id)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * Optional wrapper that renders the `role="tabpanel"` region linked to a {@link StudioTabs} tab.
 * Pass the same `baseId`/`tabId` you would derive with {@link tabElementIds}; here we recompute the
 * ids from a shared `baseId` supplied by the caller so the linkage is explicit.
 */
export function StudioTabPanel({
  baseId,
  tabId,
  children,
  className
}: {
  baseId: string;
  tabId: string;
  children: React.ReactNode;
  className?: string;
}) {
  const ids = tabElementIds(baseId, tabId);
  return (
    <div id={ids.panelId} role="tabpanel" aria-labelledby={ids.tabId} tabIndex={0} className={className}>
      {children}
    </div>
  );
}
