import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EtlDefinitionsPage } from "../EtlDefinitionsPage";
import {
  createEtlConnectorDefinition, getEtlConnectorDefinition, getEtlConnectorVersion,
  listEtlConnectorBindings, listEtlConnectorDefinitions, listEtlConnectorVersions,
  publishEtlConnectorDefinition, updateEtlConnectorDraft, validateEtlConnectorDraft,
  createEtlDefinition,
  EtlApiError,
  getEtlDefinition,
  getEtlVersion,
  listEtlBindings,
  listEtlDefinitions,
  listEtlVersions,
  publishEtlDefinition,
  updateEtlDraft,
  validateEtlDraft
} from "../etlApi";
import { createDefaultEtlPipeline } from "../authoringHelpers";
import { createDefaultEtlConnectorPipeline } from "../connectorAuthoringHelpers";
import type {
  EtlBindingSummary,
  EtlDraftView,
  EtlDefinitionSummaryView,
  EtlPage,
  EtlPipeline,
  EtlPublishedArtifactView,
  EtlPublishedVersionView,
  EtlValidationView
} from "../etlTypes";

vi.mock("../etlApi", async importOriginal => ({
  ...(await importOriginal<typeof import("../etlApi")>()),
  createEtlConnectorDefinition: vi.fn(), getEtlConnectorDefinition: vi.fn(), getEtlConnectorVersion: vi.fn(),
  listEtlConnectorBindings: vi.fn(), listEtlConnectorDefinitions: vi.fn(), listEtlConnectorVersions: vi.fn(),
  publishEtlConnectorDefinition: vi.fn(), updateEtlConnectorDraft: vi.fn(), validateEtlConnectorDraft: vi.fn(),
  createEtlDefinition: vi.fn(),
  getEtlDefinition: vi.fn(),
  getEtlVersion: vi.fn(),
  listEtlBindings: vi.fn(),
  listEtlDefinitions: vi.fn(),
  listEtlVersions: vi.fn(),
  publishEtlDefinition: vi.fn(),
  updateEtlDraft: vi.fn(),
  validateEtlDraft: vi.fn()
}));

