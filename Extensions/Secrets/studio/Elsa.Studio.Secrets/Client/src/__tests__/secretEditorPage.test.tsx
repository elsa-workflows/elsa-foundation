import React, { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioWorkspaceTabProvider, studioNavigationRequestedEvent, type ElsaStudioModuleApi, type StudioEndpointContext, type StudioWorkspaceTabHandle } from "@elsa-workflows/studio-sdk";
import { SecretEditorPage } from "../SecretEditorPage";

const disposers = new Set<() => void>();
afterEach(() => { for (const dispose of disposers) dispose(); window.history.replaceState(null, "", "/"); });
const descriptors = {
  types: [{ name: "text", displayName: "Text", supportedStoreNames: ["encrypted"] }],
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }], scopes: []
};
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (error: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function endpoint(post = vi.fn(async () => ({ name: "DbConnectionString" }))) {
  return { baseUrl: "https://fixture.example", http: { getJson: vi.fn(async () => descriptors), postJson: post } } as unknown as StudioEndpointContext;
}
function change(input: HTMLInputElement, value: string) {
  flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value); input.dispatchEvent(new Event("input", { bubbles: true })); });
}
function control(host: HTMLElement, field: string) { return host.querySelector<HTMLInputElement>(`[data-secret-field="${field}"]`)!; }
function button(host: HTMLElement, text: string) { return [...host.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent?.trim() === text)!; }
function click(host: HTMLElement, text: string) { flushSync(() => button(host, text).click()); }
function blocked() { const event = new Event(studioNavigationRequestedEvent, { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }
function renderEditor(options: { context?: StudioEndpointContext; confirm?: ReturnType<typeof vi.fn> } = {}) {
  window.history.replaceState(null, "", "/security/secrets/new?search=accepted&query=draft&scope=Team&backend=original");
  const host = document.createElement("div"); document.body.append(host); const root = createRoot(host);
  const navigate = vi.fn(); const confirm = options.confirm ?? vi.fn(async () => true);
  let context = options.context ?? endpoint(); let active = true;
  const markPendingWork = vi.fn(); const setTitle = vi.fn();
  const notifications = { notify: vi.fn() };
  const draw = () => {
    const tab: StudioWorkspaceTabHandle = { id: "secret-editor", active, location: "/security/secrets/new?search=accepted&query=draft&scope=Team&backend=original", isActive: () => active, markPendingWork, setTitle };
    const api = { backend: context, backendSession: context, dialogs: { confirm }, notifications } as unknown as ElsaStudioModuleApi;
    flushSync(() => root.render(<StudioWorkspaceTabProvider value={tab}><Activity mode={active ? "visible" : "hidden"}><SecretEditorPage api={api} navigate={navigate} /></Activity></StudioWorkspaceTabProvider>));
  };
  draw();
  const unmount = () => { flushSync(() => root.unmount()); host.remove(); disposers.delete(unmount); }; disposers.add(unmount);
  return { host, navigate, confirm, markPendingWork, notifications, unmount,
    switchEndpoint(next: StudioEndpointContext) { context = next; draw(); },
    setActive(next: boolean) { active = next; draw(); }
  };
}
async function ready(host: HTMLElement) { await vi.waitFor(() => expect(control(host, "name")).not.toBeNull()); }
function fill(host: HTMLElement) { change(control(host, "name"), "DbConnectionString"); change(host.querySelector<HTMLInputElement>(".secrets-value-input input")!, "private-test-material"); }
function submit(host: HTMLElement) { flushSync(() => host.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }))); }

