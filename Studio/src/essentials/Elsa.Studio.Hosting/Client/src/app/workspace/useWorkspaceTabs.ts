import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type RefObject } from "react";
import {
  authSessionEndedEvent,
  isHomeRunDetailPath,
  recordHomeRunDetailOpened,
  requestStudioNavigation,
  studioWorkspaceTabRequestedEvent,
  type StudioWorkspaceTabRequest,
  type StudioWorkspaceTabTitle
} from "@elsa-workflows/studio-sdk";
import { currentRelativeLocation, observeHistoryWrites, readHistoryTabId, withHistoryTabId } from "./history";
import {
  activateTab,
  createTab,
  defaultLocationKey,
  getActiveTab,
  homeLocation,
  insertTab,
  locationPathname,
  mergeDuplicateResources,
  moveTab,
  neighbourOf,
  removeTabs,
  setPinned,
  unqualifyLocation,
  updateTab,
  type WorkspaceState,
  type WorkspaceTab
} from "./model";
import { forgetWorkspaceTitles, readWorkspace, restoreWorkspace, writeWorkspace, type WorkspaceScope } from "./storage";

export interface WorkspaceTabsOptions {
  scope: WorkspaceScope;
  /** Adds the backend qualifier; null when the location targets another backend. */
  qualifyPath(path: string): string | null;
  /** The resource a location shows, from its route's workspace options; null for lists and unknown routes. */
  resourceKeyOf(location: string): string | null;
  /** Default title used in guard notices. */
  titleOf(tab: WorkspaceTab): string;
  contentRef: RefObject<HTMLElement | null>;
}

export interface WorkspaceTabsController {
  state: WorkspaceState;
  activeTab: WorkspaceTab;
  /** Pathname of the active tab, which is the screen the shell renders. */
  path: string;
  notice: string | null;
  dismissNotice(): void;
  navigate(path: string): void;
  open(path: string, options?: { activate?: boolean; newTab?: boolean }): void;
  activate(id: string): void;
  close(id: string): void;
  closeOthers(id: string): void;
  closeAll(): void;
  setPinned(id: string, pinned: boolean): void;
  move(id: string, toIndex: number): void;
  setTitle(id: string, title: StudioWorkspaceTabTitle | null): void;
  /** Whether the tab is active now; read inside async callbacks, unlike `state.activeId` from a render. */
  isActive(id: string): boolean;
  /** Marks a background tab as holding pending work, so it is kept mounted and closes only with notice. */
  markPendingWork(id: string): void;
  screenKeyOf(location: string): string;
}

const unsavedNotice = (title: string) => `"${title}" has unsaved changes or a save in progress. Save or discard them before closing its tab.`;

