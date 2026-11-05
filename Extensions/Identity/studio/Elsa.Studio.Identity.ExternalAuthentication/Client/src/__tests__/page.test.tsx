import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ExternalAuthenticationPage } from "../ExternalAuthenticationPage";
import { connectionChanges, type ConnectionCatalog, type ConnectionCommand, type ConnectionPreview, type ConnectionTest } from "../sdk";
import { catalog as fixtureCatalog, connectionDocument, view } from "./fixtures";
import { preparePreviewReturn } from "../previewReturn";

const cleanup = new Set<() => void>();
beforeEach(() => { window.sessionStorage.clear(); window.history.replaceState(null, "", "/security/external-sign-in?backend=selected"); });
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); vi.restoreAllMocks(); });

function harness(initial = fixtureCatalog) {
  let catalog: ConnectionCatalog = structuredClone(initial);
  const observed: ConnectionTest = { connectionRecordId: "record", materialRevision: "material", observedAt: new Date().toISOString(),
    readiness: { ready: true, failure: 0, protocolFailure: 0 }, protocolFailure: 0, stale: false };
  const preview: ConnectionPreview = { connectionKey: "company", connectionRecordId: "record", materialRevision: "material", issuer: "https://idp.example", subject: "opaque-subject",
    admission: 2, roleIds: ["reader"], permissions: ["identity/users:read"], admissionFailure: 0, observedAt: observed.observedAt, stale: false };
  const getJson = vi.fn().mockImplementation(async (path: string) => {
    if (path.includes("/antiforgery")) return { requestToken: "csrf", formFieldName: "__csrf", headerName: "RequestVerificationToken" };
    if (path.includes("/lookups/roles")) return { items: [{ id: "reader", name: "Reader", canDelegate: true }], totalCount: 1 };
    const detail = catalog.connections.find(item => path.endsWith("/external-connections/" + encodeURIComponent(item.document.id)));
    if (detail) return { revision: catalog.revision, connection: { ...detail, readiness: { ready: true, failure: 0, protocolFailure: 0 } } };
    if (path.includes("/results/")) { expect(window.location.search).not.toContain("externalPreviewHandle"); return preview; }
    return structuredClone(catalog);
  });
  const postJson = vi.fn().mockImplementation(async (path: string, body: ConnectionCommand) => {
    if (path.endsWith("/assess")) {
      const proposed = structuredClone(catalog);
      if (body.change === connectionChanges.create) proposed.connections.push({ ...view, document: { ...body.record, id: "record", enabled: false } });
      else proposed.connections = proposed.connections.map(item => item.document.id === body.record.id ? { ...item, document: body.record } : item);
      return { canApply: true, proposed };
    }
    if (path.endsWith("/commands")) {
      if (body.change === connectionChanges.create) catalog.connections.push({ ...view, document: { ...body.record, id: "record", enabled: false } });
      else catalog.connections = catalog.connections.map(item => item.document.id === body.record.id ? { ...item, document: { ...item.document, ...body.record } } : item);
      catalog.revision = "r2";
      return { saved: true, revision: "r2", recordId: "record" };
    }
    if (path.endsWith("/test")) return observed;
    if (path === "/secrets/picker") return { items: [{ name: "credential", typeName: "text", status: "Active", value: "never-retain" }] };
    throw new Error("Unexpected test request");
  });
  const context = { baseUrl: "https://backend.example/shell", http: { getJson, postJson } } as unknown as StudioEndpointContext;
  const confirm = vi.fn().mockResolvedValue(true);
  const dialogs = { confirm } as unknown as StudioDialogApi;
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container);
  const render = (nextContext = context) => flushSync(() => root.render(<ExternalAuthenticationPage context={nextContext} dialogs={dialogs} />));
  const dispose = () => { flushSync(() => root.unmount()); container.remove(); cleanup.delete(dispose); };
  cleanup.add(dispose); render();
  return { container, context, dialogs, render, dispose, getJson, postJson, confirm, observed, preview, setCatalog: (next: ConnectionCatalog) => { catalog = next; } };
}
async function button(container: HTMLElement, text: string) {
  await vi.waitFor(() => expect(Array.from(container.querySelectorAll("button")).some(item => item.textContent?.trim() === text)).toBe(true));
  return Array.from(container.querySelectorAll("button")).find(item => item.textContent?.trim() === text)!;
}
function click(element: HTMLElement) { flushSync(() => element.click()); }
function field(container: HTMLElement, text: string) {
  const label = Array.from(container.querySelectorAll("label")).find(item => item.textContent?.trim() === text)!;
  return label.htmlFor ? container.querySelector<HTMLInputElement>(`[id="${label.htmlFor}"]`)! : label.querySelector<HTMLInputElement>("input")!;
}
function type(input: HTMLInputElement, value: string) {
  flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true })); input.dispatchEvent(new Event("change", { bubbles: true })); });
}
function select(input: HTMLSelectElement, value: string) { flushSync(() => { input.value = value; input.dispatchEvent(new Event("change", { bubbles: true })); }); }

