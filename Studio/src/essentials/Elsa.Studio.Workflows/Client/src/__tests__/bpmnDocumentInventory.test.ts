// @vitest-environment jsdom
import { describe, expect, it } from "vitest";
import type { ActivityCatalogItem, ActivityNode, VariableDefinition } from "../workflowTypes";
import { sequenceActivity, forEachActivity, writeLine } from "./fixtures";
import { bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";
import { inventoryBpmnDocument, remapBpmnDocumentNativeIdentities } from "../bpmn/bpmnDocumentInventory";

const source = `<?xml version="1.0"?>
<b:definitions xmlns:b="${bpmnNamespaces.model}" xmlns:v="urn:vendor" targetNamespace="urn:inventory">
 <!-- keep XML IDs and bytes, even when a native identity equals a standard identity -->
 <b:process id="P"><b:task id="Task"/><b:subProcess id="Sub"><b:task id="NestedTask"/></b:subProcess></b:process>
 <b:process id="Other"><b:task id="OtherTask"/></b:process>
 <b:extensionElements><v:opaque nodeId="native-body">native-body scope-other</v:opaque></b:extensionElements>
</b:definitions>`;
const processActivity: ActivityCatalogItem = { ...writeLine, activityVersionId: "exact-process", activityTypeKey: "Elsa.Activities.Bpmn.Activities.BpmnProcess" };
const catalog = [processActivity, writeLine, sequenceActivity, forEachActivity];
const leaf = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] });
const arg = (expressionType: string, value: unknown, referenceKey = "argument") => ({ referenceKey, value: { expressionType, value }, extra: "keep" });
function owner(nodeId = "P"): ActivityNode {
  return { nodeId, activityVersionId: "exact-process", inputs: [], outputs: [], moduleData: { nodeId: "native-body", keep: true }, structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { documentXml: source, processId: "P", variables: [],
      scopeHeaders: [
        { scopeElementId: "Sub", nodeId: "scope-sub", activityVersionId: "exact-sub", inputs: [], outputs: [], variables: [] },
        { scopeElementId: "Other", nodeId: "scope-other", activityVersionId: "exact-other", inputs: [], outputs: [], variables: [], future: { keep: true } }
      ], activities: [
        { nodeId: "native-body", activityVersionId: sequenceActivity.activityVersionId, inputs: [], outputs: [], structure: {
          kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [leaf("sequence-leaf")], variables: [] }
        } }, leaf("listener"), leaf("nested-work"), leaf("other-work")
      ], bindings: [
        { elementId: "Task", childNodeId: "native-body", listenerNodeId: "listener", opaque: { producerNodeId: "native-body" } },
        { elementId: "NestedTask", childNodeId: "nested-work" }, { elementId: "OtherTask", childNodeId: "other-work" }
      ], futureMetadata: { declaringScopeId: "scope-other", nodeId: "native-body" }
    }
  } };
}
function nodes(value: ActivityNode) { return value.structure!.payload.activities as ActivityNode[]; }
function headers(value: ActivityNode) { return readBpmnDocument(value).payload.scopeHeaders!; }
function map(value: ActivityNode) { return new Map([...inventoryBpmnDocument(value, catalog).nativeNodes.keys()].map(id => [id, `copy:${id}`])); }

