import { describe, expect, it } from "vitest";
import {
  activateTab,
  createTab,
  defaultLocationKey,
  insertTab,
  maxRetainedBackgroundTabs,
  mergeDuplicateResources,
  moveTab,
  removeTabs,
  setPinned,
  updateTab,
  type WorkspaceState
} from "../app/workspace/model";
import { parseWorkspace, readWorkspace, restoreWorkspace, sanitizeLocation, serializeWorkspace, workspaceStorageKey, writeWorkspace, type WorkspaceScope } from "../app/workspace/storage";

function workspace(...locations: string[]): WorkspaceState {
  const tabs = locations.map((location, index) => ({ ...createTab(location, index, `t${index + 1}`), retained: true }));
  return { tabs, activeId: tabs[0].id };
}

const ids = (state: WorkspaceState) => state.tabs.map(tab => tab.id);

describe("workspace tab model", () => {
  it("opens a tab beside the active one and after the pinned group", () => {
    let state = setPinned(workspace("/a", "/b", "/c"), "t3", true);
    expect(ids(state)).toEqual(["t3", "t1", "t2"]);
    state = activateTab(state, "t3");
    state = insertTab(state, createTab("/d", 10, "t4"));
    expect(ids(state)).toEqual(["t3", "t4", "t1", "t2"]);
  });

  it("activates the next tab when the active one closes, else the previous, and never leaves the workspace empty", () => {
    let state = activateTab(workspace("/a", "/b", "/c"), "t2");
    state = removeTabs(state, new Set(["t2"]));
    expect(state.activeId).toBe("t3");
    state = removeTabs(state, new Set(["t3"]));
    expect(state.activeId).toBe("t1");
    state = removeTabs(state, new Set(["t1"]));
    expect(state.tabs).toHaveLength(1);
    expect(state.tabs[0].location).toBe("/");
    expect(state.activeId).toBe(state.tabs[0].id);
  });

  it("reorders only within the pinned or unpinned group", () => {
    let state = setPinned(workspace("/a", "/b", "/c", "/d"), "t1", true);
    state = moveTab(state, "t4", 0);
    expect(ids(state)).toEqual(["t1", "t4", "t2", "t3"]);
    state = moveTab(state, "t1", 3);
    expect(ids(state)).toEqual(["t1", "t4", "t2", "t3"]);
    state = setPinned(state, "t1", false);
    expect(ids(state)).toEqual(["t1", "t4", "t2", "t3"]);
    expect(state.tabs[0].pinned).toBe(false);
  });

  it("releases the least recently used clean background tabs and keeps guarded ones retained", () => {
    const count = maxRetainedBackgroundTabs + 3;
    let state = workspace(...Array.from({ length: count }, (_, index) => `/screen/${index}`));
    state = updateTab(state, "t2", { guarded: true, lastActiveAt: 0 });
    state = activateTab(state, `t${count}`, 1000);
    const retainedBackground = state.tabs.filter(tab => tab.id !== state.activeId && tab.retained);
    expect(retainedBackground).toHaveLength(maxRetainedBackgroundTabs);
    expect(state.tabs.find(tab => tab.id === "t2")?.retained).toBe(true);
  });

  it("folds tabs that show one resource into the active tab, keeping the earliest place and pin", () => {
    const keyOf = (location: string) => new URL(location, "http://studio.invalid").searchParams.get("definition");
    let state = workspace("/runs", "/editor?definition=x&folderId=f", "/editor?definition=x");
    state = setPinned(state, "t2", true);
    state = updateTab(state, "t2", { retained: false, title: { title: "Order flow", screenKey: "k" } });
    state = activateTab(state, "t3");
    const merged = mergeDuplicateResources(state, keyOf);
    expect(merged.tabs.map(tab => [tab.id, tab.location, tab.pinned])).toEqual([["t3", "/editor?definition=x", true], ["t1", "/runs", false]]);
    expect(merged.activeId).toBe("t3");
    expect(merged.tabs[0].title?.title).toBe("Order flow");
    expect(mergeDuplicateResources(merged, keyOf)).toBe(merged);
  });

  it("never drops a mounted duplicate, which may hold edits", () => {
    const keyOf = (location: string) => new URL(location, "http://studio.invalid").searchParams.get("definition");
    const state = workspace("/editor?definition=x", "/editor?definition=x&folderId=f");
    expect(mergeDuplicateResources(state, keyOf)).toBe(state);
  });

  it("matches locations by path and sorted query, ignoring the backend qualifier", () => {
    expect(defaultLocationKey("/runs?b=2&a=1&backend=x")).toBe(defaultLocationKey("/runs/?a=1&b=2"));
    expect(defaultLocationKey("/runs?a=1")).not.toBe(defaultLocationKey("/runs?a=2"));
  });
});