describe("connection administration page", () => {
  it("returns keyboard focus to the action after cancelling its asynchronous confirmation", async () => {
    const host = harness();
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(host.container.querySelector(".external-auth-row")!);
    const trigger = await button(host.container, "Archive");
    const modalFocus = document.createElement("button"); document.body.appendChild(modalFocus); cleanup.add(() => modalFocus.remove());
    host.confirm.mockImplementation(async () => { modalFocus.focus(); return false; });
    trigger.focus(); click(trigger);
    await vi.waitFor(() => expect(host.confirm).toHaveBeenCalled());
    await vi.waitFor(() => expect(trigger.disabled).toBe(false));
    await vi.waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(host.postJson.mock.calls.some(([path]) => path.endsWith("/commands"))).toBe(false);
  });

  it.each([false, true])("reconciles repaired readiness after a configuration test, refusing stale observations (%s)", async stale => {
    const host = harness(); const original = host.getJson.getMockImplementation()!;
    host.getJson.mockImplementation(async (path: string) => path.endsWith("/external-connections/record")
      ? { revision: fixtureCatalog.revision, connection: { ...view, readiness: { ready: false, failure: 5, protocolFailure: 0 } } }
      : original(path));
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.textContent).toContain("The referenced secret could not be resolved"));
    host.observed.stale = stale;
    click(await button(host.container, "Test configuration"));
    await vi.waitFor(() => expect(host.container.textContent).toContain(stale ? "Configuration changed during the test" : "Configuration test passed"));
    if (stale) expect(host.container.textContent).not.toContain("Current configuration ready");
    else await vi.waitFor(() => expect(host.container.textContent).toContain("Current configuration ready"));
    expect((await button(host.container, "Preview sign-in")).disabled).toBe(stale);
  });

  it("shows current secret readiness to a provider reader separately from configured enablement", async () => {
    const host = harness({ ...fixtureCatalog, canManageConnections: false,
      connections: [{ ...view, document: { ...connectionDocument, enabled: true } }] });
    const original = host.getJson.getMockImplementation()!;
    host.getJson.mockImplementation(async (path: string) => path.endsWith("/external-connections/record")
      ? { revision: fixtureCatalog.revision, connection: { ...view, readiness: { ready: false, failure: 5, protocolFailure: 0 } } }
      : original(path));
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.textContent).toContain("The referenced secret could not be resolved"));
    expect(host.container.textContent).toContain("Enabled in configuration");
    expect(host.container.textContent).not.toContain("Sign-in enabled");
    expect(host.container.textContent).not.toContain("Test configuration");
    expect(host.postJson).not.toHaveBeenCalled();
  });
  it.each([401, 403, 404])("clears protected connection configuration when refreshed access returns %s", async status => {
    const host = harness();
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    const original = host.getJson.getMockImplementation()!;
    host.getJson.mockImplementation(async (path: string) => {
      if (path.endsWith("/external-connections")) throw { status };
      return original(path);
    });
    click(await button(host.container, "Refresh"));
    await vi.waitFor(() => expect(host.container.querySelector("[role=alert]")).not.toBeNull());
    expect(host.container.querySelector(".external-auth-row")).toBeNull();
    expect(host.container.querySelector(".external-auth-editor")).toBeNull();
    expect((await button(host.container, "Add connection")).disabled).toBe(true);
  });

  it("renders integrator policy names as literal options even when they match JavaScript object properties", async () => {
    const host = harness({ ...fixtureCatalog, provisioningPolicies: ["jit", "constructor", "__proto__"] });
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(await button(host.container, "Add connection"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Integrator policy: constructor"));
    expect(host.container.textContent).toContain("Integrator policy: __proto__");
  });
  it("creates a disabled draft through assessment/confirmation with an explicit Elsa provisioning policy and secret reference", async () => {
    const host = harness({ ...fixtureCatalog, connections: [], revision: null });
    await vi.waitFor(() => expect(host.container.textContent).toContain("No external sign-in connections yet"));
    click(await button(host.container, "Add connection"));
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    type(field(host.container, "Connection key"), "company"); type(field(host.container, "Display name"), "Company");
    type(field(host.container, "Issuer URL"), "https://idp.example"); type(field(host.container, "Client ID"), "client");
    type(field(host.container, "Client secret reference"), "credential");
    const policyLabel = Array.from(host.container.querySelectorAll("label")).find(label => label.textContent === "Unknown identities")!;
    select(host.container.querySelector<HTMLSelectElement>(`[id="${policyLabel.htmlFor}"]`)!, "jit");
    await vi.waitFor(() => expect(host.container.textContent).toContain("Reader"));
    const role = Array.from(host.container.querySelectorAll<HTMLInputElement>("input[type=checkbox]")).find(input => input.closest("label")?.textContent?.trim() === "Reader")!;
    click(role);
    expect(host.container.textContent).toContain("Later sign-ins use roles saved in Elsa");
    expect(host.container.textContent).not.toContain("group mapping");
    click(await button(host.container, "Save draft"));
    await vi.waitFor(() => expect(host.postJson.mock.calls.some(([path]) => path.endsWith("/commands"))).toBe(true));
    const command = host.postJson.mock.calls.find(([path]) => path.endsWith("/commands"))![1] as ConnectionCommand;
    expect(command.change).toBe(connectionChanges.create); expect(command.expectedRevision).toBeNull();
    expect(command.record.enabled).toBe(false); expect(command.record.admission).toMatchObject({ type: "jit", defaultRoleIds: ["reader"] });
    expect(command.record.secrets.clientSecret).toEqual({ name: "credential", typeName: "text", scope: null });
    expect(host.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Save draft", message: expect.stringContaining("Studio:") }));
  });
  it("keeps a deployment record read-only and prepares a complete non-shadowing draft only through the explicit override action", async () => {
    const baseline = { ...view, source: 0 as const, overridePermitted: true, configurationEditable: false, document: { ...connectionDocument, id: "deployment:company", enabled: true } };
    const host = harness({ ...fixtureCatalog, connections: [baseline] });
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    expect(field(host.container, "Issuer URL").closest("fieldset")!.disabled).toBe(true);
    click(await button(host.container, "Prepare full override"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("deployment connection continues to apply"));
    click(await button(host.container, "Save draft"));
    await vi.waitFor(() => expect(host.postJson.mock.calls.some(([path]) => path.endsWith("/commands"))).toBe(true));
    const command = host.postJson.mock.calls.find(([path]) => path.endsWith("/commands"))![1] as ConnectionCommand;
    expect(command.change).toBe(connectionChanges.create); expect(command.record.overridesDeployment).toBe(false); expect(command.record.enabled).toBe(false);
    expect(command.record.settings).toEqual(baseline.document.settings); expect(command.record.secrets).toEqual(baseline.document.secrets);
  });
  it("round-trips a real-preview form contract back to the saved draft, strips result handles before retrieval and unlocks enable only after test/preview", async () => {
    let submission: { action: string; fields: Record<string, string> } | null = null;
    vi.spyOn(HTMLFormElement.prototype, "submit").mockImplementation(function (this: HTMLFormElement) {
      submission = { action: this.action, fields: Object.fromEntries(new FormData(this).entries()) as Record<string, string> };
    });
    const host = harness();
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull()); click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    expect((await button(host.container, "Enable sign-in")).disabled).toBe(true);
    click(await button(host.container, "Test configuration"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Configuration test passed"));
    expect((await button(host.container, "Enable sign-in")).disabled).toBe(true);
    click(await button(host.container, "Preview sign-in"));
    await vi.waitFor(() => expect(submission).not.toBeNull());
    const posted = submission as unknown as { action: string; fields: Record<string, string> };
    expect(posted.action).toBe("https://backend.example/shell/sso/preview/company"); expect(posted.fields.__csrf).toBe("csrf"); expect(posted.fields.connectionRecordId).toBe("record");
    const destination = new URL(posted.fields.returnUrl); destination.searchParams.set("externalPreviewHandle", "h".repeat(43)); destination.searchParams.set("externalPreviewConnection", "company");
    host.dispose(); window.history.replaceState(null, "", destination);
    const returned = harness();
    await vi.waitFor(() => expect(returned.container.textContent).toContain("Preview complete"));
    expect(returned.container.textContent).toContain("Would be created"); expect(window.location.search).toBe("?backend=selected");
    const enable = await button(returned.container, "Enable sign-in"); await vi.waitFor(() => expect(enable.disabled).toBe(false)); click(enable);
    await vi.waitFor(() => expect(returned.postJson.mock.calls.some(([path]) => path.endsWith("/commands"))).toBe(true));
    expect(returned.postJson.mock.calls.find(([path]) => path.endsWith("/commands"))![1]).toMatchObject({ change: connectionChanges.enable, record: { id: "record", enabled: true } });
  });
  it("restores a cancelled preview to its saved draft without reading identity data or unlocking enable", async () => {
    const destination = new URL(preparePreviewReturn(new URL(window.location.href), "https://backend.example/shell", view, null, window.sessionStorage));
    destination.searchParams.set("externalPreviewConnection", "company"); destination.searchParams.set("externalPreviewFailure", "ProviderFailed");
    window.history.replaceState(null, "", destination);
    const returned = harness();
    await vi.waitFor(() => expect(returned.container.textContent).toContain("cancelled or could not be verified"));
    expect(returned.container.querySelector(".external-auth-editor input")).not.toBeNull();
    expect(window.location.search).toBe("?backend=selected"); expect((await button(returned.container, "Enable sign-in")).disabled).toBe(true);
    expect(returned.getJson.mock.calls.some(([path]) => path.includes("/results/"))).toBe(false);
    expect(returned.postJson).not.toHaveBeenCalled();
  });
  it("shows read-only source/capability states and never exposes write or preview controls to a reader", async () => {
    const host = harness({ ...fixtureCatalog, canManageConnections: false });
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull()); click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    expect(host.container.textContent).toContain("read-only access"); expect((await button(host.container, "Add connection")).disabled).toBe(true);
    expect(host.container.textContent).not.toContain("Test configuration"); expect(host.container.textContent).not.toContain("Enable sign-in");
    expect(field(host.container, "Issuer URL").closest("fieldset")!.disabled).toBe(true);
    expect(host.postJson).not.toHaveBeenCalled();
  });
  it("explains unavailable host management and keeps connection controls read-only", async () => {
    const host = harness({ ...fixtureCatalog, canManageConnections: false, managementUnavailableReason: "identity-coordination-unavailable" });
    await vi.waitFor(() => expect(host.container.textContent).toContain("Ask the host administrator"));
    expect((await button(host.container, "Add connection")).disabled).toBe(true);
    expect(host.container.textContent).not.toContain("Test configuration");
    expect(host.postJson).not.toHaveBeenCalled();
  });
  it("keeps an unsaved editor under workspace probes and reports safe conflict messages without discarding it", async () => {
    const host = harness();
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull()); click(host.container.querySelector(".external-auth-row")!);
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-editor input")).not.toBeNull());
    await vi.waitFor(() => expect(host.container.textContent).toContain("Current configuration ready"));
    type(field(host.container, "Display name"), "My draft name");
    await vi.waitFor(async () => expect((await button(host.container, "Save changes")).disabled).toBe(false));
    const probe = new CustomEvent("elsa:studio-navigation-requested", { cancelable: true, detail: { intent: "probe" } }); window.dispatchEvent(probe);
    expect(probe.defaultPrevented).toBe(true);
    host.postJson.mockRejectedValue({ status: 409, payload: { failure: "revision-conflict", detail: "never-render-secret" } });
    click(await button(host.container, "Save changes"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Connections changed on the server"));
    expect(field(host.container, "Display name").value).toBe("My draft name"); expect(host.container.textContent).not.toContain("never-render-secret");
  });
  it("ignores late responses from a previously selected backend instead of repopulating its identities/settings", async () => {
    const host = harness();
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    let resolve!: (value: ConnectionCatalog) => void;
    host.getJson.mockImplementationOnce(() => new Promise<ConnectionCatalog>(done => { resolve = done; }));
    click(await button(host.container, "Refresh"));
    await vi.waitFor(() => expect(typeof resolve).toBe("function"));
    const next = { baseUrl: "https://different.example/shell", http: { getJson: vi.fn().mockResolvedValue({ ...fixtureCatalog, connections: [] }), postJson: vi.fn() } } as unknown as StudioEndpointContext;
    host.render(next);
    await vi.waitFor(() => expect(host.container.textContent).toContain("No external sign-in connections yet"));
    resolve(fixtureCatalog);
    await new Promise(done => setTimeout(done, 0));
    expect(host.container.querySelector(".external-auth-row")).toBeNull();
  });
});
