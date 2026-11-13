import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, AuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardApi } from "../api";
import { createDashboardAuthoringController } from "../authoring";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { QueryControls } from "../QueryControls";
import { NativeDialog, QuestionPickerDialog, ShareDialog } from "../ResourceDialogs";
import { ResultView } from "../ResultView";
import { initialDefinition, type DashboardResource, type DatasetDeclaration, type QueryResult, type QuestionDefinition } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const dataset: DatasetDeclaration = { id: "acme", ownerModuleId: "Acme", schemaRevision: "1", title: "Processing", description: "Declared business data", fields: [{ id: "category", label: "Category", type: "string", filterOperators: ["eq", "isNull"], groupable: true, projectable: true, sortable: false, nullable: true }], metrics: [{ id: "duration", revision: "1", label: "Duration", unit: "s", description: "Elapsed duration" }] };
const resource = (id = "q1"): DashboardResource => ({ id, kind: "question", revision: "r1", name: `Question ${id}`, description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: true, canShare: true, canCopy: true, canDelete: true }, definition: initialDefinition(dataset), definitionState: "available", createdAt: "2026-11-06T12:00:00Z", updatedAt: "2026-11-06T12:00:00Z" });
const disposers: (() => void)[] = [];
function render(children: React.ReactNode) {
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  flushSync(() => root.render(children));
  const dispose = () => { flushSync(() => root.unmount()); container.remove(); };
  disposers.push(dispose); return { container, dispose };
}
function button(container: HTMLElement, text: string) { const target = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === text); expect(target).toBeDefined(); flushSync(() => target!.click()); }
function inputValue(input: HTMLInputElement, value: string) { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value); flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true }))); }
afterEach(() => { disposers.splice(0).forEach(dispose => { try { dispose(); } catch { /* An explicitly closed surface was already unmounted. */ } }); vi.restoreAllMocks(); });

describe("native authoring proposal binding", () => {
  it("validates and applies an exact typed proposal to its editor only, then undoes it without saving", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const native = new DashboardApi(api.backend); const controller = createDashboardAuthoringController("client-a");
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : resource()) as typeof api.backend.http.getJson;
    const post = vi.fn(async () => ({ isValid: true, problems: [] })); api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const put = vi.spyOn(api.backend.http, "putJson");
    const { container } = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={controller} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => controller.snapshots.size === 1);
    const snapshot = [...controller.snapshots.values()][0];
    const definition = { ...snapshot.definition, query: { ...snapshot.definition.query, groupBy: ["category"], parameters: [{ name: "kind", type: "string", required: true, nullable: false, default: { type: "string", literal: "old-default" } }] }, parameterValues: { kind: { type: "string", literal: "retail" } } };
    const outcome = await controller.useProposal(snapshot.generationId, { client: snapshot.client, target: snapshot.target, workingRevision: snapshot.workingRevision, definition, definitionFingerprint: JSON.stringify(definition) });
    expect(outcome.outcome).toBe("applied");
    await waitUntil(() => [...container.querySelectorAll("button")].some(item => item.textContent === "Undo applied proposal"));
    expect(post.mock.calls).toHaveLength(1); expect(put).not.toHaveBeenCalled();
    expect(container.querySelector<HTMLInputElement>('input[value="retail"]')).not.toBeNull();
    button(container, "Undo applied proposal");
    await waitUntil(() => [...controller.snapshots.values()][0].definition.query.groupBy.length === 0);
    expect(put).not.toHaveBeenCalled();
    expect(native.context).toBe(api.backend);
  });

  it("refuses a pending proposal when its exact working revision changes during native validation", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); const controller = createDashboardAuthoringController("client-a");
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : resource()) as typeof api.backend.http.getJson;
    let finish!: (value: { isValid: boolean; problems: never[] }) => void;
    api.backend.http.postJson = vi.fn(() => new Promise(resolve => { finish = resolve; })) as typeof api.backend.http.postJson;
    const { container } = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" authoring={controller} navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => controller.snapshots.size === 1);
    const snapshot = [...controller.snapshots.values()][0];
    const pending = controller.useProposal(snapshot.generationId, { ...snapshot, definition: { ...snapshot.definition, query: { ...snapshot.definition.query, groupBy: ["category"] } } });
    await waitUntil(() => typeof finish === "function");
    const name = [...container.querySelectorAll("label")].find(label => label.textContent === "Name")!.querySelector("input")!;
    inputValue(name, "User changed the draft");
    finish({ isValid: true, problems: [] });
    expect((await pending).outcome).toBe("refused");
    expect([...controller.snapshots.values()][0].definition.query.groupBy).toEqual([]);
  });
});

