import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import type { ActivityDataSelectionHost } from "../activityDataSelection";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getDraftRevision } from "../workflow-editor/editorHelpers";
import { useWorkflowActivityDataSelection } from "../workflow-editor/useWorkflowActivityDataSelection";
import type { ActivityCatalogItem, ActivityNode, ValidationError, WorkflowDraft } from "../workflowTypes";

// The workflow editor's side of Choose data (spec 1215 T018): requests analyze the live document as a snapshot against
// the revision the save queue last acknowledged. Browsing never saves, and server findings are shown only while they
// describe the document on screen.

let mounted: { root: Root; container: HTMLElement } | null = null;
let current: ActivityDataSelectionHost | null = null;
let rerender: (props: Partial<HarnessProps>) => void = () => undefined;

afterEach(() => {
  clearApiCapabilityCache();
  if (mounted) {
    const owned = mounted;
    flushSync(() => owned.root.unmount());
    owned.container.remove();
    mounted = null;
  }
  current = null;
});

interface HarnessProps {
  draft: WorkflowDraft | null;
  persistedDraft: WorkflowDraft | null;
  enabled: boolean;
  validationErrors: ValidationError[];
}

function render(endpoint: StudioEndpointContext, initial: HarnessProps, revealActivity = vi.fn()) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  function Harness() {
    const [props, setProps] = React.useState(initial);
    rerender = next => setProps(previous => ({ ...previous, ...next }));
    current = useWorkflowActivityDataSelection({
      context: endpoint,
      enabled: props.enabled,
      draft: props.draft,
      persistedDraft: props.persistedDraft,
      catalogByVersion: catalog,
      validationErrors: props.validationErrors,
      revealActivity
    });
    return null;
  }
  flushSync(() => root.render(<Harness />));
  mounted = { root, container };
}

const catalog = new Map<string, ActivityCatalogItem>([
  ["get-customer-v1", {
    activityVersionId: "get-customer-v1", activityTypeKey: "Acme.GetCustomer", version: "1", category: "Acme", displayName: "Get customer",
    executionType: "Task", inputs: [], outputs: [{ referenceKey: "customer-id", name: "CustomerId", displayName: "Customer ID" }]
  }]
]);

function draft(customerId: unknown, overrides: Partial<WorkflowDraft> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    layout: [],
    activityPresentation: [{ nodeId: "producer", displayName: "Load the customer" }],
    validationErrors: [],
    state: {
      variables: [], inputs: [], outputs: [],
      rootActivity: {
        nodeId: "root", activityVersionId: "sequence", inputs: [], outputs: [],
        structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
          { nodeId: "producer", activityVersionId: "get-customer-v1", inputs: [], outputs: [] },
          { nodeId: "consumer", activityVersionId: "invoice-v1", inputs: [], outputs: [],
            customerId: { typeName: "System.String", expression: { type: "Literal", value: customerId } } }
        ] } }
      } as ActivityNode
    },
    ...overrides
  };
}

function endpoint(advertised = true) {
  const postJson = vi.fn(async (_path: string, body: { documentRevision: string }) => ({
    contractVersion: "1", state: "empty", code: "activity-data.metadata", resourceKind: "WorkflowDefinition", resourceId: "draft-1",
    baseRevision: "base-2", documentRevision: body.documentRevision, analysis: { contractRevision: "c", target: null, sources: [], findings: [] }
  }));
  const putJson = vi.fn();
  const context = {
    baseUrl: `test://workflow-activity-data-${Math.random()}`,
    http: {
      getJson: vi.fn(async (path: string) => {
        if (path !== "/capabilities") throw new Error(`Unexpected GET ${path}`);
        return { capabilities: advertised ? [{
          id: "elsa.api.activity-data-selection", contractVersion: "1",
          links: [{ rel: "workflow-data-context", href: "design/workflows/activity-data/context" }]
        }] : [] };
      }),
      postJson,
      putJson
    }
  } as unknown as StudioEndpointContext;
  return { context, postJson, putJson };
}

