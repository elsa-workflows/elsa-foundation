import React, { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, StudioWorkspaceTabProvider, reviewStudioNavigation, type ElsaStudioModuleApi, type StudioNavigationIntent, type StudioWorkspaceTabHandle } from "@elsa-workflows/studio-sdk";
import { createDashboardAuthoringController, type DashboardAuthoringController } from "../authoring";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { initialDefinition, type DashboardResource, type DatasetDeclaration, type QueryResult } from "../types";
import { beginNamedDraft, selectResourceAction, stubApi, waitUntil } from "../../__tests__/testSupport";

const dataset: DatasetDeclaration = { id: "acme", ownerModuleId: "Acme", schemaRevision: "1", title: "Processing", description: "Declared business data", fields: [{ id: "category", label: "Category", type: "string", filterOperators: ["eq"], groupable: true, projectable: true, sortable: true, nullable: false }], metrics: [{ id: "duration", revision: "1", label: "Duration", unit: "s", description: "Elapsed duration" }] };
const resource = (): DashboardResource => ({ id: "q1", kind: "question", revision: "r1", name: "Question q1", description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: true, canShare: true, canCopy: true, canDelete: true }, definition: initialDefinition(dataset), definitionState: "available", createdAt: "2026-11-06T12:00:00Z", updatedAt: "2026-11-06T12:00:00Z" });
const result = (): QueryResult => ({ outcome: "succeeded", interpretation: "Current authorized processing data.", columns: [{ id: "category", label: "Category", type: "string" }], rows: [{ key: "private", values: { category: { type: "string", value: "PREVIOUS-PROTECTED-ROW" } } }], coverage: { status: "unknown", description: "Retained records only." } });
const disposers: (() => void)[] = [];

function button(container: HTMLElement, text: string) {
  const action = text === "Preview question" || text === "Refresh results" ? "Preview" : text;
  let target = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === action);
  if (!target && container.querySelector('[aria-label="More question actions"]')) {
    flushSync(() => container.querySelector<HTMLButtonElement>('[aria-label="More question actions"]')!.click());
    target = [...document.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent?.trim() === action);
  }
  expect(target).toBeDefined(); flushSync(() => target!.click());
}
function value(container: HTMLElement, label: string) {
  if ((label === "Name" || label === "Description") && ![...container.querySelectorAll("label")].some(item => item.textContent === label)) button(container, "Properties");
  if (label === "Maximum result rows/groups") {
    const build = container.querySelector<HTMLButtonElement>('.named-question-view-toolbar [data-tab-id="build"]');
    if (build?.getAttribute("aria-selected") !== "true") flushSync(() => build?.click());
    const sort = [...container.querySelectorAll<HTMLAnchorElement>(".named-query-section-nav a")].find(item => item.textContent?.startsWith("Sort"));
    if (sort?.getAttribute("aria-current") !== "page") flushSync(() => sort?.click());
  }
  return ([...container.querySelectorAll("label")].find(item => item.textContent === label)!.control as HTMLInputElement);
}
function edit(input: HTMLInputElement | HTMLTextAreaElement, text: string) {
  const disclosure = input.closest("details");
  if (disclosure && !disclosure.open) flushSync(() => disclosure.querySelector("summary")!.click());
  const prototype = input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, text);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
function navigation(intent: StudioNavigationIntent = "navigate", from = "/questions/q1") {
  return reviewStudioNavigation(from, intent === "probe" ? from : "/dashboards", intent);
}
function unloadAllowed() { return window.dispatchEvent(new Event("beforeunload", { cancelable: true })); }

/** Actual React Activity cleanup/restart, with the same retained tab handle the host supplies. */
function mount(api: ElsaStudioModuleApi, controller: DashboardAuthoringController, view: "detail" | "create" | "edit" = "detail", navigate = vi.fn<(path: string) => void>(), kind: "question" | "dashboard" = "question") {
  const path = view === "detail" ? `/${kind}s/${kind === "question" ? "q1" : "d1"}` : view === "edit" ? "/dashboards/d1/edit" : `/${kind}s/new`;
  window.history.replaceState({}, "", path);
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  let active = true;
  const draw = () => {
    const tab: StudioWorkspaceTabHandle = { id: "question-tab", active, location: path, isActive: () => active, markPendingWork: vi.fn(), setTitle: vi.fn() };
    flushSync(() => root.render(<Activity mode={active ? "visible" : "hidden"}><StudioWorkspaceTabProvider value={tab}><AnonymousAuthProvider><NamedDashboardsRoute api={api} kind={kind} view={view} authoring={controller} navigate={navigate} /></AnonymousAuthProvider></StudioWorkspaceTabProvider></Activity>));
  };
  draw();
  disposers.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return {
    container: document.body, navigate,
    hide() { active = false; window.history.replaceState({}, "", "/dashboards"); draw(); },
    reveal() { active = true; window.history.replaceState({}, "", path); draw(); }
  };
}
function fixture() {
  const api = { ...stubApi(), dialogs: { prompt: vi.fn(async () => "Private copy"), confirm: vi.fn(async () => true), alert: vi.fn(async () => undefined) } };
  const controller = createDashboardAuthoringController("navigation-editor");
  api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : resource()) as typeof api.backend.http.getJson;
  api.backend.http.postJson = vi.fn(async (url: string) => url.endsWith("/validate") ? { isValid: true, problems: [] } : result()) as typeof api.backend.http.postJson;
  return { api, controller };
}
function dashboard(): DashboardResource {
  return { ...resource(), id: "d1", kind: "dashboard", name: "Operations", description: "Current operations", definition: undefined, tiles: [{ id: "tile-1", questionId: "q1", size: "medium" }] };
}
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });

