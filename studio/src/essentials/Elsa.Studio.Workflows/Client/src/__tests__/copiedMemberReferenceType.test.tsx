import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioEndpointContext, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { activityDataMemberReferenceContribution, activityResultReferenceContribution } from "../activityResultReferenceContribution";
import { expandStateFromWire } from "../activityInputWire";
import { clearApiCapabilityCache } from "../api/capabilities";
import { useActivityClipboard } from "../workflow-editor/useActivityClipboard";
import { useWorkflowActivityDataSelection } from "../workflow-editor/useWorkflowActivityDataSelection";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { leafNode, sequenceActivity, sequenceNode, writeLine } from "./fixtures";

// #4693: a copied member reference resolves the same type details as its original once its producer is remapped.

const getCustomer: ActivityCatalogItem = { ...writeLine, activityVersionId: "get-customer-v1", activityTypeKey: "Acme.GetCustomer", displayName: "Get Customer",
  outputs: [{ referenceKey: "Customer", name: "Customer", displayName: "Customer" }] };
const line: ActivityCatalogItem = { ...writeLine, inputs: [{ name: "Text", referenceKey: "text", typeName: "String", isWrapped: true }] };
const catalog = new Map([[sequenceActivity.activityVersionId, sequenceActivity], [line.activityVersionId, line], [getCustomer.activityVersionId, getCustomer]]);
const member = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "Customer", isOptional: false, memberPath: ["customerId"] } };
const descriptor: StudioActivityDescriptor = { typeName: "WriteLine", inputs: [{ name: "Text", displayName: "Text", referenceKey: "text", typeName: "System.String", isWrapped: true }], outputs: [], ports: [] };
const descriptors: StudioExpressionDescriptor[] = [{ type: "Literal", displayName: "Literal", editingMode: "literal" }];

function initial(): WorkflowDraft {
  const producer: ActivityNode = { nodeId: "producer", activityVersionId: getCustomer.activityVersionId, inputs: [], outputs: [] };
  const consumer = leafNode("consumer"); consumer.inputs = [{ referenceKey: "text", value: member }];
  return { id: "draft-1", definitionId: "definition-1", authoringRevision: "r1", layout: [], activityPresentation: [], validationErrors: [],
    state: expandStateFromWire({ variables: [], inputs: [], outputs: [], rootActivity: sequenceNode("root", [producer, consumer]) }) };
}

/** Answers like the server: a focused member query on a Get Customer producer in the snapshot is typed String. */
function server() {
  let revision = "r1";
  type Body = { draftId: string; documentRevision: string; expectedBaseRevision: string; nodeId: string; inputKey: string;
    snapshot: { rootActivity: ActivityNode }; query: { source?: { expressionType: string; value: unknown }; memberPath: string[] } };
  const postJson = vi.fn(async (_path: string, body: Body) => {
    const reply = { contractVersion: "1", resourceKind: "WorkflowDefinition", resourceId: body.draftId, documentRevision: body.documentRevision };
    if (body.expectedBaseRevision !== revision) return { ...reply, state: "stale", code: "activity-data.base-changed", baseRevision: revision };
    const producers = body.snapshot.rootActivity.structure!.payload.activities as ActivityNode[];
    const source = body.query.source?.value as { producerNodeId: string; projectionKey: string } | undefined;
    const producer = producers.find(node => node.nodeId === source?.producerNodeId && node.activityVersionId === getCustomer.activityVersionId);
    const leaf = { state: "known", kind: "scalar", type: { alias: "String", collectionKind: "Single" }, members: [], presence: "required",
      sourceContractRevision: "c", profileRevision: "p", metadataRevision: "m", isNullable: false, isSensitive: false, isCredential: false };
    return { ...reply, state: "supported", code: "activity-data.metadata", baseRevision: revision, analysis: {
      contractRevision: "c", memberReadsEnabled: true, findings: [],
      target: { displayName: "Text", target: { nodeId: body.nodeId, inputKey: body.inputKey }, constraint: null },
      sources: producer ? [{ id: `activity:${producer.nodeId}:Customer`, displayName: producer.nodeId, availability: "definite", reasonCode: null,
        activityResultSource: { producerNodeId: producer.nodeId, projectionKey: "Customer", isOptional: false },
        binding: { expressionType: "ActivityResultMember", value: { producerNodeId: producer.nodeId, projectionKey: "Customer", isOptional: false, memberPath: body.query.memberPath } },
        shape: leaf }] : [] } };
  });
  const context = { baseUrl: `test://copied-member-${Math.random()}`, http: {
    getJson: vi.fn(async () => ({ capabilities: [{ id: "elsa.api.activity-data-selection", contractVersion: "1",
      links: [{ rel: "workflow-data-context", href: "design/workflows/activity-data/context" }] }] })),
    postJson, putJson: vi.fn()
  } } as unknown as StudioEndpointContext;
  return { context, postJson, save: (next: string) => { revision = next; } };
}

