import { describe, expect, it } from "vitest";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { readBpmnDocument } from "../bpmn/bpmnDocument";
import { inventoryBpmnDocument } from "../bpmn/bpmnDocumentInventory";
import { ActivityCopyRefusal, pasteActivitiesIntoDraft, planActivityDuplicate, snapshotSelectedActivities } from "../workflow-editor/duplicateActivities";
import { sequenceActivity, sequenceNode, writeLine } from "./fixtures";

const process: ActivityCatalogItem = { ...writeLine, activityVersionId: "bpmn-exact", activityTypeKey: "Elsa.Activities.Bpmn.Activities.BpmnProcess" };
const catalog = [process, sequenceActivity, writeLine];
const arg = (expressionType: string, value: unknown) => ({ referenceKey: "value", value: { expressionType, value }, keep: true });
const leaf = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] });
const xml = `<b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:v="urn:opaque" targetNamespace="urn:clipboard"><!-- exact source --><b:process id="P"><b:task id="T"/><b:subProcess id="S"><b:task id="ST"/></b:subProcess></b:process><b:process id="Other"><b:task id="OT"/></b:process><b:extensionElements><v:keep nodeId="N">H N P</v:keep></b:extensionElements></b:definitions>`;
function document(): ActivityNode {
  const child = leaf("Nested");
  child.inputs = [arg("ActivityResult", { producerNodeId: "SN", producerScopeId: "H", projectionKey: "$result" }),
    arg("Variable", { declaringScopeId: "P", referenceKey: "outer" }), arg("Literal", { producerNodeId: "SN", declaringScopeId: "H" })];
  return { nodeId: "P", activityVersionId: process.activityVersionId, inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: xml, processId: "P", variables: [],
    scopeHeaders: [{ scopeElementId: "S", nodeId: "H", activityVersionId: "header@exact", inputs: [arg("Variable", { declaringScopeId: "P", referenceKey: "outer" })], outputs: [], variables: [] },
      { scopeElementId: "Other", nodeId: "OtherHeader", activityVersionId: "other@exact", inputs: [], outputs: [], variables: [] }],
    activities: [sequenceNode("N", [child]), leaf("Listener"), leaf("SN"), leaf("ON")],
    bindings: [{ elementId: "T", childNodeId: "N", listenerNodeId: "Listener", vendor: { nodeId: "N" } }, { elementId: "ST", childNodeId: "SN" }, { elementId: "OT", childNodeId: "ON" }],
    vendor: { nodeId: "H", producerNodeId: "N" }
  } } };
}
function draft(root: ActivityNode): WorkflowDraft { return { id: "draft", definitionId: "definition", state: { rootActivity: root },
  layout: [{ nodeId: "P", x: 10, y: 20 }, { nodeId: "H", x: 1.25, y: 2.5 }, { nodeId: "Nested", x: 3.125, y: 4.25 }],
  activityPresentation: [{ nodeId: "P", displayName: "Process" }, { nodeId: "H", displayName: "Scope" }], validationErrors: [] }; }
function snapshot(owner: ActivityNode, source: WorkflowDraft, selectedNodeIds = ["P"]) {
  return snapshotSelectedActivities({ selectedNodeIds, scopeOwner: owner,
    scopeActivities: owner.structure!.payload.activities as ActivityNode[], catalog, layout: source.layout, presentation: source.activityPresentation ?? [] })!;
}
function allocator() { let count = 0; return (_: string, used: Set<string>) => { const id = `copied-${++count}`; if (used.has(id)) throw new Error("duplicate allocator"); used.add(id); return id; }; }

