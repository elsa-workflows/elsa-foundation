import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { projectRecommendedPalette, useWorkflowEditorData } from "../workflow-editor/useWorkflowEditorData";
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

function createApi(
  expressionResponses: Array<unknown | Error>,
  options: {
    activities?: unknown[];
    recommended?: unknown[];
  } = {}
) {
  let expressionAttempt = 0;
  const getJson = vi.fn(async (path: string) => {
    if (path === "/capabilities") {
      return {
        capabilities: [
          { id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }] },
          { id: "elsa.api.activity-design", contractVersion: "1", links: [
            { rel: "activity-catalog", href: "design/activities/catalog" },
            { rel: "recommended-activity-definitions", href: "design/activities/definitions/picker" }
          ] },
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
    if (path.includes("/design/activities/catalog")) return { activities: options.activities ?? [] };
    if (path.includes("/design/activities/definitions/picker")) return { items: options.recommended ?? [], nextOffset: null };
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
      <output data-testid="palette">{data.paletteCatalog.map(activity => activity.activityVersionId).join(",")}</output>
      <output data-testid="catalog">
        {data.catalog.map(activity => `${activity.activityVersionId}:${activity.activityDefinitionVersion ?? "-"}`).join(",")}
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

  it("offers only the exact recommended catalog version while retaining historical versions for resolution", async () => {
    const version = (activityVersionId: string, semanticVersion: string) => ({
      activityVersionId,
      activityTypeKey: "Contoso.Invoice",
      version: semanticVersion,
      displayName: "Invoice",
      category: "Finance",
      executionType: "Action",
      available: true,
      inputs: [],
      outputs: [],
      ports: [],
      containerStructure: null,
      authoringTemplate: { nodeId: "activity", activityVersionId, inputs: {}, outputs: {}, structure: null }
    });
    const api = createApi([[]], {
      activities: [
        {
          ...version("write-line-v1", "1.0.0"),
          activityTypeKey: "Elsa.WriteLine",
          displayName: "Write Line",
          category: "Primitives"
        },
        version("invoice-v10", "10.0.0"),
        version("invoice-v2", "2.0.0")
      ],
      recommended: [{
        definitionId: "invoice-definition",
        activityTypeKey: "Contoso.Invoice",
        category: "Finance",
        displayName: "Invoice",
        versionId: "invoice-v2",
        version: "2.0.0",
        isAvailable: true
      }]
    });
    const container = render(api.context);

    await waitFor(() => expect(container.querySelector("[data-testid='palette']")?.textContent).toBe("invoice-v2"));
    expect(container.querySelector("[data-testid='catalog']")?.textContent)
      .toBe("write-line-v1:-,invoice-v10:10.0.0,invoice-v2:2.0.0");
  });

  it("keeps built-in engine intrinsics in the palette when the backend advertises recommendations", async () => {
    // Live regression (#929): with the recommendation relation advertised, the palette is projected from
    // the recommendation set. Built-in intrinsics (Set Variable / Set Output) have no persisted catalog row
    // and therefore no recommendation, so the projection dropped them even though the catalog GET returns
    // them. This drives the full hook path: listActivities (real normalization) → projectRecommendedPalette.
    const intrinsic = (activityVersionId: string, activityTypeKey: string, displayName: string, intrinsicBlock: unknown) => ({
      activityVersionId,
      activityTypeKey,
      version: "1",
      displayName,
      category: "Primitives",
      description: `${displayName} intrinsic`,
      executionType: "Action",
      available: true,
      availabilityReason: null,
      // Raw backend shape: inputs carry `type` (not `typeName`); authoringTemplate.inputs is a dictionary.
      inputs: [{ referenceKey: "value", name: "Value", type: "Elsa.Any", displayName: "Value", order: 1, isBrowsable: true, isRequired: true, isNullable: true, uiHint: "single-line" }],
      outputs: [],
      ports: [{ name: "Done", displayName: "Done", type: null, isBrowsable: true }],
      containerStructure: null,
      authoringTemplate: { nodeId: "intrinsic", activityVersionId, inputs: {}, outputs: {}, structure: null },
      intrinsic: intrinsicBlock
    });
    const api = createApi([[]], {
      activities: [
        {
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.WriteLine",
          version: "1.0.0",
          displayName: "Write Line",
          category: "Primitives",
          executionType: "Action",
          available: true,
          inputs: [],
          outputs: [],
          ports: [],
          containerStructure: null,
          authoringTemplate: { nodeId: "activity", activityVersionId: "write-line-v1", inputs: {}, outputs: {}, structure: null }
        },
        intrinsic("elsa.intrinsic.set@1", "Elsa.SetVariable", "Set Variable", { kind: "Set", valueInputKey: "value", variableInputKey: "variable", outputNameInputKey: null }),
        intrinsic("elsa.intrinsic.set-output@1", "Elsa.SetOutput", "Set Output", { kind: "SetOutput", valueInputKey: "value", variableInputKey: null, outputNameInputKey: "name" })
      ],
      // The backend recommends the reconciled CLR catalog row but not the row-less intrinsics.
      recommended: [{
        definitionId: "write-line-definition",
        activityTypeKey: "Elsa.WriteLine",
        category: "Primitives",
        displayName: "Write Line",
        versionId: "write-line-v1",
        version: "1.0.0",
        isAvailable: true
      }]
    });
    const container = render(api.context);

    await waitFor(() => expect(container.querySelector("[data-testid='palette']")?.textContent).toContain("write-line-v1"));
    const palette = container.querySelector("[data-testid='palette']")!.textContent!.split(",");
    expect(palette).toContain("elsa.intrinsic.set@1");
    expect(palette).toContain("elsa.intrinsic.set-output@1");
  });

  it("re-appends only row-less intrinsics the recommendation projection omits, once, in catalog order", () => {
    const catalogItem = (activityVersionId: string, activityTypeKey: string, extra: Record<string, unknown> = {}) => ({
      activityVersionId,
      activityTypeKey,
      version: "1",
      displayName: activityTypeKey,
      category: "Primitives",
      executionType: "Action",
      available: true,
      inputs: [],
      outputs: [],
      ports: [],
      containerStructure: null,
      authoringTemplate: { nodeId: "activity", activityVersionId, inputs: [], outputs: [], structure: null },
      ...extra
    });
    const setVariable = catalogItem("elsa.intrinsic.set@1", "Elsa.SetVariable", {
      intrinsic: { kind: "Set", valueInputKey: "value", variableInputKey: "variable", outputNameInputKey: null }
    });
    const catalog = [
      catalogItem("write-line-v1", "Elsa.WriteLine"),
      setVariable
    ];
    const recommendation = {
      definitionId: "write-line-definition",
      activityTypeKey: "Elsa.WriteLine",
      category: "Primitives",
      displayName: "Write Line",
      versionId: "write-line-v1",
      version: "1.0.0",
      isAvailable: true
    };

    // The intrinsic is appended after the projected recommendation, exactly once.
    expect(projectRecommendedPalette(catalog, [recommendation]).map(activity => activity.activityVersionId))
      .toEqual(["write-line-v1", "elsa.intrinsic.set@1"]);
    // No recommendations at all: still surfaces the intrinsic (never silently empty for a row-less item).
    expect(projectRecommendedPalette(catalog, []).map(activity => activity.activityVersionId))
      .toEqual(["elsa.intrinsic.set@1"]);
  });

  it("removes a cleared recommendation and rejects retired, stale, or type-mismatched rows", () => {
    const catalog = [{
      activityVersionId: "invoice-v2",
      activityTypeKey: "Contoso.Invoice",
      version: "2.0.0",
      displayName: "Invoice",
      category: "Finance",
      executionType: "Action",
      available: true,
      inputs: [],
      outputs: [],
      ports: [],
      containerStructure: null,
      authoringTemplate: { nodeId: "activity", activityVersionId: "invoice-v2", inputs: [], outputs: [], structure: null }
    }];
    const recommendation = {
      definitionId: "invoice-definition",
      activityTypeKey: "Contoso.Invoice",
      category: "Finance",
      displayName: "Invoice",
      versionId: "invoice-v2",
      version: "2.0.0",
      isAvailable: true
    };

    expect(projectRecommendedPalette(catalog, [recommendation])).toHaveLength(1);
    expect(projectRecommendedPalette(catalog, [])).toEqual([]);
    expect(projectRecommendedPalette(catalog, [{ ...recommendation, isAvailable: false }])).toEqual([]);
    expect(projectRecommendedPalette(catalog, [{ ...recommendation, versionId: "cleared-version" }])).toEqual([]);
    expect(projectRecommendedPalette(catalog, [{ ...recommendation, activityTypeKey: "Contoso.Other" }])).toEqual([]);
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
