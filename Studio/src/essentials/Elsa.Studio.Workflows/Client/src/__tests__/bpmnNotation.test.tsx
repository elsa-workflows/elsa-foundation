import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { BpmnNotation, bpmnNotationFromElement, type BpmnNotationDescriptor } from "../bpmn/bpmnNotation";

const model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
function render(descriptor: BpmnNotationDescriptor) {
  const container = document.createElement("div");
  container.innerHTML = renderToStaticMarkup(<BpmnNotation descriptor={descriptor} />);
  return container.querySelector("svg")!;
}
function xml(source: string, id: string) {
  const document = new DOMParser().parseFromString(`<b:definitions xmlns:b="${model}" xmlns:tns="urn:notation" xmlns:vendor="urn:vendor" targetNamespace="urn:notation">${source}</b:definitions>`, "application/xml");
  return [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
}

describe("BPMN integrated SVG notation", () => {
  it.each([
    ["startEvent", 1, "1.5"], ["intermediateCatchEvent", 2, "1.5"], ["intermediateThrowEvent", 2, "1.5"], ["endEvent", 1, "3.5"]
  ] as const)("distinguishes %s event boundary rings", (type, rings, stroke) => {
    const svg = render({ type, width: 40, height: 40 });
    expect(svg.querySelectorAll("circle")).toHaveLength(rings);
    expect(svg.querySelector(".wf-bpmn-notation-outline")!.getAttribute("stroke-width")).toBe(stroke);
    expect(svg.getAttribute("viewBox")).toBe("0 0 40 40");
    expect(svg.getAttribute("role")).toBe("img");
  });

  it("shows interrupting vs non-interrupting boundary events with accurate double/dashed rings", () => {
    const interrupting = render({ type: "boundaryEvent", eventDefinitions: ["timerEventDefinition"] });
    const nonInterrupting = render({ type: "boundaryEvent", cancelActivity: false, eventDefinitions: ["timerEventDefinition"] });
    expect(interrupting.querySelectorAll("circle[stroke-dasharray]")).toHaveLength(0);
    expect(nonInterrupting.querySelectorAll("circle[stroke-dasharray='4 3']")).toHaveLength(2);
    expect(nonInterrupting.getAttribute("aria-label")).toContain("non-interrupting");
    expect(nonInterrupting.querySelector("[data-bpmn-marker='timer'] path")!.getAttribute("d")).toContain("V50");
  });

  it("fills throwing message symbols while keeping catching messages open", () => {
    const catchEvent = render({ type: "intermediateCatchEvent", eventDefinitions: ["messageEventDefinition"] });
    const throwEvent = render({ type: "intermediateThrowEvent", eventDefinitions: ["messageEventDefinition"] });
    expect(catchEvent.querySelector("[data-bpmn-marker='message'] rect")!.getAttribute("fill")).toBe("none");
    expect(throwEvent.querySelector("[data-bpmn-marker='message'] rect")!.getAttribute("fill")).toBe("currentColor");
    expect(throwEvent.querySelector(".wf-bpmn-symbol-inverse")).not.toBeNull();
  });

  it.each(["message", "timer", "signal", "error", "escalation", "cancel", "compensation", "conditional", "link", "terminate"])("draws a recognizable %s event symbol", kind => {
    const svg = render({ type: "intermediateCatchEvent", eventDefinitions: [kind] });
    const marker = svg.querySelector(`[data-bpmn-marker='${kind}']`)!;
    expect(marker).not.toBeNull();
    expect(marker.querySelector("text")).toBeNull();
    expect(svg.getAttribute("aria-label")).toContain(kind);
  });

  it("distinguishes multiple and parallel-multiple catch events", () => {
    const descriptor = { type: "startEvent", eventDefinitions: ["messageEventDefinition", "signalEventDefinition"] };
    const multiple = render(descriptor);
    const parallel = render({ ...descriptor, parallelMultiple: true });
    expect(multiple.querySelector("[data-bpmn-marker='multiple'] polygon")).not.toBeNull();
    expect(multiple.querySelector("[data-bpmn-marker='multiple'] polygon")!.getAttribute("points")!.split(" ")).toHaveLength(5);
    expect(parallel.querySelector("[data-bpmn-marker='parallelMultiple'] path")).not.toBeNull();
    expect(parallel.getAttribute("aria-label")).toContain("parallel multiple");
  });

  it.each([
    ["exclusiveGateway", "path"], ["parallelGateway", "path"], ["inclusiveGateway", "circle"],
    ["complexGateway", "path"], ["eventBasedGateway", "polygon"]
  ])("keeps %s as a diamond with its own marker", (type, marker) => {
    const svg = render({ type, width: 50, height: 50 });
    expect(svg.querySelector("polygon.wf-bpmn-notation-outline")!.getAttribute("points")).toBe("25,2 48,25 25,48 2,25");
    expect(svg.querySelector(`[data-bpmn-marker='${type}'] ${marker}`)).not.toBeNull();
  });

  it("retains hidden exclusive markers and instantiating event-gateway ring semantics", () => {
    expect(render({ type: "exclusiveGateway", gatewayMarkerVisible: false }).querySelector("[data-bpmn-marker='exclusiveGateway']")!.children).toHaveLength(0);
    expect(render({ type: "eventBasedGateway" }).querySelectorAll("circle")).toHaveLength(2);
    expect(render({ type: "eventBasedGateway", instantiate: true }).querySelectorAll("circle")).toHaveLength(1);
    expect(render({ type: "eventBasedGateway", eventGatewayType: "Parallel" }).querySelectorAll("circle")).toHaveLength(1);
  });

  it.each([
    ["userTask", "user"], ["serviceTask", "service"], ["manualTask", "manual"], ["scriptTask", "script"],
    ["businessRuleTask", "business-rule"], ["sendTask", "send"], ["receiveTask", "receive"]
  ])("gives %s its semantic task icon", (type, marker) => {
    const svg = render({ type });
    expect(svg.querySelector("rect.wf-bpmn-notation-outline")!.getAttribute("rx")).toBe("10");
    expect(svg.querySelector(`[data-bpmn-marker='${marker}']`)).not.toBeNull();
  });

  it("draws call, transaction, event, collapsed and ad-hoc subprocess distinctions", () => {
    expect(render({ type: "callActivity" }).querySelector("rect.wf-bpmn-notation-outline")!.getAttribute("stroke-width")).toBe("3.5");
    expect(render({ type: "transaction" }).querySelectorAll("rect")).toHaveLength(3); // two boundaries + collapsed marker
    expect(render({ type: "subProcess", eventSubProcess: true }).querySelector(".wf-bpmn-notation-outline")!.getAttribute("stroke-dasharray")).toBe("2 3");
    expect(render({ type: "subProcess", isExpanded: true }).querySelector("[data-bpmn-marker='collapsed']")).toBeNull();
    const adhoc = render({ type: "adHocSubProcess", compensation: true, loop: "sequential" });
    expect([...adhoc.querySelectorAll("[data-bpmn-marker]")].map(element => element.getAttribute("data-bpmn-marker"))).toEqual(["sequential", "compensation", "ad-hoc", "collapsed"]);
    expect(new Set([...adhoc.querySelectorAll("[data-bpmn-marker]")].map(element => element.getAttribute("transform"))).size).toBe(4);
  });

  it("marks an instantiating receive task separately from an ordinary receive", () => {
    expect(render({ type: "receiveTask" }).querySelector("circle")).toBeNull();
    const instantiate = render({ type: "receiveTask", instantiate: true });
    expect(instantiate.querySelector("[data-bpmn-marker='instantiating-receive'] circle")).not.toBeNull();
    expect(instantiate.querySelector("[data-bpmn-marker='receive'] rect")).not.toBeNull();
  });

  it("distinguishes standard loops and both multi-instance directions", () => {
    expect(render({ type: "task", loop: "standard" }).querySelector("[data-bpmn-marker='standard'] path")!.getAttribute("d")).toContain("A5 5");
    expect(render({ type: "task", loop: "parallel" }).querySelector("[data-bpmn-marker='parallel'] path")!.getAttribute("d")).toBe("M2 1V13M6 1V13M10 1V13");
    expect(render({ type: "task", loop: "sequential" }).querySelector("[data-bpmn-marker='sequential'] path")!.getAttribute("d")).toBe("M1 2H13M1 6H13M1 10H13");
  });

  it("renders data document folds, input/output arrows, collection bars and cylindrical stores", () => {
    const input = render({ type: "dataInput" });
    const output = render({ type: "dataOutput", collection: true });
    expect(input.querySelector("[data-bpmn-marker='dataInput']")!.getAttribute("fill")).toBe("none");
    expect(output.querySelector("[data-bpmn-marker='dataOutput']")!.getAttribute("fill")).toBe("currentColor");
    expect(output.querySelector("[data-bpmn-marker='collection']")).not.toBeNull();
    expect(output.querySelector(".wf-bpmn-notation-outline")!.getAttribute("d")).toContain("V14H46");
    expect(render({ type: "dataObjectReference" }).querySelector("path.wf-bpmn-notation-outline")).not.toBeNull();
    expect(render({ type: "dataStoreReference" }).querySelector("path.wf-bpmn-notation-outline")!.getAttribute("d")).toContain("C");
  });

  it("keeps pool/lane orientation, empty-pool notation and participant multiplicity distinct", () => {
    const pool = render({ type: "participant", name: "Sales", width: 600, height: 200, participantMultiplicity: true });
    expect(pool.getAttribute("viewBox")).toBe("0 0 600 200");
    expect(pool.querySelector("path:not([data-bpmn-marker])")!.getAttribute("d")).toBe("M30 1V199");
    expect(pool.querySelector("text")!.getAttribute("transform")).toContain("rotate(-90)");
    expect(pool.querySelector("[data-bpmn-marker='participant-multiplicity']")).not.toBeNull();
    const empty = render({ type: "participant", isExpanded: false, name: "External" });
    expect(empty.querySelectorAll("path")).toHaveLength(0);
    const vertical = render({ type: "participant", horizontal: false });
    expect(vertical.querySelector("path")!.getAttribute("d")).toBe("M1 30H399");
    expect(render({ type: "participant", horizontal: false, isExpanded: false, name: "External" }).querySelector("text")!.getAttribute("transform")).toContain("rotate(-90)");
    const lane = render({ type: "lane", name: "Reviewer" });
    expect(lane.querySelectorAll("path")).toHaveLength(0);
    expect(lane.querySelector("text")!.getAttribute("transform")).toContain("rotate(-90)");
  });

  it("renders annotations as brackets and groups as transparent dash-dot boundaries", () => {
    const annotation = render({ type: "textAnnotation", text: "Review\nRetain the explanation", width: 160, height: 70 });
    expect(annotation.querySelector("[data-bpmn-marker='annotation-bracket']")!.getAttribute("d")).toBe("M10 2H2V68H10");
    expect([...annotation.querySelectorAll("tspan")].map(element => element.textContent).join(" ")).toBe("Review Retain the explanation");
    expect(annotation.getAttribute("aria-label")).toContain("Retain the explanation");
    expect(annotation.querySelectorAll("tspan").length).toBeGreaterThan(1);
    expect(render({ type: "group" }).querySelector("rect")!.getAttribute("stroke-dasharray")).toBe("8 3 2 3");
  });

  it("signals unknown semantic types rather than presenting an ordinary task", () => {
    const unknown = render({ type: "vendor:Task", name: "Retained module shape" });
    expect(unknown.getAttribute("data-bpmn-notation-known")).toBe("false");
    expect(unknown.getAttribute("aria-label")).toContain("unknown BPMN notation; content retained");
    expect(unknown.querySelector("rect")!.getAttribute("rx")).toBeNull();
    expect(unknown.querySelector("rect")!.getAttribute("stroke-dasharray")).toBe("5 4");
    expect(unknown.querySelector("text")!.textContent).toBe("?");
  });
});

describe("notation descriptors from complete standard XML", () => {
  it.each([" false ", " 0 "])("preserves non-interrupting notation for XML boolean %s", value => {
    const source = `<b:process id="p"><b:subProcess id="sub" triggeredByEvent=" true "/><b:boundaryEvent id="event" cancelActivity="${value}" isInterrupting="${value}"/></b:process>`;
    expect(bpmnNotationFromElement(xml(source, "event"))).toMatchObject({ cancelActivity: false, interrupting: false });
    expect(bpmnNotationFromElement(xml(source, "sub")).eventSubProcess).toBe(true);
  });

  it("collapses XML whitespace for data-object IDREFs without interpreting a QName", () => {
    const source = '<b:process id="p"><b:dataObject id="data" isCollection=" true "/><b:dataObjectReference id="ref" dataObjectRef=" data "/></b:process>';
    expect(bpmnNotationFromElement(xml(source, "ref")).collection).toBe(true);
    expect(bpmnNotationFromElement(xml(source.replace('dataObjectRef=" data "', 'dataObjectRef="tns:data"'), "ref")).collection).toBeUndefined();
  });

  it("resolves QName group labels without treating data-object IDREFs as QNames", () => {
    const source = '<b:category id="category"><b:categoryValue id="value" value="Shared responsibilities"/></b:category><b:process id="p"><b:group id="group" categoryValueRef="tns:value"/><b:dataObject id="data" isCollection="true"/><b:dataObjectReference id="ref" dataObjectRef="tns:data"/></b:process>';
    expect(bpmnNotationFromElement(xml(source, "group")).name).toBe("Shared responsibilities");
    expect(bpmnNotationFromElement(xml(source.replace('categoryValueRef="tns:value"', 'categoryValueRef="vendor:value"'), "group")).name).toBeUndefined();
    expect(bpmnNotationFromElement(xml(source, "ref")).collection).toBeUndefined();
  });

  it.each(["timer", "message", "signal"])("renders referenced %s event definitions using namespace identity", kind => {
    const source = `<b:${kind}EventDefinition id="Shared"/><b:process id="p"><b:intermediateCatchEvent id="event"><b:eventDefinitionRef>tns:Shared</b:eventDefinitionRef></b:intermediateCatchEvent></b:process>`;
    const element = xml(source, "event"), before = element.ownerDocument.documentElement.outerHTML;
    const descriptor = bpmnNotationFromElement(element);
    expect(descriptor.eventDefinitions).toEqual([`${kind}EventDefinition`]);
    expect(render(descriptor).querySelector(`[data-bpmn-marker='${kind}']`)).not.toBeNull();
    expect(element.ownerDocument.documentElement.outerHTML).toBe(before);
    for (const reference of ["vendor:Shared", "tns:Missing", "unbound:Shared"]) {
      const unresolved = bpmnNotationFromElement(xml(source.replace("tns:Shared", reference), "event"));
      expect(render(unresolved).getAttribute("aria-label")).toContain("unresolved event definition");
      expect(render(unresolved).querySelector("[data-bpmn-marker='unresolved']")).not.toBeNull();
    }
  });

  it("does not infer a namespace for prefixed event references when targetNamespace is absent", () => {
    const element = xml('<b:timerEventDefinition id="Shared"/><b:process id="p"><b:intermediateCatchEvent id="event"><b:eventDefinitionRef>unbound:Shared</b:eventDefinitionRef></b:intermediateCatchEvent></b:process>', "event");
    element.ownerDocument.documentElement.removeAttribute("targetNamespace");
    expect(render(bpmnNotationFromElement(element)).querySelector("[data-bpmn-marker='unresolved']")).not.toBeNull();
  });

  it("reads event definitions, non-interrupting and parallel-multiple semantics without rewriting XML", () => {
    const element = xml('<b:process id="p"><b:boundaryEvent id="event" cancelActivity="false" parallelMultiple="true"><b:messageEventDefinition/><b:signalEventDefinition/></b:boundaryEvent></b:process>', "event");
    const original = new XMLSerializer().serializeToString(element);
    const descriptor = bpmnNotationFromElement(element, { width: 36, height: 36 });
    expect(descriptor).toMatchObject({ type: "boundaryEvent", cancelActivity: false, parallelMultiple: true,
      eventDefinitions: ["messageEventDefinition", "signalEventDefinition"], width: 36, height: 36 });
    expect(new XMLSerializer().serializeToString(element)).toBe(original);
  });

  it("reads loop/body markers and preserves zero/one XML boolean forms", () => {
    const element = xml('<b:process id="p"><b:subProcess id="s" triggeredByEvent="1" isForCompensation="0"><b:multiInstanceLoopCharacteristics isSequential="true"/></b:subProcess></b:process>', "s");
    expect(bpmnNotationFromElement(element, { isExpanded: false })).toMatchObject({
      type: "subProcess", loop: "sequential", eventSubProcess: true, compensation: false, isExpanded: false
    });
    expect(bpmnNotationFromElement(xml('<b:task id="t"><b:standardLoopCharacteristics/></b:task>', "t")).loop).toBe("standard");
  });

  it("resolves collection and group references and distinguishes empty participants", () => {
    const data = xml('<b:process id="p"><b:dataObject id="data" isCollection="true"/><b:dataObjectReference id="ref" dataObjectRef="data"/></b:process>', "ref");
    expect(bpmnNotationFromElement(data).collection).toBe(true);
    const group = xml('<b:category id="category"><b:categoryValue id="value" value="Shared responsibilities"/></b:category><b:process id="p"><b:group id="group" categoryValueRef="value"/></b:process>', "group");
    expect(bpmnNotationFromElement(group).name).toBe("Shared responsibilities");
    expect(bpmnNotationFromElement(xml('<b:collaboration id="c"><b:participant id="empty"/></b:collaboration>', "empty")).isExpanded).toBe(false);
    expect(bpmnNotationFromElement(xml('<b:process id="p"/><b:collaboration id="c"><b:participant id="pool" processRef="p"/></b:collaboration>', "pool")).isExpanded).toBe(true);
  });

  it("keeps foreign namespace shapes explicit even when their local name resembles a task", () => {
    const element = xml('<vendor:task id="custom"/>', "custom");
    expect(render(bpmnNotationFromElement(element)).getAttribute("data-bpmn-notation-known")).toBe("false");
  });
});
