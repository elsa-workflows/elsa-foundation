import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnCreateConnection, planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { planBpmnCondition, planBpmnDefaultFlow } from "../bpmn/bpmnDocumentPropertyOperations";
import { planBpmnReconnect, planBpmnReplace, prepareBpmnDelete } from "../bpmn/bpmnDocumentTransformOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("complete-source replacement/reconnect/deletion", () => {
  function referencedStart() {
    const owner = operationOwner(), created = planBpmnCreateShape(owner, operationScope, { toolId: "startEvent-timer", position: { x: 0, y: 0 } });
    const next = applyBpmnDocumentTransaction(owner, created.transaction), parsed = readBpmnDocument(next), event = parsed.byId.get(created.selection!.elementId)!;
    const definition = event.getElementsByTagNameNS(ns.model, "timerEventDefinition")[0];
    definition.setAttribute("id", "SharedTimer");
    parsed.document.documentElement.insertBefore(definition, parsed.document.documentElement.firstElementChild);
    const reference = parsed.document.createElementNS(ns.model, "b:eventDefinitionRef"); reference.textContent = "q:SharedTimer"; event.append(reference);
    next.structure!.payload.documentXml = new XMLSerializer().serializeToString(parsed.document);
    return { owner: next, selection: created.selection! };
  }

  it("retains an equivalent referenced event trigger without requiring destructive replacement", () => {
    const { owner, selection } = referencedStart(), next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnReplace(owner, operationScope, selection, "startEvent-timer").transaction));
    expect(next.byId.get(selection.elementId)!.getElementsByTagNameNS(ns.model, "eventDefinitionRef")[0].textContent).toBe("q:SharedTimer");
    expect(next.byId.get(selection.elementId)!.getElementsByTagNameNS(ns.model, "timerEventDefinition")).toHaveLength(0);
    expect(next.byId.has("SharedTimer")).toBe(true);
  });

  it("requires explicit removal of a referenced trigger and retains its shared definition", () => {
    const { owner, selection } = referencedStart(), before = structuredClone(owner);
    expect(() => planBpmnReplace(owner, operationScope, selection, "startEvent")).toThrow("explicit");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnReplace(owner, operationScope, selection, "startEvent", true).transaction));
    expect(next.byId.get(selection.elementId)!.getElementsByTagNameNS(ns.model, "eventDefinitionRef")).toHaveLength(0);
    expect(next.byId.get("SharedTimer")!.getElementsByTagNameNS(ns.model, "timeDuration")[0].textContent).toBe("PT1H");
    expect(next.payload.activities).toEqual(readBpmnDocument(owner).payload.activities);
    expect(owner).toEqual(before);
  });

  it("refuses typed construct replacement inside retained vendor payloads", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<v:child/>', '<v:child/><b:task id="Opaque"/>');
    const before = structuredClone(owner);
    expect(() => planBpmnReplace(owner, operationScope, { elementId: "Opaque" }, "serviceTask")).toThrow("retained");
    expect(owner).toEqual(before);
  });

  it("leaves BPMN-looking literals inside extension payloads untouched while deleting actual relationships", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<v:child/>', '<v:child/><b:incoming>F</b:incoming>');
    const prepared = prepareBpmnDelete(owner, operationScope, ["F"]);
    if (prepared.kind !== "ready") throw new Error("unexpected cleanup refusal");
    const parsed = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect(parsed.document.getElementsByTagNameNS("urn:vendor", "keep")[0].getElementsByTagNameNS(ns.model, "incoming")[0].textContent).toBe("F");
    expect(parsed.byId.has("F")).toBe(false);
  });

  it("retains opaque bend comments unless the caller explicitly maps which source vertices remain", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<di:waypoint x="350.25"', '<di:waypoint x="280" y="140.5"><!-- opaque bend --></di:waypoint><di:waypoint x="350.25"');
    const intent = { kind: "sequenceFlow" as const, elementId: "F", diId: "FDI", sourceId: "G", targetId: "B", points: [{ x: 550, y: 145 }, { x: 450.25, y: 140.5 }] };
    expect(() => planBpmnReconnect(owner, operationScope, intent)).toThrow("explicit mapping");
    const plan = planBpmnReconnect(owner, operationScope, { ...intent, sourceIndices: [1, 2] });
    const parsed = readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction));
    expect(parsed.byId.get("FDI")!.firstElementChild!.innerHTML).toContain("opaque bend");
  });

  it("orders self-loop incoming/outgoing references and refuses removing an event-body start trigger", () => {
    const owner = operationOwner(), loop = planBpmnReconnect(owner, operationScope, { kind: "sequenceFlow", elementId: "F", diId: "FDI", sourceId: "G", targetId: "G", points: [{ x: 550, y: 120 }, { x: 560, y: 130 }] });
    expect([...readBpmnDocument(applyBpmnDocumentTransaction(owner, loop.transaction)).byId.get("G")!.children].map(child => child.localName)).toEqual(["incoming", "outgoing"]);
    const scope = { ...operationScope, scopeElementId: "ES", diagramId: "EventDiagram" }, start = planBpmnCreateShape(owner, scope, { toolId: "startEvent-message-event-body", position: { x: 0, y: 0 } });
    const next = applyBpmnDocumentTransaction(owner, start.transaction);
    expect(() => planBpmnReplace(next, scope, start.selection!, "startEvent", true)).toThrow("event subprocess");
  });
  it("replaces task family while retaining its semantic/DI/native identity and vendor configuration", () => {
    const owner = operationOwner(), next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnReplace(owner, operationScope, { elementId: "A", diId: "ADI" }, "serviceTask").transaction));
    expect(next.byId.get("A")!.localName).toBe("serviceTask");
    expect(next.byId.get("ADI")!.getAttribute("bpmnElement")).toBe("A");
    expect(next.payload.activities).toEqual(readBpmnDocument(owner).payload.activities);
    expect(next.payload.bindings).toEqual(readBpmnDocument(owner).payload.bindings);
    expect(next.byId.get("A")!.getElementsByTagNameNS("urn:vendor", "exact")[0].getAttribute("qname")).toBe("q:A");
  });

  it("requires explicit event-definition replacement and preserves unrelated event extension configuration", () => {
    const owner = operationOwner(), created = planBpmnCreateShape(owner, operationScope, { toolId: "intermediateCatchEvent-timer", position: { x: 0, y: 0 } });
    const timer = applyBpmnDocumentTransaction(owner, created.transaction);
    expect(() => planBpmnReplace(timer, operationScope, created.selection!, "intermediateCatchEvent-message")).toThrow("explicit");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(timer, planBpmnReplace(timer, operationScope, created.selection!, "intermediateCatchEvent-message", true).transaction));
    expect(next.byId.get(created.selection!.elementId)!.getElementsByTagNameNS(ns.model, "timerEventDefinition")).toHaveLength(0);
    expect(next.byId.get(created.selection!.elementId)!.getElementsByTagNameNS(ns.model, "messageEventDefinition")).toHaveLength(1);
    expect(() => planBpmnReplace(timer, operationScope, created.selection!, "endEvent")).toThrow("flow role");
  });

  it("reconnects one real flow, retains labels/conditions/vendor identity and updates actual QName incoming/outgoing refs", () => {
    const owner = operationOwner(), conditioned = applyBpmnDocumentTransaction(owner, planBpmnCondition(owner, operationScope, "F", "route === 'F'").transaction);
    const defaulted = applyBpmnDocumentTransaction(conditioned, planBpmnDefaultFlow(conditioned, operationScope, "A", "F").transaction);
    const next = readBpmnDocument(applyBpmnDocumentTransaction(defaulted, planBpmnReconnect(defaulted, operationScope, { elementId: "F", diId: "FDI", kind: "sequenceFlow", sourceId: "G", targetId: "B", points: [{ x: 550, y: 145 }, { x: 500, y: 145 }, { x: 450.25, y: 140.5 }] }).transaction));
    expect(next.byId.get("F")!.getAttribute("sourceRef")).toBe("G");
    expect(next.byId.get("A")!.hasAttribute("default")).toBe(false);
    expect(next.byId.get("A")!.getElementsByTagNameNS(ns.model, "outgoing")).toHaveLength(0);
    expect(next.byId.get("G")!.getElementsByTagNameNS(ns.model, "outgoing")[0].textContent).toBe("q:F");
    expect(next.byId.get("F")!.textContent).toBe("route === 'F'");
    expect(next.byId.has("FDI")).toBe(true);
    expect([...next.byId.keys()].filter(id => id.startsWith("sequenceFlow"))).toEqual([]);
  });

  it("reconnects a data association to another owner using actual new I/O without inserting a duplicate relationship", () => {
    const owner = operationOwner(), created = planBpmnCreateConnection(owner, operationScope, { kind: "dataInputAssociation", sourceId: "Data", targetId: "A", points: [{ x: 275, y: 84 }, { x: 150, y: 100.5 }] });
    const first = applyBpmnDocumentTransaction(owner, created.transaction), plan = planBpmnReconnect(first, operationScope, { ...created.selection!, diId: created.selection!.diId!, kind: "dataInputAssociation", sourceId: "Data", targetId: "B", points: [{ x: 275, y: 84 }, { x: 400, y: 100.5 }] });
    const next = readBpmnDocument(applyBpmnDocumentTransaction(first, plan.transaction)), relationship = next.byId.get(created.selection!.elementId)!;
    expect(relationship.parentElement).toBe(next.byId.get("B"));
    const target = relationship.getElementsByTagNameNS(ns.model, "targetRef")[0].textContent!;
    expect(next.byId.get("B")!.contains(next.byId.get(target)!)).toBe(true);
    expect(next.document.getElementsByTagNameNS(ns.model, "dataInputAssociation")).toHaveLength(1);
    expect(next.byId.get("A")!.getElementsByTagNameNS(ns.model, "dataInput")).toHaveLength(1); // Previously authored item is retained.
  });

  it("requires native cleanup before bound deletion, then removes incident refs/DI in one canonical transaction", () => {
    const owner = operationOwner(), assessment = prepareBpmnDelete(owner, operationScope, ["A"]);
    expect(assessment).toMatchObject({ kind: "requiresNativeCleanup", reason: "The host must resolve native ownership and references in the same transaction.", cleanup: { bindingElementIds: ["A"], activityRootIds: ["native"], headerScopeIds: [] } });
    if (assessment.kind === "requiresNativeCleanup") expect(assessment.commands).toContainEqual({ kind: "remove", target: { elementId: "A" } });
    expect(readBpmnDocument(owner).byId.has("A")).toBe(true);
    const prepared = prepareBpmnDelete(owner, operationScope, ["A"], { activities: [], bindings: [] });
    expect(prepared.kind).toBe("ready");
    if (prepared.kind !== "ready") throw new Error("unexpected cleanup refusal");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    for (const id of ["A", "F", "ADI", "FDI"]) expect(next.byId.has(id)).toBe(false);
    expect(next.byId.get("B")!.getElementsByTagNameNS(ns.model, "incoming")).toHaveLength(0);
    expect(next.byId.has("Note")).toBe(true);
  });

  it("deletes a standard body across independent DI planes while retaining their unrelated styles/metadata", () => {
    const owner = operationOwner(), prepared = prepareBpmnDelete(owner, operationScope, ["S"]);
    if (prepared.kind !== "ready") throw new Error("unexpected cleanup refusal");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    for (const id of ["S", "Child", "SDI", "ChildDI"]) expect(next.byId.has(id)).toBe(false);
    expect(next.byId.get("SubPlane")!.hasAttribute("bpmnElement")).toBe(false);
    expect(next.byId.has("SubDiagram")).toBe(true);
    expect(next.payload.activities).toEqual(readBpmnDocument(owner).payload.activities);
  });
});