describe("dashboard viewer and editor routes", () => {
  function dashboardFixture(current = dashboard()) {
    const { api, controller } = fixture();
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("/dashboards/") ? current : resource()) as typeof api.backend.http.getJson;
    return { api, controller };
  }

  it("opens saved dashboards as viewers with visible results and no layout controls", async () => {
    const { api, controller } = dashboardFixture(); const navigate = vi.fn();
    const viewer = mount(api, controller, "detail", navigate, "dashboard");
    await waitUntil(() => viewer.container.querySelector(".named-result tbody tr") !== null);
    expect(viewer.container.querySelector("input")).toBeNull();
    expect(viewer.container.textContent).toContain("Current operations");
    expect(viewer.container.textContent).toContain("Question q1");
    expect(viewer.container.querySelector('button[aria-label="Dashboard actions"]')).toBeTruthy();
    expect([...viewer.container.querySelectorAll("button")].map(item => item.textContent?.trim())).toContain("Edit dashboard");
    expect([...viewer.container.querySelectorAll("button")].some(item => item.textContent?.includes("Move earlier"))).toBe(false);
    expect([...viewer.container.querySelectorAll("select")]).toHaveLength(0);
    button(viewer.container, "Edit dashboard"); expect(navigate).toHaveBeenCalledWith("/dashboards/d1/edit");
  });

  it("shows a useful empty state for dashboards without saved questions", async () => {
    const empty = dashboard(); empty.tiles = [];
    const { api, controller } = dashboardFixture(empty);
    const viewer = mount(api, controller, "detail", vi.fn(), "dashboard");
    await waitUntil(() => viewer.container.querySelector("h2")?.textContent === "Operations");
    expect(viewer.container.textContent).toContain("No questions on this dashboard");
    expect(viewer.container.querySelector(".named-result")).toBeNull();
    expect(viewer.container.querySelector(".named-definition-meta")).toBeNull();
  });

  it("does not grant editor controls through a direct edit URL without current edit access", async () => {
    const readOnly = dashboard(); readOnly.capabilities = { ...readOnly.capabilities, canEdit: false };
    const { api, controller } = dashboardFixture(readOnly);
    const editorUrl = mount(api, controller, "edit", vi.fn(), "dashboard");
    await waitUntil(() => editorUrl.container.querySelector(".named-result tbody tr") !== null);
    expect(editorUrl.container.querySelector("input")).toBeNull();
    expect(editorUrl.container.textContent).toContain("Current operations");
    expect(editorUrl.container.textContent).not.toContain("Edit dashboard");
    expect(editorUrl.container.textContent).not.toContain("Add saved question");
    expect(editorUrl.container.querySelector(".named-result")).not.toBeNull();
  });

  it.each([false, true])("uses one host confirmation when Cancel leaves the editor (accept=%s)", async accept => {
    const { api, controller } = dashboardFixture(); const moved: string[] = [];
    const navigate = vi.fn(async (path: string) => { if (await reviewStudioNavigation("/dashboards/d1/edit", path)) moved.push(path); });
    const confirm = vi.spyOn(api.dialogs, "confirm").mockResolvedValue(accept);
    const editor = mount(api, controller, "edit", navigate, "dashboard");
    await waitUntil(() => editor.container.querySelector(".named-definition-meta input") !== null);
    edit(value(editor.container, "Name"), "Unsaved operations"); button(editor.container, "Cancel");
    await waitUntil(() => confirm.mock.calls.length === 1);
    if (accept) await waitUntil(() => moved.length === 1);
    expect(moved).toEqual(accept ? ["/dashboards/d1"] : []);
    if (!accept) expect(value(editor.container, "Name").value).toBe("Unsaved operations");
  });

  it("saves editor changes with the current revision and returns to the saved viewer", async () => {
    const { api, controller } = dashboardFixture(); const navigate = vi.fn();
    const put = vi.fn(async (_url: string, input: { description: string }) => ({ ...dashboard(), revision: "r2", description: input.description }));
    api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller, "edit", navigate, "dashboard");
    await waitUntil(() => editor.container.querySelector("textarea") !== null);
    edit(editor.container.querySelector<HTMLTextAreaElement>("textarea")!, "Saved operations summary");
    button(editor.container, "Save"); await waitUntil(() => put.mock.calls.length === 1);
    await waitUntil(() => navigate.mock.calls.length === 1);
    expect(put.mock.calls[0][1].description).toBe("Saved operations summary");
    expect(navigate).toHaveBeenCalledWith("/dashboards/d1");
    expect(api.backend.http.postJson).not.toHaveBeenCalled();
  });
});

