import { beforeEach, describe, expect, it, vi } from "vitest";
import { connectionChanges, ExternalAuthenticationClient, ExternalAuthenticationClientError, externalAuthenticationError } from "../sdk";
import { cleanPreviewUrl, preparePreviewReturn, takePreviewReturn } from "../previewReturn";

import { connectionDocument as document, view, catalog } from "./fixtures";

function client() {
  const getJson = vi.fn().mockImplementation(async (path: string) => path.includes("/antiforgery")
    ? { requestToken: "csrf", formFieldName: "__custom", headerName: "X-Custom-CSRF" } : catalog);
  const postJson = vi.fn().mockResolvedValue({ saved: true });
  return { getJson, postJson, api: new ExternalAuthenticationClient({ getJson, postJson }, "https://backend.example/shell") };
}
beforeEach(() => { window.sessionStorage.clear(); vi.restoreAllMocks(); });

describe("external authentication SDK", () => {
  it("uses the selected transport and backend-declared protocol/header names for protected revisioned commands", async () => {
    const { api, getJson, postJson } = client();
    const abort = new AbortController();
    const command = { change: connectionChanges.enable, expectedRevision: "r1", record: document };
    await api.apply(command, abort.signal);
    expect(getJson).toHaveBeenCalledWith("/sso/antiforgery", { signal: abort.signal, credentials: "include" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/identity/external-connections/commands", command,
      { signal: abort.signal, credentials: "include", headers: { "X-Custom-CSRF": "csrf" } });
  });
  it("prepares only a selected-shell preview form using the actual antiforgery field and saved record", async () => {
    const { api } = client();
    const submission = await api.previewSubmission(view, "https://studio.example/security/external-sign-in");
    expect(submission).toEqual({ action: "https://backend.example/shell/sso/preview/company", fields:
      { __custom: "csrf", connectionRecordId: "record", returnUrl: "https://studio.example/security/external-sign-in" } });
    for (const callback of ["https://elsewhere.example/shell/sso/preview/company/callback", "https://backend.example/other/sso/preview/company/callback", "https://user:password@backend.example/shell/sso/preview/company/callback"])
      await expect(api.previewSubmission({ ...view, previewCallback: callback }, "/")).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
  });
  it.each(["https://other.example/auth", "//other.example/auth", "/%2e%2e/elsewhere", "/%252e%252e/escape", "/auth?redirect=elsewhere", "/auth\\escape"])("rejects unsafe protocol metadata %s before protected requests", async protocolPath => {
    const { api, getJson, postJson } = client(); getJson.mockResolvedValue({ ...catalog, protocolPath });
    await expect(api.apply({ change: 0, expectedRevision: null, record: document })).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
    expect(postJson).not.toHaveBeenCalled();
  });
  it("never retains values or unknown fields from the public secret picker and filters inactive/expired/wrong-type rows", async () => {
    const { api, postJson } = client();
    postJson.mockResolvedValue({ items: [
      { name: "live", typeName: "text", status: "Active", scope: "identity", value: "secret-canary", password: "never-retain" },
      { name: "disabled", typeName: "text", status: "Disabled" }, { name: "other", typeName: "json", status: "Active" },
      { name: "expired", typeName: "text", status: "Active", expiresAt: "2020-01-01T00:00:00Z" },
      { name: "invalid-date", typeName: "text", status: "Active", expiresAt: "invalid" }
    ] });
    const result = await api.secrets(" live ");
    expect(result.items).toEqual([{ name: "live", displayName: null, typeName: "text", scope: "identity" }]);
    expect(JSON.stringify(result)).not.toContain("secret-canary"); expect(JSON.stringify(result)).not.toContain("never-retain");
    expect(postJson).toHaveBeenCalledWith("/secrets/picker", { search: "live", typeNames: ["text"], storeNames: [], activeOnly: true }, { signal: undefined });
  });
  it("retrieves a bounded one-use preview with credentials and refuses malformed handles without a request", async () => {
    const { api, getJson } = client(); await api.list(); getJson.mockClear();
    await expect(api.readPreview("company", "short")).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
    expect(getJson).not.toHaveBeenCalled();
    await api.readPreview("company", "a".repeat(43));
    expect(getJson).toHaveBeenCalledWith("/sso/preview/company/results/" + "a".repeat(43), { signal: undefined, credentials: "include" });
  });
  it("presents closed failure messages rather than upstream response/exception text", () => {
    expect(externalAuthenticationError({ status: 409, payload: { failure: "final-login-path", detail: "secret-canary" } })).toContain("another usable administrator");
    expect(externalAuthenticationError(new Error("secret-canary"))).not.toContain("secret-canary");
    expect(externalAuthenticationError({ status: 503, message: "secret-canary" })).not.toContain("secret-canary");
    expect(externalAuthenticationError({ status: 503, payload: { failure: "constructor" } })).toBeTypeOf("string");
  });
});

describe("preview return", () => {
  it("retains only source/test metadata, preserves backend selection, removes query handles and consumes the local binding once", () => {
    const url = new URL("https://studio.example/security/external-sign-in?backend=selected");
    const destination = new URL(preparePreviewReturn(url, "https://backend.example/shell", view, null, window.sessionStorage));
    destination.searchParams.set("externalPreviewHandle", "h".repeat(43)); destination.searchParams.set("externalPreviewConnection", "company");
    const restored = takePreviewReturn(destination, "https://backend.example/shell", window.sessionStorage)!;
    expect(restored.pending.recordId).toBe("record"); expect(restored.pending.materialRevision).toBe("material");
    expect(destination.searchParams.get("backend")).toBe("selected");
    expect(cleanPreviewUrl(new URL(destination)).search).toBe("?backend=selected");
    expect(() => takePreviewReturn(destination, "https://backend.example/shell", window.sessionStorage)).toThrow(ExternalAuthenticationClientError);
    expect(window.sessionStorage.length).toBe(0);
  });
  it("refuses a return belonging to a different selected backend before any identity data is read", () => {
    const destination = new URL(preparePreviewReturn(new URL("https://studio.example/security/external-sign-in"), "https://backend.example/shell", view, null, window.sessionStorage));
    destination.searchParams.set("externalPreviewHandle", "h".repeat(43)); destination.searchParams.set("externalPreviewConnection", "company");
    expect(() => takePreviewReturn(destination, "https://different.example/shell", window.sessionStorage)).toThrow(/another backend/);
  });
  it("accepts only a closed failure on the same one-use local binding and removes failure parameters", () => {
    const destination = new URL(preparePreviewReturn(new URL("https://studio.example/security/external-sign-in?backend=selected"), "https://backend.example/shell", view, null, window.sessionStorage));
    destination.searchParams.set("externalPreviewConnection", "company"); destination.searchParams.set("externalPreviewFailure", "ProviderFailed");
    const restored = takePreviewReturn(destination, "https://backend.example/shell", window.sessionStorage)!;
    expect(restored.handle).toBeNull(); expect(restored.error).toContain("cancelled or could not be verified");
    expect(restored.pending.recordId).toBe("record"); expect(cleanPreviewUrl(new URL(destination)).search).toBe("?backend=selected");
    expect(() => takePreviewReturn(destination, "https://backend.example/shell", window.sessionStorage)).toThrow(/another backend/);
  });
  it.each(["private-upstream-detail", "__proto__", "constructor"])("refuses an unknown failure category %s", failure => {
    const destination = new URL(preparePreviewReturn(new URL("https://studio.example/security/external-sign-in"), "https://backend.example/shell", view, null, window.sessionStorage));
    destination.searchParams.set("externalPreviewConnection", "company"); destination.searchParams.set("externalPreviewFailure", failure);
    expect(() => takePreviewReturn(destination, "https://backend.example/shell", window.sessionStorage)).toThrow(/incomplete/);
  });
});
