import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ConnectionCreateDialog } from "../ConnectionCreateDialog";
import { ConnectionsPage } from "../ConnectionsPage";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "../connectionTypes";

const rawSecretValue = "this-value-must-never-render-or-serialize";
const cleanups: Array<() => void> = [];

afterEach(() => {
  cleanups.splice(0).forEach(cleanup => cleanup());
  vi.useRealTimers();
});

describe("connection credential creation", () => {
  it("defaults to existing Secrets and sends only reference fields", async () => {
    const calls: Array<{ url: string; body: unknown }> = [];
    const postJson = vi.fn(async (url: string, body: unknown) => {
      calls.push({ url, body });
      return url === "/secrets/picker" ? pickerResponse() : createdConnection();
    });
    const onCreated = vi.fn();
    const { container } = renderDialog({ context: endpointContext(postJson), onCreated });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    expect(container.querySelector('input[type="radio"][value="secret"]')).toHaveProperty("checked", true);
    expect(container.textContent).toContain("Scope: tenant-a");
    expect(container.textContent).not.toContain(rawSecretValue);

    changeText(container, "Name", "provider.connection");
    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    expect(button(container, "Create").disabled).toBe(false);

    clickButton(container, "Create");
    await waitFor(() => calls.some(call => call.url === "/connections"));

    const createCall = calls.find(call => call.url === "/connections");
    await waitFor(() => onCreated.mock.calls.length === 1);
    expect(createCall?.body).toEqual({
      name: "provider.connection",
      typeName: "http",
      authScheme: "bearer",
      settings: {},
      secretFields: {
        token: { sourceKind: "secret", existingSecretName: "payments.api" },
        signingKey: { sourceKind: "secret", existingSecretName: "signing.key" }
      }
    });
    expect(JSON.stringify(createCall?.body)).not.toContain(rawSecretValue);
    expect(onCreated).toHaveBeenCalledWith(createdConnection());
    expect(calls.filter(call => call.url === "/secrets/picker").map(call => call.body)).toEqual([
      pickerRequest(""), pickerRequest("")
    ]);
  });

  it("keeps a selected reference while server search changes and the result cap omits it", async () => {
    const calls: Array<{ url: string; body: unknown }> = [];
    const postJson = vi.fn(async (url: string, body: unknown) => {
      calls.push({ url, body });
      if (url !== "/secrets/picker") return createdConnection();
      const search = (body as { search: string }).search;
      if (search === "no-match") return {
        items: Array.from({ length: 50 }, (_, index) => ({
          name: `other.secret.${index}`,
          displayName: `Other Secret ${index}`,
          typeName: "text",
          status: "active",
          scope: "tenant-a"
        }))
      };
      return search === ""
        ? pickerResponse()
        : { items: pickerResponse().items.filter(item => item.name === search) };
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    changeText(container, "Name", "provider.connection");
    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    changeText(container, "Search Secrets for Access token", "no-match");
    await waitFor(() => container.textContent?.includes("Showing up to 50 matches") === true);
    const selected = control<HTMLSelectElement>(container, "Existing Secret for Access token");
    expect(selected.value).toBe("payments.api");
    expect(selected.textContent).toContain("Payments API · payments.api");
    expect(button(container, "Create").disabled).toBe(false);

    clickButton(container, "Create");
    await waitFor(() => calls.some(call => call.url === "/connections"));
    expect(calls.find(call => call.url === "/connections")?.body).toEqual({
      name: "provider.connection",
      typeName: "http",
      authScheme: "bearer",
      settings: {},
      secretFields: {
        token: { sourceKind: "secret", existingSecretName: "payments.api" },
        signingKey: { sourceKind: "secret", existingSecretName: "signing.key" }
      }
    });
  });

  it("debounces picker searches until typing pauses", async () => {
    vi.useFakeTimers();
    const calls: Array<{ url: string; body: unknown }> = [];
    const postJson = vi.fn(async (url: string, body: unknown) => {
      calls.push({ url, body });
      return url === "/secrets/picker" ? pickerResponse() : createdConnection();
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });

    changeText(container, "Search Secrets for Access token", "pay");
    changeText(container, "Search Secrets for Access token", "payments");
    expect(calls.filter(call => call.url === "/secrets/picker")).toHaveLength(2);

    await vi.advanceTimersByTimeAsync(249);
    expect(calls.filter(call => call.url === "/secrets/picker")).toHaveLength(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(calls.filter(call => call.url === "/secrets/picker")).toHaveLength(3);
    expect(calls.filter(call => call.url === "/secrets/picker").map(call => call.body)).toEqual([
      pickerRequest(""), pickerRequest(""), pickerRequest("payments")
    ]);
    vi.useRealTimers();
  });

  it("submits only the new value after switching sources and preserves new-value compatibility", async () => {
    const calls: Array<{ url: string; body: unknown }> = [];
    const postJson = vi.fn(async (url: string, body: unknown) => {
      calls.push({ url, body });
      return url === "/secrets/picker" ? pickerResponse() : createdConnection();
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    changeText(container, "Name", "provider.connection");
    chooseSecret(container, "Access token", "payments.api");
    chooseRadio(container, "New value");
    changeText(container, "New value for Access token", "discard-this-value");
    chooseRadio(container, "Existing Secret");
    expect(control<HTMLSelectElement>(container, "Existing Secret for Access token").value).toBe("");
    expect(button(container, "Create").disabled).toBe(true);
    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    chooseRadio(container, "New value");
    expect(control<HTMLInputElement>(container, "New value for Access token").value).toBe("");
    changeText(container, "New value for Access token", "  replacement-token  ");
    expect(button(container, "Create").disabled).toBe(false);

    clickButton(container, "Create");
    await waitFor(() => calls.some(call => call.url === "/connections"));

    expect(calls.find(call => call.url === "/connections")?.body).toEqual({
      name: "provider.connection",
      typeName: "http",
      authScheme: "bearer",
      settings: {},
      secretFields: {
        token: { value: "  replacement-token  " },
        signingKey: { sourceKind: "secret", existingSecretName: "signing.key" }
      }
    });
    expect(calls.filter(call => call.url === "/secrets/picker")).toHaveLength(2);
  });

  it("clears credentials and settings on scheme and type changes", async () => {
    const calls: Array<{ url: string; body: unknown }> = [];
    const postJson = vi.fn(async (url: string, body: unknown) => {
      calls.push({ url, body });
      return url === "/secrets/picker" ? pickerResponse() : createdConnection({ typeName: "worker", authScheme: "worker" });
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    changeText(container, "Name", "worker.connection");
    changeText(container, "Allowed hosts", "api.example");
    chooseSecret(container, "Access token", "payments.api");
    changeSelect(container, "Auth scheme", "basic");

    expect(control<HTMLSelectElement>(container, "Existing Secret for Password").value).toBe("");
    expect(control<HTMLInputElement>(container, "Username (required)").value).toBe("");
    changeText(container, "Username (required)", "worker-user");
    changeSelect(container, "Type", "worker");
    changeText(container, "Queue (required)", "jobs");
    chooseRadio(container, "New value");
    changeText(container, "New value for Worker key", "worker-key");
    expect(button(container, "Create").disabled).toBe(false);

    clickButton(container, "Create");
    await waitFor(() => calls.some(call => call.url === "/connections"));

    expect(calls.find(call => call.url === "/connections")?.body).toEqual({
      name: "worker.connection",
      typeName: "worker",
      authScheme: "worker",
      settings: { queue: "jobs" },
      secretFields: { apiKey: { value: "worker-key" } }
    });
  });

  it("shows permission and retry states while preserving keyboard dialog behavior", async () => {
    const postJson = vi.fn()
      .mockRejectedValueOnce({ status: 403 })
      .mockResolvedValue(pickerResponse());
    const onCancel = vi.fn();
    const { container } = renderDialog({ context: endpointContext(postJson), onCancel });

    expect(document.activeElement).toBe(control<HTMLInputElement>(container, "Name"));
    await waitFor(() => container.textContent?.includes("Secrets read permission") === true);
    expect(container.textContent).toContain("Access to existing Secrets is denied.");
    expect(container.textContent).not.toContain(rawSecretValue);
    clickButton(container, "Retry");
    await waitFor(() => container.textContent?.includes("Payments API") === true);
    expect(postJson).toHaveBeenCalledTimes(3);

    const close = container.querySelector<HTMLButtonElement>('[aria-label="Close create connection dialog"]')!;
    close.focus();
    document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(button(container, "Cancel"));
    document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true }));
    expect(document.activeElement).toBe(close);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("shows a safe generic picker failure and retries successfully", async () => {
    const postJson = vi.fn()
      .mockRejectedValueOnce(new Error("provider failure details"))
      .mockResolvedValue(pickerResponse());
    const { container } = renderDialog({ context: endpointContext(postJson) });

    await waitFor(() => container.textContent?.includes("Existing Secrets could not be loaded.") === true);
    expect(container.textContent).not.toContain("provider failure details");
    clickButton(container, "Retry");
    await waitFor(() => container.textContent?.includes("Payments API") === true);
    expect(postJson).toHaveBeenCalledTimes(3);
  });

  it("preserves a selected reference when backend validation reports it unavailable", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url === "/secrets/picker") return pickerResponse();
      throw new Error("The selected secret is unavailable for connection credentials.");
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    changeText(container, "Name", "provider.connection");
    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    clickButton(container, "Create");

    await waitFor(() => container.textContent?.includes("The selected secret is unavailable") === true);
    expect(control<HTMLSelectElement>(container, "Existing Secret for Access token").value).toBe("payments.api");
    expect(control<HTMLSelectElement>(container, "Existing Secret for Signing key").value).toBe("signing.key");
    expect(button(container, "Create").disabled).toBe(false);
  });

  it("restores focus to the New connection opener while the post-create refresh is pending", async () => {
    const refresh = deferred<{ items: ConnectionMetadata[] }>();
    let listCalls = 0;
    const getJson = vi.fn((url: string) => {
      if (url.startsWith("/connections?")) {
        listCalls++;
        return listCalls === 1 ? Promise.resolve({ items: [] }) : refresh.promise;
      }
      if (url === "/connections/descriptors") return Promise.resolve(testDescriptors);
      throw new Error(`Unexpected GET: ${url}`);
    });
    const postJson = vi.fn(async (url: string) => url === "/secrets/picker" ? pickerResponse() : createdConnection());
    const context = endpointContext(postJson);
    (context.http as unknown as { getJson: (url: string) => Promise<unknown> }).getJson = getJson;
    const { container: host } = renderConnectionsPage(context);

    await waitFor(() => !button(host, "New connection").disabled);
    const opener = button(host, "New connection");
    opener.focus();
    flushSync(() => opener.click());
    await waitFor(() => host.querySelector('[role="dialog"]') !== null);
    await waitFor(() => host.textContent?.includes("Payments API") === true);
    changeText(host, "Name", "provider.connection");
    chooseSecret(host, "Access token", "payments.api");
    chooseSecret(host, "Signing key", "signing.key");
    clickButton(host, "Create");

    await waitFor(() => !host.querySelector('[role="dialog"]') && listCalls === 2);
    expect(opener.disabled).toBe(false);
    expect(document.activeElement).toBe(opener);
    refresh.resolve({ items: [createdConnection()] });
  });

  it("clears endpoint A rows and descriptors when endpoint B fails, then recovers B", async () => {
    const endpointAConnection = createdConnection({ name: "a.connection", displayName: "Endpoint A Connection" });
    const endpointBConnection = createdConnection({ name: "b.connection", displayName: "Endpoint B Connection" });
    let endpointBUnavailable = true;
    const endpointA = endpointContext(vi.fn());
    const endpointB = endpointContext(vi.fn());
    (endpointA.http as unknown as { getJson: (url: string) => Promise<unknown> }).getJson = vi.fn(async url => {
      if (url.startsWith("/connections?")) return { items: [endpointAConnection] };
      if (url === "/connections/descriptors") return testDescriptors;
      throw new Error(`Unexpected endpoint A GET: ${url}`);
    });
    (endpointB.http as unknown as { getJson: (url: string) => Promise<unknown> }).getJson = vi.fn(async url => {
      if (url.startsWith("/connections?")) {
        if (endpointBUnavailable) throw new Error("Endpoint B is unavailable.");
        return { items: [endpointBConnection] };
      }
      if (url === "/connections/descriptors") return endpointBDescriptors;
      throw new Error(`Unexpected endpoint B GET: ${url}`);
    });
    const { container: host, rerender } = renderConnectionsPage(endpointA);

    await waitFor(() => host.textContent?.includes("Endpoint A Connection") === true);
    expect(button(host, "New connection").disabled).toBe(false);
    rerender(endpointB);
    await waitFor(() => host.textContent?.includes("Endpoint B is unavailable.") === true);

    expect(host.textContent).not.toContain("Endpoint A Connection");
    expect(button(host, "New connection").disabled).toBe(true);

    endpointBUnavailable = false;
    clickButton(host, "Search");
    await waitFor(() => host.textContent?.includes("Endpoint B Connection") === true);
    expect(host.textContent).not.toContain("Endpoint A Connection");
    expect(button(host, "New connection").disabled).toBe(false);
    clickButton(host, "New connection");
    await waitFor(() => host.querySelector('[role="dialog"]') !== null);
    expect(control<HTMLSelectElement>(host, "Type").value).toBe("endpoint-b");
  });

  it.each(["Revoke", "Delete"] as const)("does not %s endpoint A after confirmation when the page switches to endpoint B", async action => {
    const endpointAConnection = createdConnection({ name: "a.connection", displayName: "Endpoint A Connection" });
    const endpointBConnection = createdConnection({ name: "b.connection", displayName: "Endpoint B Connection" });
    const endpointA = endpointContext(vi.fn());
    const endpointB = endpointContext(vi.fn());
    const getConnections = (connection: ConnectionMetadata) => vi.fn(async url => {
      if (url.startsWith("/connections?")) return { items: [connection] };
      if (url === "/connections/descriptors") return testDescriptors;
      throw new Error(`Unexpected GET: ${url}`);
    });
    (endpointA.http as unknown as { getJson: ReturnType<typeof vi.fn> }).getJson = getConnections(endpointAConnection);
    (endpointB.http as unknown as { getJson: ReturnType<typeof vi.fn> }).getJson = getConnections(endpointBConnection);
    const confirm = deferred<boolean>();
    const dialogs = { confirm: vi.fn(() => confirm.promise), prompt: vi.fn(), alert: vi.fn() } as unknown as StudioDialogApi;
    const postA = endpointA.http.postJson as ReturnType<typeof vi.fn>;
    const deleteA = vi.fn();
    (endpointA.http as unknown as { deleteJson: ReturnType<typeof vi.fn> }).deleteJson = deleteA;
    const { container: host, rerender } = renderConnectionsPage(endpointA, dialogs);

    await waitFor(() => host.textContent?.includes("Endpoint A Connection") === true);
    clickButton(host, "Details");
    clickButton(host, action);
    expect(dialogs.confirm).toHaveBeenCalledWith({ message: `${action} a.connection?`, tone: "danger" });

    rerender(endpointB);
    await waitFor(() => host.textContent?.includes("Endpoint B Connection") === true);
    confirm.resolve(true);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(postA).not.toHaveBeenCalled();
    expect(deleteA).not.toHaveBeenCalled();
  });

  it("ignores a successful test response from the previous endpoint", async () => {
    const endpointAConnection = createdConnection({ name: "a.connection", displayName: "Endpoint A Connection" });
    const endpointBConnection = createdConnection({ name: "b.connection", displayName: "Endpoint B Connection" });
    const testResult = deferred<{ succeeded: boolean; message: string }>();
    const endpointA = endpointContext(vi.fn(() => testResult.promise));
    const endpointB = endpointContext(vi.fn());
    installConnectionList(endpointA, endpointAConnection);
    installConnectionList(endpointB, endpointBConnection);
    const { container: host, rerender } = renderConnectionsPage(endpointA);

    await waitFor(() => host.textContent?.includes("Endpoint A Connection") === true);
    clickButton(host, "Details");
    clickButton(host, "Test");
    expect(endpointA.http.postJson).toHaveBeenCalledWith("/connections/a.connection/test", {});

    rerender(endpointB);
    await waitFor(() => host.textContent?.includes("Endpoint B Connection") === true);
    testResult.resolve({ succeeded: true, message: "Endpoint A test succeeded." });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(host.textContent).not.toContain("Endpoint A test succeeded.");
    expect(host.querySelector('[role="alert"]')).toBeNull();
  });

  it("ignores a failed test response from the previous endpoint", async () => {
    const endpointAConnection = createdConnection({ name: "a.connection", displayName: "Endpoint A Connection" });
    const endpointBConnection = createdConnection({ name: "b.connection", displayName: "Endpoint B Connection" });
    const testResult = deferred<{ succeeded: boolean; message?: string }>();
    const endpointA = endpointContext(vi.fn(() => testResult.promise));
    const endpointB = endpointContext(vi.fn());
    installConnectionList(endpointA, endpointAConnection);
    installConnectionList(endpointB, endpointBConnection);
    const { container: host, rerender } = renderConnectionsPage(endpointA);

    await waitFor(() => host.textContent?.includes("Endpoint A Connection") === true);
    clickButton(host, "Details");
    clickButton(host, "Test");

    rerender(endpointB);
    await waitFor(() => host.textContent?.includes("Endpoint B Connection") === true);
    testResult.reject(new Error("Endpoint A test failed."));
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(host.textContent).not.toContain("Endpoint A test failed.");
    expect(host.querySelector('[role="alert"]')).toBeNull();
  });

  it("blocks an unresolved required credential and prevents cancel or duplicate submit while saving", async () => {
    const createRequest = deferred<unknown>();
    const calls: string[] = [];
    const postJson = vi.fn(async (url: string) => {
      calls.push(url);
      if (url === "/connections") return createRequest.promise;
      return pickerResponse();
    });
    const onCreated = vi.fn();
    const { container } = renderDialog({ context: endpointContext(postJson), onCreated });

    await waitFor(() => container.textContent?.includes("Payments API") === true);
    changeText(container, "Name", "provider.connection");
    expect(button(container, "Create").disabled).toBe(true);
    expect(calls).not.toContain("/connections");

    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    const form = container.querySelector("form")!;
    flushSync(() => {
      form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
    });
    await waitFor(() => container.textContent?.includes("Creating…") === true);
    await waitFor(() => calls.filter(url => url === "/connections").length === 1);
    expect(button(container, "Cancel").disabled).toBe(true);
    expect(control<HTMLInputElement>(container, "Name").disabled).toBe(true);
    expect(calls.filter(url => url === "/connections")).toHaveLength(1);

    createRequest.resolve(createdConnection());
    await waitFor(() => onCreated.mock.calls.length === 1);
  });

  it("keeps Create actions outside the scroll body and focuses a failed-save message without clearing fields", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url === "/secrets/picker") return pickerResponse();
      throw new Error("Fixture connection create failed.");
    });
    const { container } = renderDialog({ context: endpointContext(postJson) });
    await waitFor(() => container.textContent?.includes("Payments API") === true);

    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const content = dialog.querySelector<HTMLElement>(".connections-create-content")!;
    const actions = dialog.querySelector<HTMLElement>(".connections-create-actions")!;
    expect(content.contains(actions)).toBe(false);
    expect(actions.parentElement).toBe(dialog.querySelector("form"));

    changeText(container, "Name", "preserved.connection");
    chooseSecret(container, "Access token", "payments.api");
    chooseSecret(container, "Signing key", "signing.key");
    clickButton(container, "Create");

    await waitFor(() => container.querySelector("[role='alert']") !== null);
    const alert = container.querySelector<HTMLElement>("[role='alert']")!;
    await waitFor(() => document.activeElement === alert);
    expect(alert.textContent).toBe("Fixture connection create failed.");
    expect(control<HTMLInputElement>(container, "Name").value).toBe("preserved.connection");
    expect(content.contains(actions)).toBe(false);
  });

  it("ignores old endpoint results and clears old form state on endpoint changes", async () => {
    const oldPicker = deferred<unknown>();
    const oldContext = endpointContext(vi.fn(() => oldPicker.promise));
    const newContext = endpointContext(vi.fn(async () => ({ items: [{
      name: "new.secret", displayName: "New endpoint Secret", typeName: "text", status: "active", scope: "tenant-b"
    }] })));
    const { container, rerender } = renderDialog({ context: oldContext });

    changeText(container, "Name", "old-endpoint.connection");
    rerender({ context: newContext });
    expect(control<HTMLInputElement>(container, "Name").value).toBe("");
    expect(container.textContent).not.toContain("New endpoint Secret");

    oldPicker.resolve(pickerResponse("old.secret", "Old endpoint Secret"));
    await waitFor(() => container.textContent?.includes("New endpoint Secret") === true);
    expect(container.textContent).not.toContain("Old endpoint Secret");
    expect(control<HTMLInputElement>(container, "Name").value).toBe("");
    expect(button(container, "Create").disabled).toBe(true);
  });
});

