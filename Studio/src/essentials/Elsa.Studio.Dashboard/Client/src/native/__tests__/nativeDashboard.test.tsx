import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardApi } from "../api";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { QueryControls } from "../QueryControls";
import { ResultView } from "../ResultView";
import { initialDefinition, serializeTiles, type DashboardResource, type DatasetDeclaration, type QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";
import { createDashboardAuthoringController } from "../authoring";

const dataset: DatasetDeclaration = { id: "acme.processing", ownerModuleId: "Acme.Processing", schemaRevision: "1", title: "Processing records", description: "Explicitly exposed processing records.", availability: "ready", fields: [{ id: "category", label: "Customer category", type: "string", filterOperators: ["eq", "ne", "in"], nullable: false, groupable: true, sortable: true, projectable: true, businessDimension: true }, { id: "duration", label: "Processing time", type: "decimal", filterOperators: ["gt"], nullable: true, groupable: false, sortable: true, projectable: true, aggregates: ["average"] }], metrics: [{ id: "processing.duration", revision: "1", label: "Average processing time", unit: "seconds", description: "Elapsed processing time for eligible records." }] };
const question = (): DashboardResource => ({ id: "q1", kind: "question", name: "Processing by category", description: "", ownerId: "user-a", revision: "r1", capabilities: { canRead: true, canEdit: true, canCopy: true, canShare: true, canDelete: true }, definition: initialDefinition(dataset), definitionState: "available", tiles: [], createdAt: "2026-11-06T10:00:00Z", updatedAt: "2026-11-06T10:00:00Z" });
const result = (): QueryResult => ({ outcome: "succeeded", columns: [{ id: "category", label: "Customer category", type: "string" }, { id: "value", label: "Failure rate", type: "decimal", unit: "%" }], rows: [{ key: "a", values: { category: { type: "string", value: "retail" }, value: { type: "decimal", value: "0" } } }, { key: "b", values: { category: { type: "string", value: "enterprise" }, value: { type: "decimal", value: null, nullReason: "noEligibleDenominator" } } }], coverage: { status: "unknown", description: "History collection cannot attest completeness." }, generatedAt: "2026-11-06T12:00:00Z" });
const mounted: (() => void)[] = [];
function render(content: React.ReactNode) { const container = document.createElement("div"); document.body.append(container); const root = createRoot(container); flushSync(() => root.render(content)); mounted.push(() => { flushSync(() => root.unmount()); container.remove(); }); return container; }
function click(container: HTMLElement, text: string) { const button = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === text); expect(button).toBeDefined(); flushSync(() => button!.click()); }
function changeLimit(container: HTMLElement, value: string) {
  const input = [...container.querySelectorAll("label")].find(label => label.textContent === "Maximum result rows/groups")!.querySelector("input")!;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
  return input;
}
afterEach(() => { mounted.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });

describe("native dashboard authorization and persistence boundary", () => {
  it("sends one quoted strong revision on every saved mutation, preview, execution and drill", async () => {
    const api = stubApi();
    const transport = vi.fn(async (_url: string, _body?: unknown, _init?: unknown) => question());
    api.backend.http.postJson = transport as typeof api.backend.http.postJson;
    api.backend.http.putJson = transport as typeof api.backend.http.putJson;
    const remove = vi.fn(async (_url: string, _init?: unknown) => undefined);
    api.backend.http.deleteJson = remove as typeof api.backend.http.deleteJson;
    const client = new DashboardApi(api.backend); const saved = question();
    await client.save(saved, "key"); await client.copy(saved, "copy", "key");
    await client.share(saved, []); await client.restore(saved, "old");
    await client.savedPreview(saved.id, saved.revision, saved.definition!);
    await client.execute(saved.id, saved.revision); await client.savedDrill(saved, {});
    await client.tile(saved, "tile", "q-revision"); await client.tileDrill(saved, "tile", "q-revision", {});
    await client.remove(saved);
    expect(transport).toHaveBeenCalledTimes(9);
    for (const call of transport.mock.calls) expect(call[2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(remove.mock.calls[0][1]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(() => client.execute(saved.id, '"r1"')).toThrow("saved revision is invalid");
    expect(() => client.execute(saved.id, "r1\r\nInjected: value")).toThrow("saved revision is invalid");
    expect(transport).toHaveBeenCalledTimes(9);
  });

  it("refreshes saved questions and tiles through revision-bound native endpoints without raw preview or provider traffic", async () => {
    const api = stubApi(); const post = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => result()); api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const client = new DashboardApi(api.backend);
    await client.execute("q/1", "revision-7");
    await client.tile({ ...question(), kind: "dashboard", id: "d1", revision: "dashboard-3" }, "tile/2", "question-8");
    expect(post.mock.calls[0]).toEqual(["https://backend.example/_elsa/studio/questions/q%2F1/execute", {}, { headers: { "If-Match": '"revision-7"' }, signal: undefined }]);
    expect(post.mock.calls[1]).toEqual(["https://backend.example/_elsa/studio/dashboards/d1/tiles/tile%2F2/execute", { questionRevision: "question-8" }, { headers: { "If-Match": '"dashboard-3"' }, signal: undefined }]);
    expect(post.mock.calls.every(args => !String(args[0]).includes("preview") && !String(args[0]).includes("weaver"))).toBe(true);
  });

  it("preserves unavailable references by tile identity and revision without sending protected IDs or overrides", async () => {
    const api = stubApi(); const put = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => question()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const tiles = [{ id: "first", questionId: "q1", size: "medium" as const }, { id: "second", questionId: "protected", size: "wide" as const, referenceState: "unavailable" as const, parameters: { privateValue: { type: "string", literal: "private" } } }];
    await new DashboardApi(api.backend).save({ ...question(), kind: "dashboard", tiles }, "key");
    const body = put.mock.calls[0][1] as { tiles: unknown[] };
    expect(body.tiles[1]).toEqual({ id: "second", size: "wide", preserveReference: true });
    expect(JSON.stringify(body)).not.toContain("protected"); expect(JSON.stringify(body)).not.toContain("private");
    expect(put.mock.calls[0][2]).toEqual({ headers: { "If-Match": '"r1"' }, signal: undefined });
  });

  it("keeps authorized metadata editable when the current question query is withheld", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const authoring = createDashboardAuthoringController("metadata-editor");
    const withheld = { ...question(), definition: undefined, definitionState: "withheld" as const };
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [] } : withheld) as typeof api.backend.http.getJson;
    const put = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => ({ ...withheld, name: "Safe metadata update", revision: "r2" }));
    api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={authoring} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => view.textContent?.includes("Metadata-only save preserves its query") === true);
    const name = [...view.querySelectorAll("label")].find(label => label.textContent === "Name")!.querySelector("input")!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(name, "Safe metadata update");
    flushSync(() => name.dispatchEvent(new Event("input", { bubbles: true })));
    click(view, "Save question"); await waitUntil(() => put.mock.calls.length === 1);
    expect(put.mock.calls[0][1]).toEqual({ name: "Safe metadata update", description: "", preserveDefinition: true });
    expect(put.mock.calls[0][2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(view.querySelector(".named-query-controls")).toBeNull(); expect(authoring.snapshots.size).toBe(0);
  });

  it("retains distinct tile instances for the same question", () => {
    const tiles = serializeTiles([{ id: "one", questionId: "q1", size: "small" }, { id: "two", questionId: "q1", size: "full" }]);
    expect(tiles.map(tile => tile.id)).toEqual(["one", "two"]); expect(tiles.map(tile => tile.questionId)).toEqual(["q1", "q1"]);
  });

  it("clears previously displayed values when a native refresh reports lost access", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let denied = false;
    api.backend.http.postJson = vi.fn(async () => { if (denied) throw Object.assign(new Error("Question is unavailable."), { status: 404 }); return result(); }) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    denied = true; click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("Question is unavailable.") === true);
    expect(view.textContent).not.toContain("retail"); expect(view.textContent).not.toContain("enterprise"); expect(view.textContent).not.toContain("Processing by category"); expect(view.querySelector(".named-query-controls")).toBeNull();
  });

  it.each([403, 404])("checks saved-resource authority for a dirty preview and clears protected content after %s", async status => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const authoring = createDashboardAuthoringController("dirty-editor");
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    const post = vi.fn(async (url: string, _body: unknown, _init?: unknown) => {
      // Source access alone remains valid. Only the saved resource rejects the revoked request.
      if (url.endsWith("/questions/q1/preview")) throw Object.assign(new Error("Saved question access was revoked."), { status });
      return result();
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={authoring} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => authoring.snapshots.size === 1);
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    changeLimit(view, "7"); click(view, "Preview question");
    await waitUntil(() => view.textContent?.includes("Current access could not authorize this definition") === true);
    await waitUntil(() => authoring.snapshots.size === 0);
    const request = post.mock.calls[1];
    expect(request[0]).toBe("https://backend.example/_elsa/studio/questions/q1/preview");
    expect(request[1]).toMatchObject({ query: { limit: 7, dataset: { id: dataset.id } }, parameterValues: {} });
    expect(request[2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(post.mock.calls.some(call => call[0].endsWith("/dashboard-queries/preview"))).toBe(false);
    expect(view.querySelector(".named-query-controls")).toBeNull(); expect(view.querySelector(".named-result")).toBeNull();
    expect(view.textContent).not.toContain("retail"); expect(view.textContent).not.toContain("Processing by category");
  });

  it("retains a dirty draft with visible recovery when its exact saved revision changed before preview", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const authoring = createDashboardAuthoringController("dirty-editor");
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    const post = vi.fn(async (url: string, _body: unknown, _init?: unknown) => {
      if (url.endsWith("/questions/q1/preview")) throw Object.assign(new Error("The saved question changed. Reload its current revision."), { status: 412 });
      return result();
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={authoring} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => authoring.snapshots.size === 1);
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    const limit = changeLimit(view, "7"); click(view, "Preview question");
    await waitUntil(() => view.textContent?.includes("The saved question changed. Reload its current revision.") === true);
    expect(post.mock.calls[1][0]).toBe("https://backend.example/_elsa/studio/questions/q1/preview");
    expect(post.mock.calls[1][2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(limit.value).toBe("7"); expect(view.querySelector(".named-query-controls")).not.toBeNull();
    expect(view.textContent).toContain("Reload saved definition"); expect(view.textContent).toContain("Your editable draft remains here");
    expect(view.textContent).toContain("Previous result values are withheld"); expect(view.querySelector(".named-result")).toBeNull(); expect(view.textContent).not.toContain("retail");
    expect([...authoring.snapshots.values()][0]).toMatchObject({ target: { resourceId: "q1", revision: "r1" }, definition: { query: { limit: 7 } } });
  });

  it("keeps a dirty saved preview from drilling through an unbound source request", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi();
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    const post = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => { const value = result(); value.rows[0].drill = { token: "source-membership" }; return value; });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    changeLimit(view, "7"); click(view, "Preview question"); await waitUntil(() => view.textContent?.includes("retail") === true);
    expect(post.mock.calls[0][0]).toBe("https://backend.example/_elsa/studio/questions/q1/preview");
    expect(view.textContent).toContain("Save this question before investigating its changed definition");
    expect([...view.querySelectorAll("button")].some(button => button.textContent?.includes("View matching records"))).toBe(false);
  });

  it.each([404, 422, 503])("previews a new draft through the raw source API and distinguishes source withdrawal from an outage (%s)", async status => {
    window.history.replaceState({}, "", "/questions/new");
    const api = stubApi(); const authoring = createDashboardAuthoringController("new-editor");
    api.backend.http.getJson = vi.fn(async () => ({ items: [dataset] })) as typeof api.backend.http.getJson;
    let fail = false;
    const post = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => {
      if (fail) throw Object.assign(new Error(status === 404 ? "This data source is no longer available under current access." : status === 422 ? "A field or metric is no longer valid under current source support." : "The data source is temporarily unavailable. Retry later."), { status });
      const value = result(); value.rows[0].values.value.value = "41"; return value;
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="create" authoring={authoring} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => Boolean(view.querySelector(".named-query-controls option[value='acme.processing']")));
    const selected = view.querySelector<HTMLSelectElement>(".named-query-controls > label > select")!;
    flushSync(() => { selected.value = dataset.id; selected.dispatchEvent(new Event("change", { bubbles: true })); });
    click(view, "Preview question"); await waitUntil(() => view.textContent?.includes("retail") === true);
    expect(view.querySelector("tbody td:nth-child(2)")?.textContent).toBe("41");
    fail = true; click(view, "Preview question");
    await waitUntil(() => view.textContent?.includes(status === 404 ? "This data source is no longer available" : status === 422 ? "A field or metric is no longer valid" : "The data source is temporarily unavailable") === true);
    expect(post.mock.calls.filter(call => !call[0].endsWith("/validate")).every(call => call[0] === "https://backend.example/_elsa/studio/dashboard-queries/preview")).toBe(true);
    expect(post.mock.calls.filter(call => call[0].endsWith("/validate"))).toHaveLength(status === 422 ? 1 : 0);
    expect(post.mock.calls.every(call => !(call[2] as { headers?: unknown } | undefined)?.headers)).toBe(true);
    if (status === 404) {
      await waitUntil(() => authoring.snapshots.size === 0);
      expect(view.querySelector(".named-result")).toBeNull(); expect(view.querySelector(".named-query-controls")).toBeNull();
      expect(view.textContent).not.toContain("retail"); expect(view.textContent).toContain("Reload current access");
    } else {
      expect(view.querySelector(".named-query-controls")).not.toBeNull(); expect(view.textContent).toContain("Previous result values are withheld");
      expect(view.querySelector(".named-result")).toBeNull(); expect(view.textContent).not.toContain("retail"); expect(authoring.snapshots.size).toBe(1);
      expect(view.textContent).not.toContain("No authorized records matched");
    }
  });

  it("does not let an older cancelled preview replace a newer definition", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let complete!: (value: QueryResult) => void;
    api.backend.http.postJson = vi.fn(() => new Promise<QueryResult>(resolve => { complete = resolve; })) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => Boolean(complete)); click(view, "Cancel query"); complete(result());
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.textContent).not.toContain("retail"); expect(view.textContent).toContain("Query cancelled");
  });

  it.each([403, 404, 422, 503])("withholds the question aggregate after a failed current drill disclosure check (%s)", async status => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const authoring = createDashboardAuthoringController("drill-editor");
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    const post = vi.fn(async (url: string, _body: unknown, _init?: unknown) => {
      if (url.endsWith("/questions/q1/drill")) throw Object.assign(new Error("The current matching records are unavailable."), { status });
      const value = result(); value.rows[0].drill = { token: "current-membership" }; return value;
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={authoring} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => authoring.snapshots.size === 1);
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    click(view, "View matching records");
    await waitUntil(() => view.textContent?.includes("The current matching records are unavailable") === true);
    expect(post.mock.calls[1][0]).toBe("https://backend.example/_elsa/studio/questions/q1/drill");
    expect(post.mock.calls[1][2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
    expect(view.querySelectorAll(".named-result")).toHaveLength(0); expect(view.textContent).not.toContain("retail");
    expect(view.textContent).not.toContain("No authorized records matched");
    if (status === 403 || status === 404) {
      await waitUntil(() => authoring.snapshots.size === 0); expect(view.querySelector(".named-query-controls")).toBeNull();
    } else {
      expect(view.textContent).toContain("Previous result values are withheld");
      expect(changeLimit(view, "7").value).toBe("7"); expect(authoring.snapshots.size).toBe(1);
    }
  });

  it.each(["preview", "drill"] as const)("withholds earlier rows after %s cancellation and ignores a late reply", async mode => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi();
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let complete!: (value: QueryResult) => void; let signal!: AbortSignal; let calls = 0;
    api.backend.http.postJson = vi.fn((_url: string, _body: unknown, init?: unknown) => {
      if (++calls === 1) { const value = result(); value.rows[0].drill = { token: "current-membership" }; return Promise.resolve(value); }
      signal = (init as { signal: AbortSignal }).signal; return new Promise<QueryResult>(resolve => { complete = resolve; });
    }) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    click(view, mode === "preview" ? "Refresh results" : "View matching records"); await waitUntil(() => typeof complete === "function");
    click(view, mode === "preview" ? "Cancel query" : "Close");
    expect(signal.aborted).toBe(true); expect(view.querySelectorAll(".named-result")).toHaveLength(0);
    expect(view.textContent).toContain(mode === "preview" ? "Query cancelled" : "Investigation cancelled");
    expect(view.textContent).toContain("Previous result values are withheld"); expect(view.querySelector(".named-query-controls")).not.toBeNull();
    const late = result(); late.rows[0].values.category.value = "LATE-PRIVATE-ROW"; complete(late);
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.querySelectorAll(".named-result")).toHaveLength(0); expect(view.textContent).not.toContain("LATE-PRIVATE-ROW");
    expect(view.textContent).not.toContain("No authorized records matched");
  });

  it("cancels an overlapping refresh when drill disclosure fails so its late reply cannot restore rows", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi();
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let complete!: (value: QueryResult) => void; let signal!: AbortSignal; let executions = 0;
    api.backend.http.postJson = vi.fn((url: string, _body: unknown, init?: unknown) => {
      if (url.endsWith("/drill")) return Promise.reject(Object.assign(new Error("Current source disclosure could not be established."), { status: 422 }));
      if (++executions === 1) { const value = result(); value.rows[0].drill = { token: "current-membership" }; return Promise.resolve(value); }
      signal = (init as { signal: AbortSignal }).signal; return new Promise<QueryResult>(resolve => { complete = resolve; });
    }) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    click(view, "Refresh results"); await waitUntil(() => typeof complete === "function");
    click(view, "View matching records"); await waitUntil(() => view.textContent?.includes("Current source disclosure could not be established") === true);
    expect(signal.aborted).toBe(true); expect(view.querySelectorAll(".named-result")).toHaveLength(0);
    complete(result()); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.querySelectorAll(".named-result")).toHaveLength(0); expect(view.textContent).not.toContain("retail");
    expect(view.querySelector(".named-query-controls")).not.toBeNull();
  });
});