describe("complete BPMN documents through the actual activity clipboard", () => {
  it("copies the complete native forest and scope headers while retaining each document's standard IDs and XML bytes", () => {
    const original = document(), root = sequenceNode("outer", [original]), source = draft(root), before = structuredClone(source);
    const copied = snapshot(root, source);
    expect(copied.layout.map(row => row.nodeId)).toContain("H");
    const result = pasteActivitiesIntoDraft({ draft: source, frames: [], snapshot: copied, catalog, allocateId: allocator() })!;
    expect(result).not.toBeNull(); expect(source).toEqual(before);
    const activities = result.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[];
    expect(activities).toHaveLength(2);
    const next = activities[1], parsed = readBpmnDocument(next), ids = result.idMap;
    expect(parsed.payload.documentXml).toBe(xml); expect(parsed.payload.processId).toBe("P"); expect(next.nodeId).toBe(ids.get("P"));
    expect(parsed.payload.scopeHeaders?.map(header => header.nodeId)).toEqual([ids.get("H"), ids.get("OtherHeader")]);
    expect(parsed.payload.bindings[0]).toEqual({ elementId: "T", childNodeId: ids.get("N"), listenerNodeId: ids.get("Listener"), vendor: { nodeId: "N" } });
    const copiedChild = (parsed.payload.activities[0].structure!.payload.activities as ActivityNode[])[0];
    expect(copiedChild.nodeId).toBe(ids.get("Nested"));
    expect(copiedChild.inputs).toEqual([arg("ActivityResult", { producerNodeId: ids.get("SN"), producerScopeId: ids.get("H"), projectionKey: "$result" }),
      arg("Variable", { declaringScopeId: ids.get("P"), referenceKey: "outer" }), arg("Literal", { producerNodeId: "SN", declaringScopeId: "H" })]);
    expect(parsed.payload.vendor).toEqual({ nodeId: "H", producerNodeId: "N" });
    expect(new Set([...inventoryBpmnDocument(original, catalog).nativeNodes.keys(), ...inventoryBpmnDocument(next, catalog).nativeNodes.keys()]).size).toBe(16);
    expect(result.draft.activityPresentation).toContainEqual({ nodeId: ids.get("H"), displayName: "Scope" });
    expect(result.draft.layout).toContainEqual({ nodeId: ids.get("H"), x: 1.25, y: 2.5 });
    expect(result.draft.layout).toContainEqual({ nodeId: ids.get("Nested"), x: 3.125, y: 4.25 });
  });

  it("remaps typed references to a copied native parent and sibling without confusing XML identities", () => {
    const owner = document(), sibling = leaf("Sibling");
    owner.inputs = [arg("Variable", { referenceKey: "parentValue", declaringScopeId: "Container" }),
      arg("ActivityResult", { producerNodeId: "Sibling", producerScopeId: "Container", projectionKey: "$result" })];
    const container = sequenceNode("Container", [owner, sibling]), root = sequenceNode("outer", [container]), source = draft(root);
    const copied = snapshot(root, source, ["Container"]);
    const result = pasteActivitiesIntoDraft({ draft: source, frames: [], snapshot: copied, catalog, allocateId: allocator() })!;
    const nextContainer = (result.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[])[1];
    const next = (nextContainer.structure!.payload.activities as ActivityNode[])[0];
    expect(next.inputs).toEqual([arg("Variable", { referenceKey: "parentValue", declaringScopeId: result.idMap.get("Container") }),
      arg("ActivityResult", { producerNodeId: result.idMap.get("Sibling"), producerScopeId: result.idMap.get("Container"), projectionKey: "$result" })]);
    expect(readBpmnDocument(next).payload.documentXml).toBe(xml);
  });

  it("remaps cross-document typed references when both native documents are selected", () => {
    const first = document(), second = document();
    second.nodeId = "Document2"; second.structure!.payload.activities = []; second.structure!.payload.bindings = []; second.structure!.payload.scopeHeaders = [];
    first.inputs = [arg("ActivityResult", { producerNodeId: "Document2", producerScopeId: "outer", projectionKey: "$result" })];
    const root = sequenceNode("outer", [first, second]), source = draft(root);
    const result = pasteActivitiesIntoDraft({ draft: source, frames: [], snapshot: snapshot(root, source, ["P", "Document2"]), catalog, allocateId: allocator() })!;
    const copied = (result.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[]).slice(2);
    expect(copied[0].inputs).toEqual([arg("ActivityResult", { producerNodeId: result.idMap.get("Document2"), producerScopeId: "outer", projectionKey: "$result" })]);
    expect(copied.map(node => readBpmnDocument(node).payload.documentXml)).toEqual([xml, xml]);
  });

  it("refuses an unqualified native reference dialect before any source/history/side-table mutation", () => {
    const owner = document(), native = (owner.structure!.payload.activities as ActivityNode[])[1];
    native.inputs = [arg("VendorReference", { nodeId: "H" })];
    const root = sequenceNode("outer", [owner]), source = draft(root), before = structuredClone(source);
    expect(() => planActivityDuplicate({ draft: source, frames: [], snapshot: snapshot(root, source), catalog, allocateId: allocator() })).toThrow(ActivityCopyRefusal);
    expect(source).toEqual(before);
  });

  it("remaps ordinary consumers and scoped defaults that refer to the copied BPMN document", () => {
    const owner = document(), consumer = leaf("Consumer");
    consumer.inputs = [arg("ActivityResult", { producerNodeId: "P", producerScopeId: "Container", projectionKey: "$result" }),
      arg("Literal", { producerNodeId: "P", declaringScopeId: "H" })];
    consumer.outputs = [arg("Variable", { referenceKey: "value", declaringScopeId: "H" })];
    const container = sequenceNode("Container", [owner, consumer]);
    container.structure!.payload.variables = [{ referenceKey: "defaulted", type: { alias: "String", collectionKind: "single" },
      default: { expressionType: "ActivityResult", value: { producerNodeId: "P", producerScopeId: "Container", projectionKey: "$result" } } }];
    const root = sequenceNode("outer", [container]), source = draft(root);
    const result = pasteActivitiesIntoDraft({ draft: source, frames: [], snapshot: snapshot(root, source, ["Container"]), catalog, allocateId: allocator() })!;
    const copiedContainer = (result.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[])[1];
    const copiedConsumer = (copiedContainer.structure!.payload.activities as ActivityNode[])[1];
    expect(copiedConsumer.inputs).toEqual([arg("ActivityResult", { producerNodeId: result.idMap.get("P"), producerScopeId: result.idMap.get("Container"), projectionKey: "$result" }), consumer.inputs[1]]);
    expect(copiedConsumer.outputs).toEqual([arg("Variable", { referenceKey: "value", declaringScopeId: result.idMap.get("H") })]);
    expect(copiedContainer.structure!.payload.variables).toMatchObject([{ default: { value: { producerNodeId: result.idMap.get("P"), producerScopeId: result.idMap.get("Container") } } }]);
  });

  it("refuses cyclic BPMN ownership before generic traversal or identity allocation", () => {
    const owner = document(), native = (owner.structure!.payload.activities as ActivityNode[])[0];
    (native.structure!.payload.activities as ActivityNode[]).push(native);
    const root = sequenceNode("outer", [owner]), source = draft(root);
    expect(() => snapshot(root, source)).toThrow(ActivityCopyRefusal);
  });

  it("refuses native identity collisions across individually valid documents before allocation", () => {
    const first = document(), second = structuredClone(first); second.nodeId = "Document2";
    const root = sequenceNode("outer", [first, second]), source = draft(root);
    let allocated = 0;
    const clipboard = { activities: [first, second], layout: [], presentation: [], siblingConnections: [] };
    expect(() => planActivityDuplicate({ draft: source, frames: [], snapshot: clipboard, catalog, allocateId: () => { allocated++; return "unused"; } })).toThrow(ActivityCopyRefusal);
    expect(allocated).toBe(0);
    expect(first.structure!.payload.documentXml).toBe(xml); expect(second.structure!.payload.documentXml).toBe(xml);
  });

  it("does not guess native ownership from an ID-shaped vendor object in a declared native container", () => {
    const owner = document(), container = sequenceNode("Container", [owner]);
    const opaque = { nodeId: "P", activityVersionId: "opaque-version", inputs: [], outputs: [], vendor: "exact" };
    container.structure!.payload.vendor = opaque;
    const root = sequenceNode("outer", [container]), source = draft(root);
    const result = pasteActivitiesIntoDraft({ draft: source, frames: [], snapshot: snapshot(root, source, ["Container"]), catalog, allocateId: allocator() })!;
    const copied = (result.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[])[1];
    expect(copied.structure!.payload.vendor).toEqual(opaque);
    expect((copied.structure!.payload.activities as ActivityNode[])[0].nodeId).toBe(result.idMap.get("P"));
  });

  it("refuses identity allocation that would capture an ordinary consumer's retained external reference", () => {
    const owner = document(), consumer = leaf("Consumer");
    consumer.inputs = [arg("ActivityResult", { producerNodeId: "copied-1", producerScopeId: "external", projectionKey: "$result" })];
    const root = sequenceNode("outer", [owner, consumer]), source = draft(root), before = structuredClone(source);
    expect(() => planActivityDuplicate({ draft: source, frames: [], snapshot: snapshot(root, source, ["P", "Consumer"]), catalog, allocateId: allocator() })).toThrow(/capture external reference/);
    expect(source).toEqual(before);
  });
});
