import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardApi } from "../api";
import { NamedDashboardsRoute, QuestionTile } from "../NamedDashboards";
import { ShareDialog } from "../ResourceDialogs";
import { initialDefinition, type DashboardResource, type DatasetDeclaration, type QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const dataset: DatasetDeclaration = { id: "acme", ownerModuleId: "Acme", schemaRevision: "1", title: "Processing", description: "Declared data", fields: [], metrics: [] };
const resource = (): DashboardResource => ({ id: "q1", kind: "question", revision: "r1", name: "Shared question", description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: true, canShare: true, canDelete: true, canCopy: true }, definition: initialDefinition(dataset), definitionState: "available", createdAt: "", updatedAt: "" });
const result = (marker: string): QueryResult => ({ outcome: "succeeded", columns: [{ id: "value", label: "Count", type: "integer" }], rows: [{ key: "row", values: { value: { type: "integer", value: marker } } }] });
const cleanups: (() => void)[] = [];
function mount(children: React.ReactNode) {
  const view = document.createElement("div"); document.body.append(view); const root = createRoot(view);
  flushSync(() => root.render(children)); cleanups.push(() => { flushSync(() => root.unmount()); view.remove(); }); return view;
}
function click(view: HTMLElement, name: string) {
  const button = [...view.querySelectorAll("button")].find(item => item.textContent?.trim() === name);
  expect(button, `Action ${name}`).toBeDefined(); flushSync(() => button!.click());
}
function edit(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
function editor(saved: DashboardResource) {
  window.history.replaceState({}, "", `/${saved.kind}s/${saved.id}`);
  const api = stubApi();
  const dialogs = { confirm: vi.fn(async (_options: unknown) => true), prompt: vi.fn(async (_options: unknown) => null), alert: vi.fn(async (_options: unknown) => undefined) };
  const full = { ...api, dialogs };
  api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : url.includes("/dashboards/") ? saved : resource()) as typeof api.backend.http.getJson;
  if (saved.kind === "question") api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : saved) as typeof api.backend.http.getJson;
  api.backend.http.postJson = vi.fn(async () => result("0")) as typeof api.backend.http.postJson;
  const view = mount(<AnonymousAuthProvider><NamedDashboardsRoute api={full} kind={saved.kind} view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
  return { api, dialogs, view };
}
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); vi.restoreAllMocks(); });