describe("native editor host navigation guards", () => {
  it("allows a clean saved definition to navigate, close, probe and unload", async () => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    for (const intent of ["navigate", "close", "probe"] as const) expect(navigation(intent)).toBe(true);
    expect(unloadAllowed()).toBe(true); expect(confirm).not.toHaveBeenCalled();
  });

  it.each(["navigate", "close"] as const)("reports dirty work to a noninteractive probe and confirms a %s request", async intent => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(api.dialogs, "confirm").mockResolvedValue(false);
    const { container } = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(container, "Maximum result rows/groups"), "7");
    expect(navigation("probe")).toBe(false); expect(unloadAllowed()).toBe(false); expect(confirm).not.toHaveBeenCalled();
    expect(await navigation(intent)).toBe(false); expect(confirm).toHaveBeenCalledTimes(1);
    expect(value(container, "Maximum result rows/groups").value).toBe("7");
    confirm.mockResolvedValue(true); expect(await navigation(intent)).toBe(true); expect(confirm).toHaveBeenCalledTimes(2);
  });

  it.each(["edit", "hide"] as const)("rejects a stale discard decision after a new %s", async change => {
    const { api, controller } = fixture();
    let decide!: (allowed: boolean) => void;
    vi.spyOn(api.dialogs, "confirm").mockImplementation(() => new Promise(resolve => { decide = resolve; }));
    const editor = mount(api, controller);
    await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Name"), "First edit");
    const result = navigation();
    if (change === "hide") editor.hide();
    else edit(value(editor.container, "Name"), "Newer edit");
    decide(true);
    expect(await result).toBe(false);
  });

  it("uses the host guard once for the native Back action instead of asking two discard questions", async () => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(api.dialogs, "confirm").mockResolvedValue(false);
    const moved: string[] = [];
    const go = vi.fn(async (path: string) => { if (await reviewStudioNavigation("/questions/q1", path)) moved.push(path); });
    const { container } = mount(api, controller, "detail", go); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(container, "Maximum result rows/groups"), "7"); button(container, "Back to questions");
    await waitUntil(() => confirm.mock.calls.length === 1);
    expect(moved).toEqual([]);
    confirm.mockResolvedValue(true); button(container, "Back to questions");
    await waitUntil(() => moved.length === 1); expect(moved).toEqual(["/questions"]); expect(confirm).toHaveBeenCalledTimes(2);
  });

  it("blocks pending keyed creation without prompting and retries the same authored operation key", async () => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    let fail!: (error: Error) => void; let attempts = 0;
    const post = vi.fn((url: string, body: unknown) => {
      if (!url.endsWith("/questions")) return Promise.resolve({ isValid: true, problems: [] });
      if (++attempts === 1) return new Promise<DashboardResource>((_resolve, reject) => { fail = reject; });
      const input = body as { name: string; definition: DashboardResource["definition"] };
      return Promise.resolve({ ...resource(), id: "created-question", name: input.name, definition: input.definition });
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const { container, navigate } = mount(api, controller, "create");
    await beginNamedDraft("Native question");
    await waitUntil(() => Boolean(container.querySelector(".named-query-controls option[value='acme']")));
    expect(container.querySelector<HTMLButtonElement>('[data-tab-id="preview"]')?.disabled).toBe(true);
    expect(container.textContent).not.toContain("Query form");
    expect([...container.querySelectorAll("label")].some(item => item.textContent === "Description")).toBe(false);
    edit(value(container, "Name"), "An independent native question");
    const datasetSelect = container.querySelector<HTMLSelectElement>(".named-query-controls [data-query-path='query.dataset'] select")!;
    flushSync(() => { datasetSelect.value = "acme"; datasetSelect.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(container.querySelector<HTMLButtonElement>('[data-tab-id="preview"]')?.disabled).toBe(false);
    expect(post.mock.calls.some(call => call[0].endsWith("/preview"))).toBe(false);
    button(container, "Save question"); await waitUntil(() => typeof fail === "function");
    for (const intent of ["navigate", "close", "probe"] as const) expect(navigation(intent, "/questions/new")).toBe(false);
    expect(unloadAllowed()).toBe(false); expect(confirm).not.toHaveBeenCalled();
    fail(new Error("The keyed save response was lost. Retry the same operation."));
    await waitUntil(() => container.textContent?.includes("keyed save response was lost") === true);
    expect(value(container, "Name").value).toBe("An independent native question"); expect(navigation("probe", "/questions/new")).toBe(false);
    button(container, "Save question"); await waitUntil(() => navigate.mock.calls.length === 1);
    const creates = post.mock.calls.filter(call => call[0].endsWith("/questions"));
    expect(creates).toHaveLength(2);
    const initial = creates[0][1] as { operationKey: string }; const retry = creates[1][1] as { operationKey: string };
    expect(initial.operationKey).toBeTruthy(); expect(retry.operationKey).toBe(initial.operationKey);
    await waitUntil(() => navigation("probe", "/questions/new") === true); expect(confirm).not.toHaveBeenCalled();
  });
});

describe("settled native mutations", () => {
  it.each(["Save", "Delete"] as const)("releases navigation after a settled %s loses current access", async action => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const denied = () => Promise.reject(Object.assign(new Error("Current resource access was denied."), { status: 403 }));
    api.backend.http.putJson = vi.fn(denied) as typeof api.backend.http.putJson;
    api.backend.http.deleteJson = vi.fn(denied) as typeof api.backend.http.deleteJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    if (action === "Save") edit(value(editor.container, "Name"), "Unsaved before access loss");
    button(editor.container, `${action} question`);
    await waitUntil(() => editor.container.textContent?.includes("Current access could not authorize this definition") === true);
    await waitUntil(() => navigation("probe") === true);
    expect(unloadAllowed()).toBe(true); expect(navigation("navigate")).toBe(true); expect(navigation("close")).toBe(true);
    expect(confirm).not.toHaveBeenCalled(); await waitUntil(() => controller.snapshots.size === 0);
    expect(editor.container.querySelector(".named-query-controls")).toBeNull();
  });
});

