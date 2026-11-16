import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { ConnectionPickerEditor } from "../ConnectionPickerEditor";
import type { ConnectionMetadata } from "../connectionTypes";

const cleanups: Array<() => void> = [];
const connectionDescriptor = {
  name: "Connection",
  typeName: "ConnectionReference",
  uiHint: "connection-picker",
  connectionType: "http"
} as const;

afterEach(() => {
  cleanups.splice(0).forEach(cleanup => cleanup());
});

describe("Connection picker editor", () => {
  it("announces a safe load failure, preserves the reference through retry, and recovers", async () => {
    const pendingRetry = deferred<{ items: ConnectionMetadata[] }>();
    let attempt = 0;
    const postJson = vi.fn(() => {
      switch (attempt++) {
        case 0: return Promise.reject(new Error("provider secret: first failure"));
        case 1: return pendingRetry.promise;
        default: return Promise.resolve({ items: [connection] });
      }
    });
    const onChange = vi.fn();
    const context = endpointContext(postJson);
    const { host } = renderEditor({ endpointContext: context, onChange });

    await waitFor(() => Array.from(host.querySelectorAll("button")).some(item => item.textContent?.trim() === "Retry"));
    const status = host.querySelector<HTMLElement>('[role="status"]');
    expect(status).not.toBeNull();
    const statusRegion = status!;
    const select = host.querySelector<HTMLSelectElement>("select")!;
    const statusId = statusRegion.id;

    expect(statusRegion.textContent).toContain("Connections could not be loaded.");
    expect(statusRegion.textContent).toContain("Retry");
    expect(statusRegion.textContent).not.toContain("provider secret");
    expect(host.querySelector('[role="alert"]')).toBeNull();
    expect(select.getAttribute("aria-describedby")).toBe(statusId);
    expect(select.disabled).toBe(true);
    expect(select.value).toBe("__current-reference");
    expect(select.textContent).toContain("existing.connection");
    expect(onChange).not.toHaveBeenCalled();

    clickButton(host, "Retry");
    await waitFor(() => host.querySelector('[role="status"]')?.textContent?.includes("Loading connections…") === true);
    expect(host.querySelector('[role="status"]')?.id).toBe(statusId);
    expect(select.disabled).toBe(true);
    expect(select.value).toBe("__current-reference");

    pendingRetry.reject(new Error("provider secret: repeated failure"));
    await waitFor(() => host.querySelector('[role="status"]')?.textContent?.includes("Connections could not be loaded.") === true);
    expect(host.querySelector('[role="status"]')?.id).toBe(statusId);
    expect(host.querySelector('[role="status"]')?.textContent).not.toContain("provider secret");
    expect(select.value).toBe("__current-reference");
    expect(onChange).not.toHaveBeenCalled();

    clickButton(host, "Retry");
    await waitFor(() => select.value === connection.name);

    expect(select.disabled).toBe(false);
    expect(select.selectedOptions[0].textContent).toContain(connection.displayName);
    expect(host.querySelector('[role="status"]')?.id).toBe(statusId);
    expect(host.querySelector('[role="status"]')?.textContent).not.toContain("Connections could not be loaded.");
    expect(onChange).not.toHaveBeenCalled();
    expect(postJson.mock.calls).toEqual(Array.from({ length: 3 }, () => [
      "/connections/picker",
      { search: "", typeName: "http" }
    ]));
    expect(context.http.requestJson).not.toHaveBeenCalled();
    expect(context.http.getJson).not.toHaveBeenCalled();
    expect(context.http.putJson).not.toHaveBeenCalled();
    expect(context.http.deleteJson).not.toHaveBeenCalled();
  });

  it.each(["resolve", "reject"] as const)("ignores a late endpoint A %s after endpoint B loads", async outcome => {
    const endpointAResult = deferred<{ items: ConnectionMetadata[] }>();
    const endpointA = endpointContext(vi.fn(() => endpointAResult.promise));
    const endpointB = endpointContext(vi.fn(async () => ({ items: [connection] })));
    const { host, rerender } = renderEditor({ endpointContext: endpointA });

    rerender({ endpointContext: endpointB });
    await waitFor(() => host.querySelector<HTMLSelectElement>("select")?.value === connection.name);

    if (outcome === "resolve") {
      endpointAResult.resolve({ items: [{ ...connection, name: "stale.connection", displayName: "Stale endpoint A" }] });
    } else {
      endpointAResult.reject(new Error("private endpoint A failure"));
    }
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(host.textContent).not.toContain("Stale endpoint A");
    expect(host.textContent).not.toContain("private endpoint A failure");
    expect(host.textContent).not.toContain("Connections could not be loaded.");
    expect(host.querySelector<HTMLSelectElement>("select")?.value).toBe(connection.name);
    expect(endpointA.http.postJson).toHaveBeenCalledTimes(1);
    expect(endpointB.http.postJson).toHaveBeenCalledTimes(1);
  });

  it.each(["resolve", "reject"] as const)("suppresses a late %s after unmount", async outcome => {
    const pending = deferred<{ items: ConnectionMetadata[] }>();
    const postJson = vi.fn(() => pending.promise);
    const onChange = vi.fn();
    const { host, cleanup } = renderEditor({ endpointContext: endpointContext(postJson), onChange });

    await waitFor(() => postJson.mock.calls.length === 1);
    cleanup();
    if (outcome === "resolve") pending.resolve({ items: [connection] });
    else pending.reject(new Error("private unmounted failure"));
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(host.isConnected).toBe(false);
    expect(host.textContent).toBe("");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("keeps disabled selections locked and labels a missing reference unavailable", async () => {
    const postJson = vi.fn(async () => ({ items: [connection] }));
    const { host, rerender } = renderEditor({ endpointContext: endpointContext(postJson), disabled: true });
    const select = host.querySelector<HTMLSelectElement>("select")!;

    await waitFor(() => select.value === connection.name);
    expect(select.disabled).toBe(true);

    rerender({ disabled: false, value: { connection: "missing.connection", type: "http" } });

    expect(select.disabled).toBe(false);
    expect(select.value).toBe("__current-reference");
    expect(select.selectedOptions[0].textContent).toContain("missing.connection — Unavailable");
    expect(host.textContent).toContain("This connection is unavailable.");
    expect(postJson).toHaveBeenCalledTimes(1);
  });
});

const connection: ConnectionMetadata = {
  tenantId: "tenant-a",
  name: "existing.connection",
  displayName: "Existing Connection",
  typeName: "http",
  authScheme: "bearer",
  settings: {},
  secretFieldNames: [],
  managedBy: "studio",
  owner: "tenant",
  status: "Ready",
  createdAt: "2026-10-20T00:00:00Z",
  tags: []
};

function endpointContext(postJson: ReturnType<typeof vi.fn>): StudioEndpointContext {
  return {
    baseUrl: "https://server.example",
    http: {
      requestJson: vi.fn(),
      getJson: vi.fn(),
      postJson,
      putJson: vi.fn(),
      deleteJson: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}

function renderEditor(options: {
  endpointContext: StudioEndpointContext;
  onChange?: (value: unknown) => void;
  disabled?: boolean;
  value?: unknown;
}): { host: HTMLDivElement; rerender(overrides: Partial<typeof options>): void; cleanup(): void } {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const onChange = options.onChange ?? vi.fn();
  const render = (overrides: Partial<typeof options> = {}) => {
    const props: StudioExpressionEditorProps & { endpointContext: StudioEndpointContext } = {
      syntax: "Connection",
      descriptor: connectionDescriptor,
      value: overrides.value ?? options.value ?? { connection: connection.name, type: "http" },
      disabled: overrides.disabled ?? options.disabled,
      context: {
        syntax: "Connection",
        surface: "inline",
        descriptor: connectionDescriptor,
        activity: {},
        expressionDescriptors: [{ type: "Connection", editingMode: "reference" }]
      },
      onChange,
      endpointContext: overrides.endpointContext ?? options.endpointContext
    };
    flushSync(() => root.render(<ConnectionPickerEditor {...props} />));
  };
  render();
  const cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  cleanups.push(cleanup);
  return { host, rerender: render, cleanup };
}

function button(host: HTMLElement, text: string) {
  const result = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(item => item.textContent?.trim() === text);
  if (!result) throw new Error(`Button not found: ${text}`);
  return result;
}

function clickButton(host: HTMLElement, text: string) {
  flushSync(() => button(host, text).click());
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
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}