let doc: WorkflowDocumentApi, clipboard: ReturnType<typeof useActivityClipboard>;
let acknowledge: (draft: WorkflowDraft) => void, show: (nodeId: string) => void;
let root: Root, container: HTMLDivElement, backend: ReturnType<typeof server>;
function Harness() {
  doc = useWorkflowDocument();
  const [persisted, setPersisted] = React.useState<WorkflowDraft | null>(null); acknowledge = setPersisted;
  const [selected, setSelected] = React.useState("consumer"); show = setSelected;
  clipboard = useActivityClipboard({ resourceKind: "workflow", draft: doc.state.draft, frames: doc.state.frames, catalogByVersion: catalog,
    selectedNodeIds: ["producer", "consumer"], disabled: false, definitionId: doc.state.draft?.definitionId ?? "",
    editDraft: doc.editDraft, select: doc.select, setStatus: () => {} });
  const host = useWorkflowActivityDataSelection({ context: backend.context, enabled: true, draft: doc.state.draft, persistedDraft: persisted,
    saving: false, catalogByVersion: catalog, validationErrors: [], revealActivity: () => {} });
  const activity = (doc.state.draft?.state.rootActivity?.structure?.payload.activities as ActivityNode[] | undefined)?.find(node => node.nodeId === selected);
  return activity ? <ActivityPropertiesPanel key={selected} activity={activity} descriptor={descriptor} editors={[]}
    expressionEditors={[activityResultReferenceContribution, activityDataMemberReferenceContribution]} expressionDescriptors={descriptors}
    expressionDescriptorStatus="ready" descriptorStatus="ready" visibleVariables={[]} scopeStatus="ready" activityDataSelection={host} onChange={() => {}} /> : null;
}
const card = () => container.querySelector(".wf-data-reference");
const typeLine = () => card()?.querySelector(".wf-data-chooser-note")?.textContent ?? null;

beforeEach(() => {
  backend = server(); container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Harness />));
  const loaded = initial();
  flushSync(() => { doc.loadDraft(loaded); acknowledge(loaded); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); clearApiCapabilityCache(); });

describe("copied activity-data member reference (#4693)", () => {
  it.each(["paste", "duplicate"] as const)("shows the original's String type line on a %s copy once its producer is remapped", async operation => {
    await vi.waitFor(() => expect(typeLine()).toBe("String"));
    expect(clipboard.copy()).toBe(true);
    flushSync(() => { clipboard[operation](); });
    const nodes = doc.state.draft!.state.rootActivity!.structure!.payload.activities as ActivityNode[];
    const [copiedProducer, copiedConsumer] = nodes.slice(2);
    flushSync(() => show(copiedConsumer!.nodeId));
    expect(copiedProducer!.nodeId).not.toBe("producer");
    // Before the save is acknowledged, then after it, as the autosave of the copy lands.
    await vi.waitFor(() => expect(typeLine()).toBe("String"));
    const asked = backend.postJson.mock.calls.map(call => call[1]).filter(body => body.nodeId === copiedConsumer!.nodeId);
    expect(asked.at(-1)!.query).toMatchObject({ source: { expressionType: "ActivityResult", value: { producerNodeId: copiedProducer!.nodeId } }, memberPath: ["customerId"] });
    backend.save("r2");
    flushSync(() => acknowledge({ ...doc.state.draft!, authoringRevision: "r2" }));
    await vi.waitFor(() => expect(typeLine()).toBe("String"));
  });
});