describe("retained native editor Activity lifecycle", () => {
  it("withholds a new draft's earlier preview after reveal while keeping its authored definition", async () => {
    const { api, controller } = fixture();
    const post = vi.fn(async () => result()); api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const editor = mount(api, controller, "create");
    await beginNamedDraft("Native question");
    await waitUntil(() => Boolean(editor.container.querySelector(".named-query-controls option[value='acme']")));
    edit(value(editor.container, "Name"), "Keep this new local question");
    const select = editor.container.querySelector<HTMLSelectElement>(".named-query-controls [data-query-path='query.dataset'] select")!;
    flushSync(() => { select.value = "acme"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    edit(value(editor.container, "Maximum result rows/groups"), "7");
    button(editor.container, "Preview question"); await waitUntil(() => editor.container.textContent?.includes("PREVIOUS-PROTECTED-ROW") === true);
    const origin = [...controller.snapshots.values()][0];
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0); editor.reveal();
    await waitUntil(() => controller.snapshots.size === 1);
    expect(editor.container.querySelector(".named-result")).toBeNull(); expect(editor.container.textContent).not.toContain("PREVIOUS-PROTECTED-ROW");
    expect(value(editor.container, "Name").value).toBe("Keep this new local question"); expect(value(editor.container, "Maximum result rows/groups").value).toBe("7");
    expect([...controller.snapshots.values()][0]).toMatchObject({ generationId: origin.generationId, workingRevision: origin.workingRevision, definition: origin.definition });
    expect(post).toHaveBeenCalledTimes(1);
  });

  it.each(["failure", "success"] as const)("settles a parked native Delete without changing another tab or leaving guards stuck (%s)", async outcome => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    let complete!: () => void; let fail!: (error: Error) => void; let signal!: AbortSignal; let deleted = false;
    api.backend.http.getJson = vi.fn(async (url: string) => {
      if (url.includes("dashboard-datasets")) return { items: [dataset] };
      if (deleted) throw Object.assign(new Error("The deleted question is unavailable."), { status: 404 });
      return resource();
    }) as typeof api.backend.http.getJson;
    api.backend.http.deleteJson = vi.fn((_url: string, options?: unknown) => {
      signal = (options as { signal: AbortSignal }).signal;
      return new Promise<void>((resolve, reject) => { complete = resolve; fail = reject; });
    }) as typeof api.backend.http.deleteJson;
    const go = vi.fn((path: string) => { window.history.pushState({}, "", path); });
    const editor = mount(api, controller, "detail", go); await waitUntil(() => controller.snapshots.size === 1);
    expect(navigation("probe")).toBe(true);
    button(editor.container, "Delete question"); await waitUntil(() => typeof complete === "function");
    expect(navigation("probe")).toBe(false); expect(confirm).not.toHaveBeenCalled();
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0); expect(signal.aborted).toBe(false);
    if (outcome === "failure") fail(new Error("The delete request could not complete. Retry or continue editing."));
    else { deleted = true; complete(); }
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(go).not.toHaveBeenCalled(); expect(window.location.pathname).toBe("/dashboards");
    editor.reveal();
    if (outcome === "failure") {
      await waitUntil(() => controller.snapshots.size === 1);
      expect(editor.container.textContent).toContain("delete request could not complete");
      const save = [...editor.container.querySelectorAll("button")].find(item => item.textContent === "Save question")!;
      expect(save.disabled).toBe(false); expect(navigation("probe")).toBe(true); expect(unloadAllowed()).toBe(true); expect(go).not.toHaveBeenCalled();
    } else {
      await waitUntil(() => go.mock.calls.length === 1); expect(go).toHaveBeenCalledWith("/questions");
      expect(window.location.pathname).toBe("/questions"); expect(editor.container.textContent).not.toContain("PREVIOUS-PROTECTED-ROW");
    }
    expect(confirm).not.toHaveBeenCalled();
  });

  it("keeps an in-flight native Create parked and navigates only its own tab once it returns", async () => {
    const { api, controller } = fixture(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    let finish!: (value: DashboardResource) => void; let signal!: AbortSignal;
    let created!: DashboardResource;
    const post = vi.fn((url: string, body: unknown, options?: unknown) => {
      if (!url.endsWith("/questions")) return Promise.resolve({ isValid: true, problems: [] });
      const input = body as { name: string; definition: DashboardResource["definition"] };
      created = { ...resource(), id: "parked-create", name: input.name, definition: input.definition };
      signal = (options as { signal: AbortSignal }).signal;
      return new Promise<DashboardResource>(resolve => { finish = resolve; });
    });
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const go = vi.fn((path: string) => { window.history.pushState({}, "", path); });
    const editor = mount(api, controller, "create", go);
    await beginNamedDraft("Native question");
    await waitUntil(() => Boolean(editor.container.querySelector(".named-query-controls option[value='acme']")));
    edit(value(editor.container, "Name"), "Created while this tab is parked");
    const select = editor.container.querySelector<HTMLSelectElement>(".named-query-controls [data-query-path='query.dataset'] select")!;
    flushSync(() => { select.value = "acme"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    button(editor.container, "Save question"); await waitUntil(() => typeof finish === "function");
    expect(navigation("probe", "/questions/new")).toBe(false); expect(confirm).not.toHaveBeenCalled();
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0);
    expect(signal.aborted).toBe(false);
    finish(created); await new Promise(resolve => setTimeout(resolve, 20));
    expect(go).not.toHaveBeenCalled(); expect(window.location.pathname).toBe("/dashboards");
    expect(post.mock.calls.filter(call => call[0].endsWith("/questions"))).toHaveLength(1);
    editor.reveal(); await waitUntil(() => go.mock.calls.length === 1);
    expect(go).toHaveBeenCalledWith("/questions/parked-create"); expect(window.location.pathname).toBe("/questions/parked-create");
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(go).toHaveBeenCalledTimes(1); expect(signal.aborted).toBe(false); expect(confirm).not.toHaveBeenCalled();
  });

  it("keeps a local edit and proposal Undo across hide/reveal while rechecking current access", async () => {
    const { api, controller } = fixture();
    let holdRead = false; let revalidated!: (value: DashboardResource) => void;
    api.backend.http.getJson = vi.fn((url: string) => {
      if (url.includes("dashboard-datasets")) return Promise.resolve({ items: [dataset] });
      if (holdRead) return new Promise<DashboardResource>(resolve => { revalidated = resolve; });
      return Promise.resolve(resource());
    }) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    const limit = value(editor.container, "Maximum result rows/groups"); edit(limit, "7");
    const origin = [...controller.snapshots.values()][0];
    const definition = { ...origin.definition, query: { ...origin.definition.query, groupBy: ["category"] } };
    const applied = await controller.useProposal(origin.generationId, { ...origin, definition });
    expect(applied.outcome).toBe("applied");
    await waitUntil(() => editor.container.textContent?.includes("Undo applied proposal") === true);
    button(editor.container, "Preview question"); await waitUntil(() => editor.container.textContent?.includes("PREVIOUS-PROTECTED-ROW") === true);
    const before = [...controller.snapshots.values()][0];
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0);
    // The next access read is held, so assertions distinguish retained state from remounting.
    holdRead = true; editor.reveal(); await waitUntil(() => typeof revalidated === "function");
    expect(editor.container.textContent).not.toContain("PREVIOUS-PROTECTED-ROW");
    expect(controller.snapshots.size).toBe(0);
    revalidated(resource()); await waitUntil(() => controller.snapshots.size === 1);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7"); expect(editor.container.textContent).toContain("Undo applied proposal");
    expect([...controller.snapshots.values()][0].generationId).toBe(before.generationId);
    expect([...controller.snapshots.values()][0].workingRevision).toBe(before.workingRevision);
    button(editor.container, "Undo applied proposal");
    await waitUntil(() => [...controller.snapshots.values()][0].definition.query.groupBy.length === 0);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7");
  });

  it.each([403, 404, "withheld"] as const)("clears protected state when fresh access on reveal refuses the saved question (%s)", async refusal => {
    const { api, controller } = fixture(); let denied = false;
    api.backend.http.getJson = vi.fn(async (url: string) => {
      if (url.includes("dashboard-datasets")) return { items: [dataset] };
      if (!denied) return resource();
      if (refusal === "withheld") return { ...resource(), definition: undefined, definitionState: "withheld" };
      throw Object.assign(new Error("Saved question access is no longer available."), { status: refusal });
    }) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7");
    button(editor.container, "Preview question"); await waitUntil(() => editor.container.textContent?.includes("PREVIOUS-PROTECTED-ROW") === true);
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0); denied = true; editor.reveal();
    await waitUntil(() => /unavailable|withheld|authorize/.test(editor.container.textContent?.toLowerCase() ?? ""));
    expect(controller.snapshots.size).toBe(0); expect(editor.container.querySelector(".named-query-controls")).toBeNull(); expect(editor.container.querySelector(".named-result")).toBeNull();
    expect(editor.container.textContent).not.toContain("PREVIOUS-PROTECTED-ROW");
    expect(editor.container.textContent?.toLowerCase()).toMatch(/unavailable|withheld|authorize/);
  });

  it("reconciles a sharing downgrade while retaining a dirty draft with a newer saved revision", async () => {
    const { api, controller } = fixture(); let downgraded = false;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : downgraded ? {
      ...resource(), revision: "r2", capabilities: { canRead: true, canEdit: false, canShare: false, canDelete: false, canCopy: true }
    } : resource()) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7");
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0); downgraded = true; editor.reveal();
    await waitUntil(() => editor.container.textContent?.includes("The saved definition changed while this tab was away") === true);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7");
    expect(editor.container.textContent).toContain("Read-only definition");
    expect(editor.container.querySelector<HTMLFieldSetElement>(".named-query-controls")!.disabled).toBe(true);
    for (const action of ["Save question", "Share", "Delete question"]) expect([...editor.container.querySelectorAll("button")].some(item => item.textContent === action)).toBe(false);
    expect(controller.snapshots.size).toBe(0);
  });

  it("retains a dirty draft across a concurrent saved revision and requires visible reconciliation", async () => {
    const { api, controller } = fixture(); let changed = false;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : changed ? { ...resource(), revision: "r2", description: "Concurrent saved edit" } : resource()) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    const limit = value(editor.container, "Maximum result rows/groups"); edit(limit, "7");
    button(editor.container, "Preview question"); await waitUntil(() => editor.container.textContent?.includes("PREVIOUS-PROTECTED-ROW") === true);
    editor.hide(); await waitUntil(() => controller.snapshots.size === 0); changed = true; editor.reveal();
    await waitUntil(() => editor.container.textContent?.toLowerCase().includes("changed") === true);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7");
    expect(editor.container.textContent).not.toContain("PREVIOUS-PROTECTED-ROW");
    expect(editor.container.textContent?.toLowerCase()).toMatch(/reload|reconcile|review/);
    expect([...editor.container.querySelectorAll("button")].some(item => item.textContent?.includes("Reload"))).toBe(true);
  });
});


