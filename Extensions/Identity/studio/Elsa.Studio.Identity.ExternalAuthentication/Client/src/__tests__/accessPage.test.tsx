import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { UserAccessPage } from "../UserAccessPage";
import type { IdentityLinks, IdentitySessions, IdentityUsers, LinkAttachment, LinkRemoval } from "../sdk";
import { users as initialUsers, links as initialLinks, sessions as initialSessions } from "./accessFixtures";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); vi.restoreAllMocks(); });
function harness(options: { users?: IdentityUsers; links?: IdentityLinks; sessions?: IdentitySessions } = {}) {
  let users = structuredClone(options.users ?? initialUsers); let links = structuredClone(options.links ?? initialLinks);
  let sessions = structuredClone(options.sessions ?? initialSessions);
  const getJson = vi.fn().mockImplementation(async (path: string) => {
    if (path.includes("/antiforgery")) return { requestToken: "csrf", formFieldName: "__csrf", headerName: "X-CSRF" };
    const uri = new URL(path, "https://backend.example"); const skip = Number(uri.searchParams.get("skip") ?? 0);
    if (path.includes("/users?")) return { ...structuredClone(users), items: users.items.slice(skip, skip + 25) };
    const id = decodeURIComponent(path.split("/users/")[1].split("/")[0]);
    if (path.includes("/links?")) return { ...structuredClone(links), userId: id, items: id === "user-1" ? links.items.slice(skip, skip + 25) : [] };
    if (path.includes("/sessions?")) return { ...structuredClone(sessions), userId: id, items: id === "user-1" ? sessions.items.slice(skip, skip + 25) : [] };
    throw new Error("Unexpected read");
  });
  const postJson = vi.fn().mockImplementation(async (path: string, body: LinkAttachment | LinkRemoval | { expectedRevision: string }) => {
    if (path.endsWith("/attach")) { links = { ...links, userRevision: "u2", catalogRevision: "c2" }; return { attached: true, failure: null }; }
    if (path.endsWith("/remove")) { links = { ...links, items: [], totalCount: 0, userRevision: "u2", catalogRevision: "c2" }; return { removed: true, failure: null }; }
    if (path.endsWith("/revoke")) { sessions = { ...sessions, items: sessions.items.map(item => ({ ...item, state: "revoked", canRevoke: false })) }; return { revoked: true, alreadyRevoked: false, failure: null }; }
    throw new Error("Unexpected mutation: " + Object.keys(body).join(","));
  });
  const context = { baseUrl: "https://backend.example/shell", http: { getJson, postJson } } as unknown as StudioEndpointContext;
  const confirm = vi.fn().mockResolvedValue(true); const dialogs = { confirm } as unknown as StudioDialogApi;
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container);
  const render = (next = context, hidden = false) => flushSync(() => root.render(<React.Activity mode={hidden ? "hidden" : "visible"}><UserAccessPage context={next} dialogs={dialogs} /></React.Activity>));
  const dispose = () => { flushSync(() => root.unmount()); container.remove(); cleanup.delete(dispose); };
  cleanup.add(dispose); render();
  return { container, context, getJson, postJson, confirm, render, dispose, setUsers: (next: IdentityUsers) => { users = next; },
    setLinks: (next: IdentityLinks) => { links = next; }, setSessions: (next: IdentitySessions) => { sessions = next; } };
}
async function button(container: HTMLElement, text: string) {
  await vi.waitFor(() => expect([...container.querySelectorAll("button")].some(item => item.textContent?.trim() === text)).toBe(true));
  return [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === text)!;
}
function click(element: HTMLElement) { flushSync(() => element.click()); }
function field(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(item => item.textContent === text)!;
  return container.querySelector<HTMLInputElement>(`[id="${label.htmlFor}"]`)!;
}
function type(input: HTMLInputElement, value: string) {
  flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value); input.dispatchEvent(new Event("input", { bubbles: true })); });
}
function connection(container: HTMLElement) { return field(container, "Connection") as unknown as HTMLSelectElement; }
async function choose(host: ReturnType<typeof harness>) {
  await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull()); click(host.container.querySelector(".external-auth-row")!);
  await vi.waitFor(() => expect(host.container.textContent).toContain("opaque-subject"));
}
async function draft(host: ReturnType<typeof harness>) {
  await choose(host); flushSync(() => { connection(host.container).value = "deployment:company"; connection(host.container).dispatchEvent(new Event("change", { bubbles: true })); });
  type(field(host.container, "External subject"), " Exact Subject ");
}

