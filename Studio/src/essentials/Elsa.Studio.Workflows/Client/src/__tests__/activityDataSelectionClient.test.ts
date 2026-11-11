import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import {
  hasWorkflowActivityDataSelection,
  readActivityDataContextResult,
  requestActivityDefinitionActivityData,
  requestWorkflowActivityData,
  wholeValueSourceQuery
} from "../api/activityDataSelection";
import { clearApiCapabilityCache } from "../api/capabilities";
import { updateDraft } from "../api/workflowDesign";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";
import { collectSourcePages, maximumSourcePages } from "../activityDataSelection";
import type { ActivityDataContextResult } from "../api/activityDataSelection";
import { describeInferredSource } from "../conversionSource";

function page(state: ActivityDataContextResult["state"], ids: string[], hasMoreSources: boolean): ActivityDataContextResult {
  return {
    state, code: null, documentRevision: "rev", baseRevision: "base", target: null, hasMoreSources,
    sources: ids.map(id => ({ id, displayName: id, binding: null, reasonCode: null, availability: null, activityResultSource: null, type: null }))
  };
}

describe("reading sources page by page", () => {
  it("reads on while the server has more and joins the pages in order", async () => {
    const skips: number[] = [];
    const result = await collectSourcePages(async skip => {
      skips.push(skip);
      return skip === 0 ? page("supported", ["input:a"], true) : page("supported", ["activity:p:$result"], false);
    }, new AbortController().signal);
    expect(skips).toEqual([0, 100]);
    expect(result.sources.map(source => source.id)).toEqual(["input:a", "activity:p:$result"]);
    expect(result.hasMoreSources).toBe(false);
  });

  it("stops at the bound and keeps saying more exist", async () => {
    const request = vi.fn(async (skip: number) => page("supported", [`source:${skip}`], true));
    const result = await collectSourcePages(request, new AbortController().signal);
    expect(request).toHaveBeenCalledTimes(maximumSourcePages);
    expect(result.sources).toHaveLength(maximumSourcePages);
    expect(result.hasMoreSources).toBe(true);
  });

  it("rejects a read whose pages answer different documents", async () => {
    const result = await collectSourcePages(async skip => ({ ...page("supported", [`source:${skip}`], skip === 0), documentRevision: skip === 0 ? "rev-1" : "rev-2" }),
      new AbortController().signal);
    expect(result).toMatchObject({ state: "stale", code: "activity-data.revision-changed", sources: [], target: null });
  });

  it("returns the first refusal and stops when cancelled", async () => {
    const refusal = await collectSourcePages(async skip => skip === 0 ? page("supported", ["a"], true) : page("stale", [], false),
      new AbortController().signal);
    expect(refusal.state).toBe("stale");
    const controller = new AbortController();
    const request = vi.fn(async () => { controller.abort(); return page("supported", ["a"], true); });
    await expect(collectSourcePages(request, controller.signal)).rejects.toThrow();
    expect(request).toHaveBeenCalledTimes(1);
  });
});

describe("conversion caption", () => {
  it("names a stored activity output as such, not as an expression result", () => {
    expect(describeInferredSource("ActivityResult", { producerNodeId: "p", projectionKey: "$result" }, "auto")).toBe("Activity output");
  });
});

// The language-neutral source client (spec 1215 T018) and the structured wire it feeds (T019): one capability with a
// relation per resource kind, metadata-only answers bound to the requested document, and a chosen binding that
// survives save, Code Apply and reopen exactly as the server returned it.

afterEach(() => clearApiCapabilityCache());

const capabilityDocument = {
  capabilities: [
    {
      id: "elsa.api.activity-data-selection",
      contractVersion: "1",
      links: [
        { rel: "activity-data-context", href: "design/activities/drafts/{draftId}/activity-data/context", templated: true },
        { rel: "workflow-data-context", href: "design/workflows/activity-data/context" }
      ]
    },
    {
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }]
    }
  ]
};