describe("current native disclosure and reuse review", () => {
  it("reconciles revoked tile bindings at the same dashboard revision while preserving local layout edits", async () => {
    const { api, controller } = fixture(); let revoked = false;
    const dashboard = (): DashboardResource => ({ ...resource(), id: "d1", kind: "dashboard", definition: undefined, name: "Working dashboard", tiles: revoked ? [
      { id: "tile-1", size: "medium", referenceState: "unavailable" }
    ] : [{ id: "tile-1", questionId: "q1", size: "medium", referenceState: "available", parameters: { privateCategory: { type: "string", literal: "OLD-PROTECTED-OVERRIDE" } } }] });
    api.backend.http.getJson = vi.fn(async (url: string) => {
      if (url.includes("/dashboards/")) return dashboard();
      if (revoked) throw Object.assign(new Error("Question access was revoked."), { status: 404 });
      return resource();
    }) as typeof api.backend.http.getJson;
    const put = vi.fn(async (_url: string, input: unknown) => ({ ...dashboard(), name: (input as { name: string }).name, revision: "r2" }));
    api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller, "edit", vi.fn(), "dashboard");
    await waitUntil(() => editor.container.textContent?.includes("Working dashboard") === true);
    edit(value(editor.container, "Name"), "Keep my local dashboard name");
    const size = ([...editor.container.querySelectorAll("label")].find(item => item.textContent?.startsWith("Size for question 1"))!.control as HTMLSelectElement);
    flushSync(() => { size.value = "wide"; size.dispatchEvent(new Event("change", { bubbles: true })); });
    editor.hide(); revoked = true; editor.reveal();
    await waitUntil(() => editor.container.textContent?.includes("Its reference is preserved during layout saves") === true);
    expect(value(editor.container, "Name").value).toBe("Keep my local dashboard name");
    expect(editor.container.textContent).not.toContain("Settings for question 1");
    button(editor.container, "Save"); await waitUntil(() => put.mock.calls.length === 1);
    expect(put.mock.calls[0][1]).toMatchObject({ name: "Keep my local dashboard name", tiles: [{ id: "tile-1", size: "wide", preserveReference: true }] });
    expect(JSON.stringify(put.mock.calls[0][1])).not.toContain("q1"); expect(JSON.stringify(put.mock.calls[0][1])).not.toContain("OLD-PROTECTED-OVERRIDE");
  });

  it.each([false, true])("restores current question disclosure at the same revision while preserving local metadata (dirty=%s)", async dirty => {
    const { api, controller } = fixture(); let disclosed = false;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : disclosed ? resource() : {
      ...resource(), definition: undefined, definitionState: "withheld", capabilities: { ...resource().capabilities, canCopy: false }
    }) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => editor.container.textContent?.includes("Parts of this definition are withheld") === true);
    if (dirty) edit(value(editor.container, "Name"), "Keep permitted metadata");
    editor.hide(); disclosed = true; editor.reveal();
    await waitUntil(() => controller.snapshots.size === 1);
    expect(editor.container.querySelector(".named-query-controls")).not.toBeNull();
    expect(editor.container.textContent).not.toContain("Parts of this definition are withheld");
    expect(value(editor.container, "Name").value).toBe(dirty ? "Keep permitted metadata" : "Question q1");
    expect(navigation("probe")).toBe(!dirty);
  });

  it("fetches current readable question uses before save without changing its draft or CAS revision", async () => {
    const { api, controller } = fixture(); let reused = false;
    const confirm = vi.fn(async () => false); api.dialogs.confirm = confirm;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : {
      ...resource(), uses: reused ? [{ id: "d-current", name: "Recently linked dashboard", tileCount: 2 }] : []
    }) as typeof api.backend.http.getJson;
    const put = vi.fn(async (_url: string, _body: unknown, _options?: unknown) => resource()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7"); reused = true;
    button(editor.container, "Save question");
    await waitUntil(() => confirm.mock.calls.length === 1);
    expect((confirm.mock.calls[0] as unknown[])[0]).toMatchObject({ title: "Update reused question", message: expect.stringContaining("Recently linked dashboard") });
    expect(editor.container.textContent).toContain("Recently linked dashboard");
    expect(editor.container.textContent).toContain("Save draft as new question");
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7"); expect(put).not.toHaveBeenCalled();
    expect(editor.container.textContent).toContain("Unsaved changes");
    confirm.mockResolvedValue(true); button(editor.container, "Save question");
    await waitUntil(() => put.mock.calls.length === 1);
    expect(put.mock.calls[0][2]).toMatchObject({ headers: { "If-Match": '"r1"' } });
  });

  it("clears protected question state when the save usage review loses current resource access", async () => {
    const { api, controller } = fixture(); let revoked = false;
    api.backend.http.getJson = vi.fn(async (url: string) => {
      if (url.includes("dashboard-datasets")) return { items: [dataset] };
      if (revoked) throw Object.assign(new Error("The question is no longer readable."), { status: 404 });
      return resource();
    }) as typeof api.backend.http.getJson;
    const put = vi.fn(async () => resource()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7"); revoked = true; button(editor.container, "Save question");
    await waitUntil(() => editor.container.textContent?.includes("Current access could not authorize this definition") === true);
    expect(editor.container.querySelector(".named-query-controls")).toBeNull(); expect(put).not.toHaveBeenCalled(); await waitUntil(() => controller.snapshots.size === 0);
  });

  it.each([false, true])("restores a same-revision dashboard tile without losing permitted local sizes (dirty=%s)", async dirty => {
    const { api, controller } = fixture(); let disclosed = false;
    const dashboard = (): DashboardResource => ({ ...resource(), id: "d1", kind: "dashboard", name: "Restored dashboard", definition: undefined,
      tiles: disclosed ? [{ id: "tile-1", questionId: "q1", size: "medium", referenceState: "available" }] :
        JSON.parse('[{"id":"tile-1","questionId":null,"size":"medium","referenceState":"unavailable","presentation":null,"parameters":null}]') });
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("/dashboards/") ? dashboard() : resource()) as typeof api.backend.http.getJson;
    const editor = mount(api, controller, "edit", vi.fn(), "dashboard");
    await waitUntil(() => editor.container.textContent?.includes("Its reference is preserved during layout saves") === true);
    if (dirty) {
      const select = ([...editor.container.querySelectorAll("label")].find(item => item.textContent?.startsWith("Size for question 1"))!.control as HTMLSelectElement);
      flushSync(() => { select.value = "wide"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    }
    editor.hide(); disclosed = true; editor.reveal();
    await waitUntil(() => editor.container.textContent?.includes("Settings for question 1") === true);
    const select = ([...editor.container.querySelectorAll("label")].find(item => item.textContent?.startsWith("Size for question 1"))!.control as HTMLSelectElement);
    expect(select.value).toBe(dirty ? "wide" : "medium");
    expect(reviewStudioNavigation("/dashboards/d1/edit", "/dashboards/d1/edit", "probe")).toBe(!dirty);
  });

  it("keeps an explicit local replacement when original question disclosure is restored", async () => {
    const { api, controller } = fixture(); let disclosed = false;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : disclosed ? resource() : {
      ...resource(), definition: undefined, definitionState: "withheld", capabilities: { ...resource().capabilities, canCopy: false }
    }) as typeof api.backend.http.getJson;
    const editor = mount(api, controller); await waitUntil(() => editor.container.textContent?.includes("Parts of this definition are withheld") === true);
    button(editor.container, "Replace unavailable query"); await waitUntil(() => editor.container.querySelector(".named-query-controls") !== null);
    const select = editor.container.querySelector<HTMLSelectElement>(".named-query-controls [data-query-path='query.dataset'] select")!;
    flushSync(() => { select.value = "acme"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    edit(value(editor.container, "Maximum result rows/groups"), "9");
    editor.hide(); disclosed = true; editor.reveal(); await waitUntil(() => controller.snapshots.size === 1);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("9"); expect(navigation("probe")).toBe(false);
  });

  it("preserves the local question and original CAS baseline when its save review sees a newer revision", async () => {
    const { api, controller } = fixture(); let changed = false;
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : { ...resource(), revision: changed ? "r2" : "r1", uses: [] }) as typeof api.backend.http.getJson;
    const put = vi.fn(async () => resource()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7"); changed = true; button(editor.container, "Save question");
    await waitUntil(() => editor.container.textContent?.includes("original revision remain here") === true);
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7"); expect(editor.container.textContent).toContain("Unsaved changes"); expect(put).not.toHaveBeenCalled();
  });

  it("ignores an aborted usage-review reply after a real Activity hide and reveal", async () => {
    const { api, controller } = fixture(); let hold = false; let resolveReview!: (resource: DashboardResource) => void;
    api.backend.http.getJson = vi.fn((url: string) => {
      if (url.includes("dashboard-datasets")) return Promise.resolve({ items: [dataset] });
      if (hold) { hold = false; return new Promise<DashboardResource>(resolve => { resolveReview = resolve; }); }
      return Promise.resolve({ ...resource(), uses: [] });
    }) as typeof api.backend.http.getJson;
    const put = vi.fn(async () => resource()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const editor = mount(api, controller); await waitUntil(() => controller.snapshots.size === 1);
    edit(value(editor.container, "Maximum result rows/groups"), "7"); hold = true; button(editor.container, "Save question");
    await waitUntil(() => typeof resolveReview === "function"); editor.hide(); await waitUntil(() => controller.snapshots.size === 0); editor.reveal();
    await waitUntil(() => controller.snapshots.size === 1);
    resolveReview({ ...resource(), uses: [{ id: "stale-dashboard", name: "STALE-REVIEW-REPLY", tileCount: 1 }] });
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(editor.container.textContent).not.toContain("STALE-REVIEW-REPLY"); expect(api.dialogs.confirm).not.toHaveBeenCalled(); expect(put).not.toHaveBeenCalled();
    expect(value(editor.container, "Maximum result rows/groups").value).toBe("7");
  });


  it("reviews retained dashboard references and copies a question independently from its viewer", async () => {
    const { api, controller } = fixture();
    const dashboard: DashboardResource = { ...resource(), id: "d1", kind: "dashboard", name: "Original dashboard", definition: undefined, tiles: [{ id: "tile-1", questionId: "q1", size: "medium", referenceState: "available" }] };
    api.backend.http.getJson = vi.fn(async (url: string) => url.includes("/audience") ? { items: [] } : url.includes("/dashboards/") ? dashboard : resource()) as typeof api.backend.http.getJson;
    const post = vi.fn(async (url: string) => url.includes("/questions/q1/copies") ? { ...resource(), id: "q-private", name: "Question q1 copy" } : url.includes("/dashboards/d1/copies") ? { ...dashboard, id: "d-private", name: "Private copy" } : result());
    api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const navigate = vi.fn(); const editor = mount(api, controller, "detail", navigate, "dashboard");
    await waitUntil(() => editor.container.textContent?.includes("Original dashboard") === true);
    selectResourceAction(editor.container, "Dashboard actions", "Make a copy");
    await waitUntil(() => editor.container.textContent?.includes("Review dashboard copy dependencies") === true);
    expect(api.dialogs.prompt).not.toHaveBeenCalled();
    await waitUntil(() => editor.container.textContent?.includes("Copy Question q1 as an independent question") === true);
    button(editor.container, "Copy Question q1 as an independent question"); button(editor.container, "Create independent private question");
    await waitUntil(() => editor.container.textContent?.includes("Private question created") === true);
    expect(editor.container.textContent).toContain("Original dashboard"); expect(editor.container.querySelector(".named-definition-meta")).toBeNull(); expect(navigate).not.toHaveBeenCalled();
    expect(post.mock.calls.filter(call => call[0].includes("/dashboards/d1/copies"))).toHaveLength(0);
    expect(editor.container.querySelector('a[href="/questions/q-private"]')).not.toBeNull();
    button(editor.container, "Continue with retained references"); await waitUntil(() => navigate.mock.calls.length === 1);
    expect(api.dialogs.prompt).toHaveBeenCalledOnce(); expect(navigate).toHaveBeenCalledWith("/dashboards/d-private");
    expect(post.mock.calls.filter(call => call[0].includes("/questions/q1/copies"))).toHaveLength(1);
    expect(post.mock.calls.filter(call => call[0].includes("/dashboards/d1/copies"))).toHaveLength(1);
  });

});
