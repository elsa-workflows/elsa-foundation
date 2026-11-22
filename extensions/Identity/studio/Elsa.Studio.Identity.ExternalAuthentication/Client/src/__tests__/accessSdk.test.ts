import { describe, expect, it, vi } from "vitest";
import { ExternalAuthenticationClient, ExternalAuthenticationClientError, identityAccessError } from "../sdk";
import { users, links, sessions, session } from "./accessFixtures";

function setup() {
  const getJson = vi.fn().mockImplementation(async (path: string) => path.includes("/antiforgery")
    ? { requestToken: "csrf", formFieldName: "__field", headerName: "X-CSRF" } : path.includes("/links?") ? links : path.includes("/sessions?") ? sessions : users);
  const postJson = vi.fn().mockResolvedValue({ attached: true, removed: true, revoked: true });
  return { getJson, postJson, api: new ExternalAuthenticationClient({ getJson, postJson }, "https://backend.example/shell") };
}
describe("user access SDK", () => {
  it("discovers protected routes through user read access and preserves exact subjects and optimistic revisions", async () => {
    const { api, getJson, postJson } = setup(); const abort = new AbortController();
    const attachment = { connectionRecordId: "deployment:company", subject: " Exact Subject ", expectedUserRevision: "u1", expectedCatalogRevision: "c1" };
    await api.attach("user-1", attachment, abort.signal);
    expect(getJson.mock.calls.some(([path]) => path === "/_elsa/identity/external-connections")).toBe(false);
    expect(getJson).toHaveBeenCalledWith("/_elsa/identity/external-connections/users?skip=0&take=25&search=", { signal: abort.signal });
    expect(postJson).toHaveBeenCalledWith("/_elsa/identity/external-connections/users/user-1/links/attach", attachment,
      { signal: abort.signal, credentials: "include", headers: { "X-CSRF": "csrf" } });
    const removal = { provider: "company", issuer: "https://idp.example", subject: "Opaque/Subject", expectedLinkRevision: "l1", expectedUserRevision: "u1", expectedCatalogRevision: "c1" };
    await api.removeLink("user-1", removal); await api.revokeSession("user-1", session);
    expect(postJson.mock.calls[1][1]).toEqual(removal); expect(postJson.mock.calls[2][1]).toEqual({ expectedRevision: "s1" });
  });
  it("encodes exact searches and applies bounded paging independently", async () => {
    const { api, getJson } = setup(); await api.users(" a+b@example.test ", 25); await api.links("user-1", 25); await api.sessions("user-1", 50);
    expect(getJson.mock.calls.map(([path]) => path)).toEqual([
      "/_elsa/identity/external-connections/users?skip=25&take=25&search=a%2Bb%40example.test",
      "/_elsa/identity/external-connections/users/user-1/links?skip=25&take=25", "/_elsa/identity/external-connections/users/user-1/sessions?skip=50&take=25"]);
  });
  it("refuses mixed-user and oversized response pages before they can become selected-user state", async () => {
    const { api, getJson } = setup(); getJson.mockResolvedValue({ ...links, userId: "other" });
    await expect(api.links("user-1")).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
    getJson.mockResolvedValue({ ...sessions, items: [{ ...session, userId: "other" }] });
    await expect(api.sessions("user-1")).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
    getJson.mockResolvedValue({ ...users, items: Array(26).fill(users.items[0]), totalCount: 26 });
    await expect(api.users()).rejects.toBeInstanceOf(ExternalAuthenticationClientError);
    getJson.mockClear(); await expect(api.links("..")).rejects.toBeInstanceOf(ExternalAuthenticationClientError); expect(getJson).not.toHaveBeenCalled();
  });
  it("explains recovery, uncertain commit, permission and missing-module failures with closed messages", () => {
    expect(identityAccessError({ payload: { error: "final-login-path", detail: "never-show" } })).toContain("this user and an administrator");
    expect(identityAccessError({ payload: { error: "link-removal-unavailable", detail: "never-show" } })).toContain("could not be confirmed");
    expect(identityAccessError({ status: 403, payload: { detail: "never-show" } })).toContain("permission");
    expect(identityAccessError({ status: 404 })).toContain("module");
    expect(identityAccessError({ status: 500, payload: { error: "never-show", detail: "never-show" } })).not.toContain("never-show");
  });
});
