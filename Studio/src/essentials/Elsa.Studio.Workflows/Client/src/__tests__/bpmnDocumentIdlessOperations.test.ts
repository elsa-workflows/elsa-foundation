import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { prepareBpmnPoolDelete } from "../bpmn/bpmnDocumentPoolOperations";
import { planBpmnReplace, prepareBpmnDelete } from "../bpmn/bpmnDocumentTransformOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("retained idless operation dependencies", () => {
  it("refuses stranding an actual idless sequence flow across scopes", () => {
    const owner = operationOwner(), before = structuredClone(owner);
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:sequenceFlow sourceRef="G" targetRef="B"/><b:sequenceFlow id="F"');
    const source = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "TX" })).toThrow("cross standard scopes");
    expect(owner).toEqual(source); expect(before.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
  });

  it("moves mixed identified and idless incident flows by descending physical address", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:exclusiveGateway id="G"/>', '<b:exclusiveGateway id="G"/><b:task id="C"/><b:sequenceFlow sourceRef="G" targetRef="C" v:keep="first"/><b:sequenceFlow id="Middle" sourceRef="G" targetRef="C"/><b:sequenceFlow sourceRef="G" targetRef="C" v:keep="last"/>')
      .replace('<bd:BPMNShape id="GDI"', '<bd:BPMNShape id="CDI" bpmnElement="C"><dc:Bounds x="700" y="120" width="100" height="80"/></bd:BPMNShape><bd:BPMNShape id="GDI"');
    const before = structuredClone(owner), prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G", "C"], parentElementId: "TX" });
    if (prepared.kind !== "ready") throw new Error("Unbound syntax needs no native ownership decision");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    const flows = [...next.document.getElementsByTagNameNS(ns.model, "sequenceFlow")].filter(flow => flow.getAttribute("sourceRef") === "G");
    expect(flows).toHaveLength(3); expect(flows.every(flow => flow.parentElement!.id === "TX")).toBe(true);
    expect(flows.filter(flow => !flow.hasAttribute("id"))).toHaveLength(2);
    expect(new Set(flows.map(flow => flow.getAttributeNS("urn:vendor", "keep")).filter(Boolean))).toEqual(new Set(["first", "last"]));
    expect(owner).toEqual(before);
  });

  it("checks idless incident flow roles on an actual gateway replacement", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:sequenceFlow sourceRef="G" targetRef="B"/><b:sequenceFlow id="F"');
    expect(() => planBpmnReplace(owner, operationScope, { elementId: "G", diId: "GDI" }, "eventBasedGateway")).toThrow("receive task");
  });

  it("retains unfinished incident roles during a no-op replacement", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:sequenceFlow id="F" sourceRef="A" targetRef="B"/>', '<b:endEvent id="End"/><b:sequenceFlow id="F" sourceRef="End" targetRef="G"/>');
    const plan = planBpmnReplace(owner, operationScope, { elementId: "G", diId: "GDI" }, "exclusiveGateway");
    expect(applyBpmnDocumentTransaction(owner, plan.transaction)).toBe(owner);
  });

  function auxiliary(kind: "task" | "endEvent", associationId = true) {
    const owner = operationOwner(), input = kind === "endEvent", direction = input ? "dataInput" : "dataOutput";
    const io = `<b:${direction} id="AuxIo"/>`, body = input ? `${io}<b:inputSet><b:dataInputRefs>AuxIo</b:dataInputRefs></b:inputSet>`
      : `<b:ioSpecification>${io}<b:inputSet/><b:outputSet><b:dataOutputRefs>AuxIo</b:dataOutputRefs></b:outputSet></b:ioSpecification>`;
    const association = `<b:${direction}Association ${associationId ? 'id="AuxAssociation"' : ""}><b:sourceRef>${input ? "Data" : "AuxIo"}</b:sourceRef><b:targetRef>${input ? "AuxIo" : "Data"}</b:targetRef></b:${direction}Association>`;
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:exclusiveGateway id="G"/>', `<b:${kind}>${body}${association}</b:${kind}><b:exclusiveGateway id="G"/>`);
    return owner;
  }

  it.each(["task", "endEvent"] as const)("retains unrelated data syntax beneath an idless %s", kind => {
    const owner = auxiliary(kind), before = structuredClone(owner);
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "TX" });
    if (prepared.kind !== "ready") throw new Error("Unrelated unbound syntax needs no ownership decision");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect(next.byId.get("AuxAssociation")!.parentElement!.hasAttribute("id")).toBe(false);
    expect(next.byId.get("G")!.parentElement!.id).toBe("TX"); expect(owner).toEqual(before);
  });

  it.each([false, true])("checks affected associations below idless owners (identified association=%s)", identified => {
    const owner = auxiliary("task", identified), before = structuredClone(owner);
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" })).toThrow("incident data association");
    expect(owner).toEqual(before);
  });

  it.each([false, true])("matches a neutral association's real idless owner (identified association=%s)", identified => {
    const owner = auxiliary("task", identified);
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "P" });
    if (prepared.kind !== "ready") throw new Error("Neutral data syntax needs no ownership decision");
    expect(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)).toBe(owner);
  });

  it("retains an unchanged unfinished cross-process association without requiring edit access to its owner", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('</b:process>', '</b:process><b:process id="Other"><b:task><b:ioSpecification><b:dataOutput id="ForeignOutput"/><b:inputSet/><b:outputSet/></b:ioSpecification><b:dataOutputAssociation id="ForeignAssociation"><b:sourceRef>ForeignOutput</b:sourceRef><b:targetRef>Data</b:targetRef></b:dataOutputAssociation></b:task></b:process>');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "P" });
    if (prepared.kind !== "ready") throw new Error("An unchanged source needs no ownership decision");
    expect(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)).toBe(owner);
  });

  it("matches an untouched idless event when a preceding identified event moves with its host", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:exclusiveGateway id="G"/>', '<b:eventBasedGateway id="G"/><b:task id="C"/><b:receiveTask id="R"/><b:boundaryEvent id="MovingBoundary" attachedToRef="C"/><b:boundaryEvent attachedToRef="R"/><b:sequenceFlow id="UnfinishedInput" sourceRef="G" targetRef="R"/>')
      .replace('<bd:BPMNShape id="GDI"', '<bd:BPMNShape id="CDI" bpmnElement="C"><dc:Bounds x="800" y="100" width="100" height="80"/></bd:BPMNShape><bd:BPMNShape id="MovingBoundaryDI" bpmnElement="MovingBoundary"><dc:Bounds x="800" y="160" width="36" height="36"/></bd:BPMNShape><bd:BPMNShape id="GDI"');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["C"], parentElementId: "TX" });
    if (prepared.kind !== "ready") throw new Error("An unbound host needs no native ownership decision");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect(next.byId.get("MovingBoundary")!.parentElement!.id).toBe("TX");
    expect([...next.document.getElementsByTagNameNS(ns.model, "boundaryEvent")].find(event => event.getAttribute("attachedToRef") === "R")!.parentElement!.id).toBe("P");
  });

  it("does not steal a data definition from an idless reference", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:dataObjectReference dataObjectRef=" Object "/><b:sequenceFlow id="F"');
    expect(() => prepareBpmnReparent(owner, operationScope, { elementIds: ["Data"], parentElementId: "TX" })).toThrow("shared data-object");
  });

  it("removes actual idless incident relationships and boundaries with their deleted host", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:boundaryEvent attachedToRef="B"><b:timerEventDefinition/></b:boundaryEvent><b:sequenceFlow sourceRef="G" targetRef="B"/><b:sequenceFlow id="F"');
    const prepared = prepareBpmnDelete(owner, operationScope, ["B"]);
    if (prepared.kind !== "ready") throw new Error("Deleting unbound B needs no native cleanup");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect(next.document.getElementsByTagNameNS(ns.model, "sequenceFlow")).toHaveLength(0);
    expect(next.document.getElementsByTagNameNS(ns.model, "boundaryEvent")).toHaveLength(0);
    expect(next.payload.bindings).toEqual(owner.structure!.payload.bindings);
  });

  it("removes mixed idless dependent siblings without shifting retained boundary addresses", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:sequenceFlow id="F"', '<b:boundaryEvent attachedToRef="B"/><b:boundaryEvent id="MiddleBoundary" attachedToRef="B"/><b:boundaryEvent attachedToRef="B"/><b:boundaryEvent attachedToRef="A"/><b:sequenceFlow id="F"');
    const prepared = prepareBpmnDelete(owner, operationScope, ["B"]);
    if (prepared.kind !== "ready") throw new Error("Unbound deletion needs no native cleanup");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    const boundaries = [...next.document.getElementsByTagNameNS(ns.model, "boundaryEvent")];
    expect(boundaries).toHaveLength(1); expect(boundaries[0].getAttribute("attachedToRef")).toBe("A");
    expect(boundaries[0].hasAttribute("id")).toBe(false);
    expect(next.document.getElementsByTagNameNS(ns.diagram, "BPMNEdge")).toHaveLength(0);
  });

  it("refuses an active edge without DI identity atomically rather than inventing an identity", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('</bd:BPMNPlane>', '<bd:BPMNEdge bpmnElement="F"><di:waypoint x="100" y="100"/><di:waypoint x="200" y="100"/></bd:BPMNEdge></bd:BPMNPlane>');
    const before = structuredClone(owner);
    expect(() => prepareBpmnDelete(owner, operationScope, ["B"])).toThrow("edge identity");
    expect(owner).toEqual(before);
  });

  it("retains an unchanged unfinished idless DI dependency during a no-op containment request", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<bd:BPMNPlane id="SubPlane" bpmnElement="S">', '<bd:BPMNPlane id="SubPlane" bpmnElement="S"><bd:BPMNShape bpmnElement="G"><dc:Bounds x="10" y="10" width="50" height="50"/></bd:BPMNShape>');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "P" });
    if (prepared.kind !== "ready") throw new Error("An unchanged source needs no ownership decision");
    expect(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)).toBe(owner);
  });

  it("retains a process shared by an idless participant", () => {
    const original = operationOwner(), created = planBpmnCreateShape(original, operationScope, { toolId: "participant-expanded", position: { x: 1200, y: 800 } });
    const owner = applyBpmnDocumentTransaction(original, created.transaction), parsed = readBpmnDocument(owner), pool = parsed.byId.get(created.selection!.elementId)!;
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('</b:collaboration>', `<b:participant processRef="${pool.getAttribute("processRef")}"/></b:collaboration>`);
    expect(() => prepareBpmnPoolDelete(owner, created.nextScope!, created.selection!.elementId, { deleteProcess: true })).toThrow("shared process");
  });
});
