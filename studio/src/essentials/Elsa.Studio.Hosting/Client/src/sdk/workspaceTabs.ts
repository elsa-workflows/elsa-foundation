import { createContext, useCallback, useContext, useEffect, useRef } from "react";

/**
 * How a route's screens take part in Studio workspace tabs. The host derives tab titles, icons and
 * history from navigation and route metadata, so most routes need nothing here.
 */
export interface StudioRouteWorkspaceOptions {
  /**
   * The resource a location of this route shows, such as `workflow-definition:<id>`. Two workspace tabs
   * never show the same resource: navigating to one that another tab already shows focuses that tab, so a
   * hidden editor and a visible one cannot write the same draft. Return nothing for lists and other
   * locations that may be open in several tabs.
   */
  resourceKey?(location: URL): string | null | undefined;
}

/** A module-supplied tab title, such as a workflow name with `Draft` or `Version 3` as its detail. */
export interface StudioWorkspaceTabTitle {
  title: string;
  detail?: string;
}

/** The workspace tab that renders the calling screen. */
export interface StudioWorkspaceTabHandle {
  readonly id: string;
  /** False while the tab is retained in the background; its effects are torn down until it returns. */
  readonly active: boolean;
  /** The Studio-relative location the tab shows, without the backend qualifier. */
  readonly location: string;
  /** Whether the tab is active right now. Unlike `active`, this is current inside async callbacks. */
  isActive(): boolean;
  /** Marks a background tab as holding pending work, so it is kept mounted and closes only with notice. */
  markPendingWork(): void;
  setTitle(title: StudioWorkspaceTabTitle | null): void;
}

const StudioWorkspaceTabContext = createContext<StudioWorkspaceTabHandle | null>(null);

/** Host-only: supplies the tab handle to the screen a workspace tab renders. */
export const StudioWorkspaceTabProvider = StudioWorkspaceTabContext.Provider;

/** The tab that renders this screen, or null outside a workspace tab (older hosts, dialogs, tests). */
export function useStudioWorkspaceTab() {
  return useContext(StudioWorkspaceTabContext);
}

/**
 * The location this screen shows. The address bar belongs to the active tab, so a screen that reads its
 * location while rendering uses this instead of `window.location`: a background tab can still render
 * (a request resolves, the registry refreshes) while another tab owns the address. It returns a new
 * `URL` on every render, so depend on `.pathname`, `.search` or a parameter, never the object itself.
 */
export function useStudioRouteLocation(): URL {
  const tab = useStudioWorkspaceTab();
  return tab && !tab.active ? new URL(tab.location, window.location.origin) : new URL(window.location.href);
}

/**
 * Returns a function that runs navigation only while this screen's tab is active. History and the address
 * bar belong to the active tab, so a background tab whose request completes (a create that opens the new
 * resource, say) must not write them: the action is held and runs when the tab returns, the latest one
 * winning, and the tab is marked as holding pending work so it stays mounted until then. Outside a
 * workspace tab it runs at once.
 */
export function useStudioWorkspaceWhenActive() {
  const tab = useStudioWorkspaceTab();
  const tabRef = useRef(tab);
  tabRef.current = tab;
  const heldRef = useRef<(() => void) | null>(null);
  // A background tab mounts its effects again when it returns, which is when held navigation runs: after
  // the rest of that commit's effects, so the screen's own history listeners are back first.
  useEffect(() => {
    const held = heldRef.current;
    heldRef.current = null;
    if (held) queueMicrotask(held);
  });
  return useCallback((action: () => void) => {
    const current = tabRef.current;
    if (!current || current.isActive()) {
      action();
      return;
    }
    heldRef.current = action;
    current.markPendingWork();
  }, []);
}

/**
 * Names the workspace tab after the resource this screen shows. The host keeps the title until the tab
 * moves to another screen, so a background tab stays recognizable while its effects are torn down.
 */
export function useStudioWorkspaceTitle(title: string | null | undefined, detail?: string | null) {
  const tab = useStudioWorkspaceTab();
  const setTitle = tab?.setTitle;
  const value = title?.trim();
  useEffect(() => {
    if (setTitle && value) setTitle({ title: value, detail: detail?.trim() || undefined });
  }, [setTitle, value, detail]);
}

export const studioWorkspaceTabRequestedEvent = "elsa:studio-workspace-tab-requested";

export interface StudioWorkspaceTabRequest {
  path: string;
  activate: boolean;
}

/**
 * Asks the host to open an in-app Studio path in another workspace tab, or to focus the tab that already
 * shows that resource. Returns false when no host handled the request, so callers can fall back to
 * ordinary navigation.
 */
export function openStudioWorkspaceTab(path: string, options: { activate?: boolean } = {}) {
  if (typeof window === "undefined") return false;
  return !window.dispatchEvent(new CustomEvent<StudioWorkspaceTabRequest>(studioWorkspaceTabRequestedEvent, {
    cancelable: true,
    detail: { path, activate: options.activate ?? true }
  }));
}

/**
 * Alt (Option) + primary click opens an in-app link in a workspace tab. Ctrl/Cmd, Shift and middle clicks
 * stay with the browser so its own tabs, windows and context menu keep working.
 */
export function isStudioWorkspaceTabClick(event: { button: number; altKey: boolean; ctrlKey: boolean; metaKey: boolean; shiftKey: boolean }) {
  return event.button === 0 && event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey;
}
