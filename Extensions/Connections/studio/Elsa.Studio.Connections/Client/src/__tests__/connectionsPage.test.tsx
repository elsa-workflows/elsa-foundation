import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ConnectionsPage } from "../ConnectionsPage";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "../connectionTypes";

const cleanups: Array<() => void> = [];

afterEach(() => {
  cleanups.splice(0).forEach(cleanup => cleanup());
});

describe("Connections page", () => {
  it("keeps details closed until requested and restores focus after Escape", async () => {
    const { context } = endpointContext();
    const { host } = renderPage(context);

    await waitFor(() => detailsButton(host) !== null);
    const opener = detailsButton(host)!;
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    opener.focus();
    click(opener);
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    const titleId = dialog!.getAttribute("aria-labelledby");
    expect(titleId).toBeTruthy();
    expect(dialog!.querySelector("h2")?.id).toBe(titleId);
    expect(dialog!.querySelector("h2")?.textContent).toBe(connection.displayName);

    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));

    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it("distinguishes an empty collection from an empty search and clears the search", async () => {
    const { context } = endpointContext({
      getJson: vi.fn(async (url: string) => url.startsWith("/connections?") ? { items: [] } : descriptors)
    });
    const { host } = renderPage(context);

    await waitFor(() => host.textContent?.includes("No connections yet") === true);
    changeSearch(host, "missing-service");
    click(button(host, "Search"));

    await waitFor(() => host.textContent?.includes("No matching connections") === true);
    expect(host.textContent).toContain("Try a different name or clear your search.");
    click(button(host, "Clear search"));
    await waitFor(() => host.textContent?.includes("No connections yet") === true);
    expect(host.textContent).not.toContain("No matching connections");
  });

  it("shows a list error and retries the current search", async () => {
    const listQueries: string[] = [];
    let listCalls = 0;
    const { context } = endpointContext({
      getJson: vi.fn(async (url: string) => {
        if (url.startsWith("/connections?")) {
          listQueries.push(new URL(url, "https://server.example").searchParams.get("search") ?? "");
          listCalls++;
          if (listCalls === 2) throw new Error("temporary network failure");
          return { items: [connection] };
        }
        return descriptors;
      })
    });
    const { host } = renderPage(context);

    await waitFor(() => detailsButton(host) !== null);
    changeSearch(host, "customer");
    click(button(host, "Search"));
    await waitFor(() => host.textContent?.includes("Connections could not be loaded") === true);
    expect(host.querySelector('[role="alert"]')?.textContent).toContain("Connections could not be loaded");

    click(button(host, "Retry"));
    await waitFor(() => detailsButton(host) !== null);
    expect(listQueries).toEqual(["", "customer", "customer"]);
  });

  it("locks test, revoke, and delete while a connection test is pending", async () => {
    const result = deferred<{ succeeded: boolean; message: string }>();
    const postJson = vi.fn(async () => result.promise);
    const dialogs = createDialogs();
    const { context } = endpointContext({ postJson });
    const { host } = renderPage(context, dialogs);

    await waitFor(() => detailsButton(host) !== null);
    click(detailsButton(host)!);
    click(button(host, "Test"));
    await waitFor(() => findButton(host, "Testing…") !== null);

    expect(button(host, "Testing…").disabled).toBe(true);
    expect(button(host, "Revoke").disabled).toBe(true);
    expect(button(host, "Delete").disabled).toBe(true);
    click(button(host, "Testing…"));
    expect(postJson).toHaveBeenCalledTimes(1);
    expect(dialogs.confirm).not.toHaveBeenCalled();

    result.resolve({ succeeded: true, message: "Connection is available." });
    await waitFor(() => findButton(host, "Test") !== null);
    expect(host.querySelector('[role="status"].connections-notice')?.textContent).toContain("Connection is available.");
  });

  it("announces an unsuccessful connection test as an error", async () => {
    const postJson = vi.fn(async () => ({ succeeded: false, message: "Authorization has expired." }));
    const { context } = endpointContext({ postJson });
    const { host } = renderPage(context);

    await waitFor(() => detailsButton(host) !== null);
    click(detailsButton(host)!);
    click(button(host, "Test"));
    await waitFor(() => host.querySelector('[role="alert"].connections-notice') !== null);

    expect(host.querySelector('[role="alert"].connections-notice')?.textContent).toContain("Authorization has expired.");
    expect(host.querySelector('[role="status"].connections-notice')).toBeNull();
  });

  it.each(["Revoke", "Delete"] as const)("does not %s a connection when confirmation is cancelled", async action => {
    const confirm = vi.fn(async () => false);
    const dialogs = createDialogs({ confirm });
    const { context, http } = endpointContext();
    const { host } = renderPage(context, dialogs);

    await waitFor(() => detailsButton(host) !== null);
    click(detailsButton(host)!);
    click(button(host, action));
    await waitFor(() => confirm.mock.calls.length === 1);
    await waitFor(() => findButton(host, action)?.disabled === false);

    expect(confirm).toHaveBeenCalledWith({ message: `${action} ${connection.name}?`, tone: "danger" });
    expect(http.postJson).not.toHaveBeenCalled();
    expect(http.deleteJson).not.toHaveBeenCalled();
    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
    expect(host.textContent).not.toContain("revoked");
    expect(host.textContent).not.toContain("deleted");
  });
});

