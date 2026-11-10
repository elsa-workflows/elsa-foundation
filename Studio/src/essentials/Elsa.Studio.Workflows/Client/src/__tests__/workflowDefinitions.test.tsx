import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { StudioWorkspaceTabProvider, type StudioWeaverRegistry, type StudioEndpointContext, type StudioWorkspaceTabHandle } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { clearApiCapabilityCache } from "../api/capabilities";
import { WorkflowDefinitions } from "../workflow-editor/WorkflowDefinitions";
import { useWorkflowDefinitionBrowseSession } from "../workflow-editor/workflowDefinitionBrowseSession";
import { getDialogs, setDialogs } from "../workflow-editor/dialogs";
import { flowchartActivity } from "./fixtures/workflowCatalog";
import { createWeaverRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent, type WorkflowAgentController } from "../workflowAgent";
import type { StudioWeaverClientExecutionResult, StudioWeaverPreparedInvocation } from "@elsa-workflows/studio-sdk";

let mounted: { root: Root; container: HTMLDivElement } | null = null;
let previousClipboard: PropertyDescriptor | undefined;

const hostDialogs = getDialogs();

afterEach(() => {
  setDialogs(hostDialogs);
  if (previousClipboard) Object.defineProperty(navigator, "clipboard", previousClipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
  previousClipboard = undefined;
  clearApiCapabilityCache();
  vi.restoreAllMocks();
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
  window.history.replaceState({}, "", "/");
});

describe("workflow definition list", () => {
  it("separates names and IDs and isolates the identity popover from row navigation", async () => {
    const definition = {
      id: "definition-exact-0123456789abcdef0123456789abcdef",
      name: "Invoice evaluator",
      description: "Evaluates invoice policy.",
      createdAt: "2026-07-01T00:00:00Z",
      lastModifiedAt: "2026-07-02T00:00:00Z",
      draftId: "draft-exact-id",
      draftSourceVersionId: "draft-source-version-exact-id",
      latestVersionId: "latest-version-exact-id",
      latestVersion: "2.0.0",
      publishedVersionId: "published-version-exact-id",
      publishedVersion: "1.0.0",
      versionCount: 2,
      publicationState: "published"
    };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? definitionCapabilities : { items: [definition] });
    const navigate = vi.fn();
    const writeText = vi.fn().mockResolvedValue(undefined);
    previousClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.querySelector(".wf-definition-primary-identifier .studio-copyable-identifier__value")?.getAttribute("aria-label")).toBe(`Definition ID: ${definition.id}`));
    const row = container.querySelector<HTMLElement>(".wf-definition-grid .wf-grid-row")!;
    expect(row.querySelector(".wf-definition-name strong")?.textContent).toBe(definition.name);
    expect(row.querySelector(".wf-definition-description")?.textContent).toBe(definition.description);
    expect(container.querySelector('[role="columnheader"].wf-definition-id-header')?.textContent).toBe("Definition ID");
    const disclosure = row.querySelector<HTMLButtonElement>('button[aria-label="Identifiers for Invoice evaluator"]')!;
    const copyDefinitionId = row.querySelector<HTMLButtonElement>('button[aria-label="Copy Definition ID"]')!;
    expect(row.querySelector(".wf-definition-id")?.contains(copyDefinitionId)).toBe(true);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(disclosure.getAttribute("aria-expanded")).toBe("false");

    click(disclosure);
    expect(disclosure.getAttribute("aria-expanded")).toBe("true");
    const details = document.querySelector<HTMLElement>('[role="dialog"][aria-label="Workflow definition identifiers for Invoice evaluator"]')!;
    for (const identifier of [definition.draftId, definition.draftSourceVersionId, definition.latestVersionId, definition.publishedVersionId]) {
      expect(details.textContent).toContain(identifier);
    }
    expect(details.textContent).toContain(definition.description);
    click(details.querySelector('[aria-label="Draft ID: draft-exact-id"]')!);
    expect(navigate).not.toHaveBeenCalled();
    click(details.querySelector('button[aria-label="Copy Draft ID"]')!);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(definition.draftId));
    flushSync(() => details.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(disclosure.getAttribute("aria-expanded")).toBe("false");

    const copyIconPath = copyDefinitionId.querySelector("svg path")!;
    flushSync(() => copyIconPath.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(definition.id));
    expect(navigate).not.toHaveBeenCalled();

    click(row.querySelector<HTMLElement>(".wf-definition-name")!);
    expect(navigate).toHaveBeenCalledWith(definition.id);
  });

  it("places the definition title and create action above the collection controls", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? definitionCapabilities : { items: [{ id: "definition-1", name: "Invoice evaluator" }] });
    const container = render(context(getJson), vi.fn());

    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    const heading = container.querySelector<HTMLElement>("h1");
    expect(heading?.textContent).toBe("Workflow definitions");
    const header = heading?.closest("header");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search all workflows']");
    const states = container.querySelector<HTMLElement>('[role="group"][aria-label="Definition state"]');
    expect(header).not.toBeNull();
    expect(search).not.toBeNull();
    expect(states).not.toBeNull();
    expect(header?.querySelector("p")?.textContent).toContain("Create and organize workflow definitions");
    const actions = header?.querySelector<HTMLElement>('[role="group"][aria-label="Workflow definition page actions"]');
    expect(actions?.querySelector("button[aria-label='Create workflow']")).toBeTruthy();
    expect(actions?.querySelector("button[aria-label='Refresh workflow definitions']")).toBeTruthy();
    expect(header!.compareDocumentPosition(search!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(header!.compareDocumentPosition(states!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(actions?.querySelector("button")?.textContent).toContain("Create workflow");
  });

  it("discloses the saved templates its create dialog offers, so a Weaver prefill can choose one (spec 1193 FR-023)", async () => {
    const template = { id: "template-1", name: "Approval", description: "Two steps", revision: "t-r3", createdAt: "2026-01-01T00:00:00Z", lastModifiedAt: "2026-01-01T00:00:00Z",
      content: { state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: flowchartActivity().activityVersionId, inputs: [], outputs: [] } },
        layout: [], activityPresentation: [] } };
    const getJson = vi.fn(async (url: string) => url === "/capabilities"
      ? { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links, { rel: "workflow-templates", href: "design/workflows/templates" }] }, activityCapability] }
      : url.endsWith("design/workflows/templates") ? [template] : routeJson(url));
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const container = render(context(getJson), vi.fn(), weaver);
    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    await vi.waitFor(() => {
      const dialog = [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.create-dialog:"))?.[1];
      expect((dialog?.content as { templates?: Array<{ id: string; revision: string }> } | undefined)?.templates).toEqual([
        { id: "template-1", name: "Approval", description: "Two steps", revision: "t-r3" }]);
    }, { timeout: 5_000, interval: 20 });
  });

  it("discloses the open create dialog only once it has read the templates it offers (spec 1193 FR-023)", async () => {
    const template = { id: "template-1", name: "Approval", description: "Two steps", revision: "t-r3", createdAt: "2026-01-01T00:00:00Z", lastModifiedAt: "2026-01-01T00:00:00Z",
      content: { state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: flowchartActivity().activityVersionId, inputs: [], outputs: [] } },
        layout: [], activityPresentation: [] } };
    let releaseTemplates!: () => void;
    const templatesRead = new Promise<void>(resolve => { releaseTemplates = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links, { rel: "workflow-templates", href: "design/workflows/templates" }] }, activityCapability] };
      if (url.endsWith("design/workflows/templates")) { await templatesRead; return [template]; }
      return routeJson(url);
    });
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const container = render(context(getJson), vi.fn(), weaver);
    const dialog = () => [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.create-dialog:"))?.[1];
    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    await vi.waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).endsWith("design/workflows/templates"))).toBe(true), { timeout: 5_000, interval: 20 });
    await settle();
    expect(nameInput(container)).not.toBeNull();
    // The dialog is open but its offered templates are not read yet: a context now would be one about to change.
    expect(dialog()).toBeUndefined();
    const picker = () => container.querySelector('select[aria-label="Starting point"]')!;
    expect(picker().getAttribute("aria-busy")).toBe("true");
    releaseTemplates();
    await vi.waitFor(() => expect((dialog()?.content as { templates?: unknown[] } | undefined)?.templates).toHaveLength(1), { timeout: 5_000, interval: 20 });
    expect(picker().getAttribute("aria-busy")).toBe("false");
    const disclosed = dialog()!;
    await settle();
    expect(dialog()!.target!.revision).toBe(disclosed.target!.revision);
  });

  it("discloses the visible list only once its permissions are read, so that read never moves a reviewed revision (spec 1193 FR-020)", async () => {
    let releaseTags!: () => void;
    const tagsRead = new Promise<void>(resolve => { releaseTags = resolve; });
    const permissions = { canAssignTags: true, canManageFolders: true, canManageTags: true, canShareViews: true };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links,
        { rel: "workflow-tags", href: "design/workflows/tags" }, { rel: "workflow-organization", href: "design/workflows/definitions/{definitionId}/organization", templated: true }] }, activityCapability] };
      if (url.includes("design/workflows/tags")) { await tagsRead; return { items: [], permissions }; }
      return routeJson(url);
    });
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const container = render(context(getJson), vi.fn(), weaver);
    const list = () => [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.library:"))?.[1];
    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    await vi.waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("design/workflows/tags"))).toBe(true), { timeout: 5_000, interval: 20 });
    await settle();
    // Rows are current, but the permissions are not read yet: the page has no state it can stand behind, so nothing is disclosed.
    expect(list()).toBeUndefined();
    const table = () => container.querySelector('[role="table"][aria-label="Workflow definitions"]')!;
    expect(table().getAttribute("aria-busy")).toBe("true");
    releaseTags();
    await vi.waitFor(() => expect(list()).toBeDefined(), { timeout: 5_000, interval: 20 });
    expect(table().getAttribute("aria-busy")).toBe("false");
    const disclosed = list()!;
    expect((disclosed.content as { permissions: unknown }).permissions).toEqual(permissions);
    await settle();
    expect(list()!.target!.revision).toBe(disclosed.target!.revision);
  });

  it("discloses the visible list with no permissions when its first permission read fails, and settles (spec 1193 FR-020)", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return libraryCapabilities();
      if (url.includes("design/workflows/tags")) throw new Error("tags unavailable");
      return routeJson(url);
    });
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const container = render(context(getJson), vi.fn(), weaver);
    const list = () => [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.library:"))?.[1];
    await vi.waitFor(() => expect(list()).toBeDefined(), { timeout: 5_000, interval: 20 });
    expect((list()!.content as { permissions: unknown }).permissions).toEqual({ canAssignTags: false, canManageFolders: false, canManageTags: false, canShareViews: false });
    expect(container.querySelector('[role="table"][aria-label="Workflow definitions"]')!.getAttribute("aria-busy")).toBe("false");
  });

  it("settles a list opened on a saved view when its first view read fails, disclosing no view (spec 1193 FR-020)", async () => {
    window.history.replaceState({}, "", "/?view=view-1");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links,
        { rel: "workflow-views", href: "design/workflows/views" }] }, activityCapability] };
      if (url.includes("design/workflows/views")) throw new Error("views unavailable");
      return routeJson(url);
    });
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const container = render(context(getJson), vi.fn(), weaver);
    const list = () => [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.library:"))?.[1];
    await vi.waitFor(() => expect(list()).toBeDefined(), { timeout: 5_000, interval: 20 });
    expect(getJson.mock.calls.some(([url]) => String(url).includes("design/workflows/views"))).toBe(true);
    expect((list()!.content as { view: unknown }).view).toBeNull();
    expect(container.querySelector('[role="table"][aria-label="Workflow definitions"]')!.getAttribute("aria-busy")).toBe("false");
  });

  it("never settles the create dialog on a template read that failed for a catalog still loading (spec 1193 FR-023)", async () => {
    let releaseCatalog!: () => void;
    const catalogRead = new Promise<void>(resolve => { releaseCatalog = resolve; });
    let templateReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return templateCapabilities();
      if (url.endsWith("design/workflows/templates")) { if (templateReads++ === 0) throw new Error("transient"); return [savedTemplate("t-r3")]; }
      if (url.endsWith("design/activities/catalog")) { await catalogRead; return routeJson(url); }
      return routeJson(url);
    });
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const disclosed = recordDialogDisclosures(weaver);
    const container = render(context(getJson), vi.fn(), weaver);
    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    await vi.waitFor(() => expect(templateReads).toBe(1), { timeout: 5_000, interval: 20 });
    await settle();
    releaseCatalog();
    await vi.waitFor(() => expect(disclosed.at(-1)).toEqual(["template-1"]), { timeout: 5_000, interval: 20 });
    // Disclosed once, with the read that followed the failure; the failure's notice did not outlive it.
    expect(disclosed).toEqual([["template-1"]]);
    expect(container.textContent).not.toContain("Saved templates could not be loaded");
  });

  it("reopens a closed dialog through a Weaver revert with its own fresh catalog and templates, even after an authority reset (spec 1193 FR-023)", async () => {
    let revision = "t-r3";
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? templateCapabilities() : url.endsWith("design/workflows/templates") ? [savedTemplate(revision)] : routeJson(url));
    const agent = createWeaverRegistry();
    const weaver = registerWorkflowAgent(agent);
    const disclosed = recordDialogDisclosures(weaver, true);
    const first = context(getJson);
    const { container, rerender } = renderWithContext(first, weaver);
    await waitFor(() => expect(container.textContent).toContain("Invoice evaluator"));
    click(container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    const dialog = () => [...weaver.library!.snapshots].find(([key]) => key.startsWith("workflows.create-dialog:"))?.[1];
    await vi.waitFor(() => expect(dialog()).toBeDefined(), { timeout: 5_000, interval: 20 });
    const snapshot = dialog()!;
    const contract = agent.toolContracts.list().find(item => item.id === "workflow_create_dialog_prefill")!;
    const invocation = { invocationId: "i-1", client: snapshot.client, target: snapshot.target, expiresAt: new Date(Date.now() + 60_000).toISOString(),
      arguments: { baseRevision: snapshot.target!.revision, change: { name: "Prefilled" } } } as unknown as StudioWeaverPreparedInvocation;
    let result: StudioWeaverClientExecutionResult | undefined;
    await new Promise<void>(resolve => flushSync(() => { void contract.executeClient!(invocation).then(value => { result = value; resolve(); }); }));
    expect(result!.outcome).toBe("applied");
    // The revert restores the draft before the prefill; with the dialog closed and the authority reset, it reopens it.
    await vi.waitFor(() => expect(nameInput(container)?.value).toBe("Prefilled"), { timeout: 5_000, interval: 20 });
    flushSync(() => container.querySelector<HTMLElement>('[role="dialog"][aria-labelledby="workflow-create-title"]')!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await vi.waitFor(() => expect(nameInput(container)).toBeNull(), { timeout: 5_000, interval: 20 });
    revision = "t-r4";
    rerender(context(getJson));
    await settle();
    disclosed.length = 0;
    const token = (result!.result!.data as { undoToken: string }).undoToken;
    flushSync(() => { expect(weaver.library!.revert!(token)).toBe("Reverted the change on this page."); });
    await vi.waitFor(() => expect(container.querySelector('select[aria-label="Starting point"]')?.getAttribute("aria-busy")).toBe("false"), { timeout: 5_000, interval: 20 });
    await vi.waitFor(() => expect(disclosed.at(-1)).toEqual(["template-1@t-r4"]), { timeout: 5_000, interval: 20 });
    // The reopened dialog never published the earlier session's templates.
    expect(disclosed.flat()).not.toContain("template-1@t-r3");
  });

  it("keeps an open create dialog and its name when its workspace tab goes to the background and returns", async () => {
    const getJson = vi.fn(async (url: string) => routeJson(url));
    const tab = renderInTab(context(getJson), vi.fn());

    await waitFor(() => expect(tab.container.textContent).toContain("Invoice evaluator"));
    click(tab.container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    await vi.waitFor(() => expect(nameInput(tab.container)).not.toBeNull(), { timeout: 5_000, interval: 20 });
    await fill(nameInput(tab.container)!, "Kept across tabs");

    tab.hide();
    await settle();
    tab.reveal();
    await settle();

    expect(tab.container.querySelector('[role="dialog"][aria-labelledby="workflow-create-title"]')).not.toBeNull();
    expect(nameInput(tab.container)?.value).toBe("Kept across tabs");
  });

  it("holds opening a definition created while its workspace tab is in the background until the tab returns", async () => {
    const getJson = vi.fn(async (url: string) => routeJson(url));
    let resolveCreate!: (value: unknown) => void;
    const postJson = vi.fn(() => new Promise(resolve => { resolveCreate = resolve; }));
    const onOpen = vi.fn();
    const tab = renderInTab(context(getJson, postJson), onOpen);

    await waitFor(() => expect(tab.container.textContent).toContain("Invoice evaluator"));
    click(tab.container.querySelector<HTMLButtonElement>("button[aria-label='Create workflow']")!);
    await vi.waitFor(() => expect(nameInput(tab.container)).not.toBeNull(), { timeout: 5_000, interval: 20 });
    await fill(nameInput(tab.container)!, "Created in background");
    await waitFor(() => expect(createButton(tab.container)?.getAttribute("aria-disabled")).toBe("false"));
    click(createButton(tab.container)!);
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));

    tab.hide();
    await settle();
    resolveCreate({ definition: { id: "created-definition", name: "Created in background" }, draft: null, versions: [] });
    await settle();

    expect(onOpen).not.toHaveBeenCalled();
    expect(tab.handle.markPendingWork).toHaveBeenCalled();

    tab.reveal();
    await waitFor(() => expect(onOpen).toHaveBeenCalledTimes(1));
    expect(onOpen).toHaveBeenCalledWith("created-definition");
  });

  it("holds the folder change after deleting a folder while its workspace tab is in the background", async () => {
    window.history.replaceState({}, "", "/workflows/definitions?folderId=same-folder");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return folderCapabilities;
      if (url.endsWith("folders/same-folder")) return { folder: { id: "same-folder", name: "Selected folder", parentId: null, revision: "r1" }, ancestors: [] };
      if (url.includes("definition-pages")) return { items: [{ id: "definition-1", name: "Invoice evaluator" }] };
      return { items: [] };
    });
    let finishDelete!: () => void;
    const deleteJson = vi.fn(() => new Promise<void>(resolve => { finishDelete = resolve; }));
    setDialogs({ confirm: async () => true, prompt: async () => null, alert: async () => undefined } as unknown as Parameters<typeof setDialogs>[0]);
    const tab = renderInTab({ ...context(getJson), http: { getJson, deleteJson } } as unknown as StudioEndpointContext, vi.fn());

    const deleteButton = () => [...tab.container.querySelectorAll<HTMLButtonElement>(".wf-folder-actions button")].find(button => button.textContent?.trim() === "Delete") ?? null;
    await vi.waitFor(() => expect(deleteButton()).not.toBeNull(), { timeout: 5_000, interval: 20 });
    click(deleteButton()!);
    await waitFor(() => expect(deleteJson).toHaveBeenCalledTimes(1));

    tab.hide();
    await settle();
    const address = window.location.pathname + window.location.search;
    const historyLength = window.history.length;
    finishDelete();
    await settle();

    expect(window.location.pathname + window.location.search).toBe(address);
    expect(window.history.length).toBe(historyLength);
    expect(tab.handle.markPendingWork).toHaveBeenCalled();

    tab.reveal();
    await waitFor(() => expect(new URLSearchParams(window.location.search).has("folderId")).toBe(false));
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(window.history.length).toBe(historyLength + 1);
  });
});

