import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnAppearanceNamespaces as colors, readBpmnDocumentAppearance } from "../bpmn/bpmnDocumentAppearance";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnCreateConnection, planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { planBpmnAppearance, planBpmnStandardProperty, readBpmnStandardProperties } from "../bpmn/bpmnDocumentPropertyOperations";
import { planBpmnReconnect, planBpmnReplace, prepareBpmnDelete } from "../bpmn/bpmnDocumentTransformOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("complete operation identity and property fidelity", () => {
  it.each(["message", "timer", "signal", "conditional"])("keeps a %s start out of ordinary transaction bodies for create, replace and reparent", trigger => {
    const owner = operationOwner(), before = structuredClone(owner), toolId = `startEvent-${trigger}`;
    expect(() => planBpmnCreateShape(owner, operationScope, { toolId, parentElementId: "TX", position: { x: 330, y: 350 } })).toThrow("none start");
    const none = planBpmnCreateShape(owner, operationScope, { toolId: "startEvent", parentElementId: "TX", position: { x: 330, y: 350 } });
    const inBody = applyBpmnDocumentTransaction(owner, none.transaction), bodyBefore = structuredClone(inBody);
    expect(() => planBpmnReplace(inBody, operationScope, none.selection!, toolId)).toThrow("none start");
    expect(inBody).toEqual(bodyBefore);
    const typed = planBpmnCreateShape(owner, operationScope, { toolId, position: { x: 20, y: 20 } });
    const inProcess = applyBpmnDocumentTransaction(owner, typed.transaction), processBefore = structuredClone(inProcess);
    expect(() => prepareBpmnReparent(inProcess, operationScope, { elementIds: [typed.selection!.elementId], parentElementId: "TX" })).toThrow("none start");
    expect(inProcess).toEqual(processBefore);
    expect(owner).toEqual(before);
  });

  it("allows a none start to move into an ordinary transaction body", () => {
    const owner = operationOwner(), none = planBpmnCreateShape(owner, operationScope, { toolId: "startEvent", position: { x: 20, y: 20 } });
    const source = applyBpmnDocumentTransaction(owner, none.transaction);
    const moved = prepareBpmnReparent(source, operationScope, { elementIds: [none.selection!.elementId], parentElementId: "TX" });
    if (moved.kind !== "ready") throw new Error("An unconfigured none start needs no native ownership decision");
    expect(readBpmnDocument(applyBpmnDocumentTransaction(source, moved.plan.transaction)).byId.get(none.selection!.elementId)!.parentElement!.id).toBe("TX");
  });

  function receiveBoundary(idless: boolean) {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:task id="B">', '<b:receiveTask id="B">').replace('</b:incoming></b:task>', '</b:incoming></b:receiveTask>')
      .replace('<b:exclusiveGateway id="G"/>', `<b:boundaryEvent ${idless ? "" : 'id="Boundary"'} attachedToRef="B"><b:timerEventDefinition/></b:boundaryEvent><b:exclusiveGateway id="G"/>`);
    return owner;
  }

  it.each([false, true])("refuses creating event-gateway input to a receive task with a boundary (idless=%s)", idless => {
    const owner = receiveBoundary(idless);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:exclusiveGateway id="G"/>', '<b:eventBasedGateway id="G"/>');
    const before = structuredClone(owner);
    expect(() => planBpmnCreateConnection(owner, operationScope, { kind: "sequenceFlow", sourceId: "G", targetId: "B", points: [{ x: 550, y: 140 }, { x: 450, y: 140 }] })).toThrow("event-based gateway");
    expect(owner).toEqual(before);
    expect(() => planBpmnReconnect(owner, operationScope, { elementId: "F", diId: "FDI", kind: "sequenceFlow", sourceId: "G", targetId: "B", points: [{ x: 550, y: 140 }, { x: 450, y: 140 }] })).toThrow("event-based gateway");
    expect(owner).toEqual(before);
  });

  it.each([false, true])("refuses gateway replacement that invalidates a downstream boundary (idless=%s)", idless => {
    const owner = receiveBoundary(idless), connection = planBpmnCreateConnection(owner, operationScope, { kind: "sequenceFlow", sourceId: "G", targetId: "B", points: [{ x: 550, y: 140 }, { x: 450, y: 140 }] });
    const next = applyBpmnDocumentTransaction(owner, connection.transaction), before = structuredClone(next);
    expect(() => planBpmnReplace(next, operationScope, { elementId: "G", diId: "GDI" }, "eventBasedGateway")).toThrow("event-based gateway");
    expect(next).toEqual(before);
  });

  it("permits repairing an unfinished receive-task boundary topology by reconnecting away from its event gateway", () => {
    const owner = receiveBoundary(true);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:exclusiveGateway id="G"/>', '<b:eventBasedGateway id="G"/>').replace('sourceRef="A" targetRef="B"', 'sourceRef="G" targetRef="B"');
    const plan = planBpmnReconnect(owner, operationScope, { elementId: "F", diId: "FDI", kind: "sequenceFlow", sourceId: "A", targetId: "B", points: [{ x: 200, y: 140 }, { x: 350, y: 140 }] });
    expect(readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction)).byId.get("F")!.getAttribute("sourceRef")).toBe("A");
  });

  it("checks idless incoming gateway relationships when replacing an upstream gateway", () => {
    const owner = receiveBoundary(true);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:sequenceFlow id="F"', '<b:sequenceFlow sourceRef="G" targetRef="B"/><b:sequenceFlow id="F"');
    expect(() => planBpmnReplace(owner, operationScope, { elementId: "G", diId: "GDI" }, "eventBasedGateway")).toThrow("event-based gateway");
  });

  it("retains opaque BPMN-looking incoming gateway relationships when creating a real boundary", () => {
    const owner = receiveBoundary(true);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:exclusiveGateway id="G"/>', '<b:eventBasedGateway id="G"/>')
      .replace('<v:exact qname="q:A"/>', '<v:exact qname="q:A"><b:sequenceFlow sourceRef="G" targetRef="B"/></v:exact>');
    expect(() => planBpmnCreateShape(owner, operationScope, { toolId: "boundaryEvent-timer", attachedToElementId: "B", position: { x: 400, y: 180 } })).not.toThrow();
  });

  it.each([false, true])("retains unfinished boundary topology during a same-endpoint reconnect (idless=%s)", idless => {
    const owner = receiveBoundary(idless);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:exclusiveGateway id="G"/>', '<b:eventBasedGateway id="G"/>').replace('sourceRef="A" targetRef="B"', 'sourceRef="G" targetRef="B"');
    const before = structuredClone(owner), points = [{ x: 575, y: 170 }, { x: 450, y: 170 }];
    const plan = planBpmnReconnect(owner, operationScope, { elementId: "F", diId: "FDI", kind: "sequenceFlow", sourceId: "G", targetId: "B", name: "Retained route", points });
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction));
    expect(next.byId.get("F")!.getAttribute("name")).toBe("Retained route");
    expect([...next.byId.values()].filter(element => element.localName === "sequenceFlow")).toHaveLength(1);
    expect([...next.byId.get("FDI")!.children].map(element => ({ x: Number(element.getAttribute("x")), y: Number(element.getAttribute("y")) }))).toEqual(points);
    expect(next.payload.activities).toEqual(owner.structure!.payload.activities);
    expect(owner).toEqual(before);
  });

  it.each([false, true])("retains an unfinished relationship during neutral containment (lane=%s)", lane => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:sequenceFlow id="F" sourceRef="A"', '<b:endEvent id="End"/><b:sequenceFlow id="F" sourceRef="End"');
    if (lane) owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:task id="A"', '<b:laneSet id="Lanes"><b:lane id="L1"><b:flowNodeRef>B</b:flowNodeRef></b:lane><b:lane id="L2"/></b:laneSet><b:task id="A"')
      .replace('<bd:BPMNShape id="ADI"', '<bd:BPMNShape id="L1DI" bpmnElement="L1"><dc:Bounds x="0" y="0" width="1000" height="220"/></bd:BPMNShape><bd:BPMNShape id="L2DI" bpmnElement="L2"><dc:Bounds x="0" y="220" width="1000" height="400"/></bd:BPMNShape><bd:BPMNShape id="ADI"');
    const before = structuredClone(owner), prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["B"], parentElementId: "P", ...(lane ? { laneId: "L2" } : {}) });
    if (prepared.kind !== "ready") throw new Error("Neutral containment needs no native ownership decision");
    const nextOwner = applyBpmnDocumentTransaction(owner, prepared.plan.transaction), next = readBpmnDocument(nextOwner);
    expect(next.byId.get("F")!.getAttribute("sourceRef")).toBe("End");
    if (lane) expect(next.byId.get("L2")!.getElementsByTagNameNS(ns.model, "flowNodeRef")[0].textContent).toBe("B");
    else expect(nextOwner).toBe(owner);
    expect(owner).toEqual(before);
  });

  it("does not interpret an opaque flow-like payload when replacing its enclosing task", () => {
    const owner = operationOwner();
    const opaque = '<v:exact qname="q:A"><b:sequenceFlow id="OpaqueFlow" sourceRef="A" targetRef="VendorLiteral"/></v:exact>';
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<v:exact qname="q:A"/>', opaque);
    const before = structuredClone(owner), next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnReplace(owner, operationScope, { elementId: "A", diId: "ADI" }, "serviceTask").transaction));
    expect(next.byId.get("A")!.localName).toBe("serviceTask");
    expect(next.document.getElementsByTagNameNS("urn:vendor", "exact")[0].getElementsByTagNameNS(ns.model, "sequenceFlow")[0].getAttribute("targetRef")).toBe("VendorLiteral");
    expect(next.payload.bindings).toEqual(owner.structure!.payload.bindings);
    expect(owner).toEqual(before);
  });

  it("retains an unchanged unfinished data association during a no-op containment request", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:outgoing>q:F</b:outgoing>',
      '<b:outgoing>q:F</b:outgoing><b:dataOutputAssociation id="UnfinishedData"><b:sourceRef>Data</b:sourceRef><b:targetRef>Child</b:targetRef></b:dataOutputAssociation>');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["A"], parentElementId: "P" });
    if (prepared.kind !== "ready") throw new Error("An unchanged native owner needs no ownership decision");
    expect(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)).toBe(owner);
  });

  it("allocates first-label paint against the generated label's actual context", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('targetNamespace="urn:test"', 'xmlns:paint="urn:root" targetNamespace="urn:test"')
      .replace('<bd:BPMNShape id="ADI"', `<bd:BPMNShape xmlns:paint="${colors.color}" id="ADI"`);
    const before = structuredClone(owner);
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnAppearance(owner, operationScope, "ADI", { labelColor: "teal" }).transaction));
    const di = next.byId.get("ADI")!, label = di.getElementsByTagNameNS(ns.diagram, "BPMNLabel")[0];
    expect(label.getAttributeNS(colors.color, "color")).toBe("teal");
    expect(di.lookupNamespaceURI("paint")).toBe(colors.color);
    expect(next.document.documentElement.lookupNamespaceURI("paint")).toBe("urn:root");
    expect(owner).toEqual(before);
  });

  it("does not create an empty label when clearing absent label color", () => {
    const owner = operationOwner();
    expect(applyBpmnDocumentTransaction(owner, planBpmnAppearance(owner, operationScope, "ADI", { labelColor: null }).transaction)).toBe(owner);
  });

  function cancelEvent(kind: "end" | "boundary") {
    const owner = operationOwner();
    const created = planBpmnCreateShape(owner, operationScope, { toolId: kind === "end" ? "endEvent-cancel" : "boundaryEvent-cancel",
      ...(kind === "end" ? { parentElementId: "TX" } : { attachedToElementId: "TX" }), position: { x: 330, y: 350 } });
    return { owner: applyBpmnDocumentTransaction(owner, created.transaction), created };
  }

  it.each([false, true])("refuses moving transaction cancellation into a process (referenced=%s)", referenced => {
    const prepared = cancelEvent("end"); let owner = prepared.owner;
    if (referenced) {
      const parsed = readBpmnDocument(owner), event = parsed.byId.get(prepared.created.selection!.elementId)!;
      const definition = event.getElementsByTagNameNS(ns.model, "cancelEventDefinition")[0]; definition.setAttribute("id", "SharedCancel");
      parsed.document.documentElement.insertBefore(definition, parsed.document.documentElement.firstElementChild);
      const reference = parsed.document.createElementNS(ns.model, "b:eventDefinitionRef"); reference.textContent = "q:SharedCancel"; event.append(reference);
      owner = { ...owner, structure: { ...owner.structure!, payload: { ...owner.structure!.payload, documentXml: new XMLSerializer().serializeToString(parsed.document) } } };
    }
    const before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: [prepared.created.selection!.elementId], parentElementId: "P" })).toThrow("transaction");
    expect(owner).toEqual(before);
  });

  it.each(["end", "boundary"] as const)("refuses replacing a transaction while retaining its cancel %s event", kind => {
    const { owner } = cancelEvent(kind), before = structuredClone(owner);
    expect(() => planBpmnReplace(owner, operationScope, { elementId: "TX", diId: "TXDI" }, "subProcess-expanded")).toThrow("transaction");
    expect(owner).toEqual(before);
  });

  it("checks an idless cancellation event when replacing its transaction scope", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:transaction id="TX"/>', '<b:transaction id="TX"><b:endEvent><b:cancelEventDefinition/></b:endEvent></b:transaction>');
    const before = structuredClone(owner);
    expect(() => planBpmnReplace(owner, operationScope, { elementId: "TX", diId: "TXDI" }, "subProcess-expanded")).toThrow("transaction");
    expect(owner).toEqual(before);
  });

  it("refuses leaving an idless boundary behind when moving its host", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:exclusiveGateway id="G"/>', '<b:boundaryEvent attachedToRef="A"><b:timerEventDefinition/></b:boundaryEvent><b:exclusiveGateway id="G"/>');
    const before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["A", "B"], parentElementId: "TX" })).toThrow("same standard flow scope");
    expect(owner).toEqual(before);
  });

  it("keeps an unchanged valid cancellation context as a no-op", () => {
    const { owner, created } = cancelEvent("end");
    const result = prepareBpmnReparent(owner, operationScope, { elementIds: [created.selection!.elementId], parentElementId: "TX" });
    if (result.kind !== "ready") throw new Error("An unchanged event needs no native context transition");
    expect(applyBpmnDocumentTransaction(owner, result.plan.transaction)).toBe(owner);
  });

  it("keeps event-body-only start triggers inside an event subprocess", () => {
    const owner = operationOwner(), created = planBpmnCreateShape(owner, operationScope, { toolId: "startEvent-error-event-body", parentElementId: "ES", position: { x: 680, y: 330 } });
    const next = applyBpmnDocumentTransaction(owner, created.transaction), before = structuredClone(next);
    expect(() => prepareBpmnReparent(next, operationScope, { elementIds: [created.selection!.elementId], parentElementId: "P" })).toThrow("event subprocess");
    expect(next).toEqual(before);
  });

  it("does not globally reject unrelated unfinished event contexts during a data move", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:task id="A"', '<b:endEvent id="Unfinished"><b:cancelEventDefinition/></b:endEvent><b:task id="A"');
    const result = prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" });
    expect(result.kind).toBe("ready");
  });

  it("retains an unfinished inner event context when moving its enclosing scope unchanged", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:task id="Child"/>', '<b:task id="Child"/><b:endEvent><b:cancelEventDefinition/></b:endEvent>');
    const result = prepareBpmnReparent(owner, operationScope, { elementIds: ["S"], parentElementId: "TX" });
    if (result.kind !== "ready") throw new Error("Unconfigured standard scope needs no native ownership change");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, result.plan.transaction));
    expect(next.byId.get("S")!.parentElement!.getAttribute("id")).toBe("TX");
    expect(next.byId.get("S")!.getElementsByTagNameNS(ns.model, "cancelEventDefinition")).toHaveLength(1);
  });

  it.each([false, true])("does not delete standard B through an opaque vendor ID (bound=%s)", bound => {
    const owner = operationOwner(), payload = owner.structure!.payload;
    payload.bindings = bound ? [{ elementId: "B", childNodeId: "native" }] : [];
    if (!bound) payload.activities = [];
    payload.documentXml = (payload.documentXml as string).replace('<v:exact qname="q:A"/>', '<v:exact qname="q:A"><v:child id="B"/></v:exact>')
      .replace('<b:sequenceFlow id="F"', '<b:sequenceFlow id="Keep" sourceRef="B" targetRef="G"/><b:sequenceFlow id="F"');
    const before = structuredClone(owner), result = prepareBpmnDelete(owner, operationScope, ["A"]);
    if (result.kind !== "ready") throw new Error("Unrelated B must not need native cleanup");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, result.plan.transaction));
    expect(next.byId.has("A")).toBe(false);
    for (const id of ["B", "BDI", "Keep"]) expect(next.byId.has(id)).toBe(true);
    expect(next.payload.bindings).toEqual(payload.bindings);
    expect(next.payload.activities).toEqual(payload.activities);
    expect(owner).toEqual(before);
  });

  it.each([false, true])("moves exactly two selected idless memberships without moving the third member (duplicate=%s)", duplicate => {
    const owner = operationOwner();
    const extra = duplicate ? "<b:flowNodeRef>A</b:flowNodeRef>" : "";
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:task id="A"', `<b:laneSet id="Lanes"><b:lane id="L1"><b:flowNodeRef>A</b:flowNodeRef>${extra}<b:flowNodeRef>B</b:flowNodeRef><b:flowNodeRef>G</b:flowNodeRef></b:lane><b:lane id="L2"/></b:laneSet><b:task id="A"`)
      .replace('<bd:BPMNShape id="ADI"', '<bd:BPMNShape id="L1DI" bpmnElement="L1"><dc:Bounds x="0" y="0" width="1000" height="220"/></bd:BPMNShape><bd:BPMNShape id="L2DI" bpmnElement="L2"><dc:Bounds x="0" y="220" width="1000" height="400"/></bd:BPMNShape><bd:BPMNShape id="ADI"');
    const before = structuredClone(owner), result = prepareBpmnReparent(owner, operationScope, { elementIds: ["A", "B"], parentElementId: "P", laneId: "L2" });
    if (result.kind !== "ready") throw new Error("A lane-only move does not change native lexical ownership");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, result.plan.transaction));
    const members = (id: string) => [...next.byId.get(id)!.getElementsByTagNameNS(ns.model, "flowNodeRef")].map(element => element.textContent);
    expect(members("L1")).toEqual(["G"]);
    expect(new Set(members("L2"))).toEqual(new Set(["A", "B"]));
    expect(owner).toEqual(before);
  });

  it("refuses stealing data definitions from an unselected whitespace IDREF", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:dataObjectReference id="SharedData" dataObjectRef=" Object "/><b:sequenceFlow id="F"');
    const before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" })).toThrow("shared data-object");
    expect(owner).toEqual(before);
  });

  it("moves a sole whitespace IDREF with its exact data definition", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('dataObjectRef="Object"', 'dataObjectRef=" Object "');
    const result = prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" });
    if (result.kind !== "ready") throw new Error("Unconfigured data needs no native ownership change");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, result.plan.transaction));
    expect(next.byId.get("Object")!.parentElement!.getAttribute("id")).toBe("TX");
    expect(next.byId.get("Data")!.parentElement!.getAttribute("id")).toBe("TX");
    expect(next.byId.get("Data")!.getAttribute("dataObjectRef")).toBe(" Object ");
  });

  it("copies and edits a QName-selected font while retaining the shared style", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<dc:Bounds x="100.25" y="100.5" width="100" height="80"/>', '<dc:Bounds x="100.25" y="100.5" width="100" height="80"/><bd:BPMNLabel labelStyle="q:Shared"/>')
      .replace('</bd:BPMNPlane></bd:BPMNDiagram>', '</bd:BPMNPlane><bd:BPMNLabelStyle id="Shared"><dc:Font name="Original" size="12" v:keep="exact"/></bd:BPMNLabelStyle></bd:BPMNDiagram>');
    const before = structuredClone(owner), result = planBpmnAppearance(owner, operationScope, "ADI", { font: { size: 15 } });
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, result.transaction));
    expect(readBpmnDocumentAppearance(next.byId.get("ADI")!).labelFont).toMatchObject({ name: "Original", size: 15 });
    expect(next.byId.get("Shared")!.firstElementChild!.getAttribute("size")).toBe("12");
    expect(owner).toEqual(before);
  });

  it("edits label color in the actual label namespace context", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<bd:BPMNShape id="ADI"', `<bd:BPMNShape xmlns:paint="${colors.color}" id="ADI"`)
      .replace('<dc:Bounds x="100.25" y="100.5" width="100" height="80"/>', '<dc:Bounds x="100.25" y="100.5" width="100" height="80"/><bd:BPMNLabel xmlns:paint="urn:shadow" paint:keep="exact"/>');
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnAppearance(owner, operationScope, "ADI", { labelColor: "teal" }).transaction));
    const label = next.byId.get("ADI")!.getElementsByTagNameNS(ns.diagram, "BPMNLabel")[0];
    expect(label.getAttributeNS(colors.color, "color")).toBe("teal");
    expect(label.getAttributeNS("urn:shadow", "keep")).toBe("exact");
    expect(label.lookupNamespaceURI("paint")).toBe("urn:shadow");
  });

  it("resolves a returned empty-namespace idless property address in both planners", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<v:exact qname="q:A"/>', '<v:payload><field value="before"/></v:payload>');
    const properties = readBpmnStandardProperties(owner, operationScope, { elementId: "A" });
    const field = properties.children[0].children[0].children[0];
    expect(field).toMatchObject({ type: "field", namespaceURI: "" });
    const plan = planBpmnStandardProperty(owner, operationScope, { kind: "attribute", target: field.address, name: "value", value: "after" });
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction));
    expect(next.byId.get("A")!.getElementsByTagName("field")[0].getAttribute("value")).toBe("after");
  });
});
