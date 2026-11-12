import { describe, expect, it } from "vitest";
import { projectBpmnDocumentScene, readBpmnDocument } from "../bpmn/bpmnDocument";
import { assessBpmnBoundaryAttachment, assessBpmnDocumentConnection, assessBpmnDocumentResize, bpmnDocumentDisplayEndpoints, type BpmnDocumentConnectionKind } from "../bpmn/bpmnDocumentPolicy";
import { documentCanvasOwner } from "./bpmnDocumentCanvasFixture";

const extra = `<bpmn:startEvent id="Start"/><bpmn:endEvent id="End"/><bpmn:parallelGateway id="Parallel"/><bpmn:eventBasedGateway id="Events"/>
<bpmn:receiveTask id="Receive"/><bpmn:intermediateCatchEvent id="Catch"><bpmn:timerEventDefinition/></bpmn:intermediateCatchEvent>
<bpmn:intermediateThrowEvent id="Throw"><bpmn:messageEventDefinition/></bpmn:intermediateThrowEvent>
<bpmn:intermediateCatchEvent id="LinkCatch"><bpmn:linkEventDefinition/></bpmn:intermediateCatchEvent>
<bpmn:intermediateThrowEvent id="LinkThrow"><bpmn:linkEventDefinition/></bpmn:intermediateThrowEvent>
<bpmn:subProcess id="EventSub" triggeredByEvent="true"/><bpmn:task id="CompensationWork" isForCompensation="true"/>
<bpmn:boundaryEvent id="CompensationBoundary" attachedToRef="T"><bpmn:compensateEventDefinition/></bpmn:boundaryEvent>
<bpmn:sequenceFlow id="ReceiveFlow" sourceRef="Events" targetRef="Receive"/>`;
function parsed(transform: (xml: string) => string = xml => xml) { return readBpmnDocument(documentCanvasOwner(xml => transform(xml.replace('<bpmn:userTask id="O"/>', `<bpmn:userTask id="O"/>${extra}`)))); }

