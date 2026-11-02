import React, { useLayoutEffect } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider, AuthProvider, useAuthContext, type AuthProviderManager, type AuthSession } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { hasEffectivePermission, HomeTaskGroup } from "../HomeTaskGroup";
import { stubApi, waitUntil } from "./testSupport";

const definitionsPath = "/design/workflows/definitions?take=8";
const mountedViews = new Set<{ unmount(): void }>();

afterEach(() => {
  for (const view of [...mountedViews]) view.unmount();
  mountedViews.clear();
  vi.restoreAllMocks();
  window.history.replaceState({}, "", "/");
});

describe("Home workflow task group", () => {
  it("mirrors the server permission wildcard, subtree, normalization, and manage-to-read rules", () => {
    const allowsRead = (grant: string) => hasEffectivePermission([grant], "workflows/design:read");
    expect(allowsRead("*")).toBe(true);
    expect(allowsRead("*:read")).toBe(true);
    expect(allowsRead("workflows/*:manage")).toBe(true);
    expect(allowsRead("WORKFLOWS/DESIGN:MANAGE")).toBe(true);
    expect(allowsRead("workflows/design:*")).toBe(true);
    expect(allowsRead("workflows/design:read")).toBe(true);
    expect(allowsRead("workflows/design:manage:create")).toBe(false);
    expect(allowsRead(" workflows/design:read")).toBe(false);
    expect(allowsRead("workflows//design:read")).toBe(false);
    expect(allowsRead("workflows/design/*/read:manage")).toBe(false);
    expect(allowsRead("workflows/design:re*ad")).toBe(false);
  });

  it("opens the sole current draft by exact definition id, including beyond the eight-item preview", async () => {
    const api = stubApi();
    const records = Array.from({ length: 9 }, (_, index) => ({ id: `definition-${index + 1}`, name: `Workflow ${index + 1}` }));
    records[8] = { id: "definition-ninth", name: "Workflow 9", draftId: "draft-9" } as typeof records[number];
    const getJson = vi.spyOn(api.backend.http, "getJson").mockImplementation(async <T,>(url: string) => {
      expect(url).toBe(definitionsPath);
      return { items: records } as T;
    });
    window.history.replaceState({ source: "before-home" }, "", "/dashboard?keep=one#home");
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => new URLSearchParams(window.location.search).get("definition") === "definition-ninth");

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(window.location.search).toBe("?keep=one&definition=definition-ninth");
    expect(window.location.hash).toBe("#home");
    expect(window.history.state).toEqual({ source: "before-home" });
    expect(getJson).toHaveBeenCalledTimes(1);
    view.unmount();
  });

  it("shows stable identifiers when several current drafts share a name and limits the chooser after counting all", async () => {
    const api = stubApi();
    const records = Array.from({ length: 9 }, (_, index) => ({ id: `definition-${index + 1}`, name: "Duplicate name", draftId: `draft-${index + 1}` }));
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: records } as never);
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    const continueButton = view.container.querySelector<HTMLButtonElement>("button.home-task-card")!;
    flushSync(() => continueButton.focus());
    click(continueButton);
    await waitUntil(() => view.container.querySelector("[role='dialog']") !== null);
    await waitUntil(() => view.container.querySelectorAll("[aria-label='Current workflow drafts'] li").length === 8);
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));

    expect(view.container.textContent).toContain("Showing the 8 most recently changed of 9 current drafts.");
    expect(view.container.textContent).toContain("definition-1");
    expect(view.container.textContent).toContain("definition-8");
    expect(view.container.textContent).not.toContain("definition-9");
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(view.container.querySelector("[role='dialog']")).toBeNull();
    expect(continueButton).toBe(document.activeElement);
    view.unmount();
  });

  it("explains an empty current-draft result and recovers through existing Create or Browse routes", async () => {
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [{ id: "published", name: "Published only", publicationState: "published" }] } as never);
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.textContent?.includes("No workflows have a current draft to continue.") === true);

    expect(view.container.querySelector("[role='dialog']")).not.toBeNull();
    expect(view.container.querySelector("a[href^='/workflows/definitions?homeAction=create']")).not.toBeNull();
    expect(view.container.querySelector("a[href^='/workflows/definitions']")).not.toBeNull();
    view.unmount();
  });

  it("keeps empty recovery safe for an authenticated read-only user", async () => {
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [] } as never);
    const view = mount(<AuthProvider manager={authenticatedManager({ permissions: ["workflows/design:read"] })}><HomeTaskGroup api={api} /></AuthProvider>);

    await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);
    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.textContent?.includes("No workflows have a current draft to continue.") === true);

    const actionableCreateControls = [...view.container.querySelectorAll("a[href], button:not(:disabled), [role='button']:not([aria-disabled='true'])")]
      .filter(element => /create workflow/i.test(element.textContent ?? ""));
    expect(actionableCreateControls).toHaveLength(0);
    expect(view.container.textContent).toContain("Creating a workflow requires workflow design access.");
    expect(view.container.querySelector("[role='dialog'] a[href^='/workflows/definitions']")?.textContent).toContain("Browse workflows");
    view.unmount();
  });

  it("reports a server-side denial without guessing a draft or weakening endpoint authority", async () => {
    const api = stubApi();
    const forbidden = Object.assign(new Error("Forbidden"), { status: 403 });
    vi.spyOn(api.backend.http, "getJson").mockRejectedValue(forbidden);
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.querySelector("[role='alert']") !== null);

    expect(view.container.textContent).toContain("You can’t read workflow definitions with your current access.");
    expect(new URL(window.location.href).searchParams.has("definition")).toBe(false);
    view.unmount();
  });

  it("blocks authenticated known denials but leaves anonymous auth-disabled workflows usable", async () => {
    const api = stubApi();
    const getJson = vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [] } as never);
    const denied = mount(<AuthProvider manager={authenticatedManager({ permissions: [] })}><HomeTaskGroup api={api} /></AuthProvider>);
    await waitUntil(() => denied.container.querySelector("button.home-task-card") !== null);
    expect((denied.container.querySelector("button.home-task-card") as HTMLButtonElement).disabled).toBe(true);
    expect(denied.container.querySelector("a[href^='/workflows/definitions']")).toBeNull();
    expect(getJson).not.toHaveBeenCalled();
    denied.unmount();

    const anonymous = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);
    click(anonymous.container.querySelector("button.home-task-card"));
    await waitUntil(() => getJson.mock.calls.length === 1);
    await waitUntil(() => anonymous.container.textContent?.includes("No workflows have a current draft") === true);
    anonymous.unmount();
  });

  it("keeps Home workflow actions available for inherited manage grants", async () => {
    const api = stubApi();
    const getJson = vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [] } as never);
    const view = mount(<AuthProvider manager={authenticatedManager({ permissions: ["workflows/*:manage"] })}><HomeTaskGroup api={api} /></AuthProvider>);

    await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);
    expect((view.container.querySelector("button.home-task-card") as HTMLButtonElement).disabled).toBe(false);
    expect(view.container.querySelector("a.home-task-card--primary[href^='/workflows/definitions?homeAction=create']")).not.toBeNull();
    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => getJson.mock.calls.length === 1);
    await waitUntil(() => view.container.textContent?.includes("No workflows have a current draft") === true);
    view.unmount();
  });

  it("withholds only Needs attention when authenticated design access lacks runtime read", async () => {
    const api = stubApi();
    const getJson = vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [
      { id: "definition-a", name: "Draft A", draftId: "draft-a" },
      { id: "definition-b", name: "Draft B", draftId: "draft-b" }
    ] } as never);
    const view = mount(<AuthProvider manager={authenticatedManager({ permissions: ["workflows/design:manage"] })}><HomeTaskGroup api={api} /></AuthProvider>);
    await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);

    expect(view.container.querySelector("a[href^='/workflows/instances']")).toBeNull();
    const unavailable = [...view.container.querySelectorAll(".home-task-card[aria-disabled='true']")]
      .find(card => card.textContent?.includes("Needs attention"));
    expect(unavailable?.textContent).toContain("Requires access to read workflow runs.");
    expect(view.container.querySelector("a.home-task-card--primary")).not.toBeNull();
    expect(view.container.querySelector<HTMLButtonElement>("button.home-task-card")?.disabled).toBe(false);
    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.textContent?.includes("2 current drafts.") === true);
    expect(getJson).toHaveBeenCalledWith(definitionsPath, expect.anything());
    expect(view.container.querySelectorAll("[aria-label='Current workflow drafts'] li")).toHaveLength(2);
  });

  it.each(["workflows/runtime:read", "WORKFLOWS/RUNTIME:MANAGE", "workflows/*:read", "*:read", "*"])(
    "allows the exact active-incidents destination for effective runtime grant %s", async grant => {
      const api = stubApi();
      window.history.replaceState({}, "", "/dashboard?keep=one&definition=old&homeAction=create&incidentHealth=resolved#home");
      const view = mount(<AuthProvider manager={authenticatedManager({ permissions: [grant] })}><HomeTaskGroup api={api} /></AuthProvider>);
      await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);
      const link = view.container.querySelector<HTMLAnchorElement>("a[href^='/workflows/instances']");
      expect(link?.getAttribute("href")).toBe("/workflows/instances?keep=one&incidentHealth=active#home");
      click(link);
      expect(window.location.pathname).toBe("/workflows/instances");
      expect(window.location.search).toBe("?keep=one&incidentHealth=active");
      expect(window.location.hash).toBe("#home");
    }
  );

  it("retains the active-incidents destination when authentication is disabled", () => {
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={stubApi()} /></AnonymousAuthProvider>);
    const link = view.container.querySelector<HTMLAnchorElement>("a[href^='/workflows/instances']");
    expect(link?.getAttribute("href")).toBe("/workflows/instances?incidentHealth=active");
    click(link);
    expect(window.location.pathname).toBe("/workflows/instances");
  });

  it("closes discovery on backend authority change and ignores the old response", async () => {
    const api = stubApi();
    let resolveDefinitions: ((value: { items: Array<{ id: string; draftId: string; name: string }> }) => void) | undefined;
    const getJson = vi.spyOn(api.backend.http, "getJson").mockImplementation(() => new Promise(resolve => { resolveDefinitions = resolve; }));
    const nextApi = stubApi();
    nextApi.backend.baseUrl = "https://other-backend.example";
    vi.spyOn(nextApi.backend.http, "getJson").mockResolvedValue({ items: [{ id: "new-authority", draftId: "draft-new", name: "New authority" }] } as never);
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => getJson.mock.calls.length === 1);
    expect(view.container.querySelector("[role='dialog']")).not.toBeNull();
    view.rerender(<AnonymousAuthProvider><HomeTaskGroup api={nextApi} /></AnonymousAuthProvider>);
    await waitUntil(() => view.container.querySelector("[role='dialog']") === null);
    resolveDefinitions?.({ items: [{ id: "old-authority", draftId: "draft-old", name: "Old authority" }] });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(window.location.search).not.toContain("definition=");
    expect(view.container.textContent).not.toContain("Old authority");
    expect(view.container.textContent).not.toContain("New authority");
    view.unmount();
  });

  it("hides a loaded chooser and disables its focus trap in the authority-change commit", async () => {
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [
      { id: "old-definition-a", name: "Old authority A", draftId: "old-draft-a" },
      { id: "old-definition-b", name: "Old authority B", draftId: "old-draft-b" }
    ] } as never);
    const manager = authenticatedManager({ subject: "alice", tenantId: "tenant-before", permissions: ["workflows/design:read"] });
    const nextSession: AuthSession = { ...manager.getSession(), subject: "bob", tenantId: "tenant-after" };
    manager.refresh = async () => nextSession;
    let refreshSession: (() => Promise<AuthSession>) | undefined;
    let captureAuthorityCommit = false;
    const authoritySnapshots: Array<{ dialogVisible: boolean; candidateText: string[]; tabPrevented: boolean; outsideKeepsFocus: boolean }> = [];
    const view = mount(
      <AuthProvider manager={manager}>
        <>
          <button type="button" data-testid="outside-home-dialog">Outside Home dialog</button>
          <HomeTaskGroup api={api} />
          <AuthRefreshProbe
            onRefresh={refresh => { refreshSession = refresh; }}
            onAuthorityCommit={() => {
              if (!captureAuthorityCommit) return;
              captureAuthorityCommit = false;
              const outside = view.container.querySelector<HTMLButtonElement>("[data-testid='outside-home-dialog']")!;
              outside.focus();
              const tab = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
              window.dispatchEvent(tab);
              authoritySnapshots.push({
                dialogVisible: view.container.querySelector("[role='dialog']") !== null,
                candidateText: [...view.container.querySelectorAll("[aria-label='Current workflow drafts'] li")].map(item => item.textContent ?? ""),
                tabPrevented: tab.defaultPrevented,
                outsideKeepsFocus: document.activeElement === outside
              });
            }}
          />
        </>
      </AuthProvider>
    );
    await waitUntil(() => refreshSession !== undefined);
    await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);
    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.textContent?.includes("old-definition-a") === true && view.container.textContent?.includes("old-definition-b") === true);

    captureAuthorityCommit = true;
    await refreshSession!();
    await waitUntil(() => authoritySnapshots.length === 1);

    expect(authoritySnapshots).toHaveLength(1);
    expect(authoritySnapshots[0].dialogVisible).toBe(false);
    expect(authoritySnapshots[0].candidateText).toEqual([]);
    expect(authoritySnapshots[0].tabPrevented).toBe(false);
    expect(authoritySnapshots[0].outsideKeepsFocus).toBe(true);
    expect(view.container.textContent).not.toContain("Old authority A");
    expect(view.container.textContent).not.toContain("Old authority B");
    view.unmount();
  });

  it("restores focus to Home when an auth refresh disables the Continue opener", async () => {
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [
      { id: "definition-a", name: "Orders A", draftId: "draft-a" },
      { id: "definition-b", name: "Orders B", draftId: "draft-b" }
    ] } as never);
    const manager = authenticatedManager({ subject: "alice", tenantId: "tenant-a", permissions: ["workflows/design:read"] });
    const nextSession: AuthSession = { ...manager.getSession(), permissions: [] };
    manager.refresh = async () => nextSession;
    let refreshSession: (() => Promise<AuthSession>) | undefined;
    let captureAuthorityCommit = false;
    let authorityCommitted = false;
    const view = mount(
      <AuthProvider manager={manager}>
        <>
          <HomeTaskGroup api={api} />
          <AuthRefreshProbe
            onRefresh={refresh => { refreshSession = refresh; }}
            onAuthorityCommit={() => {
              if (!captureAuthorityCommit) return;
              captureAuthorityCommit = false;
              authorityCommitted = true;
            }}
          />
        </>
      </AuthProvider>
    );

    await waitUntil(() => refreshSession !== undefined);
    await waitUntil(() => view.container.querySelector("button.home-task-card") !== null);
    const continueButton = view.container.querySelector<HTMLButtonElement>("button.home-task-card")!;
    flushSync(() => continueButton.focus());
    expect(document.activeElement).toBe(continueButton);
    click(continueButton);
    await waitUntil(() => view.container.textContent?.includes("definition-a") === true && view.container.textContent?.includes("definition-b") === true);
    await waitUntil(() => view.container.querySelector("[role='dialog']")?.contains(document.activeElement) === true);

    captureAuthorityCommit = true;
    await refreshSession!();
    await waitUntil(() => authorityCommitted && continueButton.disabled);
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));

    const homeFallback = view.container.querySelector<HTMLElement>(".home-task-group")!;
    expect(document.activeElement).not.toBe(document.body);
    expect(homeFallback.tabIndex).toBe(-1);
    expect(document.activeElement).toBe(homeFallback);
    view.unmount();
  });

  it("keeps useful outside focus when the chooser closes", async () => {
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson").mockResolvedValue({ items: [
      { id: "definition-a", name: "Orders A", draftId: "draft-a" },
      { id: "definition-b", name: "Orders B", draftId: "draft-b" }
    ] } as never);
    const view = mount(
      <AnonymousAuthProvider>
        <>
          <button type="button" data-testid="outside-home-dialog">Outside Home dialog</button>
          <HomeTaskGroup api={api} />
        </>
      </AnonymousAuthProvider>
    );

    const continueButton = view.container.querySelector<HTMLButtonElement>("button.home-task-card")!;
    click(continueButton);
    await waitUntil(() => view.container.querySelector("[role='dialog']")?.contains(document.activeElement) === true);
    const outsideButton = view.container.querySelector<HTMLButtonElement>("[data-testid='outside-home-dialog']")!;
    flushSync(() => outsideButton.focus());
    expect(document.activeElement).toBe(outsideButton);

    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await waitUntil(() => view.container.querySelector("[role='dialog']") === null);
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));

    expect(document.activeElement).toBe(outsideButton);
    view.unmount();
  });

  it("closes an open chooser when Workflows turns off and ignores its pending result", async () => {
    const api = stubApi();
    let resolveDefinitions: ((value: { items: Array<{ id: string; draftId: string; name: string }> }) => void) | undefined;
    const getJson = vi.spyOn(api.backend.http, "getJson").mockImplementation(() => new Promise(resolve => { resolveDefinitions = resolve; }));
    const view = mount(<AnonymousAuthProvider><><button type="button">Outside Home task group</button><HomeTaskGroup api={api} /></></AnonymousAuthProvider>);

    window.history.replaceState({}, "", "/dashboard?keep=one");
    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => view.container.querySelector("[role='dialog']") !== null);

    const apiWithoutWorkflows = { ...api, runtime: { ...api.runtime, enabledFeatures: [] } };
    view.rerender(<AnonymousAuthProvider><><button type="button">Outside Home task group</button><HomeTaskGroup api={apiWithoutWorkflows} /></></AnonymousAuthProvider>);
    await waitUntil(() => view.container.querySelector(".home-task-group") === null);
    resolveDefinitions?.({ items: [{ id: "late", draftId: "draft-late", name: "Late result" }] });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(window.location.pathname).toBe("/dashboard");
    expect(window.location.search).toBe("?keep=one");
    expect(view.container.querySelector("[role='dialog']")).toBeNull();
    const outside = view.container.querySelector<HTMLButtonElement>("button")!;
    outside.focus();
    const tab = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    window.dispatchEvent(tab);
    expect(tab.defaultPrevented).toBe(false);

    const restoredApi = { ...api, runtime: { ...api.runtime } };
    view.rerender(<AnonymousAuthProvider><><button type="button">Outside Home task group</button><HomeTaskGroup api={restoredApi} /></></AnonymousAuthProvider>);
    expect(view.container.querySelector(".home-task-group")).not.toBeNull();
    expect(view.container.querySelector("[role='dialog']")).toBeNull();
    expect(getJson).toHaveBeenCalledTimes(1);
    view.unmount();
  });

  it("invalidates a canceled late lookup and hides workflow tasks when the module is off", async () => {
    const api = stubApi();
    let resolveDefinitions: ((value: { items: Array<{ id: string; draftId: string; name: string }> }) => void) | undefined;
    const getJson = vi.spyOn(api.backend.http, "getJson").mockImplementation(() => new Promise(resolve => { resolveDefinitions = resolve; }));
    const view = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);

    click(view.container.querySelector("button.home-task-card"));
    await waitUntil(() => getJson.mock.calls.length === 1);
    click(view.container.querySelector("[role='dialog'] button"));
    resolveDefinitions?.({ items: [{ id: "late", draftId: "draft-late", name: "Late result" }] });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(window.location.pathname).toBe("/");
    expect(view.container.querySelector("[role='dialog']")).toBeNull();
    view.unmount();

    api.runtime.enabledFeatures = [];
    const moduleOff = mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);
    expect(moduleOff.container.querySelector("[aria-label='Workflow tasks']")).toBeNull();
    moduleOff.unmount();
  });
  it("hides authoring tasks on a runtime-only backend",() => {
    const base=stubApi();const api={...base,backendSession:{capabilities:{capabilities:[{id:"elsa.api.runtime",contractVersion:"1",links:[]}]}}} as unknown as typeof base;
    const view=mount(<AnonymousAuthProvider><HomeTaskGroup api={api} /></AnonymousAuthProvider>);
    expect(view.container.querySelector("[aria-label='Workflow tasks']")).toBeNull();view.unmount();
  });

});

