import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { projectBpmnDocumentCanvas } from "../bpmn/bpmnDocumentCanvas";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnCreateConnection, planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { prepareBpmnPoolDelete } from "../bpmn/bpmnDocumentPoolOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

const header = (scopeElementId: string) => ({ scopeElementId, nodeId: `native-scope-${scopeElementId}`, activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [] });
function pooled() {
  const initial = operationOwner(), created = planBpmnCreateShape(initial, operationScope, { toolId: "participant-expanded", position: { x: 1200, y: 800 } });
  const owner = applyBpmnDocumentTransaction(initial, created.transaction), parsed = readBpmnDocument(owner), scope = created.nextScope!;
  return { owner, scope, otherPoolId: created.selection!.elementId,
    otherProcessId: parsed.byId.get(created.selection!.elementId)!.getAttribute("processRef")!,
    originalPoolId: [...parsed.byId.values()].find(element => element.localName === "participant" && element.getAttribute("processRef") === "P")!.getAttribute("id")! };
}

describe("owned standard containment decisions", () => {
  it("moves an unbound construct with its exact opaque QName and inherited XML context", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:process id="P"', '<b:process xml:lang="nl" xml:base="https://source.example/" id="P"')
      .replace('<b:exclusiveGateway id="G"/>', '<b:exclusiveGateway id="G"><b:extensionElements><v:exact qname="q:G">before<!-- keep --><![CDATA[after]]></v:exact></b:extensionElements></b:exclusiveGateway>')
      .replace('<b:transaction id="TX"/>', '<b:transaction id="TX" xmlns:q="urn:other" xml:lang="en" xml:base="https://target.example/"/>');
    const before = structuredClone(owner), prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "TX" });
    expect(readBpmnDocument(owner).byId.get("G")!.lookupNamespaceURI("q")).toBe("urn:test");
    const direct = readBpmnDocument(applyBpmnDocumentTransaction(owner, { commands: [{ kind: "reparent", target: { elementId: "G" }, parent: { elementId: "TX" } }] }));
    expect(direct.byId.get("G")!.lookupNamespaceURI("q")).toBe("urn:test");
    if (prepared.kind !== "ready") throw new Error("unexpected native ownership request");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)), moved = next.byId.get("G")!;
    expect(moved.parentElement).toBe(next.byId.get("TX"));
    expect(moved.lookupNamespaceURI("q")).toBe("urn:test");
    expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", "lang")).toBe("nl");
    expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", "base")).toBe("https://source.example/");
    expect(moved.getElementsByTagNameNS("urn:vendor", "exact")[0].innerHTML).toContain("<!-- keep -->");
    expect(next.payload.activities).toEqual(readBpmnDocument(owner).payload.activities);
    expect(owner).toEqual(before);
  });

  it("refuses split sequence dependencies, collapsed targets and cycles without mutation", () => {
    const owner = operationOwner(), before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["A"], parentElementId: "TX" })).toThrow("sequence flow");
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "S" })).toThrow("target standard body");
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["TX"], parentElementId: "TX" })).toThrow("cyclic");
    expect(owner).toEqual(before);
  });

  it("requires host scope ownership, then moves connected native work and boundaries in one edit", () => {
    const initial = operationOwner(), boundary = planBpmnCreateShape(initial, operationScope, { toolId: "boundaryEvent-timer", attachedToElementId: "A", position: { x: 120, y: 80 } });
    const owner = applyBpmnDocumentTransaction(initial, boundary.transaction), before = structuredClone(owner);
    const intent = { elementIds: ["A", "B"], parentElementId: "TX", positions: new Map([["ADI", { x: 350.25, y: 350.5 }], ["BDI", { x: 600.25, y: 350.5 }]]) };
    const pending = prepareBpmnReparent(owner, operationScope, intent);
    expect(pending).toMatchObject({ kind: "requiresNativeOwnership", ownership: { activityRootIds: ["native"], sourceScopeIds: ["P"], targetScopeId: "TX", missingHeaderScopeIds: ["TX"] } });
    expect(owner).toEqual(before);
    const payload = readBpmnDocument(owner).payload;
    const ready = prepareBpmnReparent(owner, operationScope, intent, { activities: payload.activities, bindings: payload.bindings, scopeHeaders: [header("TX")] });
    if (ready.kind !== "ready") throw new Error("expected admitted host configuration");
    const nextOwner = applyBpmnDocumentTransaction(owner, ready.plan.transaction), next = readBpmnDocument(nextOwner);
    for (const id of ["A", "B", "F", boundary.selection!.elementId]) expect(next.byId.get(id)!.parentElement).toBe(next.byId.get("TX"));
    expect(next.byId.get(boundary.selection!.elementId)!.getAttribute("attachedToRef")).toBe("A");
    expect(next.byId.get("A")!.getElementsByTagNameNS(ns.model, "outgoing")[0].textContent).toBe("q:F");
    expect(next.payload.activities).toEqual(payload.activities);
    expect(projectBpmnDocumentCanvas(nextOwner, operationScope).edges.find(edge => edge.id === "FDI")!.data!.points[0]).toEqual({ x: 450.25, y: 390.5 });
    expect(() => prepareBpmnReparent(owner, operationScope, intent, { scopeHeaders: [] })).toThrow("header chain");
  });

  it("requires host review for moved scope headers and never guesses new enclosing process headers", () => {
    const setup = pooled();
    setup.owner.structure!.payload.scopeHeaders = [header("S")];
    const prepared = prepareBpmnReparent(setup.owner, setup.scope, { elementIds: ["S"], parentElementId: setup.otherProcessId });
    expect(prepared).toMatchObject({ kind: "requiresNativeOwnership", ownership: { headerScopeIds: ["S"], missingHeaderScopeIds: [setup.otherProcessId] } });
  });

  it("moves the actual lane membership record while retaining its QName and vendor metadata", () => {
    const setup = pooled();
    const first = planBpmnCreateShape(setup.owner, setup.scope, { toolId: "lane", parentElementId: setup.originalPoolId, position: { x: 0, y: 0 } });
    const one = applyBpmnDocumentTransaction(setup.owner, first.transaction);
    const second = planBpmnCreateShape(one, setup.scope, { toolId: "lane", parentElementId: setup.originalPoolId, position: { x: 0, y: 0 } });
    const owner = applyBpmnDocumentTransaction(one, second.transaction), parsed = readBpmnDocument(owner);
    const reference = [...parsed.document.getElementsByTagNameNS(ns.model, "flowNodeRef")].find(node => node.textContent === "G")!;
    reference.textContent = "q:G"; reference.setAttributeNS("urn:vendor", "v:keep", "exact");
    owner.structure!.payload.documentXml = new XMLSerializer().serializeToString(parsed.document);
    const prepared = prepareBpmnReparent(owner, setup.scope, { elementIds: ["G"], parentElementId: "P", laneId: second.selection!.elementId });
    if (prepared.kind !== "ready") throw new Error("lane-only move must keep native ownership");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    const moved = [...next.byId.get(second.selection!.elementId)!.getElementsByTagNameNS(ns.model, "flowNodeRef")].find(node => node.textContent === "q:G")!;
    expect(moved.getAttributeNS("urn:vendor", "keep")).toBe("exact");
    expect(moved.lookupNamespaceURI("q")).toBe("urn:test");
  });

  it("refuses a move that invalidates an independent DI plane instead of discarding its metadata", () => {
    const setup = pooled();
    setup.owner.structure!.payload.documentXml = (setup.owner.structure!.payload.documentXml as string).replace('</b:definitions>', '<bd:BPMNDiagram id="OtherView"><bd:BPMNPlane id="OtherPlane" bpmnElement="P"><bd:BPMNShape id="OtherG" bpmnElement="G" v:keep="exact"><dc:Bounds x="0" y="0" width="50" height="50"/></bd:BPMNShape></bd:BPMNPlane></bd:BPMNDiagram></b:definitions>');
    const before = structuredClone(setup.owner);
    expect(() => prepareBpmnReparent(setup.owner, setup.scope, { elementIds: ["G"], parentElementId: setup.otherProcessId })).toThrow("DI plane");
    expect(setup.owner).toEqual(before);
  });

  it("moves an unshared data definition with its reference and refuses stealing shared data", () => {
    const owner = operationOwner(), prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" });
    if (prepared.kind !== "ready") throw new Error("data-only move must retain native configuration");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    for (const id of ["Object", "Data"]) expect(next.byId.get(id)!.parentElement).toBe(next.byId.get("TX"));
    const shared = applyBpmnDocumentTransaction(owner, { commands: [{ kind: "append", parent: { elementId: "P" }, xml: `<b:dataObjectReference xmlns:b="${ns.model}" id="SharedData" dataObjectRef="Object"/>` }] });
    expect(() => prepareBpmnReparent(shared, operationScope, { elementIds: ["Data"], parentElementId: "TX" })).toThrow("shared data");
  });

  it("refuses a move that turns a message flow into a same-process relationship", () => {
    const setup = pooled(), created = planBpmnCreateShape(setup.owner, setup.scope, { toolId: "task", parentElementId: setup.otherProcessId, position: { x: 1250, y: 850 } });
    const first = applyBpmnDocumentTransaction(setup.owner, created.transaction);
    const message = planBpmnCreateConnection(first, setup.scope, { kind: "messageFlow", sourceId: "B", targetId: created.selection!.elementId, points: [{ x: 400, y: 100 }, { x: 1250, y: 850 }] });
    const owner = applyBpmnDocumentTransaction(first, message.transaction), before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, setup.scope, { elementIds: ["A", "B"], parentElementId: setup.otherProcessId })).toThrow("incident relationship");
    expect(owner).toEqual(before);
  });
});