export function useWorkspaceTabs({ scope, qualifyPath, resourceKeyOf, titleOf, contentRef }: WorkspaceTabsOptions): WorkspaceTabsController {
  const scopeKey = JSON.stringify(scope);
  const [state, setStateValue] = useState<WorkspaceState>(() => restoreWorkspace(readWorkspace(scope), unqualifyLocation(currentRelativeLocation())));
  const [notice, setNotice] = useState<string | null>(null);
  const stateRef = useRef(state);
  // The active tab's newest location, kept current from every history write so a tab is never
  // restored from a stale address (screens update filters with replaceState and no event).
  const activeLocationRef = useRef(getActiveTab(state).location);
  const acceptedLocationRef = useRef(currentRelativeLocation());
  // Where the active tab was before a screen pushed an entry the shell has not accepted yet. A screen
  // that opens a resource another tab shows pushes first; that tab keeps this location.
  const locationBeforePushRef = useRef<string | null>(null);
  const pendingScrollRef = useRef<number | null>(null);
  const forgetTitlesRef = useRef(false);
  const optionsRef = useRef({ qualifyPath, resourceKeyOf, titleOf });
  optionsRef.current = { qualifyPath, resourceKeyOf, titleOf };

  const commit = useCallback((next: WorkspaceState) => {
    stateRef.current = next;
    setStateValue(next);
  }, []);

  const screenKeyOf = useCallback((location: string) => `${locationPathname(location)}|${optionsRef.current.resourceKeyOf(location) ?? ""}`, []);

  /** Another tab already showing the resource at `location`. Lists only match on an explicit open. */
  const findShowing = useCallback((location: string, options: { excludeId?: string; matchLists: boolean }) => {
    const { resourceKeyOf: keyOf } = optionsRef.current;
    const key = keyOf(location);
    const tabs = stateRef.current.tabs.filter(tab => tab.id !== options.excludeId);
    if (key) return tabs.find(tab => keyOf(tab.location) === key);
    if (!options.matchLists) return undefined;
    const locationKey = defaultLocationKey(location);
    return tabs.find(tab => !keyOf(tab.location) && defaultLocationKey(tab.location) === locationKey);
  }, []);

  /** Makes the active tab's location the address bar entry, tagged with its tab. */
  const writeAddress = useCallback((tab: WorkspaceTab, mode: "push" | "replace") => {
    const qualified = optionsRef.current.qualifyPath(tab.location) ?? optionsRef.current.qualifyPath(homeLocation) ?? homeLocation;
    const data = withHistoryTabId(tab.historyState, tab.id);
    if (mode === "push") window.history.pushState(data, "", qualified);
    else window.history.replaceState(data, "", qualified);
    acceptedLocationRef.current = currentRelativeLocation();
    activeLocationRef.current = unqualifyLocation(acceptedLocationRef.current);
  }, []);

  /**
   * Moves the workspace to another tab. Switching is not navigation, so it never asks the outgoing
   * screen's guards to block; it only probes them so the strip can mark background tabs that hold
   * unsaved or pending work.
   */
  const switchTo = useCallback((id: string, mode: "push" | "replace" = "push") => {
    const current = stateRef.current;
    if (id === current.activeId || !current.tabs.some(tab => tab.id === id)) return;
    const outgoing = getActiveTab(current);
    const outgoingLocation = locationBeforePushRef.current ?? activeLocationRef.current;
    locationBeforePushRef.current = null;
    const guarded = !requestStudioNavigation(outgoingLocation, outgoingLocation, "probe");
    let next = updateTab(current, outgoing.id, {
      location: outgoingLocation,
      scrollTop: contentRef.current?.scrollTop ?? outgoing.scrollTop,
      guarded,
      historyState: mode === "push" ? window.history.state : outgoing.historyState
    });
    next = activateTab(next, id);
    const incoming = getActiveTab(next);
    pendingScrollRef.current = incoming.scrollTop ?? 0;
    writeAddress(incoming, mode);
    commit(next);
    setNotice(null);
    if (isHomeRunDetailPath(locationPathname(incoming.location))) recordHomeRunDetailOpened();
  }, [commit, contentRef, writeAddress]);

  const open = useCallback((path: string, options: { activate?: boolean; newTab?: boolean } = {}) => {
    const location = unqualifyLocation(path);
    const activate = options.activate ?? true;
    if (!options.newTab) {
      const existing = findShowing(location, { matchLists: true });
      if (existing) {
        if (activate) switchTo(existing.id);
        return;
      }
    }
    const tab = createTab(location);
    const next = insertTab(stateRef.current, tab);
    stateRef.current = next;
    if (activate) switchTo(tab.id);
    else commit(next);
  }, [commit, findShowing, switchTo]);

  const accept = useCallback((location: string) => {
    locationBeforePushRef.current = null;
    acceptedLocationRef.current = currentRelativeLocation();
    activeLocationRef.current = unqualifyLocation(location);
    const current = stateRef.current;
    const active = getActiveTab(current);
    if (active.location !== activeLocationRef.current) commit(updateTab(current, active.id, { location: activeLocationRef.current }));
  }, [commit]);

  /** Ordinary navigation replaces the active tab's screen, unless another tab already shows that resource. */
  const navigate = useCallback((nextPath: string) => {
    const owner = findShowing(unqualifyLocation(nextPath), { excludeId: stateRef.current.activeId, matchLists: false });
    if (owner) {
      switchTo(owner.id);
      return;
    }
    if (!requestStudioNavigation(acceptedLocationRef.current, nextPath)) return;
    const qualified = optionsRef.current.qualifyPath(nextPath);
    if (!qualified) return;
    window.history.pushState(withHistoryTabId(null, stateRef.current.activeId), "", qualified);
    accept(currentRelativeLocation());
    if (isHomeRunDetailPath(locationPathname(qualified))) recordHomeRunDetailOpened();
    window.dispatchEvent(new PopStateEvent("popstate"));
  }, [accept, findShowing, switchTo]);

  /** Closes tabs, asking the active tab's guards with a `close` intent and keeping guarded background tabs. */
  const closeMany = useCallback((ids: string[], fallbackId?: string) => {
    const current = stateRef.current;
    const remove = new Set<string>();
    const kept: WorkspaceTab[] = [];
    for (const id of ids) {
      const tab = current.tabs.find(candidate => candidate.id === id);
      if (!tab) continue;
      if (id !== current.activeId) {
        if (tab.guarded) kept.push(tab);
        else remove.add(id);
        continue;
      }
      const survivor = fallbackId ? current.tabs.find(candidate => candidate.id === fallbackId) : neighbourOf({ ...current, tabs: current.tabs.filter(candidate => !ids.includes(candidate.id) || candidate.id === id) }, id);
      if (requestStudioNavigation(activeLocationRef.current, survivor?.location ?? homeLocation, "close")) remove.add(id);
      else kept.push(tab);
    }
    if (ids.length === 1 && kept.length === 1 && kept[0].id !== current.activeId) {
      // A single background tab with unfinished work comes to the front so its editor can explain itself.
      switchTo(kept[0].id);
      setNotice(unsavedNotice(optionsRef.current.titleOf(kept[0])));
      return;
    }
    setNotice(kept.length === 0 ? null : kept.length === 1 ? unsavedNotice(optionsRef.current.titleOf(kept[0])) : `${kept.length} tabs have unsaved changes or a save in progress and stayed open.`);
    if (remove.size === 0) return;
    const activeRemoved = remove.has(current.activeId);
    let next = removeTabs(current, remove);
    if (activeRemoved && fallbackId && next.tabs.some(tab => tab.id === fallbackId)) next = activateTab(next, fallbackId);
    if (!activeRemoved) {
      commit(next);
      return;
    }
    const incoming = getActiveTab(next);
    pendingScrollRef.current = incoming.scrollTop ?? 0;
    // Push, so Back returns to the closed tab's last address and reopens it.
    writeAddress(incoming, "push");
    commit(next);
  }, [commit, switchTo, writeAddress]);

  const close = useCallback((id: string) => closeMany([id]), [closeMany]);
  const closeOthers = useCallback((id: string) => closeMany(stateRef.current.tabs.filter(tab => tab.id !== id && !tab.pinned).map(tab => tab.id), id), [closeMany]);
  const closeAll = useCallback(() => {
    const unpinned = stateRef.current.tabs.filter(tab => !tab.pinned);
    const firstPinned = stateRef.current.tabs.find(tab => tab.pinned);
    closeMany(unpinned.map(tab => tab.id), firstPinned?.id);
  }, [closeMany]);

  const setTitle = useCallback((id: string, title: StudioWorkspaceTabTitle | null) => {
    const current = stateRef.current;
    const tab = current.tabs.find(candidate => candidate.id === id);
    if (!tab) return;
    const location = id === current.activeId ? activeLocationRef.current : tab.location;
    const value = title ? { title: title.title, ...(title.detail ? { detail: title.detail } : {}), screenKey: screenKeyOf(location) } : undefined;
    if (JSON.stringify(value) === JSON.stringify(tab.title)) return;
    commit(updateTab(current, id, { title: value }));
  }, [commit, screenKeyOf]);

  // The address is the active tab: tag the entry the page opened on and show a restored tab's location.
  useLayoutEffect(() => {
    const active = getActiveTab(stateRef.current);
    const qualified = optionsRef.current.qualifyPath(active.location);
    if (qualified) window.history.replaceState(withHistoryTabId(window.history.state, active.id), "", qualified);
    acceptedLocationRef.current = currentRelativeLocation();
    activeLocationRef.current = unqualifyLocation(acceptedLocationRef.current);
    pendingScrollRef.current = active.scrollTop ?? null;
  }, []);

  useEffect(() => observeHistoryWrites(() => stateRef.current.activeId, (location, tabId, kind) => {
    if (tabId !== stateRef.current.activeId) return;
    if (kind === "push") locationBeforePushRef.current ??= activeLocationRef.current;
    activeLocationRef.current = unqualifyLocation(location);
  }), []);

  // Capture phase: the shell decides which tab an entry belongs to before screens react to it. When Back
  // or Forward lands on another tab's entry, the visible screen never sees that address.
  useEffect(() => {
    const onPopState = (event: PopStateEvent) => {
      const qualified = optionsRef.current.qualifyPath(currentRelativeLocation());
      if (!qualified) {
        window.history.replaceState(window.history.state, "", acceptedLocationRef.current);
        return;
      }
      if (qualified !== currentRelativeLocation()) window.history.replaceState(window.history.state, "", qualified);
      const location = unqualifyLocation(currentRelativeLocation());
      const current = stateRef.current;
      const entryTabId = readHistoryTabId(window.history.state);
      if (entryTabId && entryTabId !== current.activeId) {
        event.stopImmediatePropagation();
        const owner = findShowing(location, { matchLists: false });
        if (current.tabs.some(tab => tab.id === entryTabId)) {
          // The entry belongs to a tab that is still open: return to that tab as it is now.
          switchTo(entryTabId, "replace");
        } else if (owner) {
          // The closed tab's resource is open elsewhere: focus that tab rather than open a second editor.
          if (owner.id !== current.activeId) switchTo(owner.id, "replace");
          // Keep the active screen's newest address, which screens may have replaced without an event.
          else writeAddress({ ...owner, location: activeLocationRef.current }, "replace");
        } else {
          // The entry belongs to a closed tab: Back reopens it at that address.
          const reopened = createTab(location, Date.now(), entryTabId);
          stateRef.current = insertTab(current, reopened);
          switchTo(entryTabId, "replace");
        }
        return;
      }
      const owner = findShowing(location, { excludeId: current.activeId, matchLists: false });
      if (owner) {
        event.stopImmediatePropagation();
        switchTo(owner.id, "replace");
        return;
      }
      const nextPath = locationPathname(location);
      if (nextPath !== locationPathname(unqualifyLocation(acceptedLocationRef.current)) && !requestStudioNavigation(acceptedLocationRef.current, currentRelativeLocation())) {
        window.history.pushState(withHistoryTabId(null, current.activeId), "", acceptedLocationRef.current);
        accept(acceptedLocationRef.current);
        return;
      }
      if (!entryTabId) window.history.replaceState(withHistoryTabId(window.history.state, current.activeId), "", currentRelativeLocation());
      accept(location);
      if (isHomeRunDetailPath(nextPath)) recordHomeRunDetailOpened();
    };
    window.addEventListener("popstate", onPopState, { capture: true });
    return () => window.removeEventListener("popstate", onPopState, { capture: true });
  }, [accept, findShowing, switchTo, writeAddress]);

  // Tabs are restored before routes are registered, so a deep link or the restored set can hold two tabs
  // for one resource. Fold them as soon as routes can name resources, before the duplicate mounts. This
  // runs again whenever routes change; that is safe because only unmounted tabs are ever dropped.
  useLayoutEffect(() => {
    const next = mergeDuplicateResources(stateRef.current, resourceKeyOf);
    if (next !== stateRef.current) commit(next);
  }, [commit, resourceKeyOf]);

  useEffect(() => {
    const onRequest = (event: Event) => {
      const detail = (event as CustomEvent<StudioWorkspaceTabRequest>).detail;
      if (!detail || typeof detail.path !== "string" || !detail.path.startsWith("/") || detail.path.startsWith("//")) return;
      event.preventDefault();
      open(detail.path, { activate: detail.activate });
    };
    window.addEventListener(studioWorkspaceTabRequestedEvent, onRequest);
    return () => window.removeEventListener(studioWorkspaceTabRequestedEvent, onRequest);
  }, [open]);

  // Background tabs have no live guards, so the shell warns before the document unloads for them.
  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (stateRef.current.tabs.some(tab => tab.id !== stateRef.current.activeId && tab.guarded)) event.preventDefault();
    };
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, []);

  const persist = useCallback(() => {
    const current = stateRef.current;
    const active = getActiveTab(current);
    let snapshot = updateTab(current, active.id, { location: activeLocationRef.current, scrollTop: contentRef.current?.scrollTop ?? active.scrollTop });
    if (forgetTitlesRef.current) snapshot = { ...snapshot, tabs: snapshot.tabs.map(tab => ({ ...tab, title: undefined })) };
    writeWorkspace(JSON.parse(scopeKey) as WorkspaceScope, snapshot);
  }, [contentRef, scopeKey]);

  useEffect(() => {
    persist();
  }, [persist, state]);

  useEffect(() => {
    const onPageHide = () => persist();
    const onSessionEnded = () => {
      forgetTitlesRef.current = true;
      forgetWorkspaceTitles(JSON.parse(scopeKey) as WorkspaceScope);
    };
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener(authSessionEndedEvent, onSessionEnded);
    return () => {
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener(authSessionEndedEvent, onSessionEnded);
    };
  }, [persist, scopeKey]);

  // Restore the shared content area's scroll offset for the tab that just became active. A remounting
  // screen may still be loading, so retry for a few frames until its content is tall enough.
  useLayoutEffect(() => {
    const target = pendingScrollRef.current;
    const content = contentRef.current;
    if (target === null || !content) return;
    pendingScrollRef.current = null;
    content.scrollTop = target;
    if (target === 0 || content.scrollTop === target) return;
    let frames = 0;
    let handle = 0;
    const retry = () => {
      content.scrollTop = target;
      if (content.scrollTop !== target && ++frames < 30) handle = window.requestAnimationFrame(retry);
    };
    handle = window.requestAnimationFrame(retry);
    return () => window.cancelAnimationFrame(handle);
  }, [contentRef, state.activeId]);

  const isActive = useCallback((id: string) => stateRef.current.activeId === id, []);
  // A background screen holding navigation until it returns keeps its tab mounted, like unsaved work.
  const markPendingWork = useCallback((id: string) => {
    const current = stateRef.current;
    const tab = current.tabs.find(candidate => candidate.id === id);
    if (!tab || tab.guarded || id === current.activeId) return;
    commit(updateTab(current, id, { guarded: true }));
  }, [commit]);

  const activeTab = getActiveTab(state);
  return useMemo(() => ({
    state,
    activeTab,
    path: locationPathname(activeTab.location),
    notice,
    dismissNotice: () => setNotice(null),
    navigate,
    open,
    activate: (id: string) => switchTo(id),
    close,
    closeOthers,
    closeAll,
    setPinned: (id: string, pinned: boolean) => commit(setPinned(stateRef.current, id, pinned)),
    move: (id: string, toIndex: number) => commit(moveTab(stateRef.current, id, toIndex)),
    setTitle,
    isActive,
    markPendingWork,
    screenKeyOf
  }), [activeTab, close, closeAll, closeOthers, commit, isActive, markPendingWork, navigate, notice, open, screenKeyOf, setTitle, state, switchTo]);
}