const connection: ConnectionMetadata = {
  tenantId: "tenant-a",
  name: "customer-api",
  displayName: "Customer API",
  typeName: "http",
  authScheme: "bearer",
  settings: {},
  secretFieldNames: [],
  managedBy: "studio",
  owner: "tenant",
  status: "Ready",
  createdAt: "2026-10-18T00:00:00Z",
  tags: []
};

const descriptors: ConnectionDescriptorsResponse = {
  types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [] }],
  schemes: [{ name: "bearer", displayName: "Bearer", fields: [], hasInteractiveAuthorization: false }]
};

function endpointContext(options: {
  getJson?: (url: string) => Promise<unknown>;
  postJson?: (url: string, body?: unknown) => Promise<unknown>;
  deleteJson?: (url: string) => Promise<unknown>;
} = {}) {
  const http = {
    requestJson: vi.fn(),
    getJson: vi.fn(options.getJson ?? (async (url: string) => {
      if (url.startsWith("/connections?")) return { items: [connection] };
      if (url === "/connections/descriptors") return descriptors;
      throw new Error(`Unexpected GET: ${url}`);
    })),
    postJson: vi.fn(options.postJson ?? (async () => ({ succeeded: true, message: "Connection is available." }))),
    putJson: vi.fn(),
    deleteJson: vi.fn(options.deleteJson ?? (async () => undefined))
  };
  const context = { baseUrl: "https://server.example", http } as unknown as StudioEndpointContext;
  return { context, http };
}

function createDialogs(overrides: Partial<StudioDialogApi> = {}): StudioDialogApi {
  return {
    confirm: vi.fn(async () => true),
    prompt: vi.fn(),
    alert: vi.fn(),
    ...overrides
  } as unknown as StudioDialogApi;
}

function renderPage(context: StudioEndpointContext, dialogs = createDialogs()) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(<ConnectionsPage context={context} dialogs={dialogs} />));
  cleanups.push(() => {
    flushSync(() => root.unmount());
    host.remove();
  });
  return { host };
}

function detailsButton(host: HTMLElement) {
  return host.querySelector<HTMLButtonElement>('button[aria-label="Details for Customer API"]');
}

function button(host: HTMLElement, text: string) {
  const result = findButton(host, text);
  if (!result) throw new Error(`Button not found: ${text}`);
  return result;
}

function findButton(host: HTMLElement, text: string) {
  return Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(item => item.textContent?.trim() === text) ?? null;
}

function click(element: HTMLElement) {
  flushSync(() => element.click());
}

function changeSearch(host: HTMLElement, value: string) {
  const input = host.querySelector<HTMLInputElement>('input[aria-label="Search connections"]');
  if (!input) throw new Error("Search connections input not found.");
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

async function waitFor(predicate: () => boolean) {
  for (let attempt = 0; attempt < 400; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for UI state.");
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(resolvePromise => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}
