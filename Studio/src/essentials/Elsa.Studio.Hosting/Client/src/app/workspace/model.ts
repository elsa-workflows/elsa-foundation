import type { StudioWorkspaceTabTitle } from "@elsa-workflows/studio-sdk";

/** A module-supplied title, kept only while the tab stays on the screen that set it. */
export interface WorkspaceTabTitle extends StudioWorkspaceTabTitle {
  screenKey: string;
}

/**
 * One workspace tab. `location` is a Studio-relative location without the backend qualifier, which the
 * connection adds when the tab becomes the address bar.
 */
export interface WorkspaceTab {
  id: string;
  location: string;
  pinned: boolean;
  title?: WorkspaceTabTitle;
  /** Scroll offset of the shared content area when the tab was last left. */
  scrollTop?: number;
  /** Set when a probe found unsaved or pending work as the tab went to the background. */
  guarded?: boolean;
  /** Whether the tab's screen is retained. Restored and evicted tabs remount from their location. */
  retained: boolean;
  /** In-memory history entry state, restored with the address when the tab returns. Never persisted. */
  historyState?: unknown;
  lastActiveAt: number;
}

export interface WorkspaceState {
  tabs: WorkspaceTab[];
  activeId: string;
}

/** Background tabs kept mounted besides the active one. Older clean tabs are released and remount on return. */
export const maxRetainedBackgroundTabs = 5;
/** Upper bound on open tabs; opening another releases the oldest clean unpinned background tab. */
export const maxWorkspaceTabs = 30;
export const homeLocation = "/";

let tabSequence = 0;
export function createTabId() {
  tabSequence += 1;
  const random = typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID().slice(0, 8) : Math.random().toString(36).slice(2, 10);
  return `tab-${Date.now().toString(36)}-${tabSequence}-${random}`;
}

export function createTab(location: string, now = Date.now(), id = createTabId()): WorkspaceTab {
  return { id, location, pinned: false, retained: false, lastActiveAt: now };
}

export function getActiveTab(state: WorkspaceState) {
  return state.tabs.find(tab => tab.id === state.activeId) ?? state.tabs[0];
}

/** Pinned tabs always lead; within each group the user's order is kept. */
export function normalizeOrder(tabs: WorkspaceTab[]) {
  return [...tabs.filter(tab => tab.pinned), ...tabs.filter(tab => !tab.pinned)];
}

export function activateTab(state: WorkspaceState, id: string, now = Date.now()): WorkspaceState {
  if (!state.tabs.some(tab => tab.id === id)) return state;
  const tabs = state.tabs.map(tab => tab.id === id ? { ...tab, retained: true, guarded: false, lastActiveAt: now } : tab);
  return { tabs: releaseBackgroundTabs(tabs, id), activeId: id };
}

/** Records what the outgoing tab looked like as it goes to the background. */
export function updateTab(state: WorkspaceState, id: string, update: Partial<Omit<WorkspaceTab, "id">>): WorkspaceState {
  return { ...state, tabs: state.tabs.map(tab => tab.id === id ? { ...tab, ...update } : tab) };
}

/** Inserts a tab after the active one (after the pinned group at the earliest), like a browser opening a link. */
export function insertTab(state: WorkspaceState, tab: WorkspaceTab): WorkspaceState {
  const tabs = [...state.tabs];
  const activeIndex = tabs.findIndex(candidate => candidate.id === state.activeId);
  const firstUnpinned = tabs.findIndex(candidate => !candidate.pinned);
  const index = Math.max(activeIndex + 1, firstUnpinned === -1 ? tabs.length : firstUnpinned);
  tabs.splice(index, 0, tab);
  return { ...state, tabs: trimOpenTabs(tabs, state.activeId) };
}

/** The neighbour that becomes active when `id` closes: the next tab, else the previous one. */
export function neighbourOf(state: WorkspaceState, id: string) {
  const index = state.tabs.findIndex(tab => tab.id === id);
  if (index === -1) return undefined;
  return state.tabs[index + 1] ?? state.tabs[index - 1];
}

/** Removes tabs; the workspace always keeps at least one, falling back to a fresh Home tab. */
export function removeTabs(state: WorkspaceState, ids: ReadonlySet<string>, now = Date.now()): WorkspaceState {
  const remaining = state.tabs.filter(tab => !ids.has(tab.id));
  if (remaining.length === 0) {
    const home = { ...createTab(homeLocation, now), retained: true };
    return { tabs: [home], activeId: home.id };
  }
  if (!ids.has(state.activeId)) return { ...state, tabs: remaining };
  const fallback = state.tabs.slice(state.tabs.findIndex(tab => tab.id === state.activeId)).find(tab => !ids.has(tab.id))
    ?? [...remaining].reverse()[0];
  return activateTab({ tabs: remaining, activeId: fallback.id }, fallback.id, now);
}