describe("native product recovery", () => {
  it("keeps an unavailable audience unknown until a successful retry, then preserves existing grants", async () => {
    const api = new DashboardApi(stubApi().backend);
    const audience = vi.spyOn(api, "audience").mockRejectedValueOnce(new Error("Audience temporarily unavailable")).mockResolvedValueOnce({ items: [{ kind: "user", id: "existing-viewer", access: "viewer" }] });
    const share = vi.spyOn(api, "share").mockResolvedValue(resource());
    const view = mount(<ShareDialog api={api} resource={resource()} onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitUntil(() => view.textContent?.includes("Audience temporarily unavailable") === true);
    expect(view.textContent).not.toContain("Personal. No audience grants.");
    click(view, "Retry current audience"); await waitUntil(() => view.textContent?.includes("existing-viewer") === true);
    click(view, "Save audience"); await waitUntil(() => share.mock.calls.length === 1);
    expect(audience).toHaveBeenCalledTimes(2); expect(share.mock.calls[0][1]).toEqual([{ kind: "user", id: "existing-viewer", access: "viewer" }]);
  });

  it("preserves usable focus on a tile moved to the last and first positions", async () => {
    const saved = { ...resource(), kind: "dashboard" as const, id: "d1", tiles: [{ id: "one", questionId: "q1", size: "wide" as const }, { id: "two", questionId: "q1", size: "medium" as const }] };
    const { view } = editor(saved);
    await waitUntil(() => view.querySelectorAll("[data-tile-id]").length === 2);
    const later = view.querySelector<HTMLButtonElement>('[data-tile-id="one"] button[data-move="1"]')!; later.focus(); flushSync(() => later.click());
    await waitUntil(() => document.activeElement === view.querySelector('[data-tile-id="one"] button[data-move="-1"]'));
    expect((document.activeElement as HTMLButtonElement).disabled).toBe(false);
    flushSync(() => view.querySelector<HTMLButtonElement>('[data-tile-id="one"] button[data-move="-1"]')!.click());
    await waitUntil(() => document.activeElement === view.querySelector('[data-tile-id="one"] button[data-move="1"]'));
  });

  it("requires confirmation to replace a withheld query with a blank local draft without disclosing or saving old fields", async () => {
    const { api, dialogs, view } = editor({ ...resource(), definition: undefined, definitionState: "withheld", capabilities: { ...resource().capabilities, canCopy: false } });
    const put = vi.spyOn(api.backend.http, "putJson").mockResolvedValue({ ...resource(), revision: "repaired-r2" });
    await waitUntil(() => view.textContent?.includes("Parts of this definition are withheld") === true);
    dialogs.confirm.mockResolvedValueOnce(false); click(view, "Replace unavailable query");
    await waitUntil(() => dialogs.confirm.mock.calls.length === 1); expect(view.querySelector(".named-query-controls")).toBeNull();
    click(view, "Replace unavailable query"); await waitUntil(() => view.querySelector(".named-query-controls") !== null);
    expect(view.querySelector<HTMLSelectElement>('[data-query-path="query.dataset"] select')?.value).toBe("");
    expect(view.textContent).toContain("Nothing has been saved"); expect(put).not.toHaveBeenCalled();
    expect(dialogs.confirm.mock.calls[1][0]).toMatchObject({ message: expect.stringContaining("blank") });
    const source = view.querySelector<HTMLSelectElement>('[data-query-path="query.dataset"] select')!;
    flushSync(() => { source.value = dataset.id; source.dispatchEvent(new Event("change", { bubbles: true })); });
    click(view, "Save question"); await waitUntil(() => view.textContent?.includes("Revision repaired-r2") === true);
    expect(view.querySelector(".named-query-controls")).not.toBeNull(); expect(view.textContent).not.toContain("Parts of this definition are withheld");
    expect(put.mock.calls[0][1]).toMatchObject({ definition: { query: { dataset: { id: "acme" } } } });
    expect(put.mock.calls[0][1]).not.toHaveProperty("preserveDefinition");
  });

  it("does not offer replacement of withheld content to a read-only viewer", async () => {
    const { view } = editor({ ...resource(), definition: undefined, definitionState: "withheld", capabilities: { ...resource().capabilities, canEdit: false, canCopy: false } });
    await waitUntil(() => view.textContent?.includes("Parts of this definition are withheld") === true);
    expect(view.textContent).not.toContain("Replace unavailable query");
  });

  it.each(["manual", "save", "preview"] as const)("uses structured native validation after %s to associate corrections and focus the affected input", async mode => {
    const saved = resource(); saved.definition!.query.time = { field: "startedAt", from: "invalid", to: "2026-11-06T00:00:00Z", timeZone: "UTC", weekStart: "monday" };
    const { api, view } = editor(saved);
    const post = vi.fn(async (url: string) => {
      if (!url.endsWith("/validate")) throw Object.assign(new Error("Invalid question settings."), { status: 422 });
      return { isValid: false, problems: [{ code: "INVALID_TIME", path: "query.time.from", message: "Supply an ISO UTC start instant." }, { code: "INVALID_VISUALIZATION", path: "visualization.y", message: "Choose a numeric value column." }] };
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    vi.spyOn(api.backend.http, "putJson").mockRejectedValue(Object.assign(new Error("Invalid question settings."), { status: 422 }));
    await waitUntil(() => view.querySelector(".named-query-controls") !== null); click(view, mode === "manual" ? "Validate question" : mode === "save" ? "Save question" : "Refresh results");
    await waitUntil(() => view.textContent?.includes("Supply an ISO UTC start instant.") === true);
    const time = view.querySelector<HTMLElement>('[data-query-path="query.time"]')!;
    expect(time.getAttribute("aria-invalid")).toBe("true"); expect(time.textContent).toContain("Supply an ISO UTC start instant.");
    const input = view.querySelector<HTMLInputElement>('[data-query-path="query.time.from"]')!;
    await waitUntil(() => document.activeElement === input); expect(input.value).toBe("invalid");
    expect(view.querySelector('[data-query-path="visualization"]')?.getAttribute("aria-invalid")).toBe("true");
    expect(post.mock.calls.at(-1)![0]).toMatch(/dashboard-queries\/validate$/);
  });

  it("shows only disclosed dashboard reuse and confirms before updating the referenced query", async () => {
    const saved = { ...resource(), uses: [{ id: "visible-dashboard", name: "Operations", tileCount: 2 }] };
    const { api, dialogs, view } = editor(saved);
    const put = vi.spyOn(api.backend.http, "putJson").mockResolvedValue({ ...saved, revision: "r2" });
    await waitUntil(() => view.querySelector('input[type="number"]') !== null);
    edit(view.querySelector<HTMLInputElement>('input[type="number"]')!, "7");
    expect(view.textContent).toContain("Dashboards you can access using this question"); expect(view.textContent).toContain("Operations");
    expect(view.textContent).not.toContain("No other dashboards");
    dialogs.confirm.mockResolvedValueOnce(false); click(view, "Save question");
    await waitUntil(() => dialogs.confirm.mock.calls.length === 1); expect(put).not.toHaveBeenCalled();
    expect(dialogs.confirm.mock.calls[0][0]).toMatchObject({ message: expect.stringContaining("Save draft as new question") });
  });

  it("cancels a tile refresh, withholds values and ignores a late success from transport", async () => {
    const module = stubApi(); const api = new DashboardApi(module.backend); const saved = { ...resource(), id: "d1", kind: "dashboard" as const };
    const tile = { id: "t1", questionId: "q1", size: "medium" as const };
    module.backend.http.getJson = vi.fn(async () => resource()) as typeof module.backend.http.getJson;
    let complete!: (value: QueryResult) => void; let signal!: AbortSignal;
    module.backend.http.postJson = vi.fn((_url: string, _body: unknown, init?: unknown) => { signal = (init as { signal: AbortSignal }).signal; return new Promise<QueryResult>(resolve => { complete = resolve; }); }) as typeof module.backend.http.postJson;
    const view = mount(<QuestionTile api={api} dashboard={saved} tile={tile} refresh={0} draftLayout={false} navigate={vi.fn()} />);
    await waitUntil(() => typeof complete === "function"); click(view, "Cancel refresh");
    expect(signal.aborted).toBe(true); expect(view.textContent).toContain("Refresh cancelled"); expect(view.querySelector(".named-result")).toBeNull();
    complete(result("PRIVATE-LATE-RESULT")); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.textContent).not.toContain("PRIVATE-LATE-RESULT");
  });
});

describe("bounded actor-scoped native request admission", () => {
  it("refreshes four real tile transports without exceeding the native two-read allowance", async () => {
    const module = stubApi(); const api = new DashboardApi(module.backend, "tenant:tile-viewer");
    const saved = { ...resource(), id: "d1", kind: "dashboard" as const };
    module.backend.http.getJson = vi.fn(async () => resource()) as typeof module.backend.http.getJson;
    let active = 0; let maximum = 0; const releases: (() => void)[] = [];
    const post = vi.fn((_url: string) => { active++; maximum = Math.max(maximum, active); return new Promise<QueryResult>(resolve => { releases.push(() => { active--; resolve(result("CURRENT-VIEWER")); }); }); });
    module.backend.http.postJson = post as typeof module.backend.http.postJson;
    const view = mount(<>{[1, 2, 3, 4].map(index => <QuestionTile key={index} api={api} dashboard={saved} tile={{ id: `t${index}`, questionId: "q1", size: "medium" }} refresh={0} draftLayout={false} navigate={vi.fn()} />)}</>);
    await waitUntil(() => post.mock.calls.length >= 2); expect(post).toHaveBeenCalledTimes(2);
    releases.shift()!(); await waitUntil(() => post.mock.calls.length === 3);
    releases.shift()!(); await waitUntil(() => post.mock.calls.length === 4);
    while (releases.length) releases.shift()!();
    await waitUntil(() => view.querySelectorAll(".named-result").length === 4);
    expect(maximum).toBe(2); expect(post.mock.calls.every(call => /\/dashboards\/d1\/tiles\/t[1-4]\/execute$/.test(call[0]))).toBe(true);
  });

  it("limits actual deferred requests across API instances and removes a cancelled queued request", async () => {
    const module = stubApi(); const first = new DashboardApi(module.backend, "tenant:user"); const second = new DashboardApi(module.backend, "tenant:user");
    let active = 0; let maximum = 0; const releases: (() => void)[] = [];
    const get = vi.fn((_url: string) => { active++; maximum = Math.max(maximum, active); return new Promise<DashboardResource>(resolve => { releases.push(() => { active--; resolve(resource()); }); }); });
    module.backend.http.getJson = get as typeof module.backend.http.getJson;
    const cancelled = new AbortController();
    const requests = [first.get("question", "one"), second.get("question", "two"), first.get("question", "cancelled", cancelled.signal).catch(cause => cause), second.get("question", "four")];
    await waitUntil(() => get.mock.calls.length >= 2); expect(get).toHaveBeenCalledTimes(2);
    cancelled.abort(); releases.shift()!(); await waitUntil(() => get.mock.calls.length === 3);
    expect(get.mock.calls.map(call => call[0])).not.toContainEqual(expect.stringContaining("/cancelled"));
    while (releases.length) releases.shift()!(); await Promise.all(requests);
    expect(maximum).toBe(2); expect(get.mock.calls.every(call => call[0].includes("/_elsa/studio/questions/"))).toBe(true);
  });
});