describe("standalone secret editor lifetime and navigation", () => {
  it("confirms one accepted create without carrying a replayable confirmation in the return URL", async () => {
    const { host, navigate, notifications } = renderEditor(); await ready(host); fill(host); submit(host);
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(notifications.notify).toHaveBeenCalledOnce();
    expect(notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Secret created." });
    expect(new URL(navigate.mock.calls[0][0], window.location.origin).searchParams.has("notice")).toBe(false);
  });
  it("does not confirm a write that settles after the editor has unmounted", async () => {
    const request = deferred<unknown>();
    const { host, navigate, notifications, unmount } = renderEditor({ context: endpoint(vi.fn(() => request.promise)) });
    await ready(host); fill(host); submit(host); unmount(); request.resolve({ name: "DbConnectionString" });
    await request.promise; await new Promise(resolve => setTimeout(resolve, 0));
    expect(navigate).not.toHaveBeenCalled(); expect(notifications.notify).not.toHaveBeenCalled();
  });
  it("keeps a dirty draft on rejected discard and returns with browse/backend context when confirmed", async () => {
    const confirm = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    const { host, navigate } = renderEditor({ confirm }); await ready(host); fill(host);
    expect(blocked()).toBe(true);
    click(host, "Cancel"); await vi.waitFor(() => expect(confirm).toHaveBeenCalledTimes(1));
    expect(navigate).not.toHaveBeenCalled(); expect(control(host, "name").value).toBe("DbConnectionString");
    click(host, "Back to secrets"); await vi.waitFor(() => expect(navigate).toHaveBeenCalledTimes(1));
    const route = new URL(navigate.mock.calls[0][0], window.location.origin);
    expect(route.pathname).toBe("/security/secrets");
    expect(Object.fromEntries(route.searchParams)).toEqual({ search: "accepted", query: "draft", scope: "Team", focusCreate: "1", backend: "original" });
    expect(blocked()).toBe(false);
  });

  it("blocks dismissal and duplicate submission during a write, then preserves the failed draft for retry", async () => {
    const request = deferred<unknown>(); const post = vi.fn(() => request.promise);
    const { host, navigate, confirm } = renderEditor({ context: endpoint(post) }); await ready(host); fill(host); submit(host); submit(host);
    expect(post).toHaveBeenCalledTimes(1); expect(blocked()).toBe(true);
    click(host, "Back to secrets");
    await vi.waitFor(() => expect(button(host, "Back to secrets").disabled).toBe(true));
    expect(button(host, "Cancel").disabled).toBe(true);
    expect(confirm).not.toHaveBeenCalled(); expect(navigate).not.toHaveBeenCalled();
    request.reject(new Error("Write unavailable."));
    await vi.waitFor(() => expect(host.querySelector("[role=alert]")?.textContent).toBe("Write unavailable."));
    expect(document.activeElement).toBe(host.querySelector("[role=alert]"));
    expect(control(host, "name").value).toBe("DbConnectionString");
    expect(host.querySelector<HTMLInputElement>(".secrets-value-input input")?.type).toBe("password");
    expect(blocked()).toBe(true); expect(button(host, "Cancel").disabled).toBe(false);
  });

  it("does not navigate a replacement backend when an old write completes", async () => {
    const request = deferred<unknown>(); const post = vi.fn(() => request.promise);
    const { host, navigate, switchEndpoint, notifications } = renderEditor({ context: endpoint(post) }); await ready(host); fill(host); submit(host);
    const replacement = endpoint(); switchEndpoint(replacement); await ready(host);
    expect(control(host, "name").value).toBe("");
    expect(host.querySelector<HTMLInputElement>(".secrets-value-input input")?.value).toBe("");
    fill(host); expect(blocked()).toBe(true);
    request.resolve({ name: "DbConnectionString" }); await request.promise; await new Promise(resolve => setTimeout(resolve, 0));
    expect(navigate).not.toHaveBeenCalled(); expect(replacement.http.postJson).not.toHaveBeenCalled(); expect(notifications.notify).not.toHaveBeenCalled();
    expect(control(host, "name").value).toBe("DbConnectionString"); expect(blocked()).toBe(true);
  });

  it("suppresses stale descriptor responses and keeps denied editors unavailable", async () => {
    const request = deferred<unknown>(); const old = endpoint(); old.http.getJson = vi.fn(() => request.promise) as typeof old.http.getJson;
    const { host, switchEndpoint } = renderEditor({ context: old });
    const denied = endpoint(); denied.http.getJson = vi.fn().mockRejectedValue(Object.assign(new Error("private authorization detail"), { status: 403 }));
    switchEndpoint(denied);
    await vi.waitFor(() => expect(host.textContent).toContain("You do not have permission"));
    request.resolve(descriptors); await request.promise; await new Promise(resolve => setTimeout(resolve, 0));
    expect(host.querySelector("form")).toBeNull(); expect(host.textContent).not.toContain("private authorization detail");
  });

  it("holds a successful background write until its workspace returns and keeps the original backend qualifier", async () => {
    const request = deferred<unknown>(); const post = vi.fn(() => request.promise);
    const { host, navigate, setActive, markPendingWork, notifications } = renderEditor({ context: endpoint(post) }); await ready(host); fill(host); submit(host);
    setActive(false); window.history.replaceState(null, "", "/agents?backend=another");
    request.resolve({ name: "DbConnectionString" });
    await vi.waitFor(() => expect(markPendingWork).toHaveBeenCalledTimes(1));
    expect(navigate).not.toHaveBeenCalled(); expect(window.location.pathname).toBe("/agents");
    window.history.replaceState(null, "", "/security/secrets/new?search=accepted&query=draft&scope=Team&backend=original");
    setActive(true); await vi.waitFor(() => expect(navigate).toHaveBeenCalledTimes(1));
    const route = new URL(navigate.mock.calls[0][0], window.location.origin);
    expect(route.searchParams.get("backend")).toBe("original"); expect(route.searchParams.get("focusSecret")).toBe("DbConnectionString");
    expect(route.searchParams.get("search")).toBe("accepted"); expect(route.searchParams.get("query")).toBe("draft");
    expect(blocked()).toBe(false); expect(post).toHaveBeenCalledTimes(1); expect(notifications.notify).not.toHaveBeenCalled();
  });

  it("does not navigate after an outstanding discard confirmation outlives the page", async () => {
    const answer = deferred<boolean>(); const confirm = vi.fn(() => answer.promise);
    const { host, navigate, unmount } = renderEditor({ confirm }); await ready(host); fill(host); click(host, "Cancel");
    expect(confirm).toHaveBeenCalledTimes(1); unmount(); answer.resolve(true); await answer.promise;
    await new Promise(resolve => setTimeout(resolve, 0)); expect(navigate).not.toHaveBeenCalled();
  });
});
