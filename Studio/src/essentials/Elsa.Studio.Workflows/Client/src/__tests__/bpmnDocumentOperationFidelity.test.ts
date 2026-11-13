import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnAppearanceNamespaces as colors, readBpmnDocumentAppearance } from "../bpmn/bpmnDocumentAppearance";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnAppearance, planBpmnStandardProperty, readBpmnStandardProperties } from "../bpmn/bpmnDocumentPropertyOperations";
import { prepareBpmnDelete } from "../bpmn/bpmnDocumentTransformOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("complete operation identity and property fidelity", () => {
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