function renderInTab(contextValue: StudioEndpointContext, onOpen: (id: string) => void) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const ai = { promptActions: { list: () => [] }, dispatchPrompt: vi.fn(), onPromptResult: () => () => {} } as unknown as StudioWeaverRegistry;
  let active = true;
  const handle = {
    id: "tab-1",
    get active() { return active; },
    location: "/workflows/definitions",
    isActive: () => active,
    markPendingWork: vi.fn(),
    setTitle: vi.fn()
  } satisfies StudioWorkspaceTabHandle;
  function Harness() {
    const session = useWorkflowDefinitionBrowseSession(contextValue);
    return <WorkflowDefinitions context={contextValue} ai={ai} onOpen={onOpen} session={session} />;
  }
  const show = (mode: "visible" | "hidden") => flushSync(() => root.render(
    <React.Activity mode={mode}>
      <StudioWorkspaceTabProvider value={handle}><Harness /></StudioWorkspaceTabProvider>
    </React.Activity>
  ));
  show("visible");
  return {
    container,
    handle,
    hide: () => { active = false; show("hidden"); },
    reveal: () => { active = true; show("visible"); }
  };
}

function routeJson(url: string) {
  if (url === "/capabilities") return { capabilities: [...definitionCapabilities.capabilities, activityCapability] };
  if (url.endsWith("design/activities/catalog")) return { activities: [flowchartActivity()] };
  return { items: [{ id: "definition-1", name: "Invoice evaluator" }] };
}