describe("explicit pool deletion decisions", () => {
  it("retains process content, native configuration and child DI by default", () => {
    const setup = pooled(), before = readBpmnDocument(setup.owner);
    const prepared = prepareBpmnPoolDelete(setup.owner, setup.scope, setup.originalPoolId);
    expect(prepared).toMatchObject({ kind: "ready", retainedProcessId: "P" });
    if (prepared.kind !== "ready") throw new Error("unexpected pool decision");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(setup.owner, prepared.plan.transaction));
    expect(next.byId.has(setup.originalPoolId)).toBe(false);
    expect(next.byId.get("P")!.outerHTML).toBe(before.byId.get("P")!.outerHTML);
    expect(next.byId.has("ADI")).toBe(true);
    expect(next.payload.activities).toEqual(before.payload.activities);
    expect(next.payload.processId).toBe("P");
  });

  it("refuses explicit deletion of a shared process while allowing participant-only deletion", () => {
    const setup = pooled(), extra = planBpmnCreateShape(setup.owner, setup.scope, { toolId: "participant-collapsed", position: { x: 2000, y: 1000 } });
    const owner = applyBpmnDocumentTransaction(setup.owner, { commands: [...extra.transaction.commands!, { kind: "attribute", target: { elementId: extra.selection!.elementId }, name: "processRef", value: "P" }] });
    const before = structuredClone(owner);
    expect(() => prepareBpmnPoolDelete(owner, setup.scope, setup.originalPoolId, { deleteProcess: true })).toThrow("shared process");
    expect(prepareBpmnPoolDelete(owner, setup.scope, setup.originalPoolId).kind).toBe("ready");
    expect(owner).toEqual(before);
  });

  it("requires native header/binding cleanup before explicit unshared process deletion", () => {
    const setup = pooled(), owner = applyBpmnDocumentTransaction(setup.owner, { commands: [{ kind: "append", parent: { elementId: setup.otherProcessId }, xml: `<b:task xmlns:b="${ns.model}" id="OtherWork"/>` }] });
    const payload = readBpmnDocument(owner).payload;
    owner.structure!.payload.activities = [...payload.activities, { nodeId: "other-native", activityVersionId: "native@exact", inputs: [], outputs: [] }];
    owner.structure!.payload.bindings = [...payload.bindings, { elementId: "OtherWork", childNodeId: "other-native" }];
    owner.structure!.payload.scopeHeaders = [header(setup.otherProcessId)];
    const pending = prepareBpmnPoolDelete(owner, setup.scope, setup.otherPoolId, { deleteProcess: true });
    expect(pending).toMatchObject({ kind: "requiresNativeCleanup", cleanup: { activityRootIds: ["other-native"], headerScopeIds: [setup.otherProcessId] } });
    const prepared = prepareBpmnPoolDelete(owner, setup.scope, setup.otherPoolId, { deleteProcess: true,
      configuration: { activities: payload.activities, bindings: payload.bindings, scopeHeaders: [] } });
    if (prepared.kind !== "ready") throw new Error("expected explicit cleanup to be admitted");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect(next.byId.has(setup.otherProcessId)).toBe(false); expect(next.byId.has(setup.otherPoolId)).toBe(false);
    expect(next.payload.activities).toEqual(payload.activities); expect(next.payload.processId).toBe("P");
  });

  it("returns a selected-owner transition requirement instead of deleting through the old owner", () => {
    const setup = pooled(), before = structuredClone(setup.owner);
    const prepared = prepareBpmnPoolDelete(setup.owner, setup.scope, setup.originalPoolId, { deleteProcess: true, nextProcessId: setup.otherProcessId });
    expect(prepared).toMatchObject({ kind: "requiresHostTransition", deletedProcessId: "P", transition: {
      selectedProcess: { from: "P", requested: setup.otherProcessId, available: [setup.otherProcessId], ownerConfigurationRequired: true }, cleanup: { activityRootIds: ["native"] }
    } });
    expect(setup.owner).toEqual(before);
  });

  it("retains the last selected process until the host supplies a replacement owner", () => {
    const setup = pooled(), removeOther = prepareBpmnPoolDelete(setup.owner, setup.scope, setup.otherPoolId, { deleteProcess: true });
    if (removeOther.kind !== "ready") throw new Error("unconfigured secondary process should be deletable");
    const owner = applyBpmnDocumentTransaction(setup.owner, removeOther.plan.transaction), before = structuredClone(owner);
    expect(prepareBpmnPoolDelete(owner, setup.scope, setup.originalPoolId, { deleteProcess: true })).toMatchObject({
      kind: "requiresHostTransition", commands: [], transition: { selectedProcess: { from: "P", available: [], ownerConfigurationRequired: true } }
    });
    expect(owner).toEqual(before);
  });

  it("refuses process deletion that would leave an actual retained call reference", () => {
    const setup = pooled(), owner = applyBpmnDocumentTransaction(setup.owner, { commands: [{ kind: "append", parent: { elementId: "P" }, xml: `<b:callActivity xmlns:b="${ns.model}" xmlns:q="urn:test" id="Caller" calledElement="q:${setup.otherProcessId}"/>` }] });
    const before = structuredClone(owner);
    expect(() => prepareBpmnPoolDelete(owner, setup.scope, setup.otherPoolId, { deleteProcess: true })).toThrow("reference");
    expect(owner).toEqual(before);
  });

  it("requires navigation out of an active process view and validates explicit next scopes", () => {
    const setup = pooled(), processScope = { ...setup.scope, scopeElementId: "P" };
    expect(prepareBpmnPoolDelete(setup.owner, processScope, setup.originalPoolId)).toMatchObject({ kind: "requiresHostTransition", transition: { nextScope: setup.scope } });
    expect(prepareBpmnPoolDelete(setup.owner, processScope, setup.originalPoolId, { nextScope: setup.scope }).kind).toBe("ready");
    expect(() => prepareBpmnPoolDelete(setup.owner, setup.scope, setup.otherPoolId, { deleteProcess: true, nextScope: { ...setup.scope, scopeElementId: setup.otherProcessId } })).toThrow("navigation");
  });

  it("refuses dangling retained DI references and keeps opaque process content intact", () => {
    const setup = pooled(), parsed = readBpmnDocument(setup.owner);
    const poolDi = [...parsed.byId.values()].find(element => element.localName === "BPMNShape" && element.getAttribute("bpmnElement") === setup.originalPoolId)!;
    setup.owner.structure!.payload.documentXml = (setup.owner.structure!.payload.documentXml as string).replace('id="FDI"', `sourceElement="${poolDi.getAttribute("id")}" id="FDI"`);
    const before = structuredClone(setup.owner);
    expect(() => prepareBpmnPoolDelete(setup.owner, setup.scope, setup.originalPoolId)).toThrow("reference");
    expect(setup.owner).toEqual(before);
  });
});
