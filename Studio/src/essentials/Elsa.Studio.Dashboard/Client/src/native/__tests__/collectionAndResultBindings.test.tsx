import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardApi } from "../api";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { initialDefinition, type DashboardResource, type DatasetDeclaration, type Page, type QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const dataset: DatasetDeclaration = { id: "processing", ownerModuleId: "Acme", schemaRevision: "1", title: "Processing", description: "Declared records", fields: [], metrics: [] };
const question = (): DashboardResource => ({ id: "q1", kind: "question", revision: "r1", name: "Finance", description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: true, canShare: true, canDelete: true, canCopy: true }, definition: initialDefinition(dataset), definitionState: "available", createdAt: "", updatedAt: "" });
const result = (marker = "42"): QueryResult => ({ outcome: "succeeded", columns: [{ id: "count", label: "Count", type: "integer" }], rows: [{ key: "one", values: { count: { type: "integer", value: marker } }, drill: { proof: "authorized-group" } }] });
const cleanups: (() => void)[] = [];
function mount(content: React.ReactNode) {
  const view = document.createElement("div"); document.body.append(view); const root = createRoot(view);
  flushSync(() => root.render(content)); cleanups.push(() => { flushSync(() => root.unmount()); view.remove(); }); return view;
}
function action(view: HTMLElement, text: string) {
  const button = [...view.querySelectorAll("button")].find(item => item.textContent?.trim() === text);
  expect(button, `Action ${text}`).toBeDefined(); return button!;
}
function click(view: HTMLElement, text: string) { flushSync(() => action(view, text).click()); }
function edit(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  Object.getOwnPropertyDescriptor(input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
function submitSearch(view: HTMLElement, value: string) {
  edit(view.querySelector<HTMLInputElement>('[aria-label="Search dashboards"]')!, value);
  flushSync(() => view.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); vi.restoreAllMocks(); });

describe("submitted collection scope", () => {
  it("preserves the submitted search when switching Mine to Shared with me", async () => {
    window.history.replaceState({}, "", "/dashboards"); const module = stubApi();
    const list = vi.spyOn(DashboardApi.prototype, "list").mockImplementation(async (_kind, ownership, query) => ({ items: [{ ...question(), kind: "dashboard", name: `${ownership}:${query}` }] }));
    const view = mount(<AnonymousAuthProvider><NamedDashboardsRoute api={module} kind="dashboard" view="list" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => view.textContent?.includes("mine:") === true);
    submitSearch(view, "finance"); await waitUntil(() => view.textContent?.includes("mine:finance") === true);
    click(view, "Shared with me"); await waitUntil(() => list.mock.calls.some(call => call[1] === "shared"));
    expect(list.mock.calls.at(-1)?.slice(0, 3)).toEqual(["dashboard", "shared", "finance"]);
    await waitUntil(() => view.textContent?.includes("shared:finance") === true);
    expect(view.querySelector<HTMLInputElement>('[aria-label="Search dashboards"]')?.value).toBe("finance");
  });

  it("carries the latest submitted search across ownership while that search is pending and ignores its old completion", async () => {
    window.history.replaceState({}, "", "/dashboards"); const module = stubApi();
    let finish!: (page: Page<DashboardResource>) => void; let pendingSignal!: AbortSignal;
    const list = vi.spyOn(DashboardApi.prototype, "list").mockImplementation(async (_kind, ownership, query, _cursor, signal) => {
      if (ownership === "mine" && query === "finance") { pendingSignal = signal!; return new Promise(resolve => { finish = resolve; }); }
      return { items: [{ ...question(), kind: "dashboard", name: `${ownership}:${query}` }] };
    });
    const view = mount(<AnonymousAuthProvider><NamedDashboardsRoute api={module} kind="dashboard" view="list" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => view.textContent?.includes("mine:") === true);
    submitSearch(view, "operations"); await waitUntil(() => view.textContent?.includes("mine:operations") === true);
    submitSearch(view, "finance"); await waitUntil(() => typeof finish === "function"); click(view, "Shared with me");
    await waitUntil(() => list.mock.calls.some(call => call[1] === "shared"));
    expect(list.mock.calls.at(-1)?.slice(0, 3)).toEqual(["dashboard", "shared", "finance"]); expect(pendingSignal.aborted).toBe(true);
    finish({ items: [{ ...question(), kind: "dashboard", name: "LATE-MINE-RESULT" }] });
    await waitUntil(() => view.textContent?.includes("shared:finance") === true);
    expect(view.textContent).not.toContain("LATE-MINE-RESULT");
    click(view, "Refresh"); await waitUntil(() => list.mock.calls.filter(call => call[1] === "shared").length === 2);
    expect(list.mock.calls.at(-1)?.[2]).toBe("finance");
  });
});

function savedQuestionEditor() {
  window.history.replaceState({}, "", "/questions/q1"); const module = stubApi(); let current = question();
  module.backend.http.getJson = vi.fn(async (url: string) => url.endsWith("dashboard-datasets") ? { items: [dataset] } : current) as typeof module.backend.http.getJson;
  const post = vi.fn(async (url: string, _body: unknown, init?: { headers?: Record<string, string>; signal?: AbortSignal }) => {
    if (url.endsWith("/drill") && init?.headers?.["If-Match"] !== `"${current.revision}"`) throw Object.assign(new Error("The definition changed. Reload before drilling."), { status: 412 });
    return result();
  });
  module.backend.http.postJson = post as typeof module.backend.http.postJson;
  const put = vi.fn(async (_url: string, body: { description: string; definition?: DashboardResource["definition"] }) => {
    current = { ...current, revision: "r2", description: body.description, definition: body.definition }; return current;
  });
  module.backend.http.putJson = put as typeof module.backend.http.putJson;
  const view = mount(<AnonymousAuthProvider><NamedDashboardsRoute api={module} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
  return { view, post, put, module };
}

describe("accepted definition result binding", () => {
  it.each(["metadata", "definition"] as const)("clears the old %s preview on save and enables current-revision refresh and drill", async change => {
    const { view, post, put } = savedQuestionEditor(); await waitUntil(() => view.querySelector('input[type="number"]') !== null);
    if (change === "definition") edit(view.querySelector<HTMLInputElement>('input[type="number"]')!, "7");
    click(view, change === "definition" ? "Preview question" : "Refresh results"); await waitUntil(() => view.querySelector(".named-result") !== null);
    if (change === "metadata") edit(view.querySelector("textarea")!, "Updated description");
    click(view, "Save question"); await waitUntil(() => view.textContent?.includes("Revision r2") === true); expect(put).toHaveBeenCalledOnce();
    expect(view.querySelector(".named-result")).toBeNull();
    expect(view.textContent).not.toContain("Save this question before investigating its changed definition.");
    expect(view.textContent).not.toContain("current data access could not be established");
    expect(action(view, "Refresh results").disabled).toBe(false);
    click(view, "Refresh results"); await waitUntil(() => view.querySelector(".named-result") !== null);
    const execute = post.mock.calls.filter(call => call[0].endsWith("/execute")).at(-1)!; expect(execute[2]?.headers).toEqual({ "If-Match": '"r2"' });
    click(view, "View matching records"); await waitUntil(() => post.mock.calls.some(call => call[0].endsWith("/drill")));
    const drill = post.mock.calls.find(call => call[0].endsWith("/drill"))!; expect(drill[2]?.headers).toEqual({ "If-Match": '"r2"' });
    await waitUntil(() => view.querySelector('[role="dialog"] .named-result') !== null); expect(view.textContent).not.toContain("Reload before drilling");
  });

  it("aborts an old in-flight refresh when save accepts a new revision and rejects its late result", async () => {
    const { view, module } = savedQuestionEditor(); let finish!: (value: QueryResult) => void; let signal!: AbortSignal;
    module.backend.http.postJson = vi.fn((_url: string, _body: unknown, init?: { signal?: AbortSignal }) => { signal = init!.signal!; return new Promise(resolve => { finish = resolve; }); }) as typeof module.backend.http.postJson;
    await waitUntil(() => view.querySelector("textarea") !== null); click(view, "Refresh results"); await waitUntil(() => typeof finish === "function");
    edit(view.querySelector("textarea")!, "Saved while refresh is pending"); click(view, "Save question"); await waitUntil(() => view.textContent?.includes("Revision r2") === true);
    expect(signal.aborted).toBe(true); finish(result("STALE-R1-COMPLETION"));
    await new Promise(resolve => setTimeout(resolve, 0)); expect(view.textContent).not.toContain("STALE-R1-COMPLETION"); expect(view.querySelector(".named-result")).toBeNull();
    expect(action(view, "Refresh results").disabled).toBe(false);
  });
});
