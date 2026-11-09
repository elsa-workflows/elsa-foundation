import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getDefinition, listDefinitions } from "../api/workflowDesign";
import type { WorkflowDefinitionSummary } from "../workflowTypes";
import { RunWorkflowPicker } from "../workflow-editor/RunWorkflowPicker";

vi.mock("../api/workflowDesign", () => ({
  getDefinition: vi.fn(),
  listDefinitions: vi.fn()
}));

let mounted: { root: Root; container: HTMLDivElement } | null = null;

afterEach(() => {
  vi.resetAllMocks();
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
});

describe("RunWorkflowPicker", () => {
  it("searches by name, distinguishes duplicate identities, and selects the exact definition by keyboard", async () => {
    vi.mocked(listDefinitions).mockResolvedValue({
      definitions: [definition("definition-a", "Process order"), definition("definition-b", "Process order")],
      page: 1,
      pageSize: 20,
      nextContinuationToken: null,
      isPaged: true
    });
    const { container, selected, submitted } = renderPicker();
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!;
    fill(search, "Process order");

    await waitFor(() => expect(container.querySelectorAll("[role='option']")).toHaveLength(2));
    expect(container.textContent).toContain("definition-a");
    expect(container.textContent).toContain("definition-b");
    expect(listDefinitions).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ search: "Process order", state: "all" }));
    expect(getDefinition).not.toHaveBeenCalled();

    const implicitSubmit = key(search, "Enter");
    expect(implicitSubmit.defaultPrevented).toBe(true);
    expect(selected()).toBe("");
    expect(submitted).not.toHaveBeenCalled();

    key(search, "ArrowDown");
    key(search, "ArrowDown");
    const chooseExactDefinition = key(search, "Enter");
    expect(chooseExactDefinition.defaultPrevented).toBe(true);
    await waitFor(() => expect(selected()).toBe("definition-b"));
    expect(submitted).not.toHaveBeenCalled();
  });

  it("hydrates a URL-selected identity and keeps its exact ID visible when lookup fails", async () => {
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [], page: 1, pageSize: 20, nextContinuationToken: null, isPaged: true });
    vi.mocked(getDefinition).mockRejectedValue(new Error("Definition is unavailable"));
    const { container } = renderPicker("opaque-definition-id");

    await waitFor(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Definition is unavailable"));
    expect(container.textContent).toContain("opaque-definition-id");
    expect(container.querySelector("button[aria-label='Copy Workflow definition ID']")).toBeTruthy();
    expect(getDefinition).toHaveBeenCalledWith(expect.anything(), "opaque-definition-id");
  });

  it("keeps a user's search text when URL identity hydration finishes later", async () => {
    let resolveIdentity!: (value: Awaited<ReturnType<typeof getDefinition>>) => void;
    vi.mocked(getDefinition).mockImplementation(() => new Promise(resolve => { resolveIdentity = resolve; }));
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [], page: 1, pageSize: 20, nextContinuationToken: null, isPaged: true });
    const { container } = renderPicker("selected-id");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!;

    await waitFor(() => expect(getDefinition).toHaveBeenCalledWith(expect.anything(), "selected-id"));
    fill(search, "a newer search");
    resolveIdentity({ definition: definition("selected-id", "Resolved workflow") } as Awaited<ReturnType<typeof getDefinition>>);

    await waitFor(() => expect(container.querySelector("[role='status']")?.textContent).not.toContain("Loading selected workflow"));
    expect(search.value).toBe("a newer search");
  });

  it("rehydrates the same selected ID after the endpoint context changes and ignores the old response", async () => {
    let resolveOld!: (value: Awaited<ReturnType<typeof getDefinition>>) => void;
    vi.mocked(getDefinition)
      .mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce({ definition: definition("same-id", "Name from new host") } as Awaited<ReturnType<typeof getDefinition>>);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [], page: 1, pageSize: 20, nextContinuationToken: null, isPaged: true });
    const { container, setContext } = renderPicker("same-id");
    await waitFor(() => expect(getDefinition).toHaveBeenCalledTimes(1));

    setContext({} as StudioEndpointContext);
    await waitFor(() => expect(getDefinition).toHaveBeenCalledTimes(2));
    resolveOld({ definition: definition("same-id", "Stale name from old host") } as Awaited<ReturnType<typeof getDefinition>>);

    await waitFor(() => expect(container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")?.value).toBe("Name from new host"));
    expect(container.textContent).not.toContain("Stale name from old host");
  });

  it("ignores stale search responses and supports server continuation", async () => {
    let resolveOld!: (value: Awaited<ReturnType<typeof listDefinitions>>) => void;
    vi.mocked(listDefinitions)
      .mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce({
        definitions: [definition("new-result", "New result")], page: 1, pageSize: 20,
        nextContinuationToken: "next-page", isPaged: true
      })
      .mockResolvedValueOnce({
        definitions: [definition("continued-result", "Continued result")], page: 2, pageSize: 20,
        nextContinuationToken: null, isPaged: true
      });
    const { container } = renderPicker();
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!;
    fill(search, "old");
    const listboxId = search.getAttribute("aria-controls");
    expect(listboxId).toBeTruthy();
    expect(container.querySelector(`#${listboxId}`)?.getAttribute("role")).toBe("listbox");
    fill(search, "new");

    await waitFor(() => expect(container.textContent).toContain("New result"));
    resolveOld({ definitions: [definition("old-result", "Old result")], page: 1, pageSize: 20, nextContinuationToken: null, isPaged: true });
    await waitFor(() => expect(container.querySelectorAll("[role='option']")).toHaveLength(1));
    expect(container.textContent).not.toContain("Old result");

    click(container.querySelector<HTMLButtonElement>("button[aria-label='Next workflow definitions']")!);
    await waitFor(() => expect(container.textContent).toContain("Continued result"));
    expect(listDefinitions).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ search: "new", continuationToken: "next-page" }));
  });

  it("keeps results open when focus moves to pagination and closes when focus leaves the picker", async () => {
    vi.mocked(listDefinitions).mockResolvedValue({
      definitions: [definition("first-result", "First result")],
      page: 1,
      pageSize: 20,
      nextContinuationToken: "next-page",
      isPaged: true
    });
    const { container } = renderPicker();
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!;
    fill(search, "first");

    await waitFor(() => expect(container.querySelector("button[aria-label='Next workflow definitions']")).not.toBeNull());
    const nextPage = container.querySelector<HTMLButtonElement>("button[aria-label='Next workflow definitions']")!;
    flushSync(() => search.dispatchEvent(new FocusEvent("focusout", { bubbles: true, relatedTarget: nextPage })));
    expect(search.getAttribute("aria-expanded")).toBe("true");
    expect(container.querySelector("[role='listbox']")).not.toBeNull();

    const apply = container.querySelector<HTMLButtonElement>("button[type='submit']")!;
    flushSync(() => nextPage.dispatchEvent(new FocusEvent("focusout", { bubbles: true, relatedTarget: apply })));
    expect(search.getAttribute("aria-expanded")).toBe("false");
  });

  it("announces empty and failed search results separately", async () => {
    vi.mocked(listDefinitions).mockResolvedValueOnce({ definitions: [], page: 1, pageSize: 20, nextContinuationToken: null, isPaged: true });
    const first = renderPicker();
    fill(first.container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!, "missing");
    await waitFor(() => expect(first.container.querySelector("[role='status']")?.textContent).toContain("No workflows found"));
    unmount();

    vi.mocked(listDefinitions).mockRejectedValue(new Error("Design is unavailable"));
    const second = renderPicker();
    fill(second.container.querySelector<HTMLInputElement>("input[aria-label='Workflow']")!, "approval");
    await waitFor(() => expect(second.container.querySelector("[role='alert']")?.textContent).toContain("Design is unavailable"));
    expect(second.container.querySelector("button[aria-label='Retry workflow search']")).toBeTruthy();
  });
});