function context(postJson = vi.fn(), document: unknown = capabilityDocument, putJson = vi.fn()) {
  return {
    baseUrl: `test://activity-data-${Math.random()}`,
    http: {
      getJson: vi.fn(async (path: string) => {
        if (path === "/capabilities") return document;
        throw new Error(`Unexpected GET ${path}`);
      }),
      postJson,
      putJson
    }
  } as unknown as StudioEndpointContext;
}

const binding = { expressionType: "ActivityResult", value: { producerNodeId: "get-customer", projectionKey: "$result", isOptional: false } };

function supportedResponse(documentRevision: string) {
  return {
    contractVersion: "1",
    state: "supported",
    code: "activity-data.metadata",
    resourceKind: "WorkflowDefinition",
    resourceId: "draft-1",
    baseRevision: "base-1",
    documentRevision,
    snapshotHash: "hash",
    contextRevision: "context",
    analysis: {
      contractRevision: "contract",
      target: { target: { nodeId: "consumer", inputKey: "customerId" }, displayName: "Customer ID", type: { alias: "String" },
        isNullable: false, isRequired: true, isStructureOwned: false, constraint: null },
      sources: [
        { id: "activity:get-customer:$result", displayName: "get-customer", scopeId: null, binding, availability: "definite",
          shape: { state: "known", kind: "object", type: { alias: "Acme.Customer", collectionKind: "Single" } }, reasonCode: null,
          activityResultSource: { kind: "ActivityResult", producerNodeId: "get-customer", projectionKey: "$result", isOptional: false } },
        { id: "activity:loop:$result", displayName: "loop", scopeId: "flow", binding: null, availability: "definite",
          shape: { state: "unknown" }, reasonCode: "EarlierProducerRun",
          activityResultSource: { producerNodeId: "loop", projectionKey: "$result", isOptional: false } }
      ],
      findings: [],
      hasMoreSources: true
    }
  };
}

const state: WorkflowDefinitionState = {
  variables: [],
  inputs: [],
  outputs: [],
  rootActivity: {
    nodeId: "root",
    activityVersionId: "sequence",
    inputs: [],
    outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
      { nodeId: "get-customer", activityVersionId: "get-customer-v1", inputs: [], outputs: [] },
      { nodeId: "consumer", activityVersionId: "invoice-v1", inputs: [], outputs: [],
        customerId: { typeName: "System.String", expression: { type: "ActivityResult", value: binding.value } } }
    ] } }
  } as ActivityNode
};