describe("ETL definitions page", () => {
  beforeEach(() => {
    vi.mocked(listEtlConnectorDefinitions).mockResolvedValue(page([summary("orders")]));
    vi.mocked(listEtlConnectorBindings).mockResolvedValue(page([
      { alias: "source", role: "Source", kind: "http" },
      { alias: "output", role: "MainOutput", kind: "http" },
      { alias: "errors", role: "ErrorOutput", kind: "http" }
    ]));
    vi.mocked(getEtlConnectorDefinition).mockImplementation(async (_context, id) => connectorDraft(id));
    vi.mocked(createEtlConnectorDefinition).mockImplementation(async (_context, request) => connectorDraft(request.id, 1, request.pipeline));
    vi.mocked(updateEtlConnectorDraft).mockImplementation(async (_context, id, request) => connectorDraft(id, request.expectedRevision + 1, request.pipeline));
    vi.mocked(validateEtlConnectorDraft).mockImplementation(async (_context, _id, request) => validResult(request.expectedRevision));
    vi.mocked(publishEtlConnectorDefinition).mockImplementation(async (_context, id, request) => published(id, request.definitionVersion, request.contentHash));
    vi.mocked(listEtlConnectorVersions).mockResolvedValue(page([]));
    vi.mocked(getEtlConnectorVersion).mockImplementation(async (_context, id, number) => ({
      version: version(id, number, hash("b")), pipeline: createDefaultEtlConnectorPipeline()
    }));
    vi.mocked(listEtlDefinitions).mockResolvedValue(page([summary("orders"), summary("returns")]));
    vi.mocked(listEtlBindings).mockResolvedValue(page(bindings()));
    vi.mocked(getEtlDefinition).mockImplementation(async (_context, id) => draft(id));
    vi.mocked(createEtlDefinition).mockImplementation(async (_context, request) => draft(request.id, 1, request.pipeline));
    vi.mocked(updateEtlDraft).mockImplementation(async (_context, id, request) => draft(id, request.expectedRevision + 1, request.pipeline));
    vi.mocked(validateEtlDraft).mockImplementation(async (_context, id, request) => validResult(request.expectedRevision));
    vi.mocked(publishEtlDefinition).mockImplementation(async (_context, id, request) => ({
      status: "published",
      version: version(id, request.definitionVersion, request.contentHash)
    }));
    vi.mocked(listEtlVersions).mockResolvedValue(page([]));
    vi.mocked(getEtlVersion).mockImplementation(async (_context, id, versionNumber) => artifact(id, versionNumber));
  });

  afterEach(() => {
    cleanupActive();
    vi.restoreAllMocks();
    vi.resetAllMocks();
  });

  it("creates, edits and publishes a connector draft through its own catalog using the validated tuple", async () => {
    const context = endpointContext();
    const rendered = mount(context, createDialogs(), "connector");
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    expect(rendered.host.textContent).toContain("Connector pipelines");
    click(buttonByText(rendered.host, "New definition"));
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: new-pipeline"));
    expect(createEtlConnectorDefinition).toHaveBeenCalledWith(context, {
      id: "new-pipeline", pipeline: createDefaultEtlConnectorPipeline()
    }, expect.any(AbortSignal));
    change(inputByLabel(rendered.host, "Output field 1 name"), "displayName");
    expect(buttonByText(rendered.host, "Validate saved revision").disabled).toBe(true);
    click(buttonByText(rendered.host, "Save draft"));
    await waitFor(() => expect(rendered.host.textContent).toContain("Saved new-pipeline at revision 2"));
    expect(updateEtlConnectorDraft).toHaveBeenCalledWith(context, "new-pipeline", expect.objectContaining({
      expectedRevision: 1,
      pipeline: expect.objectContaining({ mapping: [{ name: "displayName", origin: "source", field: "name" }] })
    }), expect.any(AbortSignal));
    click(buttonByText(rendered.host, "Validate saved revision"));
    await waitFor(() => expect(buttonByText(rendered.host, "Publish immutable version").disabled).toBe(false));
    vi.mocked(listEtlConnectorVersions).mockResolvedValue(page([version("new-pipeline", 2, hash("b"))]));
    click(buttonByText(rendered.host, "Publish immutable version"));
    await waitFor(() => expect(publishEtlConnectorDefinition).toHaveBeenCalledTimes(1));
    expect(publishEtlConnectorDefinition).toHaveBeenCalledWith(context, "new-pipeline", {
      expectedRevision: 2, definitionVersion: 2, contentHash: hash("b")
    }, expect.any(AbortSignal));
    await waitFor(() => expect(buttonContaining(rendered.host, "View version 2")).toBeTruthy());
    click(buttonContaining(rendered.host, "View version 2")!);
    await waitFor(() => expect(rendered.host.textContent).toContain(`Exact selection: etl1:new-pipeline:2:${hash("b")}`));
    expect(inputByLabel(rendered.host, "Output field 1 name", true).disabled).toBe(true);
    expect(listEtlDefinitions).not.toHaveBeenCalled();
    expect(createEtlDefinition).not.toHaveBeenCalled();
    expect(publishEtlDefinition).not.toHaveBeenCalled();
  });

  it("keeps a missing connector capability explicit without falling back to the file catalog", async () => {
    vi.mocked(listEtlConnectorDefinitions).mockRejectedValue(new EtlApiError("unavailable", null, "etl.capability-unavailable"));
    const rendered = mount(endpointContext(), createDialogs(), "connector");
    await waitFor(() => expect(rendered.host.textContent).toContain("ETL API is unavailable"));
    expect(listEtlDefinitions).not.toHaveBeenCalled();
    expect(buttonContaining(rendered.host, "orders")).toBeNull();
  });

  it("discards stale file-catalog responses when the authoring profile changes", async () => {
    let finishFile: ((result: EtlPage<EtlDefinitionSummaryView>) => void) | undefined;
    let fileSignal: AbortSignal | undefined;
    vi.mocked(listEtlDefinitions).mockImplementation((_context, _request, signal) => {
      fileSignal = signal;
      return new Promise(resolve => { finishFile = resolve; });
    });
    const context = endpointContext();
    const rendered = mount(context);
    await waitFor(() => expect(finishFile).toBeDefined());
    rendered.render(context, "connector");
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    finishFile?.(page([summary("stale-file-definition")]));
    await waitFor(() => expect(fileSignal?.aborted).toBe(true));
    expect(buttonContaining(rendered.host, "stale-file-definition")).toBeNull();
    expect(rendered.host.textContent).toContain("Connector pipelines");
  });

  it("saves a draft, validates its saved revision, publishes that exact tuple once, and views the immutable artifact", async () => {
    const context = endpointContext();
    const dialogs = createDialogs();
    let versionPageCalls = 0;
    vi.mocked(listEtlVersions).mockImplementation(async (_context, id) => {
      versionPageCalls++;
      return page([version(id, versionPageCalls === 1 ? 1 : 2, versionPageCalls === 1 ? hash("a") : hash("b"))]);
    });
    let resolvePublish: ((result: ReturnType<typeof published>) => void) | undefined;
    vi.mocked(publishEtlDefinition).mockImplementation(() => new Promise(resolve => { resolvePublish = resolve; }));

    const rendered = mount(context, dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    click(buttonContaining(rendered.host, "orders")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: orders"));

    change(inputByLabel(rendered.host, "Output field 1 name"), "displayName");
    click(buttonByText(rendered.host, "Save draft"));
    await waitFor(() => expect(rendered.host.textContent).toContain("Saved orders at revision 5"));
    expect(updateEtlDraft).toHaveBeenCalledWith(context, "orders", expect.objectContaining({
      expectedRevision: 4,
      pipeline: expect.objectContaining({ mapping: [{ name: "displayName", origin: "source", field: "name" }] })
    }), expect.any(AbortSignal));

    click(buttonByText(rendered.host, "Validate saved revision"));
    await waitFor(() => expect(rendered.host.textContent).toContain("valid as immutable version 2"));
    expect(validateEtlDraft).toHaveBeenCalledWith(context, "orders", { expectedRevision: 5 }, expect.any(AbortSignal));

    const publishButton = buttonByText(rendered.host, "Publish immutable version");
    click(publishButton);
    click(publishButton);
    await waitFor(() => expect(publishEtlDefinition).toHaveBeenCalledTimes(1));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({
      message: expect.stringContaining(hash("b")),
      confirmLabel: "Publish version"
    }));
    expect(publishEtlDefinition).toHaveBeenCalledWith(context, "orders", {
      expectedRevision: 5,
      definitionVersion: 2,
      contentHash: hash("b")
    }, expect.any(AbortSignal));
    resolvePublish?.(published("orders", 2, hash("b")));

    const selection = `etl1:orders:2:${hash("b")}`;
    await waitFor(() => expect(buttonContaining(rendered.host, "View version 2")).toBeTruthy());
    expect(rendered.host.textContent).toContain(`Selection: ${selection}`);
    click(buttonContaining(rendered.host, "View version 2")!);
    await waitFor(() => expect(rendered.host.textContent).toContain(`Exact selection: ${selection}`));
    expect(getEtlVersion).toHaveBeenCalledWith(context, "orders", 2, expect.any(AbortSignal));
    expect(inputByLabel(rendered.host, "Output field 1 name").disabled).toBe(false);
    expect(inputByLabel(rendered.host, "Output field 1 name", true).disabled).toBe(true);
  });

  it("confirms before discarding edits and preserves them when the user declines", async () => {
    const context = endpointContext();
    const dialogs = createDialogs(false);
    const rendered = mount(context, dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    click(buttonContaining(rendered.host, "orders")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: orders"));
    change(inputByLabel(rendered.host, "Output field 1 name"), "edited-name");

    click(buttonContaining(rendered.host, "returns")!);
    await waitFor(() => expect(dialogs.confirm).toHaveBeenCalledTimes(1));
    expect(getEtlDefinition).toHaveBeenCalledTimes(1);
    expect(inputByLabel(rendered.host, "Output field 1 name").value).toBe("edited-name");
    expect(rendered.host.textContent).toContain("Draft: orders");
    await waitFor(() => expect(buttonContaining(rendered.host, "returns")!.disabled).toBe(false));

    vi.mocked(dialogs.confirm).mockResolvedValue(true);
    click(buttonContaining(rendered.host, "returns")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: returns"));
    expect(getEtlDefinition).toHaveBeenCalledTimes(2);
    expect(getEtlDefinition).toHaveBeenLastCalledWith(context, "returns", expect.any(AbortSignal));
  });

  it("requires fresh validation when host approval changes before publication", async () => {
    vi.mocked(publishEtlDefinition).mockRejectedValue(new EtlApiError("invalid", 400, "etl.validation-failed"));
    const rendered = mount(endpointContext(), createDialogs());
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    click(buttonContaining(rendered.host, "orders")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: orders"));
    click(buttonByText(rendered.host, "Validate saved revision"));
    await waitFor(() => expect(buttonByText(rendered.host, "Publish immutable version").disabled).toBe(false));
    click(buttonByText(rendered.host, "Publish immutable version"));
    await waitFor(() => expect(rendered.host.textContent).toContain("Validate again to see current diagnostics"));
    expect(buttonByText(rendered.host, "Publish immutable version").disabled).toBe(true);
    expect(buttonByText(rendered.host, "Validate saved revision").disabled).toBe(false);
    expect(inputByLabel(rendered.host, "Output field 1 name").value).toBe("name");
  });

  it("keeps edits after a conflict until the user confirms an explicit reload", async () => {
    const context = endpointContext();
    const dialogs = createDialogs(false);
    vi.mocked(updateEtlDraft).mockRejectedValue(new EtlApiError("conflict", 409, "etl.definition-conflict"));
    const rendered = mount(context, dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    click(buttonContaining(rendered.host, "orders")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: orders"));
    change(inputByLabel(rendered.host, "Output field 1 name"), "local-edits-stay");
    click(buttonByText(rendered.host, "Save draft"));

    await waitFor(() => expect(rendered.host.textContent).toContain("local edits were preserved"));
    expect(getEtlDefinition).toHaveBeenCalledTimes(1);
    expect(inputByLabel(rendered.host, "Output field 1 name").value).toBe("local-edits-stay");
    expect(buttonByText(rendered.host, "Save draft").disabled).toBe(false);
    expect(buttonByText(rendered.host, "Publish immutable version").disabled).toBe(true);

    click(buttonByText(rendered.host, "Reload draft"));
    await waitFor(() => expect(dialogs.confirm).toHaveBeenCalledTimes(1));
    expect(getEtlDefinition).toHaveBeenCalledTimes(1);
    expect(inputByLabel(rendered.host, "Output field 1 name").value).toBe("local-edits-stay");
    await waitFor(() => expect(buttonByText(rendered.host, "Reload draft").disabled).toBe(false));

    vi.mocked(dialogs.confirm).mockResolvedValue(true);
    vi.mocked(getEtlDefinition).mockImplementationOnce(async (_context, id) => draft(id, 8));
    click(buttonByText(rendered.host, "Reload draft"));
    await waitFor(() => expect(inputByLabel(rendered.host, "Output field 1 name").value).toBe("name"));
    expect(getEtlDefinition).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(buttonContaining(rendered.host, "orders")?.textContent).toContain("Revision 8 · published through v1"));
    expect(dialogs.confirm).toHaveBeenLastCalledWith(expect.objectContaining({ confirmLabel: "Reload saved draft" }));
  });

  it("invalidates cached capability metadata when retrying an unavailable definitions endpoint", async () => {
    const context = endpointContext();
    const invalidate = vi.spyOn(await import("../etlApi"), "invalidateEtlApiCapabilityCache");
    vi.mocked(listEtlDefinitions)
      .mockRejectedValueOnce(new EtlApiError("unavailable", null, "etl.capability-unavailable"))
      .mockResolvedValueOnce(page([summary("appeared-after-enable")]))
      .mockResolvedValue(page([summary("appeared-after-enable")]));
    const rendered = mount(context);
    await waitFor(() => expect(rendered.host.textContent).toContain("ETL API is unavailable"));
    click(buttonByText(rendered.host, "Retry"));
    await waitFor(() => expect(buttonContaining(rendered.host, "appeared-after-enable")).toBeTruthy());
    expect(invalidate).toHaveBeenCalledWith(context);
    invalidate.mockRestore();
  });

  it("loads definition and binding pages only after explicit requests", async () => {
    const context = endpointContext();
    vi.mocked(listEtlDefinitions)
      .mockResolvedValueOnce(page([summary("orders")], "definitions-next"))
      .mockResolvedValueOnce(page([summary("returns")]));
    vi.mocked(listEtlBindings)
      .mockResolvedValueOnce(page(bindings().slice(2), "bindings-next"))
      .mockResolvedValueOnce(page(bindings().slice(0, 2)));
    const rendered = mount(context);

    await waitFor(() => expect(buttonContaining(rendered.host, "orders")).toBeTruthy());
    expect(listEtlDefinitions).toHaveBeenCalledTimes(1);
    expect(listEtlBindings).toHaveBeenCalledTimes(1);
    click(buttonByText(rendered.host, "Load more definitions"));
    await waitFor(() => expect(buttonContaining(rendered.host, "returns")).toBeTruthy());
    expect(listEtlDefinitions).toHaveBeenNthCalledWith(2, context, { limit: 50, cursor: "definitions-next" }, expect.any(AbortSignal));

    click(buttonContaining(rendered.host, "orders")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Draft: orders"));
    expect(selectByLabel(rendered.host, "Source file").selectedOptions[0].textContent).toBe("source (not loaded for Source CSV)");
    expect(rendered.host.textContent).toContain("Load more to reach aliases outside the current page.");
    expect(rendered.host.textContent).toContain("No source csv aliases are in the loaded bindings yet. Load more to check other pages.");
    click(buttonByText(rendered.host, "Load more bindings"));
    await waitFor(() => expect(listEtlBindings).toHaveBeenCalledTimes(2));
    expect(listEtlBindings).toHaveBeenNthCalledWith(2, context, { limit: 50, cursor: "bindings-next" }, expect.any(AbortSignal));
    await waitFor(() => expect(selectByLabel(rendered.host, "Source file").selectedOptions[0].textContent).toBe("source"));
  });

  it("ignores a stale definition page response after the endpoint context changes", async () => {
    let resolveOld: ((result: EtlPage<EtlDefinitionSummaryView>) => void) | undefined;
    let oldSignal: AbortSignal | undefined;
    const oldContext = endpointContext("old");
    const newContext = endpointContext("new");
    vi.mocked(listEtlDefinitions).mockImplementation((requestContext, _request, signal) => {
      if (requestContext === oldContext) {
        oldSignal = signal;
        return new Promise(resolve => { resolveOld = resolve; });
      }
      return Promise.resolve(page([summary("new-scope-definition")]));
    });

    const rendered = mount(oldContext);
    await waitFor(() => expect(resolveOld).toBeDefined());
    rendered.render(newContext);
    await waitFor(() => expect(buttonContaining(rendered.host, "new-scope-definition")).toBeTruthy());
    resolveOld?.(page([summary("old-scope-definition")]));
    await waitFor(() => expect(oldSignal?.aborted).toBe(true));
    expect(buttonContaining(rendered.host, "old-scope-definition")).toBeNull();
  });
});

let active: { host: HTMLDivElement; root: Root; cleanup(): void } | null = null;

function cleanupActive() {
  const current = active;
  active = null;
  current?.cleanup();
}

function mount(context: StudioEndpointContext, dialogs = createDialogs(), profile: "file" | "connector" = "file") {
  const host = document.createElement("div");
  document.body.append(host);
  const root = createRoot(host);
  const rendered = {
    host,
    root,
    render(nextContext: StudioEndpointContext, nextProfile = profile) {
      flushSync(() => root.render(<EtlDefinitionsPage context={nextContext} dialogs={dialogs} profile={nextProfile} />));
    },
    cleanup() {
      flushSync(() => root.unmount());
      host.remove();
    }
  };
  active = rendered;
  rendered.render(context);
  return rendered;
}

function createDialogs(confirmed = true): StudioDialogApi {
  return {
    confirm: vi.fn(async () => confirmed),
    prompt: vi.fn(async () => "new-pipeline"),
    alert: vi.fn(async () => {})
  };
}

function endpointContext(name = "host"): StudioEndpointContext {
  return { baseUrl: `https://${name}.example/studio`, http: {} as StudioEndpointContext["http"] };
}

function page<T>(items: T[], nextCursor: string | null = null): EtlPage<T> {
  return { items, nextCursor };
}

function summary(definitionId: string): EtlDefinitionSummaryView {
  return { definitionId, revision: 4, lastPublishedVersion: 1, updatedAt: "2026-10-20T10:00:00Z" };
}

function draft(definitionId: string, revision = 4, pipeline: EtlPipeline = createDefaultEtlPipeline()): EtlDraftView {
  return { definitionId, revision, lastPublishedVersion: 1, pipeline, updatedAt: "2026-10-20T10:00:00Z" };
}

function connectorDraft(definitionId: string, revision = 4, pipeline = createDefaultEtlConnectorPipeline()) {
  return { ...draft(definitionId, revision), pipeline };
}

function bindings(): EtlBindingSummary[] {
  return [
    { alias: "source", role: "Source" }, { alias: "lookup", role: "Lookup" },
    { alias: "output", role: "MainOutput" }, { alias: "errors", role: "ErrorOutput" }
  ];
}

function validResult(revision: number): EtlValidationView {
  return { revision, definitionVersion: 2, contentHash: hash("b"), diagnostics: [] };
}

function published(definitionId: string, definitionVersion: number, contentHash: string) {
  return { status: "published", version: version(definitionId, definitionVersion, contentHash) } as const;
}

function version(definitionId: string, definitionVersion: number, contentHash: string): EtlPublishedVersionView {
  return {
    definitionId, definitionVersion, contentHash,
    selection: `etl1:${definitionId}:${definitionVersion}:${contentHash}`,
    publishedAt: "2026-10-20T11:00:00Z"
  };
}

function artifact(definitionId: string, definitionVersion: number): EtlPublishedArtifactView {
  return { version: version(definitionId, definitionVersion, hash("b")), pipeline: createDefaultEtlPipeline() };
}

function hash(letter: string) { return letter.repeat(64); }

function buttonByText(container: Element, label: string): HTMLButtonElement {
  const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent === label);
  if (!button) throw new Error(`No button found: ${label}`);
  return button;
}

function buttonContaining(container: Element, text: string): HTMLButtonElement | null {
  return Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent?.includes(text)) ?? null;
}

function inputByLabel(container: Element, label: string, readOnlyArtifact = false): HTMLInputElement {
  const matches = Array.from(container.querySelectorAll("label")).filter(candidate => candidate.textContent === label);
  const target = readOnlyArtifact ? matches.at(-1) : matches[0];
  if (!target?.htmlFor) throw new Error(`No label found: ${label}`);
  const input = Array.from(container.querySelectorAll<HTMLInputElement>("input")).find(candidate => candidate.id === target.htmlFor);
  if (!input) throw new Error(`No input found for label: ${label}`);
  return input;
}

function selectByLabel(container: Element, label: string): HTMLSelectElement {
  const target = Array.from(container.querySelectorAll("label")).find(candidate => candidate.textContent === label);
  if (!target?.htmlFor) throw new Error(`No label found: ${label}`);
  const select = Array.from(container.querySelectorAll<HTMLSelectElement>("select")).find(candidate => candidate.id === target.htmlFor);
  if (!select) throw new Error(`No select found for label: ${label}`);
  return select;
}

function change(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function click(button: HTMLButtonElement) {
  flushSync(() => button.click());
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { assertion(); return; }
    catch (error) { lastError = error; }
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw lastError instanceof Error ? lastError : new Error("Timed out waiting for ETL definitions page.");
}
