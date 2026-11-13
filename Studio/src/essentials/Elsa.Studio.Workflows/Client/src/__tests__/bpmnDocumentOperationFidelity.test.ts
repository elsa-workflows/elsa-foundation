import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnAppearanceNamespaces as colors, readBpmnDocumentAppearance } from "../bpmn/bpmnDocumentAppearance";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { planBpmnAppearance, planBpmnStandardProperty, readBpmnStandardProperties } from "../bpmn/bpmnDocumentPropertyOperations";
import { planBpmnReplace, prepareBpmnDelete } from "../bpmn/bpmnDocumentTransformOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("complete operation identity and property fidelity", () => {
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