describe("private draft recovery", () => {
  it("saves a dirty question as an explicitly new private definition and retains its keyed retry", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = { ...stubApi(), dialogs: { prompt: vi.fn(async () => "Independent refined question"), confirm: vi.fn(async () => true), alert: vi.fn(async () => undefined) } }; const navigate = vi.fn();
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : resource()) as typeof api.backend.http.getJson;
    let attempts = 0;
    const post = vi.fn(async (url: string, _body: unknown) => {
      if (url.endsWith("/validate")) return { isValid: true, problems: [] };
      if (url.endsWith("/questions")) { if (++attempts === 1) throw new Error("Connection lost after create attempt."); return { ...resource("private-copy"), name: "Independent refined question" }; }
      throw new Error("Unexpected endpoint");
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const session = { status: "authenticated" as const, subject: "viewer", tenantId: "tenant", roles: [], permissions: ["studio/questions:create"] };
    const manager = { getSession: () => session, initialize: async () => session, getCapabilities: async () => ({ ownershipMode: "foundation-owned" as const, providers: [] }), login: async () => undefined, logout: async () => undefined, handleCallback: async () => session, getAccessToken: async () => null, refresh: async () => session };
    const { container } = render(<AuthProvider manager={manager}><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={navigate} /></AuthProvider>);
    await waitUntil(() => Boolean(container.querySelector('input[value="Question q1"]')));
    const name = [...container.querySelectorAll("label")].find(label => label.textContent === "Name")!.querySelector("input")!;
    inputValue(name, "My unsaved refinement");
    button(container, "Save draft as new question"); await waitUntil(() => container.textContent?.includes("Connection lost after create attempt") === true);
    expect(name.value).toBe("My unsaved refinement");
    button(container, "Save draft as new question"); await waitUntil(() => navigate.mock.calls.length > 0);
    const creates = post.mock.calls.filter(call => call[0].endsWith("/questions"));
    const first = creates[0][1] as { operationKey: string; definition: QuestionDefinition };
    const second = creates[1][1] as { operationKey: string; definition: QuestionDefinition };
    expect(first.operationKey).toBe(second.operationKey); expect(second.definition.query.dataset.id).toBe("acme");
    expect(Object.keys(second)).not.toContain("audience"); expect(Object.keys(second)).not.toContain("ownerId");
    expect(navigate).toHaveBeenCalledWith("/questions/private-copy");
    expect(post.mock.calls.some(call => call[0].endsWith("/copies"))).toBe(false);
  });
});