const testDescriptors: ConnectionDescriptorsResponse = {
  types: [
    { name: "http", displayName: "HTTP", supportedSchemes: ["bearer", "basic"], settings: [{ name: "allowedHosts", displayName: "Allowed hosts", editorHint: "text" }] },
    { name: "worker", displayName: "Worker", supportedSchemes: ["worker"], settings: [{ name: "queue", displayName: "Queue", editorHint: "text", required: true }] }
  ],
  schemes: [
    { name: "bearer", displayName: "Bearer", fields: [
      { name: "token", displayName: "Access token", sensitive: true, editorHint: "password", required: true },
      { name: "signingKey", displayName: "Signing key", sensitive: true, editorHint: "password", required: true }
    ], hasInteractiveAuthorization: false },
    { name: "basic", displayName: "Basic", fields: [
      { name: "username", displayName: "Username", sensitive: false, editorHint: "text", required: true },
      { name: "password", displayName: "Password", sensitive: true, editorHint: "password", required: true }
    ], hasInteractiveAuthorization: false },
    { name: "worker", displayName: "Worker auth", fields: [
      { name: "apiKey", displayName: "Worker key", sensitive: true, editorHint: "password", required: true }
    ], hasInteractiveAuthorization: false }
  ]
};

const endpointBDescriptors: ConnectionDescriptorsResponse = {
  types: [{ name: "endpoint-b", displayName: "Endpoint B type", supportedSchemes: ["endpoint-b-scheme"], settings: [] }],
  schemes: [{ name: "endpoint-b-scheme", displayName: "Endpoint B scheme", fields: [], hasInteractiveAuthorization: false }]
};