function nameInput(container: HTMLElement) {
  return container.querySelector<HTMLInputElement>('input[aria-label="Display name"]');
}

function createButton(container: HTMLElement) {
  const dialog = container.querySelector<HTMLElement>('[role="dialog"][aria-labelledby="workflow-create-title"]');
  return [...(dialog?.querySelectorAll<HTMLButtonElement>("button") ?? [])].find(button => button.textContent?.trim() === "Create") ?? null;
}

async function fill(element: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await settle();
}

async function settle() {
  for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0));
}

function libraryCapabilities() {
  return { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links,
    { rel: "workflow-tags", href: "design/workflows/tags" }, { rel: "workflow-organization", href: "design/workflows/definitions/{definitionId}/organization", templated: true }] }, activityCapability] };
}

function templateCapabilities() {
  return { capabilities: [{ ...definitionCapabilities.capabilities[0], links: [...definitionCapabilities.capabilities[0].links, { rel: "workflow-templates", href: "design/workflows/templates" }] }, activityCapability] };
}

function savedTemplate(revision: string) {
  return { id: "template-1", name: "Approval", description: "Two steps", revision, createdAt: "2026-01-01T00:00:00Z", lastModifiedAt: "2026-01-01T00:00:00Z",
    content: { state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: flowchartActivity().activityVersionId, inputs: [], outputs: [] } },
      layout: [], activityPresentation: [] } };
}