describe("workspace persistence", () => {
  const scope: WorkspaceScope = { origin: "https://studio.test", backendId: "local", backendBaseUrl: "https://api.test", subject: "ada", tenantId: "acme" };

  it("never persists credentials, sign-in callback state, one-shot intents or the backend qualifier", () => {
    expect(sanitizeLocation("/workflows/definitions?definition=a&backend=local&access_token=x&homeAction=create")).toBe("/workflows/definitions?definition=a");
    expect(sanitizeLocation("/signin?code=abc&state=xyz")).toBe("/signin");
    expect(sanitizeLocation("/workflows/definitions?state=archived")).toBe("/workflows/definitions?state=archived");
    expect(sanitizeLocation("//evil.test/path")).toBe("/");
    expect(sanitizeLocation("/runs#id_token=abc")).toBe("/runs");
  });

  it("round-trips tabs, pins, titles and scroll but not in-memory history state", () => {
    let state = workspace("/a", "/b");
    state = setPinned(state, "t2", true);
    state = updateTab(state, "t1", { title: { title: "Order flow", detail: "Draft", screenKey: "/a|" }, scrollTop: 240, historyState: { secret: "payload" } });
    const stored = serializeWorkspace(state);
    expect(JSON.stringify(stored)).not.toContain("payload");
    const parsed = parseWorkspace(JSON.parse(JSON.stringify(stored)))!;
    expect(ids(parsed)).toEqual(["t2", "t1"]);
    expect(parsed.tabs[1]).toMatchObject({ title: { title: "Order flow", detail: "Draft" }, scrollTop: 240, retained: false });
    expect(parsed.tabs[0].pinned).toBe(true);
  });

  it("drops malformed entries instead of failing restoration", () => {
    expect(parseWorkspace({ version: 2, tabs: [] })).toBeNull();
    const parsed = parseWorkspace({ version: 1, activeId: "missing", tabs: [null, { id: 4 }, { id: "ok", location: "/runs" }, { id: "ok", location: "/dup" }] })!;
    expect(parsed.tabs.map(tab => tab.location)).toEqual(["/runs"]);
    expect(parsed.activeId).toBe("ok");
  });

  it("scopes storage to the host, backend and identity", () => {
    const storage = window.localStorage;
    writeWorkspace(scope, workspace("/a"), storage);
    expect(readWorkspace(scope, storage)?.tabs[0].location).toBe("/a");
    expect(readWorkspace({ ...scope, backendId: "other" }, storage)).toBeNull();
    expect(readWorkspace({ ...scope, subject: "grace" }, storage)).toBeNull();
    expect(readWorkspace({ ...scope, tenantId: "globex" }, storage)).toBeNull();
    storage.removeItem(workspaceStorageKey(scope));
  });

  it("lets an explicit deep link win over the restored active tab, and Home restore it", () => {
    const restored = activateTab(workspace("/a", "/workflows/instances/r1"), "t1");
    const deepLinked = restoreWorkspace(restored, "/workflows/instances/r1?backend=local");
    expect(deepLinked.activeId).toBe("t2");
    const fresh = restoreWorkspace(restored, "/runs");
    expect(fresh.tabs).toHaveLength(3);
    expect(fresh.tabs[2].location).toBe("/runs");
    expect(fresh.activeId).toBe(fresh.tabs[2].id);
    expect(restoreWorkspace(restored, "/?backend=local").activeId).toBe("t1");
  });
});
