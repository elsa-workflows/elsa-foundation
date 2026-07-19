import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { useWorkflowEditorData } from "../workflow-editor/useWorkflowEditorData";
import { clearApiCapabilityCache } from "../api/capabilities";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  vi.clearAllMocks();
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

function createApi(expressionResponses: Array<unknown | Error>) {
  let expressionAttempt = 0;
  const getJson = vi.fn(async (path: string) => {
    if (path === "/capabilities") {
      return {
        capabilities: [
          { id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }] },
          { id: "elsa.api.activity-design", contractVersion: "1", links: [{ rel: "activity-catalog", href: "design/activities/catalog" }] },
          { id: "elsa.api.expressions", contractVersion: "1", links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }] }
        ]
      };
    }
    if (path.endsWith("/definitions/definition-1")) {
      return {
        definition: { id: "definition-1", name: "Test", createdAt: "", lastModifiedAt: "", versionCount: 0 },
        versions: []
      };
    }
    if (path.includes("/design/activities/catalog")) return { activities: [] };
    if (path.endsWith("/expressions/descriptors")) {
      const response = expressionResponses[Math.min(expressionAttempt++, expressionResponses.length - 1)];
      if (response instanceof Error) throw response;
      return await response;
    }
    throw new Error(`Unexpected GET ${path}`);
  });
  return { context: { baseUrl: "", http: { getJson } } as unknown as StudioEndpointContext, getJson };
}

const callbacks = {
  resetHistory: vi.fn(),
  loadDraft: vi.fn(),
  markSaved: vi.fn(),
  setError: vi.fn()
};

function Probe({ context }: { context: StudioEndpointContext }) {
  const data = useWorkflowEditorData({ context, definitionId: "definition-1", ...callbacks });
  return (
    <div>
      <output data-testid="expressions" data-status={data.expressionDescriptorStatus}>
        {data.expressionDescriptors.map(descriptor => descriptor.type).join(",")}
      </output>
      <button type="button" onClick={() => void data.reload()}>Reload definition</button>
      <button type="button" onClick={() => void data.reloadExpressionDescriptors()}>Retry descriptors</button>
    </div>
  );
}

function render(context: StudioEndpointContext) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(<Probe context={context} />));
  return container;
}

function rerender(context: StudioEndpointContext) {
  flushSync(() => active!.root.render(<Probe context={context} />));
}

describe("useWorkflowEditorData expression descriptor contract", () => {
  it("starts empty and reports backend empty results honestly", async () => {
    const api = createApi([[]]);
    const container = render(api.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;

    expect(output.dataset.status).toBe("loading");
    expect(output.textContent).toBe("");
    await waitFor(() => expect(output.dataset.status).toBe("ready"));
    expect(output.textContent).toBe("");
  });

  it("reports an initial transport failure without substituting client descriptors", async () => {
    const api = createApi([new Error("offline"), new Error("offline")]);
    const container = render(api.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;

    await waitFor(() => expect(output.dataset.status).toBe("failed"));
    expect(output.textContent).toBe("");
  });

  it("preserves the last backend snapshot when a reload fails", async () => {
    const descriptor = { type: "Python", displayName: "Python", editingMode: "text" };
    const api = createApi([[descriptor], new Error("offline"), new Error("offline")]);
    const container = render(api.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;

    await waitFor(() => expect(output.dataset.status).toBe("ready"));
    expect(output.textContent).toBe("Python");
    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry descriptors")!.click());
    expect(output.dataset.status).toBe("loading");
    expect(output.textContent).toBe("Python");
    await waitFor(() => expect(output.dataset.status).toBe("failed"));
    expect(output.textContent).toBe("Python");
  });

  it("descriptor Retry does not reload the draft or undo-history baseline", async () => {
    const descriptor = { type: "Python", displayName: "Python", editingMode: "text" };
    const api = createApi([[descriptor], new Error("offline"), new Error("offline")]);
    const container = render(api.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;
    await waitFor(() => expect(output.dataset.status).toBe("ready"));
    callbacks.resetHistory.mockClear();
    callbacks.loadDraft.mockClear();
    callbacks.markSaved.mockClear();

    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry descriptors")!.click());
    await waitFor(() => expect(output.dataset.status).toBe("failed"));

    expect(callbacks.resetHistory).not.toHaveBeenCalled();
    expect(callbacks.loadDraft).not.toHaveBeenCalled();
    expect(callbacks.markSaved).not.toHaveBeenCalled();
  });

  it("does not expose a descriptor snapshot from a previous authority", async () => {
    const first = createApi([[{ type: "Python", displayName: "Python", editingMode: "text" }]]);
    const second = createApi([new Error("offline"), new Error("offline")]);
    const container = render(first.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;
    await waitFor(() => expect(output.textContent).toBe("Python"));

    rerender(second.context);
    expect(output.dataset.status).toBe("loading");
    expect(output.textContent).toBe("");
    await waitFor(() => expect(output.dataset.status).toBe("failed"));
    expect(output.textContent).toBe("");
  });

  it("ignores a stale descriptor response when a newer refresh completes first", async () => {
    let resolveOlder!: (value: unknown) => void;
    let resolveNewer!: (value: unknown) => void;
    const older = new Promise(resolve => { resolveOlder = resolve; });
    const newer = new Promise(resolve => { resolveNewer = resolve; });
    const api = createApi([
      [{ type: "Python", displayName: "Python", editingMode: "text" }],
      older,
      newer
    ]);
    const container = render(api.context);
    const output = container.querySelector<HTMLOutputElement>("[data-testid='expressions']")!;
    const retry = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry descriptors")!;
    await waitFor(() => expect(output.dataset.status).toBe("ready"));

    flushSync(() => retry.click());
    flushSync(() => retry.click());
    resolveNewer([{ type: "Ruby", displayName: "Ruby", editingMode: "text" }]);
    await waitFor(() => expect(output.textContent).toBe("Ruby"));
    resolveOlder([{ type: "JavaScript", displayName: "JavaScript", editingMode: "text" }]);
    await Promise.resolve();
    await Promise.resolve();

    expect(output.textContent).toBe("Ruby");
  });
});

async function waitFor(assertion: () => void) {
  const deadline = Date.now() + 2_000;
  while (true) {
    try {
      assertion();
      return;
    } catch (error) {
      if (Date.now() >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 5));
    }
  }
}