describe("complete standard-source connection policies", () => {
  it.each(["timer", "message", "signal"])("uses referenced %s definitions for event-gateway and message-flow policy", kind => {
    const source = parsed(xml => xml.replace('<bpmn:process id="P">', `<bpmn:${kind}EventDefinition id="Shared"/><bpmn:process id="P">`)
      .replace('<bpmn:intermediateCatchEvent id="Catch"><bpmn:timerEventDefinition/>', '<bpmn:intermediateCatchEvent id="Catch"><bpmn:eventDefinitionRef>tns:Shared</bpmn:eventDefinitionRef>'));
    expect(assessBpmnDocumentConnection(source, "sequenceFlow", "Events", "Catch").allowed).toBe(true);
    expect(assessBpmnDocumentConnection(source, "messageFlow", "T2", "Catch").allowed).toBe(kind === "message");
  });

  it.each(["vendor:Shared", "tns:Missing"])("does not treat unresolved event reference %s as an untyped message event", reference => {
    const source = parsed(xml => xml.replace('<bpmn:process id="P">', '<bpmn:timerEventDefinition id="Shared"/><bpmn:process id="P">')
      .replace('<bpmn:intermediateCatchEvent id="Catch"><bpmn:timerEventDefinition/>', `<bpmn:intermediateCatchEvent id="Catch"><bpmn:eventDefinitionRef>${reference}</bpmn:eventDefinitionRef>`));
    expect(assessBpmnDocumentConnection(source, "messageFlow", "T2", "Catch").allowed).toBe(false);
    expect(assessBpmnDocumentConnection(source, "sequenceFlow", "Events", "Catch").allowed).toBe(false);
  });

  it.each<[BpmnDocumentConnectionKind, string, string]>([
    ["sequenceFlow", "T", "O"], ["sequenceFlow", "T", "T"], ["sequenceFlow", "I", "IE"],
    ["sequenceFlow", "Events", "Catch"], ["sequenceFlow", "Events", "Receive"], ["sequenceFlow", "B", "O"],
    ["messageFlow", "T", "T2"], ["messageFlow", "Pool", "Pool2"], ["messageFlow", "T2", "SB"],
    ["dataInputAssociation", "DR", "T"], ["dataInputAssociation", "DR", "Throw"],
    ["dataOutputAssociation", "T", "DR"], ["dataOutputAssociation", "Catch", "DR"],
    ["association", "T", "Note"], ["association", "DR", "T"], ["compensationAssociation", "CompensationBoundary", "O"]
  ])("admits %s %s→%s using semantic scope and typed endpoints", (kind, source, target) => {
    expect(assessBpmnDocumentConnection(parsed(), kind, source, target)).toEqual({ allowed: true });
  });

  it.each<[BpmnDocumentConnectionKind, string, string]>([
    ["sequenceFlow", "T", "I"], ["sequenceFlow", "T", "T2"], ["sequenceFlow", "End", "T"], ["sequenceFlow", "T", "Start"],
    ["sequenceFlow", "T", "B"], ["sequenceFlow", "EventSub", "T"], ["sequenceFlow", "CompensationWork", "T"],
    ["sequenceFlow", "CompensationBoundary", "T"], ["sequenceFlow", "LinkThrow", "T"], ["sequenceFlow", "T", "LinkCatch"],
    ["sequenceFlow", "Events", "T"], ["sequenceFlow", "DR", "T"],
    ["messageFlow", "T", "O"], ["messageFlow", "B", "T2"], ["messageFlow", "T2", "B"], ["messageFlow", "Parallel", "T2"],
    ["dataInputAssociation", "T", "DR"], ["dataOutputAssociation", "DR", "T"], ["dataInputAssociation", "DR", "Parallel"],
    ["compensationAssociation", "CompensationBoundary", "T"], ["compensationAssociation", "CompensationBoundary", "I"],
    ["association", "Sub", "I"], ["association", "T", "O"], ["sequenceFlow", "Missing", "T"]
  ])("refuses %s %s→%s with an explicit reason", (kind, source, target) => {
    expect(assessBpmnDocumentConnection(parsed(), kind, source, target)).toMatchObject({ allowed: false, reason: expect.any(String) });
  });

  it("uses actual QName reference/namespace identity, with no foreign-type or unknown-kind fallback", () => {
    const owner = documentCanvasOwner(xml => xml.replace('processRef="P2"', 'processRef="tns:P2"'));
    expect(assessBpmnDocumentConnection(readBpmnDocument(owner), "messageFlow", "Pool", "Pool2").allowed).toBe(true);
    expect(assessBpmnDocumentConnection(parsed(), "vendor:connection" as BpmnDocumentConnectionKind, "T", "Note").allowed).toBe(false);
  });

  it("checks boundary host constraints independently of ordinary sequence connections", () => {
    const source = parsed();
    expect(assessBpmnBoundaryAttachment(source, "B", "T").allowed).toBe(true);
    for (const host of ["EventSub", "CompensationWork", "Receive", "Parallel", "Missing"])
      expect(assessBpmnBoundaryAttachment(source, "B", host)).toMatchObject({ allowed: false, reason: expect.any(String) });
  });

  it("limits native resize to the pinned resizable families and annotation handles", () => {
    const source = parsed(), resize = (id: string, isExpanded = false, direction?: string) => assessBpmnDocumentResize(source.byId.get(id)!, { width: 120, height: 90, isExpanded, direction });
    for (const id of ["LaneA", "Pool", "Note"]) expect(resize(id).allowed).toBe(true);
    expect(resize("Sub", true).allowed).toBe(true);
    for (const id of ["B", "Start", "Parallel", "DR", "T", "Sub"]) expect(resize(id).allowed).toBe(false);
    expect(resize("Note", false, "e").allowed).toBe(true); expect(resize("Note", false, "n").allowed).toBe(false);
    expect(assessBpmnDocumentResize(source.byId.get("Sub")!, { width: 99, height: 80, isExpanded: true }).allowed).toBe(false);
  });

  it("retains all I/O semantic references while exposing their actual display owner", () => {
    const owner = documentCanvasOwner(), source = readBpmnDocument(owner), scene = projectBpmnDocumentScene(owner, "Diagram");
    const input = bpmnDocumentDisplayEndpoints(source, scene.edges.find(edge => edge.elementId === "IA")!);
    expect(input.source).toEqual({ referenceIds: ["DR", "DR2"], displayElementId: "DR" });
    expect(input.target).toEqual({ referenceIds: ["Input"], displayElementId: "T", ownerElementId: "T" });
    const output = bpmnDocumentDisplayEndpoints(source, scene.edges.find(edge => edge.elementId === "OA")!);
    expect(output.source).toEqual({ referenceIds: ["Output"], displayElementId: "T", ownerElementId: "T" });
  });
});