/** Every create-dialog context the page publishes, as its offered template ids (with revisions when asked). */
function recordDialogDisclosures(weaver: WorkflowAgentController, withRevision = false) {
  const disclosed: string[][] = [];
  const snapshots = weaver.library!.snapshots;
  const set = snapshots.set.bind(snapshots);
  snapshots.set = (key, value) => {
    if (key.startsWith("workflows.create-dialog:"))
      disclosed.push(((value.content as { templates?: Array<{ id: string; revision: string }> }).templates ?? []).map(item => withRevision ? `${item.id}@${item.revision}` : item.id));
    return set(key, value);
  };
  return disclosed;
}

function renderWithContext(initial: StudioEndpointContext, weaver: WorkflowAgentController) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const ai = { promptActions: { list: () => [] }, dispatchPrompt: vi.fn() } as unknown as StudioWeaverRegistry;
  function Harness({ contextValue }: { contextValue: StudioEndpointContext }) {
    const session = useWorkflowDefinitionBrowseSession(contextValue);
    return <WorkflowDefinitions context={contextValue} ai={ai} weaver={weaver} onOpen={vi.fn()} session={session} />;
  }
  flushSync(() => root.render(<Harness contextValue={initial} />));
  return { container, rerender: (next: StudioEndpointContext) => flushSync(() => root.render(<Harness contextValue={next} />)) };
}

