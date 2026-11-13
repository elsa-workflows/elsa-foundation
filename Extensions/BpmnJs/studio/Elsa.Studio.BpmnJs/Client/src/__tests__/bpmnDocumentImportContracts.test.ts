import { describe, expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import CommandStack from "diagram-js/lib/command/CommandStack";
import ElementRegistry from "diagram-js/lib/core/ElementRegistry";
import { create } from "diagram-js/lib/model";
import UpdateModdlePropertiesHandler from "bpmn-js/lib/features/modeling/cmd/UpdateModdlePropertiesHandler";
import UpdatePropertiesHandler from "bpmn-js/lib/features/modeling/cmd/UpdatePropertiesHandler";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import ModdleCopy from "bpmn-js/lib/features/copy-paste/ModdleCopy";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { captureNativePropertyIntents } from "../bpmnDocumentIntent";
import { diffBpmnNativeCommandGroup } from "../bpmnDocumentDiff";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { snapshotNativeNamespaces } from "../bpmnDocumentNamespaceContext";
import { authoring, edit, modeled, owner, rawXml } from "./bpmnDocumentFixture";

const ns = authoring.bpmnNamespaces;
function atomicPlaneRefusal(native: Awaited<ReturnType<typeof modeled>>, group: NativeCommandGroup, next: string) {
  const original = structuredClone(native.projection.owner), apply = vi.spyOn(authoring, "applyBpmnDocumentCommands"), geometry = vi.fn(() => []);
  try {
    expect(() => mapBpmnNativeCommandGroup(authoring, native.baseline, group, next)).toThrow(/S10c/);
    expect(() => diffBpmnNativeCommandGroup(authoring, native.baseline, group, next, geometry)).toThrow(/S10c/);
    expect(apply).not.toHaveBeenCalled(); expect(geometry).not.toHaveBeenCalled(); expect(native.projection.owner).toEqual(original);
  } finally { apply.mockRestore(); }
}
describe("pinned import and command contracts", () => {
  it("reconciles actual Bounds replacement with omitted inherited zero coordinates", async () => {
    const source = owner(), native = await modeled(source), events = new EventBus(), stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
    const shape = native.definitions.diagrams.find((entry: { id: string }) => entry.id === "main").plane.planeElement[0];
    const bounds = native.moddle.create("dc:Bounds", { width: 100, height: 80 });
    expect(captureNativePropertyIntents(shape, { bounds })).toHaveLength(1);
    stack.register("element.updateModdleProperties", new UpdateModdlePropertiesHandler(new ElementRegistry(events)));
    const work = native.definitions.rootElements.find((entry: { id: string }) => entry.id === "process").flowElements.find((entry: { id: string }) => entry.id === "work");
    const groups: NativeCommandGroup[] = [], errors: unknown[] = [], detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
    try {
      stack.execute("element.updateModdleProperties", { element: create("shape", { id: "work", businessObject: work, di: shape }), moddleElement: shape, properties: { bounds } });
      expect(errors).toEqual([]); expect(groups).toHaveLength(1);
      const saved = await native.moddle.toXML(native.definitions), commands = mapBpmnNativeCommandGroup(authoring, native.baseline, groups[0], saved.xml);
      const changed = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands)).byId.get("work-di")!.firstElementChild!;
      expect([changed.getAttribute("x"), changed.getAttribute("y"), changed.getAttribute("width"), changed.getAttribute("height")]).toEqual(["0", "0", "100", "80"]);
      expect(changed.getAttributeNS("urn:vendor", "geometry")).toBe("retain");
    } finally { detach(); }
  });
  it.each([
    ["loopMaximum", (xml: string) => xml.replace('<bpmn:outgoing>q:flow</bpmn:outgoing>', '<bpmn:outgoing>q:flow</bpmn:outgoing><bpmn:standardLoopCharacteristics loopMaximum="01"/>'), 'loopMaximum="1"'],
    ["capacity", (xml: string) => xml.replace('<bpmn:process id="process">', '<bpmn:dataStore id="store" capacity="01"/><bpmn:process id="process">'), 'capacity="1"'],
    ["resolution", (xml: string) => xml.replace('BPMNDiagram id="main"', 'BPMNDiagram id="main" resolution="090.00"'), 'resolution="90"'],
    ["messageVisibleKind", (xml: string) => xml.replace('id="flow-di"', 'id="flow-di" messageVisibleKind="initiating"'), 'id="flow-di"'],
    ["font size", (xml: string) => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '</bpmndi:BPMNPlane><bpmndi:BPMNLabelStyle id="style"><dc:Font size="012.500"/></bpmndi:BPMNLabelStyle></bpmndi:BPMNDiagram>'), 'size="12.5"']
  ] as const)("accepts equivalent declared %s number spelling without changing canonical source", async (_name, alter, spelling) => {
    const source = owner(alter(rawXml)), { projection, baseline } = await modeled(source);
    expect(baseline.xml).toContain(spelling); if (_name === "messageVisibleKind") expect(baseline.xml).not.toContain("messageVisibleKind"); expect(projection.sourceXml).toBe(source.structure!.payload.documentXml);
  });

  it("compares a combined modeled script body across retained comments and CDATA", async () => {
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub">', '<bpmn:scriptTask id="script"><bpmn:script>before <!--keep--> after <![CDATA[q:Thing]]></bpmn:script></bpmn:scriptTask><bpmn:subProcess id="sub">'));
    const { projection, baseline } = await modeled(source);
    expect(new DOMParser().parseFromString(baseline.xml, "application/xml").getElementsByTagNameNS(ns.model, "script")[0].textContent).toBe("before  after q:Thing");
    expect(projection.sourceXml).toContain('<!--keep-->'); expect(projection.sourceXml).toContain('<![CDATA[q:Thing]]>');
  });

  it("uses declared DC coordinate defaults while retaining omitted source attributes", async () => {
    const source = owner(rawXml.replace('<dc:Bounds x="100"', '<dc:Bounds'));
    const { projection } = await modeled(source);
    expect(projection.di.get("work-di")!.sourceBounds!.x).toBe(0); expect(projection.sourceXml).toBe(source.structure!.payload.documentXml);
  });

  it.each([undefined, null])("admits actual pinned name removal (%s)", async name => {
    const source = owner(), native = await modeled(source), events = new EventBus();
    const stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
    stack.register("element.updateProperties", new UpdatePropertiesHandler(new ElementRegistry(events), native.moddle,
      {} as ConstructorParameters<typeof UpdatePropertiesHandler>[2], {} as ConstructorParameters<typeof UpdatePropertiesHandler>[3]));
    const work = native.definitions.rootElements.find((element: { id: string }) => element.id === "process").flowElements.find((element: { id: string }) => element.id === "work");
    const groups: NativeCommandGroup[] = [], errors: unknown[] = [];
    const detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
    try {
      stack.execute("element.updateProperties", { element: create("shape", { id: "work", businessObject: work }), properties: { name } });
      expect(errors).toEqual([]); expect(groups).toHaveLength(1);
      const saved = await native.moddle.toXML(native.definitions), commands = mapBpmnNativeCommandGroup(authoring, native.baseline, groups[0], saved.xml);
      expect(authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands)).byId.get("work")!.hasAttribute("name")).toBe(false);
    } finally { detach(); }
  });

  it("checks annotation labels against the captured text before source mutation", async () => {
    const source = owner(rawXml.replace('</bpmn:process>', '<bpmn:textAnnotation id="note"><bpmn:text>Before</bpmn:text></bpmn:textAnnotation></bpmn:process>'));
    const { baseline } = await modeled(source), original = structuredClone(source);
    const next = edit(baseline.xml, (_document, index) => { index.get("note")!.firstElementChild!.textContent = "Unexpected"; });
    const group = (newLabel: string): NativeCommandGroup => ({ id: 1, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "note", newLabel }] });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group("Expected"), next)).toThrow("completed command intent");
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, group("Unexpected"), next);
    expect(authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands)).byId.get("note")!.textContent).toBe("Unexpected"); expect(source).toEqual(original);
  });

  it.each(['xml:lang="nl"', 'xml:space="preserve"', 'xml:base="https://example.invalid/"', "gateway default"])("refuses actual pinned copies that drop retained %s", async attributes => {
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub">', `<bpmn:task id="plain" ${attributes === "gateway default" ? "" : attributes}/><bpmn:subProcess id="sub">`));
    const native = await modeled(source), events = new EventBus(), process = native.definitions.rootElements.find((element: { id: string }) => element.id === "process");
    const copiedId = attributes === "gateway default" ? "gateway" : "plain";
    const original = process.flowElements.find((element: { id: string }) => element.id === copiedId), target = native.moddle.create(original.$type);
    new ModdleCopy(events, new BpmnFactory(native.moddle), native.moddle).copyElement(original, target, undefined, true); target.set("id", "copy");
    expect(target.$attrs).toEqual({}); process.flowElements.push(target); target.$parent = process;
    const saved = await native.moddle.toXML(native.definitions), before = structuredClone(source);
    expect(() => mapBpmnNativeCommandGroup(authoring, native.baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "copy", parentId: "process" }],
      namespaceOrigins: [{ elementId: "copy", snapshot: snapshotNativeNamespaces(authoring, native.projection, copiedId) }] }, saved.xml)).toThrow("canonical XML/configuration ownership");
    expect(source).toEqual(before);
  });
  it.each([[180, 160, false], [200, 200, false], [200, 200, true]] as const)("defers first generated-plane insertion at %s,%s (unestablished=%s) atomically to S10c", async (x, y, unestablished) => {
    const source = owner(edit(rawXml, (_document, index) => { index.get("inner")!.remove(); index.get("inner-di")!.remove(); })), native = await modeled(source);
    const next = edit(native.baseline.xml, (document, index) => {
      const task = document.createElementNS(ns.model, "bpmn:task"); task.setAttribute("id", "new-child"); index.get("sub")!.append(task);
      const plane = [...document.getElementsByTagNameNS(ns.diagram, "BPMNPlane")].find(plane => plane.getAttribute("bpmnElement") === "sub")!;
      if (unestablished) plane.setAttribute("id", "unestablished-plane");
      const shape = document.createElementNS(ns.diagram, "bpmndi:BPMNShape"); shape.setAttribute("id", "new-di"); shape.setAttribute("bpmnElement", "new-child");
      const bounds = document.createElementNS(ns.bounds, "dc:Bounds"); for (const [name, value] of Object.entries({ x, y, width: 100, height: 80 })) bounds.setAttribute(name, String(value));
      shape.append(bounds); plane.append(shape);
      index.get("work")!.setAttribute("name", "Pending");
    });
    atomicPlaneRefusal(native, { id: 1, trigger: "execute", actions: [{ command: "element.updateProperties", elementId: "work", properties: { name: "Pending" } }, { command: "shape.create", elementId: "new-child", diId: "new-di", parentId: "sub" }] }, next);
  });

  it("retains the established zero frame of an authored empty destination", async () => {
    const source = owner(), native = await modeled(source), scope = { documentOwnerNodeId: "owner", diagramId: "secondary", scopeElementId: "other" };
    const projection = projectBpmnNativeDocument(authoring, source, scope), baseline = bindBpmnNativeBaseline(authoring, projection, native.baseline.xml);
    const next = edit(baseline.xml, (document, index) => {
      const task = document.createElementNS(ns.model, "bpmn:task"); task.setAttribute("id", "authored-child"); index.get("other")!.append(task);
      const shape = document.createElementNS(ns.diagram, "bpmndi:BPMNShape"); shape.setAttribute("id", "authored-di"); shape.setAttribute("bpmnElement", "authored-child");
      const bounds = document.createElementNS(ns.bounds, "dc:Bounds"); for (const [name, value] of Object.entries({ x: 200, y: 200, width: 100, height: 80 })) bounds.setAttribute(name, String(value)); shape.append(bounds); index.get("other-plane")!.append(shape);
    });
    const changed = authoring.applyBpmnDocumentCommands(source, mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "authored-child", diId: "authored-di", parentId: "other" }] }, next));
    expect(authoring.readBpmnDocument(changed).byId.get("authored-di")!.firstElementChild!.getAttribute("x")).toBe("200");
    expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, changed, scope), next, baseline)).not.toThrow();
  });

  it.each([false, true])("defers a retained DI plane transfer atomically even with unchanged numbers (%s)", async unchanged => {
    const native = await modeled(), next = edit(native.baseline.xml, (_document, index) => {
      index.get("process")!.append(index.get("inner")!); index.get("plane")!.append(index.get("inner-di")!);
      if (!unchanged) { index.get("inner-di")!.firstElementChild!.setAttribute("x", "200"); index.get("inner-di")!.firstElementChild!.setAttribute("y", "200"); }
      index.get("work")!.setAttribute("name", "Pending");
    });
    atomicPlaneRefusal(native, { id: 1, trigger: "execute", actions: [{ command: "element.updateProperties", elementId: "work", properties: { name: "Pending" } }, { command: "shape.move", elementId: "inner", diId: "inner-di", oldParentId: "sub", parentId: "process" }] }, next);
  });

  it.each(["plane", "other-plane"])("refuses a same-ID authored plane scope change before any geometry (%s)", async planeId => {
    const native = await modeled(), next = edit(native.baseline.xml, (_document, index) => {
      index.get(planeId)!.setAttribute("bpmnElement", planeId === "plane" ? "other" : "process");
      if (planeId === "plane") index.get("work-di")!.firstElementChild!.setAttribute("x", "200");
    });
    atomicPlaneRefusal(native, { id: 1, trigger: "execute", actions: [{ command: "canvas.updateRoot", oldElementId: "process", newElementId: "other" }, { command: "shape.move", elementId: "work", diId: "work-di" }] }, next);
  });

  it("retains atomic refusal of unresolved inactive empty-plane provenance pending S10c", async () => {
    const source = owner(rawXml.replace('<bpmn:process id="other"/>', '<bpmn:process id="other"><bpmn:subProcess id="inactive-empty"/></bpmn:process>').replace('<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"/>', '<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"><bpmndi:BPMNShape id="inactive-di" bpmnElement="inactive-empty"><dc:Bounds x="50" y="50" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane>'));
    const native = await modeled(source), next = edit(native.baseline.xml, (document, index) => { const task = document.createElementNS(ns.model, "bpmn:task"); task.setAttribute("id", "new-task"); index.get("process")!.append(task); });
    atomicPlaneRefusal(native, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "new-task", parentId: "process" }] }, next);
  });

  it.each(["subProcess", "scriptTask"])("refuses incomplete modeled %s copy membership/body", async type => {
    const body = type === "subProcess" ? '<bpmn:task id="unrepresented-child"/>' : '<bpmn:script>retain body</bpmn:script>';
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub">', `<bpmn:${type} id="plain">${body}</bpmn:${type}><bpmn:subProcess id="sub">`));
    const native = await modeled(source), events = new EventBus(), process = native.definitions.rootElements.find((entry: { id: string }) => entry.id === "process");
    const original = process.flowElements.find((entry: { id: string }) => entry.id === "plain"), target = native.moddle.create(original.$type);
    new ModdleCopy(events, new BpmnFactory(native.moddle), native.moddle).copyElement(original, target, undefined, true); target.set("id", "clone");
    if (type === "scriptTask") target.set("script", "");
    process.flowElements.push(target); target.$parent = process;
    const saved = await native.moddle.toXML(native.definitions), before = structuredClone(source);
    expect(() => mapBpmnNativeCommandGroup(authoring, native.baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "clone", parentId: "process" }],
      namespaceOrigins: [{ elementId: "clone", snapshot: snapshotNativeNamespaces(authoring, native.projection, "plain") }] }, saved.xml)).toThrow("canonical XML/configuration ownership");
    expect(source).toEqual(before);
  });

});