describe("workflow Choose data host", () => {
  it("analyzes the live edit against the acknowledged revision without saving it", async () => {
    const { context, postJson, putJson } = endpoint();
    const saved = draft("C-1", { authoringRevision: "base-2" });
    // The author kept editing; the live copy still carries the revision it was loaded with.
    const live = draft("C-2", { authoringRevision: "base-1" });
    render(context, { draft: live, persistedDraft: saved, enabled: true, validationErrors: [] });
    await vi.waitFor(() => expect(current).not.toBeNull());

    expect(current!.documentRevision).toBe(getDraftRevision(live));
    expect(current!.baseRevision).toBe("base-2");
    const result = await current!.requestSources({ nodeId: "consumer", inputKey: "customerId" }, new AbortController().signal);

    expect(result.state).toBe("empty");
    const body = postJson.mock.calls[0]![1];
    expect(body).toMatchObject({
      definitionId: "definition-1",
      draftId: "draft-1",
      expectedBaseRevision: "base-2",
      documentRevision: getDraftRevision(live),
      nodeId: "consumer",
      inputKey: "customerId",
      snapshot: canonicalizeStateForWire(live.state)
    });
    expect(putJson).not.toHaveBeenCalled();
  });

  it("describes activities of the current document by their identity, with current labels", async () => {
    const { context } = endpoint();
    render(context, { draft: draft("C-1"), persistedDraft: draft("C-1", { authoringRevision: "base-1" }), enabled: true, validationErrors: [] });
    await vi.waitFor(() => expect(current).not.toBeNull());

    expect(current!.describeActivity("producer")).toEqual({ label: "Load the customer", outputLabels: { "customer-id": "Customer ID" } });
    expect(current!.describeActivity("removed")).toBeNull();
  });

  it("shows server findings for an input only while the saved document is the one on screen", async () => {
    const { context } = endpoint();
    const finding: ValidationError = { path: "consumer/inputs/customerId", type: "ValueFlow/UnavailableProducer", message: "Unavailable." };
    const other: ValidationError = { path: "consumer/inputs/other", type: "ValueFlow/UnavailableProducer", message: "Elsewhere." };
    const saved = draft("C-1", { authoringRevision: "base-1" });
    render(context, { draft: saved, persistedDraft: saved, enabled: true, validationErrors: [finding, other] });
    await vi.waitFor(() => expect(current).not.toBeNull());
    expect(current!.findingsFor({ nodeId: "consumer", inputKey: "customerId" })).toEqual([{ type: "ValueFlow/UnavailableProducer", message: "Unavailable." }]);

    flushSync(() => rerender({ draft: draft("C-9") }));
    expect(current!.findingsFor({ nodeId: "consumer", inputKey: "customerId" })).toEqual([]);
  });

  it("offers nothing without the capability, while disabled, or before a save was acknowledged", async () => {
    const unsupported = endpoint(false);
    render(unsupported.context, { draft: draft("C-1"), persistedDraft: draft("C-1", { authoringRevision: "base-1" }), enabled: true, validationErrors: [] });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(current).toBeNull();

    const supported = endpoint();
    flushSync(() => mounted!.root.unmount());
    mounted!.container.remove();
    mounted = null;
    render(supported.context, { draft: draft("C-1"), persistedDraft: null, enabled: false, validationErrors: [] });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(current).toBeNull();
    expect(supported.context.http.getJson).not.toHaveBeenCalled();

    flushSync(() => rerender({ enabled: true }));
    await vi.waitFor(() => expect(current).not.toBeNull());
    await expect(current!.requestSources({ nodeId: "consumer", inputKey: "customerId" }, new AbortController().signal)).rejects.toThrow();
    expect(supported.postJson).not.toHaveBeenCalled();
  });
});
