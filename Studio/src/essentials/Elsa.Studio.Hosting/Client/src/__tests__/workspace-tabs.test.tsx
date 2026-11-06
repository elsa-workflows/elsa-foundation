import React, { useEffect, useRef, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { openStudioWorkspaceTab, requestStudioNavigation, studioNavigationRequestedEvent, useStudioRouteLocation, useStudioWorkspaceTitle, useStudioWorkspaceWhenActive, type StudioNavigationRequest } from "../sdk";
import { useWorkspaceTabs, type WorkspaceTabsController } from "../app/workspace/useWorkspaceTabs";
import { WorkspaceTabPanels } from "../app/workspace/WorkspaceTabPanels";
import { readHistoryTabId } from "../app/workspace/history";
import { workspaceStorageKey, type WorkspaceScope } from "../app/workspace/storage";

// The test build runs React's production bundle (no `act`), so let the scheduler flush renders and
// passive effects before asserting.
async function act(action: () => unknown) {
  await action();
  await new Promise(resolve => setTimeout(resolve, 20));
}

const scope: WorkspaceScope = { origin: "http://localhost:3000", backendId: "local", backendBaseUrl: "http://api.test", subject: "ada", tenantId: null };

// Live effect bookkeeping for the editor screen, so tests can see whether a hidden tab still runs.
const editorProbe = { mountedEffects: 0, popstates: 0, intents: [] as string[], setEdits: null as null | ((value: number) => void), whenActive: null as null | ((action: () => void) => void) };

function Editor() {
  const [edits, setEdits] = useState(0);
  const location = useStudioRouteLocation();
  editorProbe.setEdits = setEdits;
  editorProbe.whenActive = useStudioWorkspaceWhenActive();
  useStudioWorkspaceTitle(`Order flow ${location.searchParams.get("definition") ?? ""}`.trim(), "Draft");
  useEffect(() => {
    editorProbe.mountedEffects += 1;
    const onPopState = () => { editorProbe.popstates += 1; };
    const guard = (event: Event) => {
      const intent = (event as CustomEvent<StudioNavigationRequest>).detail.intent ?? "navigate";
      editorProbe.intents.push(intent);
      if (edits > 0) event.preventDefault();
    };
    window.addEventListener("popstate", onPopState);
    window.addEventListener(studioNavigationRequestedEvent, guard);
    return () => {
      editorProbe.mountedEffects -= 1;
      window.removeEventListener("popstate", onPopState);
      window.removeEventListener(studioNavigationRequestedEvent, guard);
    };
  }, [edits]);
  return (
    <section className="editor">
      <span data-testid="editor-location">{location.pathname}{location.search}</span>
      <button type="button" onClick={() => setEdits(value => value + 1)}>Edit</button>
      <output data-testid="edits">{edits}</output>
      <button type="button" onClick={() => setEdits(0)}>Save</button>
    </section>
  );
}

function List() {
  return <section className="list">List</section>;
}

function renderScreen(location: string) {
  return location.startsWith("/editor") ? <Editor /> : <List />;
}

const resourceKeyOf = (location: string) => {
  const url = new URL(location, "http://localhost:3000");
  return url.pathname === "/editor" && url.searchParams.get("definition") ? `definition:${url.searchParams.get("definition")}` : null;
};

let controller: WorkspaceTabsController;

function Harness() {
  const contentRef = useRef<HTMLElement>(null);
  controller = useWorkspaceTabs({ scope, qualifyPath: path => path, resourceKeyOf, titleOf: () => "Order flow", contentRef });
  return (
    <main ref={contentRef}>
      <WorkspaceTabPanels baseId="ws" tabs={controller.state.tabs} activeId={controller.state.activeId} renderScreen={renderScreen} titleOf={() => "Screen"} setTitle={controller.setTitle} isActive={controller.isActive} markPendingWork={controller.markPendingWork} onCloseTab={controller.close} />
    </main>
  );
}

let container: HTMLDivElement;
let root: Root;

async function mount(location: string) {
  window.history.replaceState(null, "", location);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root.render(<Harness />));
}

function visiblePanel() {
  return container.querySelector<HTMLElement>(".studio-workspace-panel:not([hidden])")!;
}

function click(label: string, scopeElement: ParentNode = visiblePanel()) {
  const button = [...scopeElement.querySelectorAll("button")].find(candidate => candidate.textContent === label)!;
  button.click();
}

const currentLocation = () => `${window.location.pathname}${window.location.search}`;

beforeEach(() => {
  window.localStorage.removeItem(workspaceStorageKey(scope));
  Object.assign(editorProbe, { mountedEffects: 0, popstates: 0, intents: [], setEdits: null, whenActive: null });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  window.localStorage.removeItem(workspaceStorageKey(scope));
});