describe("ordinary query editing and truthful results", () => {
  it("makes all query facets ordinary controls and exposes declared business dimensions without JSON authoring", () => {
    const view = render(<QueryControls definition={initialDefinition(dataset)} datasets={[dataset]} disabled={false} onChange={vi.fn()} />);
    expect(view.querySelector("textarea")).toBeNull();
    for (const label of ["Dataset", "Query form", "Group by", "Metrics", "Source filters", "Time and comparison", "Result conditions", "Sort and limit", "Parameters and accepted defaults", "Presentation", "Customer category"]) expect(view.textContent).toContain(label);
    expect(view.textContent).not.toContain("countDistinct");
  });

  it("distinguishes exact zero, no denominator, unknown coverage and an empty result", () => {
    const populated = render(<ResultView result={result()} visualization={{ kind: "table", y: [] }} />);
    expect(populated.querySelectorAll("tbody tr")).toHaveLength(2); expect(populated.textContent).toContain("No eligible denominator"); expect(populated.textContent).toContain("Coverage: unknown"); expect(populated.querySelector("tbody td:nth-child(2)")?.textContent).toBe("0");
    const empty = render(<ResultView result={{ ...result(), rows: [] }} visualization={{ kind: "table", y: [] }} />);
    expect(empty.textContent).toContain("empty result, not an observed zero");
  });
});
