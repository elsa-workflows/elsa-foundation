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

/**
 * Build stable, linkable ids for a tab and its panel so `aria-controls` / `aria-labelledby` line up.
 *
 * Keyed on the tab's position, not its (arbitrary) id: sanitizing a raw id such as a file path with
 * `[^a-zA-Z0-9_-] -> "-"` is lossy, so two tabs whose ids differ only in punctuation (`a/b` vs `a.b`)
 * would collapse to the same DOM id and cross-wire their `aria-controls`/`aria-labelledby` linkage.
 * A `useId()`-scoped `baseId` keeps ids unique across tablists; the index keeps them unique within one.
 */
export function tabElementIds(baseId: string, index: number) {
  return { tabId: `${baseId}-tab-${index}`, panelId: `${baseId}-panel-${index}` };
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
      {tabs.map((tab, index) => {
        const isActive = tab.id === activeTab;
        const ids = tabElementIds(baseId, index);
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
 * Pass the same `baseId`/`index` you would derive with {@link tabElementIds}; here we recompute the
 * ids from a shared `baseId` supplied by the caller so the linkage is explicit.
 */
export function StudioTabPanel({
  baseId,
  index,
  children,
  className
}: {
  baseId: string;
  index: number;
  children: React.ReactNode;
  className?: string;
}) {
  const ids = tabElementIds(baseId, index);
  return (
    <div id={ids.panelId} role="tabpanel" aria-labelledby={ids.tabId} tabIndex={0} className={className}>
      {children}
    </div>
  );
}