describe("closed dialogs and bounded paging", () => {
  it("ignores a completed share after its originating dialog closes, even if transport ignores cancellation", async () => {
    const api = new DashboardApi(stubApi().backend); vi.spyOn(api, "audience").mockResolvedValue({ items: [] });
    let finish!: (value: DashboardResource) => void;
    const share = vi.spyOn(api, "share").mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const accepted = vi.fn(); const view = render(<ShareDialog api={api} resource={resource()} onClose={vi.fn()} onSaved={accepted} />);
    await waitUntil(() => view.container.textContent?.includes("Save audience") === true);
    button(view.container, "Save audience"); expect(share).toHaveBeenCalledOnce(); view.dispose(); finish(resource());
    await new Promise(resolve => setTimeout(resolve, 0)); expect(accepted).not.toHaveBeenCalled();
  });

  it("offers continuation after an empty audience page and forwards the same source/search binding", async () => {
    const api = new DashboardApi(stubApi().backend); vi.spyOn(api, "audience").mockResolvedValue({ items: [] });
    const candidates = vi.spyOn(api, "candidates").mockResolvedValueOnce({ items: [], continuationToken: "directory-next" }).mockResolvedValueOnce({ items: [{ kind: "user", id: "canonical-id", name: "A viewer" }] });
    const { container } = render(<ShareDialog api={api} resource={resource()} onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitUntil(() => container.textContent?.includes("Search audience") === true); button(container, "Search audience");
    await waitUntil(() => container.textContent?.includes("More audience entries") === true);
    expect(container.textContent).not.toContain("No authorized audience entries matched"); button(container, "More audience entries");
    await waitUntil(() => container.textContent?.includes("A viewer") === true);
    expect(candidates.mock.calls[1][0]).toBe(""); expect(candidates.mock.calls[1][1]).toBe("user"); expect(candidates.mock.calls[1][3]).toBe("directory-next");
  });

  it("continues both Mine and Shared question streams, retaining every unique question", async () => {
    const api = new DashboardApi(stubApi().backend);
    const list = vi.spyOn(api, "list").mockImplementation(async (_kind, ownership, _search, cursor) => cursor ? { items: [resource("later")] } : ownership === "mine" ? { items: [], continuationToken: "mine-next" } : { items: [resource("shared")] });
    const selected = vi.fn(); const { container } = render(<QuestionPickerDialog api={api} onClose={vi.fn()} onPick={selected} onCreate={vi.fn()} />);
    await waitUntil(() => container.textContent?.includes("Load more questions") === true);
    button(container, "Load more questions"); await waitUntil(() => container.textContent?.includes("Question later") === true);
    expect(list.mock.calls.filter(call => call[3] === "mine-next")).toHaveLength(1); expect(container.textContent).toContain("Question shared");
    button(container, "Add Question later"); expect(selected.mock.calls[0][0].id).toBe("later");
  });

  it("contains keyboard focus, closes with Escape and returns focus to its opener", async () => {
    const opener = document.createElement("button"); document.body.append(opener); opener.focus();
    const closed = vi.fn(); const view = render(<NativeDialog title="Keyboard witness" onClose={closed}><input aria-label="A field" /><button type="button">Last action</button></NativeDialog>);
    await waitUntil(() => view.container.querySelector("button") === document.activeElement);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement?.textContent).toBe("Last action");
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })); expect(closed).toHaveBeenCalledOnce();
    view.dispose(); expect(document.activeElement).toBe(opener); opener.remove();
  });
});

describe("declared editing bounds and chart semantics", () => {
  it("stops predicate nesting at the backend's three levels and preserves null tests without scalar arguments", () => {
    let latest: QuestionDefinition = initialDefinition(dataset);
    function Editor() { const [definition, setDefinition] = useState(latest); return <QueryControls definition={definition} datasets={[dataset]} disabled={false} onChange={value => { latest = value; setDefinition(value); }} />; }
    const { container } = render(<Editor />);
    const filters = [...container.querySelectorAll("fieldset")].find(fieldset => fieldset.querySelector(":scope > legend")?.textContent === "Source filters")!;
    button(filters, "Add condition group");
    expect(filters.querySelectorAll("button").length).toBeGreaterThan(0);
    expect([...filters.querySelectorAll("button")].filter(item => item.textContent === "Add condition group")).toHaveLength(1);
    const operator = [...filters.querySelectorAll("label")].find(label => label.textContent?.startsWith("Operator"))!.querySelector("select")!;
    flushSync(() => { operator.value = "isNull"; operator.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(latest.query.filters?.all?.[0].all?.[0]).toEqual({ field: "category", operator: "isNull", value: undefined, values: undefined });
  });

  it("renders every series, uses signed domains and breaks lines at missing observations", () => {
    const result: QueryResult = { outcome: "succeeded", columns: [{ id: "a", label: "Series A", type: "decimal" }, { id: "b", label: "Series B", type: "decimal" }], rows: [
      { key: "one", values: { a: { type: "decimal", value: "-2" }, b: { type: "decimal", value: "10" } } },
      { key: "two", values: { a: { type: "decimal", value: null, nullReason: "noEligibleValues" }, b: { type: "decimal", value: "15" } } },
      { key: "three", values: { a: { type: "decimal", value: "4" }, b: { type: "decimal", value: "20" } } }
    ] };
    const { container } = render(<ResultView result={result} visualization={{ kind: "line", y: ["a", "b"] }} />);
    expect(container.querySelectorAll("figure[data-series]")).toHaveLength(2);
    expect(container.querySelector('[data-series="a"]')?.querySelectorAll("polyline")).toHaveLength(2);
    for (const point of container.querySelectorAll("circle")) expect(Number(point.getAttribute("cy"))).toBeGreaterThanOrEqual(25);
    expect(container.textContent).toContain("Gaps represent missing values");
    const bars = render(<ResultView result={result} visualization={{ kind: "bar", y: ["a", "b"] }} />).container;
    expect(bars.querySelectorAll('[data-negative="true"]')).toHaveLength(1);
    expect(bars.querySelectorAll('[data-series="a"] .named-bar')).toHaveLength(2);
  });
});
