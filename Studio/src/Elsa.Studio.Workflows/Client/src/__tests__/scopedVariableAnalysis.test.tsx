import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useScopedVariableAnalysis } from "../api/workflows";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowDefinitionState } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

function state(name = "Counter"): WorkflowDefinitionState {
  return {
    variables: [{ referenceKey: "var-1", name, type: { alias: "Int32", collectionKind: "Single" }, storageDriverType: null, default: null }],
    rootActivity: {
      nodeId: "root",
      activityVersionId: "write-line-v1",
      inputs: [],
      outputs: [],
      structure: null,
      text: { typeName: "String", expression: { type: "Literal", value: "live draft" } }
    },
    inputs: [],
    outputs: []
  };
}

function Probe({ context, value, nodeId }: { context: never; value: WorkflowDefinitionState | null; nodeId: string | null }) {
  const analysis = useScopedVariableAnalysis(context, value, nodeId);
  return <><output data-status={analysis.status}>{analysis.visibleVariables.map(x => x.name).join(",")}</output><button onClick={analysis.retry}>Retry</button></>;
}

async function render(context: never, value: WorkflowDefinitionState | null, nodeId: string | null) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  await rerender(context, value, nodeId);
  return container;
}

async function rerender(context: never, value: WorkflowDefinitionState | null, nodeId: string | null) {
  flushSync(() => active!.root.render(<Probe context={context} value={value} nodeId={nodeId} />));
  await Promise.resolve();
}

function context(options: {
  capabilities?: object;
  capabilityError?: Error;
  get?: ReturnType<typeof vi.fn>;
  post?: ReturnType<typeof vi.fn>;
}) {
  const getJson = options.get ?? (options.capabilityError
    ? vi.fn().mockRejectedValue(options.capabilityError)
    : vi.fn().mockResolvedValue({
        capabilities: [{
          id: "elsa.api.workflow-design",
          contractVersion: "1",
          links: (options.capabilities as { scopedVariableAnalysis?: boolean } | undefined)?.scopedVariableAnalysis === false
            ? []
            : [{ rel: "scoped-variable-analysis", href: "design/workflows/scoped-variables/analyze" }]
        }]
      }));
  const postJson = options.post ?? vi.fn().mockResolvedValue({
    visibleVariables: [{ referenceKey: "var-1", name: "Counter", scopeId: "workflow", isWorkflowScope: true }],
    shadowingWarnings: []
  });
  return { value: { baseUrl: "test://scoped-variables", http: { getJson, postJson } } as never, getJson, postJson };
}