function renderConnectionsPage(initialContext: StudioEndpointContext, dialogs = { confirm: vi.fn(), prompt: vi.fn(), alert: vi.fn() } as unknown as StudioDialogApi) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const render = (context = initialContext) => {
    flushSync(() => root.render(<ConnectionsPage context={context} dialogs={dialogs} />));
  };
  render();
  cleanups.push(() => {
    flushSync(() => root.unmount());
    host.remove();
  });
  return { container: host, rerender: render, dialogs };
}

function installConnectionList(context: StudioEndpointContext, connection: ConnectionMetadata) {
  (context.http as unknown as { getJson: ReturnType<typeof vi.fn> }).getJson = vi.fn(async url => {
    if (url.startsWith("/connections?")) return { items: [connection] };
    if (url === "/connections/descriptors") return testDescriptors;
    throw new Error(`Unexpected GET: ${url}`);
  });
}

function renderDialog(options: {
  context?: StudioEndpointContext;
  onCancel?: () => void;
  onCreated?: (connection: ConnectionMetadata) => void;
} = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const initialContext = options.context ?? endpointContext(vi.fn(async () => pickerResponse()));
  const onCancel = options.onCancel ?? vi.fn();
  const onCreated = options.onCreated ?? vi.fn();
  const render = (overrides: { context?: StudioEndpointContext } = {}) => {
    flushSync(() => root.render(
      <ConnectionCreateDialog
        context={overrides.context ?? initialContext}
        descriptors={testDescriptors}
        onCancel={onCancel}
        onCreated={onCreated}
      />
    ));
  };
  render();
  const cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  cleanups.push(cleanup);
  return { container: host, rerender: render, cleanup };
}

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

