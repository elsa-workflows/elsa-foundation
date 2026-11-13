import { describe, expect, it } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import CommandStack from "diagram-js/lib/command/CommandStack";
import ElementRegistry from "diagram-js/lib/core/ElementRegistry";
import { create } from "diagram-js/lib/model";
import UpdatePropertiesHandler from "bpmn-js/lib/features/modeling/cmd/UpdatePropertiesHandler";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import ModdleCopy from "bpmn-js/lib/features/copy-paste/ModdleCopy";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { snapshotNativeNamespaces } from "../bpmnDocumentNamespaceContext";
import { authoring, edit, modeled, owner, rawXml } from "./bpmnDocumentFixture";

const ns = authoring.bpmnNamespaces;
describe("pinned import and command contracts", () => {
  it.each([
    ["loopMaximum", (xml: string) => xml.replace('<bpmn:outgoing>q:flow</bpmn:outgoing>', '<bpmn:outgoing>q:flow</bpmn:outgoing><bpmn:standardLoopCharacteristics loopMaximum="01"/>'), 'loopMaximum="1"'],
    ["capacity", (xml: string) => xml.replace('<bpmn:process id="process">', '<bpmn:dataStore id="store" capacity="01"/><bpmn:process id="process">'), 'capacity="1"'],
    ["resolution", (xml: string) => xml.replace('BPMNDiagram id="main"', 'BPMNDiagram id="main" resolution="090.00"'), 'resolution="90"'],
    ["font size", (xml: string) => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '</bpmndi:BPMNPlane><bpmndi:BPMNLabelStyle id="style"><dc:Font size="012.500"/></bpmndi:BPMNLabelStyle></bpmndi:BPMNDiagram>'), 'size="12.5"']
  ] as const)("accepts equivalent declared %s number spelling without changing canonical source", async (_name, alter, spelling) => {
    const source = owner(alter(rawXml)), { projection, baseline } = await modeled(source);
    expect(baseline.xml).toContain(spelling); expect(projection.sourceXml).toBe(source.structure!.payload.documentXml);
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
});
