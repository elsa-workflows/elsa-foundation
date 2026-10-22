import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { clearApiCapabilityCache } from "../api/capabilities";
import { WorkflowDefinitions } from "../workflow-editor/WorkflowDefinitions";
import { useWorkflowDefinitionBrowseSession } from "../workflow-editor/workflowDefinitionBrowseSession";

let mounted: { root: Root; container: HTMLDivElement } | null = null;
let previousClipboard: PropertyDescriptor | undefined;

afterEach(() => {
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
  it("keeps the definition ID primary and isolates copy and disclosure actions from row navigation", async () => {
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
    const disclosure = row.querySelector<HTMLButtonElement>(".wf-definition-secondary-identifiers .studio-disclosure-fold-trigger")!;
    const copyDefinitionId = row.querySelector<HTMLButtonElement>('button[aria-label="Copy Definition ID"]')!;
    const details = row.querySelector<HTMLElement>(".wf-definition-secondary-identifiers .studio-disclosure-fold-body")!;
    expect(details.hasAttribute("hidden")).toBe(true);
    expect(details.querySelector('[aria-label="Draft ID: draft-exact-id"]')).toBeTruthy();

    for (const key of ["Enter", " "]) {
      flushSync(() => disclosure.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
      flushSync(() => copyDefinitionId.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
    }
    expect(navigate).not.toHaveBeenCalled();

    click(disclosure);
    expect(disclosure.getAttribute("aria-expanded")).toBe("true");
    for (const identifier of [definition.draftId, definition.draftSourceVersionId, definition.latestVersionId, definition.publishedVersionId]) {
      expect(details.textContent).toContain(identifier);
    }

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
    expect(actions?.querySelector("button[title='Create workflow']")).toBeTruthy();
    expect(actions?.querySelector("button[title='Refresh workflow definitions']")).toBeTruthy();
    expect(header!.compareDocumentPosition(search!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(header!.compareDocumentPosition(states!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(actions?.querySelector("button")?.textContent).toContain("Create workflow");
  });
});

function render(contextValue: StudioEndpointContext, onOpen: (id: string) => void) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const ai = { promptActions: { list: () => [] }, dispatchPrompt: vi.fn() } as unknown as StudioAiContributionApi;
  function Harness() {
    const session = useWorkflowDefinitionBrowseSession(contextValue);
    return <WorkflowDefinitions context={contextValue} ai={ai} onOpen={onOpen} session={session} />;
  }
  flushSync(() => root.render(<Harness />));
  return container;
}

function context(getJson: (url: string) => Promise<unknown>): StudioEndpointContext {
  return { baseUrl: "https://studio.test/", http: { getJson } } as unknown as StudioEndpointContext;
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