describe("scoped-variable analysis capability gating", () => {
  it("posts canonicalized live draft state when the backend advertises support", async () => {
    const api = context({});
    const container = await render(api.value, state(), "root");

    await waitFor(() => {
      expect(api.postJson).toHaveBeenCalledTimes(1);
      expect(container.querySelector("output")?.dataset.status).toBe("ready");
    });

    expect(api.getJson).toHaveBeenCalledWith("/capabilities");
    const [path, body] = api.postJson.mock.calls[0];
    expect(path).toBe("/design/workflows/scoped-variables/analyze");
    expect(body.nodeId).toBe("root");
    expect(body.state.rootActivity.inputs).toEqual([
      { referenceKey: "text", value: { value: "live draft", expressionType: "Literal" } }
    ]);
    expect(body.state.rootActivity).not.toHaveProperty("text");
  });

  it("reports unavailable and never posts when support is false", async () => {
    for (const api of [context({ capabilities: { scopedVariableAnalysis: false } })]) {
      const container = await render(api.value, state(), "root");
      await waitFor(() => expect(container.querySelector("output")?.dataset.status).toBe("unavailable"));
      expect(api.postJson).not.toHaveBeenCalled();
      flushSync(() => active!.root.unmount());
      active!.container.remove();
      active = null;
    }
  });

  it("reports a retryable error when capability discovery fails", async () => {
    const getJson = vi.fn()
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ scopedVariableAnalysis: false });
    const api = context({ get: getJson });
    const container = await render(api.value, state(), "root");
    await waitFor(() => expect(container.querySelector("output")?.dataset.status).toBe("error"));

    flushSync(() => container.querySelector("button")?.click());
    await waitFor(() => expect(getJson).toHaveBeenCalledTimes(2));
  });

  it("returns ready empty data without posting when no node is selected", async () => {
    const api = context({});
    const container = await render(api.value, state(), null);

    await waitFor(() => expect(container.querySelector("output")?.dataset.status).toBe("ready"));
    expect(api.postJson).not.toHaveBeenCalled();
  });

  it("stays loading while capability discovery is pending even when draft state is not loaded", async () => {
    const capability = deferred<object>();
    const api = context({ get: vi.fn(() => capability.promise) });
    const container = await render(api.value, null, null);

    expect(container.querySelector("output")?.dataset.status).toBe("loading");
    expect(api.postJson).not.toHaveBeenCalled();
  });

  it("preserves prior results and retries when a supported analysis request fails", async () => {
    const post = vi.fn()
      .mockResolvedValueOnce({ visibleVariables: [{ referenceKey: "var-1", name: "Counter", scopeId: "workflow", isWorkflowScope: true }], shadowingWarnings: [] })
      .mockRejectedValueOnce(new Error("failed"))
      .mockResolvedValueOnce({ visibleVariables: [{ referenceKey: "var-1", name: "Recovered", scopeId: "workflow", isWorkflowScope: true }], shadowingWarnings: [] });
    const api = context({ post });
    const container = await render(api.value, state(), "root");
    await waitFor(() => expect(container.querySelector("output")?.dataset.status).toBe("ready"));

    await rerender(api.value, state("Renamed"), "root");
    await waitFor(() => expect(container.querySelector("output")?.dataset.status).toBe("error"));
    expect(container.querySelector("output")?.textContent).toBe("Counter");

    flushSync(() => container.querySelector("button")?.click());
    await waitFor(() => expect(container.querySelector("output")?.textContent).toBe("Recovered"));

    expect(post).toHaveBeenCalledTimes(3);
  });

  it("aborts obsolete work and ignores a stale response", async () => {
    const first = deferred<object>();
    const second = deferred<object>();
    const postJson = vi.fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    const api = context({ post: postJson });
    const container = await render(api.value, state(), "first");
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    const firstSignal = postJson.mock.calls[0][2].signal as AbortSignal;

    await rerender(api.value, state(), "second");
    await waitFor(() => {
      expect(postJson).toHaveBeenCalledTimes(2);
      expect(firstSignal.aborted).toBe(true);
    });

    second.resolve({ visibleVariables: [{ referenceKey: "b", name: "Second", scopeId: "workflow", isWorkflowScope: true }], shadowingWarnings: [] });
    await second.promise;
    await waitFor(() => expect(container.querySelector("output")?.textContent).toBe("Second"));
    first.resolve({ visibleVariables: [{ referenceKey: "a", name: "First", scopeId: "workflow", isWorkflowScope: true }], shadowingWarnings: [] });
    await first.promise;

    expect(container.querySelector("output")?.textContent).toBe("Second");
    expect(container.querySelector("output")?.dataset.status).toBe("ready");
  });

  it("refreshes only when visibility-affecting state changes", async () => {
    const api = context({});
    await render(api.value, state(), "root");
    await waitFor(() => expect(api.postJson).toHaveBeenCalledTimes(1));

    await rerender(api.value, { ...state(), outputs: [{ name: "unrelated" }] }, "root");
    expect(api.postJson).toHaveBeenCalledTimes(1);

    await rerender(api.value, state("Renamed"), "root");
    await waitFor(() => expect(api.postJson).toHaveBeenCalledTimes(2));
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 30; attempt++) {
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