function pickerResponse(firstName = "payments.api", firstDisplayName = "Payments API") {
  return {
    items: [
      { name: firstName, displayName: firstDisplayName, typeName: "text", status: "active", scope: "tenant-a", value: rawSecretValue },
      { name: "signing.key", displayName: "Signing key", typeName: "text", status: "active", scope: "tenant-b", rawValue: rawSecretValue }
    ],
    canCreateInline: true
  };
}

function pickerRequest(search: string) {
  return { search, typeNames: ["text"], storeNames: [], activeOnly: true };
}

function createdConnection(overrides: Partial<ConnectionMetadata> = {}): ConnectionMetadata {
  return {
    tenantId: "tenant-a",
    name: "provider.connection",
    displayName: "provider.connection",
    typeName: "http",
    authScheme: "bearer",
    settings: {},
    secretFieldNames: ["token"],
    managedBy: "",
    owner: "",
    status: "Ready",
    createdAt: "2026-10-18T00:00:00Z",
    tags: [],
    ...overrides
  };
}

function control<T extends HTMLElement>(container: HTMLElement, labelText: string): T {
  const label = Array.from(container.querySelectorAll("label")).find(item => item.textContent?.trim() === labelText);
  const htmlFor = label?.htmlFor;
  const element = htmlFor ? document.getElementById(htmlFor) : label?.querySelector("input, select, textarea");
  if (!element) throw new Error(`Control not found for label: ${labelText}`);
  return element as T;
}

function changeText(container: HTMLElement, labelText: string, value: string) {
  const input = control<HTMLInputElement>(container, labelText);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function changeSelect(container: HTMLElement, labelText: string, value: string) {
  const select = control<HTMLSelectElement>(container, labelText);
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  setter?.call(select, value);
  flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
}

function chooseSecret(container: HTMLElement, labelText: string, value: string) {
  changeSelect(container, `Existing Secret for ${labelText}`, value);
}

function chooseRadio(container: HTMLElement, labelText: string) {
  const label = Array.from(container.querySelectorAll("label")).find(item => item.textContent?.trim() === labelText);
  const radio = label?.querySelector<HTMLInputElement>('input[type="radio"]');
  if (!radio) throw new Error(`Radio not found: ${labelText}`);
  flushSync(() => radio.click());
}

function button(container: HTMLElement, label: string) {
  const result = Array.from(container.querySelectorAll("button")).find(item => item.textContent?.trim() === label);
  if (!result) throw new Error(`Button not found: ${label}`);
  return result;
}

function clickButton(container: HTMLElement, label: string) {
  flushSync(() => button(container, label).click());
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