describe("activity data source client", () => {
  it("analyzes the current workflow snapshot against the acknowledged base through the advertised relation", async () => {
    const postJson = vi.fn(async (_path: string, body: { documentRevision: string }, _init?: unknown) => supportedResponse(body.documentRevision));
    const endpoint = context(postJson);
    const snapshot = canonicalizeStateForWire(state);
    const controller = new AbortController();

    expect(await hasWorkflowActivityDataSelection(endpoint)).toBe(true);
    const result = await requestWorkflowActivityData(endpoint, {
      definitionId: "definition-1",
      draftId: "draft-1",
      expectedBaseRevision: "base-1",
      documentRevision: "rev-7",
      target: { nodeId: "consumer", inputKey: "customerId" },
      snapshot
    }, controller.signal);

    expect(postJson).toHaveBeenCalledTimes(1);
    const [path, body, init] = postJson.mock.calls[0]!;
    expect(path).toBe("/design/workflows/activity-data/context");
    expect(body).toEqual({
      contractVersion: "1",
      definitionId: "definition-1",
      draftId: "draft-1",
      expectedBaseRevision: "base-1",
      documentRevision: "rev-7",
      nodeId: "consumer",
      inputKey: "customerId",
      snapshot,
      query: wholeValueSourceQuery
    });
    expect(init).toEqual({ signal: controller.signal });
    expect(result.state).toBe("supported");
    expect(result.hasMoreSources).toBe(true);
    expect(result.sources.map(source => [source.id, source.binding != null, source.reasonCode])).toEqual([
      ["activity:get-customer:$result", true, null],
      ["activity:loop:$result", false, "EarlierProducerRun"]
    ]);
    // The binding is the server's, untouched.
    expect(result.sources[0]!.binding).toEqual(binding);
    expect(result.sources[0]!.type).toEqual({ alias: "Acme.Customer", collectionKind: "Single" });
  });

  it("sends an Activity Definition request with exactly the fields its endpoint declares", async () => {
    const postJson = vi.fn(async (_path: string, body: { documentRevision: string }) => supportedResponse(body.documentRevision));
    const snapshot = { provider: { providerKey: "graph" }, contract: {} };
    await requestActivityDefinitionActivityData(context(postJson), {
      draftId: "ad draft/1",
      expectedBaseRevision: 4,
      documentRevision: "rev-2",
      target: { nodeId: "consumer", inputKey: "customerId" },
      currentSnapshot: snapshot
    });

    const [path, body] = postJson.mock.calls[0]!;
    expect(path).toBe("/design/activities/drafts/ad%20draft%2F1/activity-data/context");
    expect(Object.keys(body).sort()).toEqual(["contractVersion", "currentSnapshot", "documentRevision", "expectedBaseRevision", "query", "target"]);
    expect(body).toMatchObject({ expectedBaseRevision: 4, currentSnapshot: snapshot, target: { nodeId: "consumer", inputKey: "customerId" } });
  });

  it("reports no capability on a server that does not advertise it", async () => {
    expect(await hasWorkflowActivityDataSelection(context(vi.fn(), { capabilities: [] }))).toBe(false);
  });

  it.each([
    ["another document", { ...supportedResponse("rev-other") }],
    ["another contract", { ...supportedResponse("rev-1"), contractVersion: "2" }],
    ["an unknown state", { ...supportedResponse("rev-1"), state: "partial" }],
    ["no body", null]
  ])("discloses nothing from an answer for %s", (_name, response) => {
    const result = readActivityDataContextResult(response, "rev-1");
    expect(result.state).toBe("incompatible");
    expect(result.sources).toEqual([]);
    expect(result.target).toBeNull();
  });

  it("keeps refusals free of analysis content", () => {
    const result = readActivityDataContextResult({ ...supportedResponse("rev-1"), state: "stale", code: "activity-data.base-changed" }, "rev-1");
    expect(result).toMatchObject({ state: "stale", code: "activity-data.base-changed", baseRevision: "base-1", sources: [], target: null });
  });
});

describe("a chosen activity output on the wire", () => {
  it("survives save, Code Apply and reopen as the same object, legacy scope included", async () => {
    const legacy = { producerNodeId: "get-customer", projectionKey: "$result", producerScopeId: "scope:root", isOptional: false };
    const authored: WorkflowDefinitionState = JSON.parse(JSON.stringify(state));
    const consumer = (authored.rootActivity!.structure!.payload as { activities: ActivityNode[] }).activities[1]!;
    consumer.customerId = { typeName: "System.String", expression: { type: "ActivityResult", value: legacy } };

    const wire = canonicalizeStateForWire(authored);
    const wireConsumer = (wire.rootActivity!.structure!.payload as { activities: ActivityNode[] }).activities[1]!;
    expect(wireConsumer.inputs).toEqual([{ referenceKey: "customerId", value: { expressionType: "ActivityResult", value: legacy } }]);

    // Code Apply parses the serialized document; reopen expands the server's copy.
    const applied = expandStateFromWire(JSON.parse(JSON.stringify(wire)));
    const reopened = (applied.rootActivity!.structure!.payload as { activities: ActivityNode[] }).activities[1]!;
    expect((reopened.customerId as { expression: unknown }).expression).toEqual({ type: "ActivityResult", value: legacy });
    expect(JSON.stringify(canonicalizeStateForWire(applied))).toBe(JSON.stringify(wire));

    const putJson = vi.fn(async (_path: string, body: { state: WorkflowDefinitionState; layout: [] }) => ({
      id: "draft-1", definitionId: "definition-1", state: body.state, layout: body.layout, validationErrors: [], authoringRevision: "base-2"
    }));
    const saved = await updateDraft(context(vi.fn(), capabilityDocument, putJson), {
      id: "draft-1", definitionId: "definition-1", state: authored, layout: [], validationErrors: []
    });
    expect(putJson.mock.calls[0]![1].state).toEqual(wire);
    expect(saved.authoringRevision).toBe("base-2");
  });
});