function render(contextValue: StudioEndpointContext, onOpen: (id: string) => void, weaver?: WorkflowAgentController) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const ai = { promptActions: { list: () => [] }, dispatchPrompt: vi.fn() } as unknown as StudioWeaverRegistry;
  function Harness() {
    const session = useWorkflowDefinitionBrowseSession(contextValue);
    return <WorkflowDefinitions context={contextValue} ai={ai} weaver={weaver} onOpen={onOpen} session={session} />;
  }
  flushSync(() => root.render(<Harness />));
  return container;
}

function context(getJson: (url: string) => Promise<unknown>, postJson?: (url: string, body: unknown) => Promise<unknown>): StudioEndpointContext {
  return { baseUrl: "https://studio.test/", http: { getJson, postJson } } as unknown as StudioEndpointContext;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 40; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

const definitionCapabilities = {
  capabilities: [{
    id: "elsa.api.workflow-design",
    contractVersion: "1",
    links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
  }]
};

const activityCapability = {
  id: "elsa.api.activity-design",
  contractVersion: "1",
  links: [{ rel: "activity-catalog", href: "design/activities/catalog" }]
};

const folderCapabilities = {
  capabilities: [{
    id: "elsa.api.workflow-design",
    contractVersion: "1",
    links: [
      { rel: "workflow-definitions", href: "design/workflows/definitions" },
      { rel: "workflow-definitions-page", href: "design/workflows/definition-pages" },
      { rel: "workflow-folders", href: "design/workflows/folders" },
      { rel: "workflow-folder-delete-empty", href: "design/workflows/folders/{folderId}", templated: true }
    ]
  }]
};
