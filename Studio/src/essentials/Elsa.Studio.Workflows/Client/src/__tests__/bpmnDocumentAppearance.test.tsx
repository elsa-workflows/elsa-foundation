import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { bpmnAppearanceLabelStyle, bpmnAppearanceNamespaces as ns, isBpmnAppearanceColor, readBpmnDocumentAppearance } from "../bpmn/bpmnDocumentAppearance";
import { BpmnNotation, bpmnNotationFromElement } from "../bpmn/bpmnNotation";
import { projectBpmnDocumentCanvas } from "../bpmn/bpmnDocumentCanvas";
import { documentCanvasOwner, documentCanvasScope } from "./bpmnDocumentCanvasFixture";

const model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
function fixture(shapeAttributes = "", label = "", style = "") {
  const xml = `<b:definitions xmlns:b="${model}" xmlns:d="${ns.diagram}" xmlns:dc="${ns.bounds}" xmlns:paint="${ns.color}" xmlns:old="${ns.legacyColor}" xmlns:vendor="urn:vendor">
    <b:process id="P"><b:sendTask id="T" name="Dispatch"/></b:process>
    <d:BPMNDiagram id="D"><d:BPMNPlane id="Plane" bpmnElement="P"><d:BPMNShape id="S" bpmnElement="T" ${shapeAttributes}>
      <dc:Bounds x="0.25" y="0.75" width="160" height="90"/>${label}</d:BPMNShape></d:BPMNPlane>${style}</d:BPMNDiagram></b:definitions>`;
  const document = new DOMParser().parseFromString(xml, "application/xml");
  return { document, di: document.getElementsByTagNameNS(ns.diagram, "BPMNShape")[0], element: document.getElementsByTagNameNS(model, "sendTask")[0] };
}
function svgFrom(element: Element, di: Element, status = {}) {
  const container = document.createElement("div");
  container.innerHTML = renderToStaticMarkup(<BpmnNotation descriptor={bpmnNotationFromElement(element, { appearance: readBpmnDocumentAppearance(di) })} {...status} />);
  return container.querySelector("svg")!;
}