function mount(node: React.ReactElement) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const render = (next: React.ReactElement) => flushSync(() => root.render(next));
  render(node);
  let mounted = true;
  const view = {
    container,
    rerender(next: React.ReactElement) {
      render(next);
    },
    unmount() {
      if (!mounted) return;
      mounted = false;
      flushSync(() => root.unmount());
      container.remove();
      mountedViews.delete(view);
    }
  };
  mountedViews.add(view);
  return view;
}

function AuthRefreshProbe({ onRefresh, onAuthorityCommit }: {
  onRefresh(refresh: () => Promise<AuthSession>): void;
  onAuthorityCommit(): void;
}) {
  const { refresh, session } = useAuthContext();
  useLayoutEffect(() => { onRefresh(refresh); }, [onRefresh, refresh]);
  useLayoutEffect(() => { onAuthorityCommit(); }, [onAuthorityCommit, session]);
  return null;
}

function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found");
  flushSync(() => element.click());
}

function authenticatedManager(overrides: Partial<AuthSession> = {}): AuthProviderManager {
  const session: AuthSession = { status: "authenticated", subject: "alice", tenantId: "tenant-a", roles: [], permissions: [], ...overrides };
  return {
    getSession: () => session,
    getCapabilities: async () => ({ ownershipMode: "foundation-owned", providers: [] }),
    initialize: async () => session,
    login: async () => undefined,
    handleCallback: async () => session,
    logout: async () => undefined,
    getAccessToken: async () => "token",
    refresh: async () => session
  };
}
