import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider, AuthProvider, type AuthProviderManager, type AuthSession } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { hasEffectivePermission, HomeTaskGroup } from "../HomeTaskGroup";
import { stubApi, waitUntil } from "./testSupport";

const definitionsPath = "/design/workflows/definitions?take=8";

afterEach(() => {
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
});

function mount(node: React.ReactElement) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(node));
  return {
    container,
    rerender(next: React.ReactElement) {
      flushSync(() => root.render(next));
    },
    unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
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