describe("authored BPMN DI appearance", () => {
  it("uses namespace identity and modern precedence without deleting shadowed attributes or mixed vendor content", () => {
    const { document, di } = fixture('paint:background-color="#123456" old:fill="orange" paint:border-color="rgb(20, 30, 40)" old:stroke="purple" vendor:fill="lime"',
      '<d:BPMNLabel paint:color="#654321" vendor:keep="label"><vendor:mixed>before <vendor:item/> after</vendor:mixed></d:BPMNLabel>');
    const before = new XMLSerializer().serializeToString(document);
    const appearance = readBpmnDocumentAppearance(di);
    expect(appearance).toMatchObject({ fill: "#123456", stroke: "rgb(20, 30, 40)", labelColor: "#654321", issues: [],
      sources: { fill: { namespaceURI: ns.color, localName: "background-color", value: "#123456" } } });
    expect(di.getAttributeNS(ns.legacyColor, "fill")).toBe("orange");
    expect(new XMLSerializer().serializeToString(document)).toBe(before);
  });

  it("falls back to legacy paint only when modern paint is absent or empty", () => {
    expect(readBpmnDocumentAppearance(fixture('old:fill="papayawhip" paint:border-color="" old:stroke="#abc"').di))
      .toMatchObject({ fill: "papayawhip", stroke: "#abc", sources: { fill: { namespaceURI: ns.legacyColor } } });
  });

  it("keeps a whitespace-only winning modern color invalid rather than silently choosing the shadowed legacy value", () => {
    const appearance = readBpmnDocumentAppearance(fixture('paint:border-color=" " old:stroke="red"').di);
    expect(appearance.stroke).toBeUndefined();
    expect(appearance.sources.stroke?.value).toBe(" ");
    expect(appearance.issues).toHaveLength(1);
  });

  it("retains invalid winning modern values with diagnostics instead of exposing resource URLs or silently choosing legacy", () => {
    const { document, di } = fixture('paint:background-color="url(https://example.invalid/paint)" old:fill="red" paint:border-color="var(--external)"');
    const before = new XMLSerializer().serializeToString(document);
    const appearance = readBpmnDocumentAppearance(di);
    expect(appearance.fill).toBeUndefined();
    expect(appearance.stroke).toBeUndefined();
    expect(appearance.issues).toHaveLength(2);
    expect(appearance.sources.fill?.value).toContain("url(");
    expect(new XMLSerializer().serializeToString(document)).toBe(before);
  });

  it.each(["#abc", "#abcd", "#010203", "#01020380", "rebeccapurple", "rgba(1,2,3,0.5)", "hsl(120deg 30% 40% / 50%)", "oklch(60% .2 25)"])("accepts a bounded literal color %s", value => {
    expect(isBpmnAppearanceColor(value)).toBe(true);
  });

  it.each(["url(#resource)", "red;stroke:blue", "var(--paint)", "expression(alert(1))", "rgb(1 2 potato)", "rgb(1,,2,3)", "rgb(1deg 2 3)", "#12345"])("keeps unsupported paint %s source-only", value => {
    expect(isBpmnAppearanceColor(value)).toBe(false);
  });

  it("reads shared label style and all standard font flags, including explicit false, without normalizing source", () => {
    const { document, di } = fixture("", '<d:BPMNLabel labelStyle="Shared" paint:color="teal"/>',
      '<d:BPMNLabelStyle id="Shared" vendor:keep="opaque"><dc:Font name="Aptos Display" size="17.5" isBold="1" isItalic="true" isUnderline="false" isStrikeThrough="1" vendor:font="retained"/></d:BPMNLabelStyle>');
    const before = new XMLSerializer().serializeToString(document);
    expect(readBpmnDocumentAppearance(di)).toMatchObject({ labelColor: "teal", labelFont: {
      name: "Aptos Display", size: 17.5, bold: true, italic: true, underline: false, strikeThrough: true }, issues: [] });
    expect(bpmnAppearanceLabelStyle(readBpmnDocumentAppearance(di))).toMatchObject({ fontFamily: '"Aptos Display", var(--studio-font-ui)', fontSize: "17.5px", fontWeight: 700, fontStyle: "italic", textDecorationLine: "line-through" });
    expect(new XMLSerializer().serializeToString(document)).toBe(before);
  });

  it("reports missing/ambiguous styles and invalid font facts while retaining valid siblings", () => {
    expect(readBpmnDocumentAppearance(fixture("", '<d:BPMNLabel labelStyle="missing"/>').di).issues[0].reason).toContain("missing or ambiguous");
    const duplicate = '<d:BPMNLabelStyle id="duplicate"/><d:BPMNLabelStyle id="duplicate"/>';
    expect(readBpmnDocumentAppearance(fixture("", '<d:BPMNLabel labelStyle="duplicate"/>', duplicate).di).labelFont).toBeUndefined();
    const invalid = fixture("", '<d:BPMNLabel labelStyle="font"/>', '<d:BPMNLabelStyle id="font"><dc:Font name="Available Font" size="NaN" isBold="yes" isItalic="false"/></d:BPMNLabelStyle>');
    expect(readBpmnDocumentAppearance(invalid.di)).toMatchObject({ labelFont: { name: "Available Font", italic: false }, issues: [{}, {}] });
  });

  it("uses diagram length units and honors a valid zero font size without interpreting non-decimal syntax", () => {
    const zero = fixture("", '<d:BPMNLabel labelStyle="font"/>', '<d:BPMNLabelStyle id="font"><dc:Font size="0"/></d:BPMNLabelStyle>');
    expect(readBpmnDocumentAppearance(zero.di)).toMatchObject({ labelFont: { size: 0 }, issues: [] });
    expect(bpmnAppearanceLabelStyle(readBpmnDocumentAppearance(zero.di)).fontSize).toBe("0px");
    const malformed = fixture("", '<d:BPMNLabel labelStyle="font"/>', '<d:BPMNLabelStyle id="font"><dc:Font size="0x10"/></d:BPMNLabelStyle>');
    expect(readBpmnDocumentAppearance(malformed.di).issues).toHaveLength(1);
    expect(readBpmnDocumentAppearance(malformed.di).labelFont).toBeUndefined();
  });

  it("reports invalid style ownership and missing or ambiguous standard font context", () => {
    const missing = fixture("", '<d:BPMNLabel labelStyle="font"/>', '<d:BPMNLabelStyle id="font"/>');
    expect(readBpmnDocumentAppearance(missing.di).issues[0].reason).toContain("exactly one standard font");
    const duplicate = fixture("", '<d:BPMNLabel labelStyle="font"/>', '<d:BPMNLabelStyle id="font"><dc:Font/><dc:Font/></d:BPMNLabelStyle>');
    expect(readBpmnDocumentAppearance(duplicate.di).issues[0].reason).toContain("exactly one standard font");
    const wrongOwner = fixture("", '<d:BPMNLabel labelStyle="font"><d:BPMNLabelStyle id="font"><dc:Font size="12"/></d:BPMNLabelStyle></d:BPMNLabel>');
    expect(readBpmnDocumentAppearance(wrongOwner.di).issues[0].reason).toContain("missing or ambiguous");
  });

  it("reads edge stroke and label independently, ignoring shape-only fill and unqualified/vendor lookalikes", () => {
    const { document } = fixture();
    const edge = document.createElementNS(ns.diagram, "d:BPMNEdge");
    edge.setAttributeNS(ns.color, "paint:border-color", "navy");
    edge.setAttributeNS(ns.color, "paint:background-color", "red");
    edge.setAttribute("stroke", "green");
    const label = document.createElementNS(ns.diagram, "d:BPMNLabel");
    label.setAttributeNS(ns.color, "paint:color", "orange"); edge.append(label);
    expect(readBpmnDocumentAppearance(edge)).toMatchObject({ stroke: "navy", labelColor: "orange", issues: [] });
    expect(readBpmnDocumentAppearance(edge).fill).toBeUndefined();
    expect(readBpmnDocumentAppearance(document.documentElement)).toEqual({ sources: {}, issues: [] });
  });

  it("projects paint per actual DI instance, retaining exact XML, bindings and native configuration", () => {
    const owner = documentCanvasOwner();
    const payload = owner.structure!.payload as { documentXml: string };
    // Use the actual fixture DI ID rather than a semantic ID as the appearance address.
    const sourceDocument = new DOMParser().parseFromString(payload.documentXml, "application/xml");
    const actualDi = [...sourceDocument.getElementsByTagNameNS(ns.diagram, "BPMNShape")].find(shape => shape.getAttribute("bpmnElement") === "T")!;
    actualDi.setAttributeNS(ns.color, "paint:border-color", "purple");
    payload.documentXml = new XMLSerializer().serializeToString(sourceDocument);
    const before = JSON.stringify(owner);
    const scene = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(scene.nodes.find(node => node.data.semanticElementId === "T")!.data.notation.appearance?.stroke).toBe("purple");
    expect(JSON.stringify(owner)).toBe(before);
  });

  it("renders authored stroke/fill inputs and label fonts while status remains outside primary notation", () => {
    const { element, di } = fixture('paint:background-color="#123456" paint:border-color="#abcdef"',
      '<d:BPMNLabel labelStyle="Shared" paint:color="teal"/>', '<d:BPMNLabelStyle id="Shared"><dc:Font name="Aptos" size="18" isBold="true"/></d:BPMNLabelStyle>');
    const plain = svgFrom(element, di);
    const selected = svgFrom(element, di, { selected: true, faulted: true });
    expect(selected.style.color).toBe("rgb(171, 205, 239)");
    expect(selected.style.getPropertyValue("--wf-bpmn-fill")).toBe("#123456");
    expect(selected.style.getPropertyValue("--wf-bpmn-label-color")).toBe("teal");
    expect(selected.querySelector("[data-bpmn-primary]")!.outerHTML).toBe(plain.querySelector("[data-bpmn-primary]")!.outerHTML);
    expect(selected.querySelectorAll("[data-bpmn-overlay]")).toHaveLength(2);
    expect(selected.querySelector("[data-bpmn-primary] [data-bpmn-overlay]")).toBeNull();
    expect(selected.querySelector<SVGElement>(".wf-bpmn-symbol-inverse")!.style.stroke).toBe("#123456");
    const throwing = new DOMParser().parseFromString(`<b:endEvent xmlns:b="${model}" id="End"><b:messageEventDefinition/></b:endEvent>`, "application/xml").documentElement;
    expect(svgFrom(throwing, di).querySelector<SVGElement>(".wf-bpmn-symbol-inverse")!.style.stroke).toBe("#123456");
    // SVG text consumes the same label style as HTML activity labels.
    const labelSvg = svgFrom(new DOMParser().parseFromString(`<b:lane xmlns:b="${model}" id="Lane" name="Review"/>`, "application/xml").documentElement, di);
    expect(labelSvg.querySelector("text")!.style.fontSize).toBe("18px");
    expect(labelSvg.querySelector("text")!.style.fontWeight).toBe("700");
  });
});
