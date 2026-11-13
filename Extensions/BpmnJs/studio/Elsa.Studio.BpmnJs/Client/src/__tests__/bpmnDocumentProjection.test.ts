import { describe, expect, it } from "vitest";
import SubprocessCompatibility from "bpmn-js/lib/features/drilldown/SubprocessCompatibility";
import { getFillColor } from "bpmn-js/lib/draw/BpmnRenderUtil";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { authoring, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

describe("unenabled schema2 native source projection", () => {
  it("repairs typed local QName references only in its clone while preserving exact raw source and native ownership", async () => {
    const source = owner(), snapshot = structuredClone(source);
    const result = await modeled(source);
    expect(result.projection.sourceXml).toBe(rawXml);
    expect(result.projection.repairs.map(repair => repair.original)).toEqual(expect.arrayContaining(["q:flow", "q:process", "q:work"]));
    expect(result.projection.xml).not.toContain("q:flow");
    expect(result.projection.xml).not.toContain("v:configuration");
    expect(result.projection.omitted.length).toBeGreaterThan(0);
    expect(result.projection.owner).toBe(source);
    expect(source).toEqual(snapshot);
    expect(result.projection.reservedIds.has("inner")).toBe(true);
    expect(result.projection.reservedIds.has("other-plane")).toBe(true);
  });

  it("selects the exact authored DI diagram and refuses a scope from another diagram", () => {
    const source = owner();
    const secondary = projectBpmnNativeDocument(authoring, source, { ...scope, diagramId: "secondary", scopeElementId: "other" });
    expect(secondary.scope.diagramId).toBe("secondary");
    expect(() => projectBpmnNativeDocument(authoring, source, { ...scope, diagramId: "secondary" })).toThrow(/not represented/);
    expect(() => projectBpmnNativeDocument(authoring, source, { ...scope, diagramId: "missing" })).toThrow(/diagram is unavailable/);
  });

  it("refuses unresolved namespace-qualified modeled refs rather than stripping their prefixes", () => {
    const source = owner(rawXml.replace("<bpmn:outgoing>q:flow", '<bpmn:outgoing xmlns:q="urn:foreign">q:flow'));
    expect(() => projectBpmnNativeDocument(authoring, source, scope)).toThrow(/original namespace scope/);
  });

  it("does not normalize IDREF fields, external QName strings or script text", () => {
    const source = owner(rawXml.replace('<bpmn:process id="process">', '<bpmn:itemDefinition id="item" structureRef="v:Type"/><bpmn:process id="process"><bpmn:scriptTask id="script"><bpmn:script>v:script text</bpmn:script></bpmn:scriptTask><bpmn:callActivity id="call" calledElement="v:ExternalProcess"/>'));
    const projected = projectBpmnNativeDocument(authoring, source, scope);
    expect(projected.xml).toContain('calledElement="v:ExternalProcess"');
    expect(projected.xml).toContain('structureRef="v:Type"');
    expect(projected.xml).toContain('sourceRef="work"');
    expect(projected.xml).toContain('v:script text');
    expect(projected.repairs.some(repair => repair.attribute === "sourceRef")).toBe(false);
  });

  it("records actual pinned subprocess compatibility normalization without claiming a rendered Modeler import", async () => {
    const { projection, definitions, moddle } = await modeled();
    let render: ((event: unknown, context: { definitions: unknown }) => void) | undefined;
    const events = { on(name: string, _priority: number, handler: typeof render) { expect(name).toBe("import.render.start"); render = handler; } };
    new SubprocessCompatibility(events as unknown as ConstructorParameters<typeof SubprocessCompatibility>[0], moddle);
    render!({}, { definitions });
    const saved = await moddle.toXML(definitions);
    const baseline = bindBpmnNativeBaseline(authoring, projection, saved.xml);
    expect(baseline.di.get("inner-di")).toMatchObject({ sourcePlaneId: "plane", nativePlaneScopeId: "sub", boundsOffset: { x: -470, y: -140 } });
    expect(projection.sourceXml).toBe(rawXml);
    expect(projection.sourceXml).not.toContain('x="180"');
  });

  it("uses one proved origin per generated plane while retaining nested plane correspondence", async () => {
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub"><bpmn:task id="inner"/></bpmn:subProcess>', '<bpmn:subProcess id="sub"><bpmn:task id="inner"/><bpmn:subProcess id="deep"><bpmn:task id="deep-task"/></bpmn:subProcess></bpmn:subProcess>')
      .replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '<bpmndi:BPMNShape id="deep-di" bpmnElement="deep"><dc:Bounds x="800" y="400" width="120" height="80"/></bpmndi:BPMNShape><bpmndi:BPMNShape id="deep-task-di" bpmnElement="deep-task"><dc:Bounds x="850" y="500" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>'));
    const { projection, baseline } = await modeled(source);
    expect(baseline.di.get("deep-di")).toMatchObject({ nativePlaneScopeId: "sub", boundsOffset: { x: -470, y: -140 } });
    expect(baseline.di.get("deep-task-di")).toMatchObject({ nativePlaneScopeId: "deep", boundsOffset: { x: -670, y: -340 } });
    const independentlyMoved = edit(baseline.xml, (_document, byId) => byId.get("deep-di")!.children[0].setAttribute("x", "180"));
    expect(() => bindBpmnNativeBaseline(authoring, projection, independentlyMoved)).toThrow(/normalization ledger/);
  });

  it("does not recenter an existing destination plane and permits only the pinned empty auxiliary plane", async () => {
    const source = owner(rawXml.replace('<bpmn:process id="other"/>', '<bpmn:process id="other"/><bpmn:process id="unused"/>')
      .replace('<bpmn:subProcess id="sub">', '<bpmn:subProcess id="empty"/><bpmn:subProcess id="sub">')
      .replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '<bpmndi:BPMNShape id="empty-di" bpmnElement="empty"><dc:Bounds x="800" y="100" width="120" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>')
      .replace('</bpmn:definitions>', '<bpmndi:BPMNDiagram id="existing-sub"><bpmndi:BPMNPlane id="sub-plane" bpmnElement="sub"/></bpmndi:BPMNDiagram></bpmn:definitions>'));
    const { baseline } = await modeled(source);
    expect(baseline.di.get("inner-di")).toMatchObject({ nativePlaneScopeId: "sub", boundsOffset: { x: 0, y: 0 } });
    const document = new DOMParser().parseFromString(baseline.xml, "application/xml");
    expect([...document.getElementsByTagNameNS(authoring.bpmnNamespaces.diagram, "BPMNPlane")].some(plane => plane.getAttribute("bpmnElement") === "empty")).toBe(true);
    expect(source.structure!.payload.documentXml).not.toContain('bpmnElement="empty"/>');
  });

  it("refuses nontranslation route normalization and changed DI identities", async () => {
    const { projection, baseline } = await modeled();
    const changed = edit(baseline.xml, (_document, byId) => byId.get("flow-di")!.children[1].setAttribute("x", "999"));
    expect(() => bindBpmnNativeBaseline(authoring, projection, changed)).toThrow(/not a reversible translation/);
    const replaced = edit(baseline.xml, (_document, byId) => byId.get("work-di")!.setAttribute("bpmnElement", "gateway"));
    expect(() => bindBpmnNativeBaseline(authoring, projection, replaced)).toThrow(/DI identity/);
    const lost = edit(baseline.xml, (_document, byId) => byId.get("inner")!.remove());
    expect(() => bindBpmnNativeBaseline(authoring, projection, lost)).toThrow(/standard identity 'inner'/);
  });

  it.each([["work", "name", "Unexplained"], ["gateway", "default", "high"], ["process", "isExecutable", "true"]])("refuses unexplained imported %s.%s changes", async (id, attribute, value) => {
    const { projection, baseline } = await modeled();
    const changed = edit(baseline.xml, (_document, byId) => byId.get(id)!.setAttribute(attribute, value));
    expect(() => bindBpmnNativeBaseline(authoring, projection, changed)).toThrow(/normalization ledger/);
  });

  it("refuses loss of idless event semantics and undeclared root/child geometry translations", async () => {
    const source = owner(rawXml.replace('<bpmn:endEvent id="end"/>', '<bpmn:endEvent id="end"><bpmn:messageEventDefinition messageRef="q:message"/></bpmn:endEvent>'));
    const { projection, baseline } = await modeled(source);
    const lost = edit(baseline.xml, (_document, byId) => byId.get("end")!.replaceChildren());
    expect(() => bindBpmnNativeBaseline(authoring, projection, lost)).toThrow(/normalization ledger/);
    for (const id of ["work-di", "inner-di"]) {
      const moved = edit(baseline.xml, (_document, byId) => byId.get(id)!.children[0].setAttribute("x", "999"));
      expect(() => bindBpmnNativeBaseline(authoring, projection, moved)).toThrow(/normalization ledger/);
    }
  });

  it("preserves built-in modern and legacy DI paint and exercises the actual pinned renderer precedence", async () => {
    const source = owner(rawXml.replace('xmlns:q="urn:process"', 'xmlns:paint="http://www.omg.org/spec/BPMN/non-normative/color/1.0" xmlns:bioc="http://bpmn.io/schema/bpmn/biocolor/1.0" xmlns:q="urn:process"')
      .replace('id="work-di"', 'id="work-di" paint:background-color="#123456" bioc:fill="orange"'));
    const { projection, definitions } = await modeled(source);
    const document = new DOMParser().parseFromString(projection.xml, "application/xml");
    const shape = [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === "work-di")!;
    expect(shape.getAttributeNS("http://www.omg.org/spec/BPMN/non-normative/color/1.0", "background-color")).toBe("#123456");
    expect(shape.getAttributeNS("http://bpmn.io/schema/bpmn/biocolor/1.0", "fill")).toBe("orange");
    const nativeDi = definitions.diagrams.flatMap((diagram: { plane: { planeElement?: { id: string }[] } }) => diagram.plane.planeElement ?? []).find((element: { id: string }) => element.id === "work-di");
    const nativeShape = { di: nativeDi } as unknown as Parameters<typeof getFillColor>[0];
    expect(getFillColor(nativeShape)).toBe("#123456");
    nativeDi.set("color:background-color", undefined);
    expect(getFillColor(nativeShape)).toBe("orange");
    expect(projection.sourceXml).toBe(source.structure!.payload.documentXml);
  });

  it("permits only declared typed serializer defaults while retaining their explicit authored source", async () => {
    const source = owner(rawXml.replace('id="work" name="Original"', 'id="work" name="Original" isForCompensation="0" startQuantity="1"')
      .replace('<bpmn:outgoing>q:flow</bpmn:outgoing>', '<bpmn:outgoing>q:flow</bpmn:outgoing><bpmn:multiInstanceLoopCharacteristics isSequential="false" behavior="All"/>'));
    const { projection, baseline } = await modeled(source);
    expect(baseline.xml).not.toContain('isSequential="false"');
    expect(projection.sourceXml).toContain('isSequential="false"');
    expect(projection.sourceXml).toContain('isForCompensation="0"');
  });

  it("compares QName namespace meaning even when imported value strings are unchanged", async () => {
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub">', '<bpmn:callActivity id="call" calledElement="v:ExternalProcess"/><bpmn:subProcess id="sub">'));
    const { projection, baseline } = await modeled(source);
    const rebound = edit(baseline.xml, document => document.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:v", "urn:another-vendor"));
    expect(() => bindBpmnNativeBaseline(authoring, projection, rebound)).toThrow(/normalization ledger/);
    const unprefixed = edit(baseline.xml, (_document, byId) => {
      const call = byId.get("call")!;
      call.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns", "urn:vendor"); call.setAttribute("calledElement", "ExternalProcess");
    });
    expect(() => bindBpmnNativeBaseline(authoring, projection, unprefixed)).not.toThrow();
    const unprefixedRebound = edit(unprefixed, (_document, byId) => byId.get("call")!.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns", "urn:another-vendor"));
    expect(() => bindBpmnNativeBaseline(authoring, projection, unprefixedRebound)).toThrow(/normalization ledger/);
  });
  it.each(["1", " 1 ", " true ", "0", " 0 ", " false "])("normalizes declared XML Boolean '%s' only in the private import", async value => {
    const source = owner(rawXml.replace('id="work" name="Original"', `id="work" name="Original" isForCompensation="${value}"`)
      .replace('isMarkerVisible="true"', `isMarkerVisible="${value}"`));
    const { projection, definitions } = await modeled(source);
    const expected = value.trim() === "1" || value.trim() === "true";
    const process = definitions.rootElements.find((element: { id: string }) => element.id === "process");
    expect(process.flowElements.find((element: { id: string }) => element.id === "work").isForCompensation).toBe(expected);
    const shape = definitions.diagrams.find((element: { id: string }) => element.id === "main").plane.planeElement.find((element: { id: string }) => element.id === "gateway-di");
    expect(shape.isMarkerVisible).toBe(expected);
    expect(projection.sourceXml).toBe(source.structure!.payload.documentXml);
    expect(projection.repairs).toEqual(expect.arrayContaining([expect.objectContaining({ attribute: "isForCompensation", original: value, projected: String(expected) })]));
  });

});