function renderPicker(initialDefinitionId = "", initialContext: StudioEndpointContext = {} as StudioEndpointContext) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  let currentSelection = initialDefinitionId;
  let updateContext!: (context: StudioEndpointContext) => void;
  const submitted = vi.fn();
  function Harness() {
    const [selected, setSelected] = useState(initialDefinitionId);
    const [endpointContext, setEndpointContext] = useState(initialContext);
    updateContext = setEndpointContext;
    currentSelection = selected;
    return <form onSubmit={event => { event.preventDefault(); submitted(); }}>
      <RunWorkflowPicker
        context={endpointContext}
        definitionId={selected}
        onChange={setSelected}
      />
      <button type="submit">Apply filters</button>
    </form>;
  }
  flushSync(() => root.render(<Harness />));
  return {
    container,
    selected: () => currentSelection,
    submitted,
    setContext: (context: StudioEndpointContext) => flushSync(() => updateContext(context))
  };
}

function definition(id: string, name: string): WorkflowDefinitionSummary {
  return {
    id,
    name,
    createdAt: "2026-01-01T00:00:00Z",
    lastModifiedAt: "2026-01-01T00:00:00Z",
    versionCount: 1,
    latestVersionId: `${id}-version`,
    latestVersion: "1"
  };
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 50; attempt++) {
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

function fill(input: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function key(input: HTMLInputElement, key: string) {
  const event = new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true });
  flushSync(() => input.dispatchEvent(event));
  return event;
}

function click(element: HTMLElement) {
  flushSync(() => element.click());
}

function unmount() {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
}