describe("complete-document physical native inventory", () => {
  it("includes selected and independent process headers, listeners and complete native Sequence forests", () => {
    const original = owner(), before = structuredClone(original);
    const inventory = inventoryBpmnDocument(original, catalog);
    expect([...inventory.nativeNodes.keys()]).toEqual(["P", "scope-sub", "scope-other", "native-body", "sequence-leaf", "listener", "nested-work", "other-work"]);
    expect(inventory.nativeNodes.get("scope-other")).toMatchObject({ kind: "scopeHeader", parentNodeId: "P", documentOwnerNodeId: "P" });
    expect(inventory.nativeNodes.get("sequence-leaf")).toMatchObject({ kind: "activity", parentNodeId: "native-body", documentOwnerNodeId: "P" });
    expect(inventory.documents.get("P")!.xmlIds.has("P")).toBe(true);
    expect(inventory.unqualifiedStructures).toEqual([]);
    expect(original).toEqual(before);
  });

  it("keeps XML identity domains separate across independently owned nested source documents", () => {
    const original = owner();
    const nested = owner("nested-document");
    nested.structure!.payload.activities = [];
    nested.structure!.payload.bindings = [];
    nested.structure!.payload.scopeHeaders = [];
    // The independent native document is physically owned by the actual Sequence slot.
    (nodes(original)[0].structure!.payload.activities as ActivityNode[]).push(nested);
    const inventory = inventoryBpmnDocument(original, catalog);
    expect([...inventory.documents.keys()]).toEqual(["P", "nested-document"]);
    expect(inventory.documents.get("P")!.xmlIds).toEqual(inventory.documents.get("nested-document")!.xmlIds);
    expect(inventory.nativeNodes.get("nested-document")).toMatchObject({ parentNodeId: "native-body", documentOwnerNodeId: "nested-document" });
  });

  it.each(["duplicate-native", "header-vs-descendant", "repeated-object", "cycle"])("refuses %s before any mutation", mutation => {
    const original = owner();
    const sequence = nodes(original)[0], children = sequence.structure!.payload.activities as ActivityNode[];
    if (mutation === "duplicate-native") children.push(leaf("other-work"));
    if (mutation === "header-vs-descendant") children.push(leaf("scope-other"));
    if (mutation === "repeated-object") children.push(children[0]);
    if (mutation === "cycle") children.push(sequence);
    expect(() => inventoryBpmnDocument(original, catalog)).toThrow(/duplicated|repeated ownership|cyclic ownership/);
    expect(original.structure!.payload.documentXml).toBe(source);
  });

  it("uses contributed catalog ownership slots while ignoring arbitrary literal and vendor ID-shaped objects", () => {
    const original = owner(), foreach = nodes(original)[0];
    foreach.activityVersionId = forEachActivity.activityVersionId;
    foreach.structure = { kind: "elsa.foreach.structure", schemaVersion: "1.0.0", payload: { body: leaf("contributed-leaf"), metadata: { nodeId: "scope-other" } } };
    foreach.inputs = [arg("Literal", { nodeId: "scope-other", activities: [leaf("not-owned")] })];
    const inventory = inventoryBpmnDocument(original, catalog);
    expect(inventory.nativeNodes.has("contributed-leaf")).toBe(true);
    expect(inventory.nativeNodes.has("not-owned")).toBe(false);
    expect(inventory.unqualifiedStructures).toEqual([]);
    expect(() => remapBpmnDocumentNativeIdentities(original, map(original), catalog)).toThrow(/qualified reference transform/);
  });

  it("reports an unknown ownership boundary rather than recursively guessing metadata IDs", () => {
    const original = owner();
    nodes(original)[0].structure = { kind: "vendor.unknown", schemaVersion: "7", payload: { metadata: [leaf("not-proved-owned")] } };
    const inventory = inventoryBpmnDocument(original, catalog);
    expect(inventory.nativeNodes.has("not-proved-owned")).toBe(false);
    expect(inventory.unqualifiedStructures).toMatchObject([{ nodeId: "native-body" }]);
    expect(() => remapBpmnDocumentNativeIdentities(original, map(original), catalog)).toThrow(/unqualified native ownership/);
  });

  it("refuses malformed declared children instead of accepting getChildSlots filtered loss", () => {
    const original = owner();
    (nodes(original)[0].structure!.payload.activities as unknown[]).push({ unrelated: true });
    expect(() => inventoryBpmnDocument(original, catalog)).toThrow(/malformed activities/);
  });
});

