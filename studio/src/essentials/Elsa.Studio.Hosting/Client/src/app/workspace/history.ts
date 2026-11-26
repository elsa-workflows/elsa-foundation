/** The history-state key that records which workspace tab an entry belongs to. */
export const workspaceTabHistoryKey = "elsa.studio.workspaceTab";

export function readHistoryTabId(state: unknown) {
  if (!state || typeof state !== "object") return null;
  const value = (state as Record<string, unknown>)[workspaceTabHistoryKey];
  return typeof value === "string" ? value : null;
}

export function withHistoryTabId(state: unknown, tabId: string) {
  const base = state && typeof state === "object" && !Array.isArray(state) ? state as Record<string, unknown> : {};
  return { ...base, [workspaceTabHistoryKey]: tabId };
}

export function currentRelativeLocation() {
  return `${window.location.pathname}${window.location.search}${window.location.hash}`;
}

/**
 * Screens write history directly (`pushState` + a synthetic `popstate`, or `replaceState` for filters),
 * so the shell observes those writes to keep the active tab's location current and to tag each entry
 * with the tab that wrote it. Browser Back/Forward then knows which tab an entry belongs to. The
 * wrapper only adds the tag to plain-object or empty states and otherwise passes calls through.
 */
export function observeHistoryWrites(activeTabId: () => string, onWrite: (location: string, tabId: string, kind: "push" | "replace") => void) {
  const history = window.history;
  const push = history.pushState;
  const replace = history.replaceState;
  const write = (original: History["pushState"], kind: "push" | "replace") => function (this: History, data: unknown, unused: string, url?: string | URL | null) {
    const tagged = readHistoryTabId(data);
    const tabId = tagged ?? activeTabId();
    const state = tagged || (data !== null && data !== undefined && (typeof data !== "object" || Array.isArray(data))) ? data : withHistoryTabId(data, tabId);
    original.call(this, state, unused, url);
    onWrite(currentRelativeLocation(), tabId, kind);
  };
  history.pushState = write(push, "push");
  history.replaceState = write(replace, "replace");
  return () => {
    history.pushState = push;
    history.replaceState = replace;
  };
}