export function moveTab(state: WorkspaceState, id: string, toIndex: number): WorkspaceState {
  const from = state.tabs.findIndex(tab => tab.id === id);
  if (from === -1) return state;
  const tab = state.tabs[from];
  const group = state.tabs.filter(candidate => candidate.pinned === tab.pinned);
  const offset = tab.pinned ? 0 : state.tabs.length - group.length;
  const target = Math.max(offset, Math.min(offset + group.length - 1, toIndex));
  if (target === from) return state;
  const tabs = [...state.tabs];
  tabs.splice(from, 1);
  tabs.splice(target, 0, tab);
  return { ...state, tabs };
}

export function setPinned(state: WorkspaceState, id: string, pinned: boolean): WorkspaceState {
  const tab = state.tabs.find(candidate => candidate.id === id);
  if (!tab || tab.pinned === pinned) return state;
  const others = state.tabs.filter(candidate => candidate.id !== id);
  const pinnedCount = others.filter(candidate => candidate.pinned).length;
  // A newly pinned tab joins the end of the pinned group; an unpinned one leads the unpinned group.
  others.splice(pinnedCount, 0, { ...tab, pinned });
  return { ...state, tabs: others };
}

/** Releases the least recently used clean background tabs beyond the retention budget. */
function releaseBackgroundTabs(tabs: WorkspaceTab[], activeId: string) {
  const background = tabs
    .filter(tab => tab.id !== activeId && tab.retained)
    .sort((a, b) => b.lastActiveAt - a.lastActiveAt);
  // Tabs holding unsaved or pending work are never released; they still count against the budget.
  let budget = maxRetainedBackgroundTabs - background.filter(tab => tab.guarded).length;
  const release = new Set<string>();
  for (const tab of background) {
    if (tab.guarded) continue;
    if (budget > 0) budget -= 1;
    else release.add(tab.id);
  }
  return release.size === 0 ? tabs : tabs.map(tab => release.has(tab.id) ? { ...tab, retained: false, historyState: undefined } : tab);
}

function trimOpenTabs(tabs: WorkspaceTab[], activeId: string) {
  if (tabs.length <= maxWorkspaceTabs) return tabs;
  const closable = tabs
    .filter(tab => tab.id !== activeId && !tab.pinned && !tab.guarded)
    .sort((a, b) => a.lastActiveAt - b.lastActiveAt)
    .slice(0, tabs.length - maxWorkspaceTabs)
    .map(tab => tab.id);
  return tabs.filter(tab => !closable.includes(tab.id));
}

/**
 * Folds tabs that show the same resource into one, so two editors never hold one draft. Restoration
 * runs before routes can name resources, so a deep link or a restored set can carry duplicates until
 * this runs. The active tab survives (a deep link wins) and takes the earliest duplicate's place and pin;
 * otherwise a mounted tab, else the earliest, survives. A mounted duplicate may hold edits and is kept.
 */
export function mergeDuplicateResources(state: WorkspaceState, keyOf: (location: string) => string | null): WorkspaceState {
  const groups = new Map<string, WorkspaceTab[]>();
  for (const tab of state.tabs) {
    const key = keyOf(tab.location);
    if (key) groups.set(key, [...(groups.get(key) ?? []), tab]);
  }
  let tabs = state.tabs;
  for (const group of groups.values()) {
    if (group.length < 2) continue;
    const survivor = group.find(tab => tab.id === state.activeId) ?? group.find(tab => tab.retained) ?? group[0];
    const dropped = group.filter(tab => tab !== survivor && !tab.retained);
    if (dropped.length === 0) continue;
    const members = new Set([survivor.id, ...dropped.map(tab => tab.id)]);
    const merged: WorkspaceTab = {
      ...survivor,
      pinned: survivor.pinned || dropped.some(tab => tab.pinned),
      title: survivor.title ?? dropped.find(tab => tab.title)?.title
    };
    let placed = false;
    tabs = tabs.flatMap(tab => {
      if (!members.has(tab.id)) return [tab];
      if (placed) return [];
      placed = true;
      return [merged];
    });
  }
  return tabs === state.tabs ? state : { tabs: normalizeOrder(tabs), activeId: state.activeId };
}

/** Identity used to match "already open" for explicit opens when the route names no resource. */
export function defaultLocationKey(location: string) {
  const url = new URL(location, "http://studio.invalid");
  url.searchParams.delete("backend");
  url.searchParams.sort();
  const pathname = url.pathname.length > 1 ? url.pathname.replace(/\/+$/, "") : url.pathname;
  return `${pathname}${url.search}`;
}

/** Strips the backend qualifier the connection owns; the tab stores only what it shows. */
export function unqualifyLocation(location: string) {
  const url = new URL(location, "http://studio.invalid");
  url.searchParams.delete("backend");
  return `${url.pathname}${url.search}${url.hash}`;
}

export function locationPathname(location: string) {
  const pathname = new URL(location, "http://studio.invalid").pathname;
  return pathname.length > 1 && pathname.endsWith("/") ? pathname.slice(0, -1) : pathname;
}

/** Title shown for the tab: a module title for the current screen, else the shell's default. */
export function resolveTabTitle(tab: WorkspaceTab, screenKey: string, fallback: string): StudioWorkspaceTabTitle {
  return tab.title && tab.title.screenKey === screenKey ? tab.title : { title: fallback };
}