describe("bounded whole-document native identity transform", () => {
  it("remaps every process/header/forest identity and typed references without changing XML or opaque configuration", () => {
    const original = owner(), body = nodes(original)[0], child = (body.structure!.payload.activities as ActivityNode[])[0];
    original.inputs = [arg("Variable", { referenceKey: "outer", declaringScopeId: "scope-other" })];
    headers(original)[1].inputs = [arg("Variable", { referenceKey: "independent", declaringScopeId: "scope-other" })];
    // readBpmnDocument returns the actual retained header objects; the payload still owns them.
    child.inputs = [
      arg("Variable", { referenceKey: "local", declaringScopeId: "native-body", extra: "retain" }),
      arg("ActivityResult", { producerNodeId: "other-work", producerScopeId: "scope-other", projectionKey: "$result", path: "x", isOptional: true }),
      arg("Variable", { referenceKey: "external", declaringScopeId: "outside" }),
      ...["Literal", "Object", "WorkflowRequest", "Input", "Secret", "Connection"].map(type => arg(type, { producerNodeId: "native-body", declaringScopeId: "scope-other" }, type))
    ];
    child.outputs = [arg("Variable", { referenceKey: "capture", declaringScopeId: "scope-sub" })];
    child.intrinsic = { kind: "Set", variable: { referenceKey: "target", declaringScopeId: "scope-sub" } };
    const before = structuredClone(original), next = remapBpmnDocumentNativeIdentities(original, map(original), catalog);
    const retained = readBpmnDocument(next).payload;
    expect(next.nodeId).toBe("copy:P");
    expect(retained.documentXml).toBe(source);
    expect(retained.processId).toBe("P");
    expect(retained.bindings[0]).toEqual({ elementId: "Task", childNodeId: "copy:native-body", listenerNodeId: "copy:listener", opaque: { producerNodeId: "native-body" } });
    expect(retained.scopeHeaders!.map(header => [header.scopeElementId, header.nodeId])).toEqual([["Sub", "copy:scope-sub"], ["Other", "copy:scope-other"]]);
    expect(retained.scopeHeaders![1].inputs).toEqual([arg("Variable", { referenceKey: "independent", declaringScopeId: "copy:scope-other" })]);
    const copiedChild = (retained.activities[0].structure!.payload.activities as ActivityNode[])[0];
    expect(copiedChild.nodeId).toBe("copy:sequence-leaf");
    expect(copiedChild.inputs.slice(0, 3)).toEqual([
      arg("Variable", { referenceKey: "local", declaringScopeId: "copy:native-body", extra: "retain" }),
      arg("ActivityResult", { producerNodeId: "copy:other-work", producerScopeId: "copy:scope-other", projectionKey: "$result", path: "x", isOptional: true }),
      child.inputs[2]
    ]);
    expect(copiedChild.inputs.slice(3)).toEqual(child.inputs.slice(3));
    expect(copiedChild.outputs).toEqual([arg("Variable", { referenceKey: "capture", declaringScopeId: "copy:scope-sub" })]);
    expect(copiedChild.intrinsic!.variable!.declaringScopeId).toBe("copy:scope-sub");
    expect(retained.futureMetadata).toEqual(original.structure!.payload.futureMetadata);
    expect(next.moduleData).toBe(original.moduleData);
    expect(original).toEqual(before);
  });

  it("remaps nested independent documents and retains each document XML byte-for-byte", () => {
    const original = owner(), nested = owner("nested-document");
    nested.structure!.payload.activities = [];
    nested.structure!.payload.bindings = [];
    nested.structure!.payload.scopeHeaders = [];
    (nodes(original)[0].structure!.payload.activities as ActivityNode[]).push(nested);
    const next = remapBpmnDocumentNativeIdentities(original, map(original), catalog);
    const inventory = inventoryBpmnDocument(next, catalog);
    expect([...inventory.documents.keys()]).toEqual(["copy:P", "copy:nested-document"]);
    expect([...inventory.documents.values()].map(document => document.parsed.payload.documentXml)).toEqual([source, source]);
  });

  it.each(["missing", "external-key", "duplicate-target", "workflow-target"])("rejects an invalid %s map atomically", mutation => {
    const original = owner(), before = structuredClone(original), mapping = map(original);
    if (mutation === "missing") mapping.delete("scope-other");
    if (mutation === "external-key") mapping.set("outside", "copy:outside");
    if (mutation === "duplicate-target") mapping.set("scope-other", "copy:P");
    if (mutation === "workflow-target") mapping.set("scope-other", "workflow");
    expect(() => remapBpmnDocumentNativeIdentities(original, mapping, catalog)).toThrow();
    expect(original).toEqual(before);
  });

  it.each(["VendorReference", "JavaScript"])("refuses unqualified %s expressions with the source intact", expression => {
    const original = owner();
    nodes(original)[1].inputs = [arg(expression, { declaringScopeId: "scope-other" })];
    const before = structuredClone(original);
    expect(() => remapBpmnDocumentNativeIdentities(original, map(original), catalog)).toThrow(/qualified reference transform/);
    expect(original).toEqual(before);
  });

  it("handles catalog-declared expanded arguments/captures without scanning similar vendor objects", () => {
    const original = owner(), child = nodes(original)[1];
    const declared: ActivityCatalogItem = { ...writeLine, inputs: [{ referenceKey: "value" }], outputs: [{ referenceKey: "result" }] };
    child.value = { expression: { type: "Variable", value: { referenceKey: "x", declaringScopeId: "scope-other" } }, conversion: { mode: "auto" } };
    child.result = { target: { referenceKey: "y", declaringScopeId: "scope-sub" }, opaque: true };
    child.vendor = { expression: { type: "Variable", value: { referenceKey: "x", declaringScopeId: "scope-other" } } };
    const next = remapBpmnDocumentNativeIdentities(original, map(original), [processActivity, declared, sequenceActivity]);
    expect(nodes(next)[1].value).toMatchObject({ expression: { value: { declaringScopeId: "copy:scope-other" } }, conversion: { mode: "auto" } });
    expect(nodes(next)[1].result).toMatchObject({ target: { declaringScopeId: "copy:scope-sub" }, opaque: true });
    expect(nodes(next)[1].vendor).toBe(child.vendor);
  });

  it("remaps typed defaults in selected, independent-header and native-container declarations", () => {
    const original = owner();
    const declaration = { referenceKey: "v", name: "Value", type: { alias: "String", collectionKind: "Single" },
      default: { expressionType: "Variable", value: { referenceKey: "outsideValue", declaringScopeId: "scope-other" } } } satisfies VariableDefinition;
    original.structure!.payload.variables = [declaration];
    headers(original)[1].variables = [declaration];
    nodes(original)[0].structure!.payload.variables = [declaration];
    const next = remapBpmnDocumentNativeIdentities(original, map(original), catalog);
    for (const variables of [next.structure!.payload.variables, headers(next)[1].variables, nodes(next)[0].structure!.payload.variables])
      expect(variables).toMatchObject([{ default: { value: { declaringScopeId: "copy:scope-other" } } }]);
    expect(declaration.default.value.declaringScopeId).toBe("scope-other");
  });

  it("refuses target identities that would capture retained external references", () => {
    const original = owner(), mapping = map(original);
    nodes(original)[1].inputs = [arg("Variable", { referenceKey: "external", declaringScopeId: "outside" })];
    mapping.set("scope-other", "outside");
    const before = structuredClone(original);
    expect(() => remapBpmnDocumentNativeIdentities(original, mapping, catalog)).toThrow(/capture external native reference/);
    expect(original).toEqual(before);
  });

  it("retains external ActivityResult identities and the workflow scope sentinel", () => {
    const original = owner(), child = nodes(original)[1];
    child.inputs = [arg("ActivityResult", { producerNodeId: "outside-producer", producerScopeId: "outside-scope", projectionKey: "$result" }),
      arg("Variable", { referenceKey: "workflow-value", declaringScopeId: "workflow" })];
    const next = remapBpmnDocumentNativeIdentities(original, map(original), catalog);
    expect(nodes(next)[1].inputs).toEqual(child.inputs);
  });

  it("refuses an unresolved ordinary native activity contract rather than assuming its custom fields are reference-free", () => {
    const original = owner();
    nodes(original)[1].activityVersionId = "unresolved-contributed-version";
    const before = structuredClone(original);
    expect(() => remapBpmnDocumentNativeIdentities(original, map(original), catalog)).toThrow(/exact catalog contract/);
    expect(original).toEqual(before);
  });

  it("also requires the document owner's catalog to qualify expanded invocation arguments", () => {
    const original = owner(), mapping = map(original);
    expect(() => remapBpmnDocumentNativeIdentities(original, mapping, [writeLine, sequenceActivity])).toThrow(/exact catalog contract/);
  });
});
