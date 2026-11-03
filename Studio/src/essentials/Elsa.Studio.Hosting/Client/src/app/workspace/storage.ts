import { createTab, defaultLocationKey, homeLocation, maxWorkspaceTabs, normalizeOrder, unqualifyLocation, type WorkspaceState, type WorkspaceTab } from "./model";

/** Identifies whose workspace this is: the Studio host, the backend profile and the signed-in identity. */
export interface WorkspaceScope {
  origin: string;
  backendId: string;
  backendBaseUrl: string;
  subject?: string | null;
  tenantId?: string | null;
}

interface StoredTab {
  id: string;
  location: string;
  pinned?: boolean;
  title?: { title: string; detail?: string; screenKey: string };
  scrollTop?: number;
}

interface StoredWorkspace {
  version: 1;
  activeId: string;
  tabs: StoredTab[];
}

const storagePrefix = "elsa.studio.workspace-tabs.v1:";
const maxTitleLength = 120;
// Query parameters that carry credentials, sign-in callback state or one-shot intents (create a
// workflow, finish a backend connection). Restoring them would replay the intent or leave a secret in
// storage, so a persisted tab drops them. `state` only counts beside an authorization `code`, because
// workflow lists use it for their archive filter.
const sensitiveParameter = /^(code|session_state|token|access_token|id_token|refresh_token|authproviderid|secret|password|api[-_]?key|homeaction|backendconnect)$/i;

export function workspaceStorageKey(scope: WorkspaceScope) {
  return `${storagePrefix}${JSON.stringify([scope.origin, scope.backendId, scope.backendBaseUrl, scope.subject || "anonymous", scope.tenantId || "default"])}`;
}

/** The location a tab may persist: Studio-relative, unqualified, without credentials or sign-in state. */
export function sanitizeLocation(location: string) {
  if (!location.startsWith("/") || location.startsWith("//") || location.startsWith("/\\")) return homeLocation;
  let url: URL;
  try {
    url = new URL(unqualifyLocation(location), "http://studio.invalid");
  } catch {
    return homeLocation;
  }
  if (url.origin !== "http://studio.invalid") return homeLocation;
  if (url.searchParams.has("code")) url.searchParams.delete("state");
  for (const name of [...url.searchParams.keys()]) {
    if (sensitiveParameter.test(name)) url.searchParams.delete(name);
  }
  return `${url.pathname}${url.search}${url.hash.length > 1 && !/(access_token|id_token|code)=/i.test(url.hash) ? url.hash : ""}`;
}

export function serializeWorkspace(state: WorkspaceState): StoredWorkspace {
  return {
    version: 1,
    activeId: state.activeId,
    tabs: state.tabs.slice(0, maxWorkspaceTabs).map(tab => ({
      id: tab.id,
      location: sanitizeLocation(tab.location),
      ...(tab.pinned ? { pinned: true } : {}),
      ...(tab.title ? { title: { title: tab.title.title.slice(0, maxTitleLength), ...(tab.title.detail ? { detail: tab.title.detail.slice(0, maxTitleLength) } : {}), screenKey: tab.title.screenKey } } : {}),
      ...(tab.scrollTop ? { scrollTop: Math.round(tab.scrollTop) } : {})
    }))
  };
}

/** Reads a stored workspace, dropping anything malformed. Restored tabs are not retained until visited. */
export function parseWorkspace(value: unknown, now = Date.now()): WorkspaceState | null {
  if (!value || typeof value !== "object") return null;
  const stored = value as Partial<StoredWorkspace>;
  if (stored.version !== 1 || !Array.isArray(stored.tabs)) return null;
  const seen = new Set<string>();
  const tabs: WorkspaceTab[] = [];
  for (const candidate of stored.tabs.slice(0, maxWorkspaceTabs)) {
    if (!candidate || typeof candidate !== "object" || typeof candidate.id !== "string" || typeof candidate.location !== "string") continue;
    if (seen.has(candidate.id)) continue;
    seen.add(candidate.id);
    const tab = createTab(sanitizeLocation(candidate.location), now, candidate.id);
    tab.pinned = candidate.pinned === true;
    const title = candidate.title;
    if (title && typeof title.title === "string" && typeof title.screenKey === "string" && title.title.trim())
      tab.title = { title: title.title.slice(0, maxTitleLength), screenKey: title.screenKey, ...(typeof title.detail === "string" && title.detail ? { detail: title.detail.slice(0, maxTitleLength) } : {}) };
    if (typeof candidate.scrollTop === "number" && Number.isFinite(candidate.scrollTop) && candidate.scrollTop > 0) tab.scrollTop = candidate.scrollTop;
    tabs.push(tab);
  }
  if (tabs.length === 0) return null;
  const activeId = typeof stored.activeId === "string" && seen.has(stored.activeId) ? stored.activeId : tabs[0].id;
  return { tabs: normalizeOrder(tabs), activeId };
}

export function readWorkspace(scope: WorkspaceScope, storage: Storage | undefined = safeLocalStorage()) {
  try {
    const raw = storage?.getItem(workspaceStorageKey(scope));
    return raw ? parseWorkspace(JSON.parse(raw)) : null;
  } catch {
    return null;
  }
}

export function writeWorkspace(scope: WorkspaceScope, state: WorkspaceState, storage: Storage | undefined = safeLocalStorage()) {
  try {
    storage?.setItem(workspaceStorageKey(scope), JSON.stringify(serializeWorkspace(state)));
  } catch {
    // Storage may be full or blocked; the workspace still works for this page lifetime.
  }
}

/** Drops resource titles after sign-out so a shared browser does not keep them; locations and pins stay. */
export function forgetWorkspaceTitles(scope: WorkspaceScope, storage: Storage | undefined = safeLocalStorage()) {
  const state = readWorkspace(scope, storage);
  if (!state) return;
  writeWorkspace(scope, { ...state, tabs: state.tabs.map(tab => ({ ...tab, title: undefined })) }, storage);
}

/**
 * Combines a restored workspace with the address the page opened at. An explicit deep link wins: it
 * focuses the tab that already shows it, or opens beside the restored tabs. Home restores the last
 * active tab.
 */
export function restoreWorkspace(restored: WorkspaceState | null, bootLocation: string, now = Date.now()): WorkspaceState {
  const location = sanitizeLocation(bootLocation);
  if (!restored) {
    const tab = { ...createTab(location, now), retained: true };
    return { tabs: [tab], activeId: tab.id };
  }
  const isHome = defaultLocationKey(location) === defaultLocationKey(homeLocation) && !new URL(location, "http://studio.invalid").hash;
  const match = isHome
    ? restored.tabs.find(tab => tab.id === restored.activeId)
    : restored.tabs.find(tab => defaultLocationKey(tab.location) === defaultLocationKey(location));
  if (match) {
    const tabs = restored.tabs.map(tab => tab.id === match.id ? { ...tab, location: isHome ? tab.location : location, retained: true, lastActiveAt: now } : tab);
    return { tabs, activeId: match.id };
  }
  const tab = { ...createTab(location, now), retained: true };
  return { tabs: [...restored.tabs, tab].slice(-maxWorkspaceTabs), activeId: tab.id };
}

function safeLocalStorage() {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}