describe("workspace tabs", () => {
  it("reuses the active tab for ordinary navigation", async () => {
    await mount("/list");
    await act(async () => controller.navigate("/list?page=2"));
    expect(controller.state.tabs).toHaveLength(1);
    expect(controller.activeTab.location).toBe("/list?page=2");
    expect(currentLocation()).toBe("/list?page=2");
    expect(readHistoryTabId(window.history.state)).toBe(controller.state.activeId);
  });

  it("keeps a background editor's unsaved edits while tearing down its effects", async () => {
    await mount("/editor?definition=a");
    await act(async () => click("Edit"));
    await act(async () => click("Edit"));
    expect(editorProbe.mountedEffects).toBe(1);
    const editorTab = controller.state.activeId;

    await act(async () => controller.open("/list"));
    expect(controller.state.tabs).toHaveLength(2);
    expect(currentLocation()).toBe("/list");
    // Hidden: React keeps the state, but the editor no longer listens to history or guards.
    expect(editorProbe.mountedEffects).toBe(0);
    await act(async () => window.dispatchEvent(new PopStateEvent("popstate")));
    expect(editorProbe.popstates).toBe(0);
    // The probe on the way out marked it as holding unsaved work, so the strip can show it.
    expect(editorProbe.intents).toContain("probe");
    expect(controller.state.tabs.find(tab => tab.id === editorTab)?.guarded).toBe(true);

    await act(async () => controller.activate(editorTab));
    expect(currentLocation()).toBe("/editor?definition=a");
    expect(visiblePanel().querySelector("[data-testid=edits]")?.textContent).toBe("2");
    expect(editorProbe.mountedEffects).toBe(1);
  });

  it("holds a background screen's navigation until its tab returns, leaving the active tab's address alone", async () => {
    await mount("/editor?definition=a");
    const editorTab = controller.state.activeId;
    const whenActive = editorProbe.whenActive!;
    await act(async () => controller.open("/list"));
    const listTab = controller.state.activeId;

    // A request the editor started completes in the background and opens what it created.
    await act(async () => whenActive(() => {
      window.history.pushState({}, "", "/editor?definition=a&draft=d1");
      window.dispatchEvent(new PopStateEvent("popstate"));
    }));
    expect(currentLocation()).toBe("/list");
    expect(controller.activeTab.location).toBe("/list");
    expect(controller.state.tabs.find(tab => tab.id === editorTab)?.location).toBe("/editor?definition=a");
    // The held navigation keeps the tab mounted however many other tabs are visited meanwhile.
    expect(controller.state.tabs.find(tab => tab.id === editorTab)?.guarded).toBe(true);
    for (let index = 0; index < 7; index++) await act(async () => controller.open(`/list?page=${index}`));
    expect(controller.state.tabs.find(tab => tab.id === editorTab)?.retained).toBe(true);

    await act(async () => controller.activate(editorTab));
    expect(currentLocation()).toBe("/editor?definition=a&draft=d1");
    expect(controller.activeTab.location).toBe("/editor?definition=a&draft=d1");
    expect(readHistoryTabId(window.history.state)).toBe(editorTab);
    expect(controller.state.tabs.find(tab => tab.id === listTab)?.location).toBe("/list");
  });

  it("does not let a background tab's guard block navigation in the active tab", async () => {
    await mount("/editor?definition=a");
    await act(async () => click("Edit"));
    await act(async () => controller.open("/list"));
    expect(requestStudioNavigation("/list", "/list?page=3")).toBe(true);
    await act(async () => controller.navigate("/list?page=3"));
    expect(currentLocation()).toBe("/list?page=3");
  });

  it("keeps a tab with unsaved edits open on close and explains why", async () => {
    await mount("/editor?definition=a");
    await act(async () => click("Edit"));
    const editorTab = controller.state.activeId;
    await act(async () => controller.close(editorTab));
    expect(editorProbe.intents).toContain("close");
    expect(controller.state.tabs).toHaveLength(1);
    expect(controller.notice).toMatch(/unsaved changes/);

    // A guarded background tab comes to the front instead of closing.
    await act(async () => controller.open("/list"));
    await act(async () => controller.close(editorTab));
    expect(controller.state.activeId).toBe(editorTab);
    expect(controller.state.tabs).toHaveLength(2);

    await act(async () => click("Save"));
    await act(async () => controller.close(editorTab));
    expect(controller.state.tabs.map(tab => tab.location)).toEqual(["/list"]);
  });

  it("warns before unload while a background tab holds unsaved work", async () => {
    await mount("/editor?definition=a");
    await act(async () => click("Edit"));
    await act(async () => controller.open("/list"));
    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it("focuses the tab that already shows a resource instead of opening a second editor", async () => {
    await mount("/editor?definition=a");
    const editorTab = controller.state.activeId;
    await act(async () => controller.open("/list"));
    const listTab = controller.state.activeId;
    await act(async () => controller.navigate("/editor?definition=a"));
    expect(controller.state.activeId).toBe(editorTab);
    expect(controller.state.tabs.find(tab => tab.id === listTab)?.location).toBe("/list");

    // A screen that pushes the address itself is redirected before it can react.
    await act(async () => controller.activate(listTab));
    window.history.pushState({}, "", "/editor?definition=a");
    await act(async () => window.dispatchEvent(new PopStateEvent("popstate")));
    expect(controller.state.activeId).toBe(editorTab);
    expect(controller.state.tabs.find(tab => tab.id === listTab)?.location).toBe("/list");
    expect(controller.state.tabs).toHaveLength(2);
  });

  it("returns to the tab that owns a history entry on Back, without the visible screen seeing its address", async () => {
    await mount("/list");
    const listTab = controller.state.activeId;
    await act(async () => controller.open("/editor?definition=a"));
    expect(editorProbe.mountedEffects).toBe(1);
    await act(async () => {
      window.history.back();
      await vi.waitFor(() => expect(currentLocation()).toBe("/list"));
    });
    await vi.waitFor(() => expect(controller.state.activeId).toBe(listTab));
    expect(editorProbe.popstates).toBe(0);
  });

  it("lets a background screen render from its own location", async () => {
    await mount("/editor?definition=a");
    await act(async () => controller.open("/list?page=9"));
    // A request resolving in the background re-renders the hidden editor while /list owns the address.
    await act(async () => editorProbe.setEdits?.(5));
    const hidden = container.querySelector<HTMLElement>(".studio-workspace-panel[hidden]")!;
    expect(hidden.querySelector("[data-testid=editor-location]")?.textContent).toBe("/editor?definition=a");
  });

  it("names tabs from module titles and opens explicit requests from any module", async () => {
    await mount("/editor?definition=a");
    await vi.waitFor(() => expect(controller.activeTab.title).toMatchObject({ title: "Order flow a", detail: "Draft" }));
    let handled = false;
    await act(async () => { handled = openStudioWorkspaceTab("/list?page=4"); });
    expect(handled).toBe(true);
    await vi.waitFor(() => expect(controller.activeTab.location).toBe("/list?page=4"));
    expect(openStudioWorkspaceTab("//evil.test")).toBe(false);
  });

  it("folds a deep link into the restored tab that already shows its resource", async () => {
    window.localStorage.setItem(workspaceStorageKey(scope), JSON.stringify({
      version: 1,
      activeId: "list",
      tabs: [{ id: "list", location: "/list" }, { id: "draft", location: "/editor?definition=a&folderId=f", pinned: true }]
    }));
    await mount("/editor?definition=a");
    expect(controller.state.tabs.map(tab => [tab.location, tab.pinned])).toEqual([["/editor?definition=a", true], ["/list", false]]);
    expect(controller.activeTab.location).toBe("/editor?definition=a");
    expect(container.querySelectorAll(".editor")).toHaveLength(1);
  });

  it("focuses the tab that shows a resource when Back lands on a closed tab for it", async () => {
    await mount("/editor?definition=a");
    const closedTab = controller.state.activeId;
    await act(async () => controller.open("/list"));
    await act(async () => controller.close(closedTab));
    await act(async () => controller.open("/editor?definition=a"));
    const editorTab = controller.state.activeId;
    expect(editorTab).not.toBe(closedTab);
    await act(async () => controller.activate(controller.state.tabs.find(tab => tab.location === "/list")!.id));
    // Back across the list's entries lands on the closed tab's entry for the same resource.
    window.history.replaceState({ "elsa.studio.workspaceTab": closedTab }, "", "/editor?definition=a");
    await act(async () => window.dispatchEvent(new PopStateEvent("popstate", { state: window.history.state })));
    expect(controller.state.activeId).toBe(editorTab);
    expect(controller.state.tabs.filter(tab => tab.location.startsWith("/editor"))).toHaveLength(1);
    expect(container.querySelectorAll(".editor")).toHaveLength(1);
  });

  it("keeps the active screen's newest address when Back lands on a closed tab for its resource", async () => {
    await mount("/editor?definition=a");
    const closedTab = controller.state.activeId;
    await act(async () => controller.open("/list"));
    await act(async () => controller.close(closedTab));
    await act(async () => controller.open("/editor?definition=a"));
    // The screen updates its own address without a popstate, as filter changes do.
    window.history.replaceState(window.history.state, "", "/editor?definition=a&panel=inputs");
    window.history.pushState({ "elsa.studio.workspaceTab": closedTab }, "", "/editor?definition=a");
    await act(async () => window.dispatchEvent(new PopStateEvent("popstate", { state: window.history.state })));
    expect(currentLocation()).toBe("/editor?definition=a&panel=inputs");
  });

  it("restores open tabs for the same user and backend", async () => {
    await mount("/editor?definition=a");
    await act(async () => controller.open("/list"));
    await act(async () => controller.setPinned(controller.state.activeId, true));
    await act(async () => root.unmount());
    container.remove();

    await mount("/");
    expect(controller.state.tabs.map(tab => [tab.location, tab.pinned])).toEqual([["/list", true], ["/editor?definition=a", false]]);
    expect(currentLocation()).toBe("/list");
    // Only the active tab is mounted; the restored editor waits until it is visited.
    expect(container.querySelectorAll(".studio-workspace-panel")).toHaveLength(1);
  });
});