describe("user access page", () => {
  it("returns keyboard focus after cancelling session revocation without writing", async () => {
    const host = harness(); await choose(host);
    const trigger = await button(host.container, "Revoke session");
    const modalFocus = document.createElement("button"); document.body.appendChild(modalFocus); cleanup.add(() => modalFocus.remove());
    host.confirm.mockImplementation(async () => { modalFocus.focus(); return false; });
    trigger.focus(); click(trigger);
    await vi.waitFor(() => expect(host.confirm).toHaveBeenCalled());
    await vi.waitFor(() => expect(trigger.disabled).toBe(false));
    await vi.waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(host.postJson).not.toHaveBeenCalled();
  });

  it.each(["filter", "page"])("preserves the attachment draft when refreshed %s results omit the selected user", async kind => {
    const paged = { ...initialUsers, items: [...initialUsers.items, ...Array.from({ length: 25 }, (_, index) => ({
      id: `user-${index + 3}`, userName: `person${index + 3}`, displayName: `Person ${index + 3}`, status: 0
    }))], totalCount: 27 };
    const host = harness({ users: kind === "page" ? paged : initialUsers }); await draft(host);
    if (kind === "filter") {
      host.setUsers({ ...initialUsers, items: [initialUsers.items[1]], totalCount: 1 });
      type(field(host.container, "Exact username or email"), "bob"); click(await button(host.container, "Find users"));
      await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")?.textContent).toContain("Bob"));
    } else {
      click(await button(host.container, "Next users"));
      await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")?.textContent).toContain("Person 26"));
    }
    host.confirm.mockResolvedValueOnce(false); click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Discard link draft?" })));
    expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
    expect(host.container.querySelector(".external-auth-detail")?.textContent).toContain("Alice");
    expect(field(host.container, "External subject").closest("fieldset")!.disabled).toBe(true);
    expect(host.postJson).not.toHaveBeenCalled();
  });
  it("confirms before discarding a draft whose selected account is absent from refreshed results", async () => {
    const host = harness(); await draft(host); host.setUsers({ ...initialUsers, items: [], totalCount: 0 });
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Discard link draft?" })));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Select a user to inspect links and sessions."));
  });

  it("reconciles the selected user's current status and clears selection when it disappears", async () => {
    const host = harness(); await choose(host);
    host.setUsers({ ...initialUsers, items: initialUsers.items.map(user => user.id === "user-1" ? { ...user, status: 1 } : user) });
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.container.querySelector(".external-auth-detail")?.textContent).toContain("Disabled"));
    host.setUsers({ ...initialUsers, items: [], totalCount: 0 });
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Select a user to inspect links and sessions."));
    expect(host.container.textContent).not.toContain("Current session");
  });

  it("loads links and sessions for the selected user and attaches exact subjects using current source/user revisions", async () => {
    const host = harness(); await draft(host);
    click(await button(host.container, "Attach identity"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Identity attached. Saved Elsa roles are unchanged."));
    expect(host.postJson).toHaveBeenCalledWith(expect.stringContaining("/users/user-1/links/attach"),
      { connectionRecordId: "deployment:company", subject: " Exact Subject ", expectedUserRevision: "u1", expectedCatalogRevision: "c1" }, expect.objectContaining({ headers: { "X-CSRF": "csrf" } }));
    expect(field(host.container, "External subject").value).toBe("");
    expect(host.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("Saved Elsa roles and permissions stay assigned") }));
  });
  it("confirms removal with the exact identity and sends all revisions, without touching session credentials", async () => {
    const host = harness(); await choose(host); host.confirm.mockResolvedValueOnce(false);
    click(await button(host.container, "Remove link")); await vi.waitFor(() => expect(host.confirm).toHaveBeenCalled()); expect(host.postJson).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(host.container.querySelector<HTMLButtonElement>(".external-auth-danger-action")!.disabled).toBe(false));
    click(await button(host.container, "Remove link"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Identity link removed."));
    expect(host.postJson.mock.calls[0][1]).toEqual({ provider: "company", issuer: "https://idp.example", subject: "opaque-subject", expectedLinkRevision: "l1", expectedUserRevision: "u1", expectedCatalogRevision: "c1" });
    expect(host.container.textContent).toContain("active"); expect(host.postJson.mock.calls.some(([path]) => path.endsWith("/revoke"))).toBe(false);
  });
  it("revokes one session and retains a confirmed revoked state", async () => {
    const host = harness(); await choose(host); click(await button(host.container, "Revoke session"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Session revoked. Other sessions are unchanged."));
    expect(host.postJson.mock.calls[0][1]).toEqual({ expectedRevision: "s1" }); expect(host.container.textContent).toContain("revoked");
    expect((await button(host.container, "Revoke session")).disabled).toBe(true);
  });
  it("handles current-session revocation without treating the expected reauthentication as a failed mutation", async () => {
    const host = harness({ sessions: { ...initialSessions, items: [{ ...initialSessions.items[0], current: true }] } });
    await choose(host); click(await button(host.container, "Revoke session"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Current session revoked. Sign in"));
    expect(host.getJson.mock.calls.filter(([path]) => path.includes("/sessions?")).length).toBe(1);
    expect((await button(host.container, "Refresh user access")).disabled).toBe(true);
    expect(host.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("This is your current session") }));
  });
  it("uses backend permissions and authority capabilities for read-only and legacy rows", async () => {
    const host = harness({ users: { ...initialUsers, canManageUsers: false, canRevokeTokens: false, ownershipMode: "ExternalOwned" },
      links: { ...initialLinks, canAttach: false, items: [{ ...initialLinks.items[0], qualified: false, issuer: null, canRemove: false }] } });
    await choose(host); expect(host.container.textContent).toContain("identity authority owns these users"); expect(host.container.textContent).toContain("Legacy links are read-only");
    expect((await button(host.container, "Remove link")).disabled).toBe(true); expect((await button(host.container, "Revoke session")).disabled).toBe(true);
    expect(host.container.querySelector("form.external-auth-editor")).toBeNull(); expect(host.postJson).not.toHaveBeenCalled();
  });
  it("does not fetch denied panels and explains missing session administration while retaining link inspection", async () => {
    const host = harness({ users: { ...initialUsers, canReadSessions: false } }); await choose(host);
    expect(host.container.textContent).toContain("do not have permission to read sessions"); expect(host.getJson.mock.calls.some(([path]) => path.includes("/sessions?"))).toBe(false);
    host.setUsers({ ...initialUsers, sessionAdministrationUnavailableReason: "session-administration-unavailable" });
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Session administration is unavailable"));
    expect((await button(host.container, "Remove link")).disabled).toBe(true); expect(host.container.textContent).toContain("opaque-subject");
  });
  it("keeps a failed draft, requires refresh after uncertain writes, and never renders private response details", async () => {
    const host = harness(); await draft(host); host.postJson.mockRejectedValueOnce({ status: 503, payload: { error: "link-attachment-unavailable", detail: "secret-canary" } });
    click(await button(host.container, "Attach identity"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("Attachment could not be confirmed"));
    expect(field(host.container, "External subject").value).toBe(" Exact Subject "); expect((await button(host.container, "Attach identity")).closest("fieldset")!.disabled).toBe(true);
    expect(host.container.textContent).not.toContain("secret-canary");
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect((field(host.container, "External subject").closest("fieldset")!).disabled).toBe(false));
    expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
  });
  it("preserves drafts across renders, vetoes closing/probes, and confirms before changing users", async () => {
    const host = harness(); await draft(host); host.render(); expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
    const probe = new CustomEvent("elsa:studio-navigation-requested", { cancelable: true, detail: { intent: "probe" } }); window.dispatchEvent(probe); expect(probe.defaultPrevented).toBe(true);
    host.confirm.mockResolvedValueOnce(false); click(host.container.querySelectorAll<HTMLButtonElement>(".external-auth-row")[1]);
    await vi.waitFor(() => expect(host.confirm).toHaveBeenCalled()); expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
    click(await button(host.container, "Discard link draft"));
    await vi.waitFor(() => expect(field(host.container, "External subject").value).toBe(""));
  });
  it("ignores late selected-user responses after a backend switch and clears the old identity/draft", async () => {
    const host = harness(); await vi.waitFor(() => expect(host.container.querySelector(".external-auth-row")).not.toBeNull());
    let complete!: (value: IdentityLinks) => void;
    const original = host.getJson.getMockImplementation()!;
    host.getJson.mockImplementation((path: string) => path.includes("/links?") ? new Promise<IdentityLinks>(resolve => { complete = resolve; }) : original(path));
    click(host.container.querySelector(".external-auth-row")!); await vi.waitFor(() => expect(complete).toBeTypeOf("function"));
    const other = { baseUrl: "https://other.example", http: { getJson: vi.fn().mockResolvedValue({ ...initialUsers, items: [], totalCount: 0 }), postJson: vi.fn() } } as unknown as StudioEndpointContext;
    host.render(other); await vi.waitFor(() => expect(host.container.textContent).toContain("No users match")); complete(initialLinks);
    await Promise.resolve(); expect(host.container.textContent).not.toContain("opaque-subject"); expect(host.container.textContent).not.toContain("Alice");
  });
  it("clears previously displayed identities when current user-read permission is denied", async () => {
    const host = harness(); await draft(host);
    host.getJson.mockRejectedValue({ status: 403, payload: { detail: "private-detail" } });
    click(await button(host.container, "Refresh user access"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("do not have permission"));
    expect(host.container.textContent).not.toContain("opaque-subject"); expect(host.container.textContent).not.toContain("Alice");
    expect(host.container.textContent).not.toContain("private-detail"); expect(host.container.querySelector("form.external-auth-editor")).toBeNull();
  });
  it("retains the selected user and unattached subject when workspace effects suspend and resume", async () => {
    const host = harness(); await draft(host);
    host.render(host.context, true); host.render(host.context, false);
    await vi.waitFor(() => expect(host.container.textContent).toContain("opaque-subject"));
    expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
    expect(connection(host.container).value).toBe("deployment:company");
    expect(host.postJson).not.toHaveBeenCalled();
  });
  it("paginates the bounded user directory without losing the selected user's draft", async () => {
    const many = { ...initialUsers, items: Array.from({ length: 26 }, (_, index) => ({ ...initialUsers.items[0], id: index ? `user-${index + 1}` : "user-1", userName: `user${index}`, displayName: `User ${index}` })), totalCount: 26 };
    const host = harness({ users: many }); await draft(host); click(await button(host.container, "Next users"));
    await vi.waitFor(() => expect(host.container.textContent).toContain("26–26 of 26"));
    expect(field(host.container, "External subject").value).toBe(" Exact Subject ");
    expect(host.getJson.mock.calls.some(([path]) => path.includes("/users?skip=25&take=25"))).toBe(true);
  });
});
