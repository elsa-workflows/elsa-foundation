import { describe, expect, it } from "vitest";
import { mapBpmnNativeCommandGroup } from "../bpmnDocumentCommands";
import { guardBpmnNativeCopies } from "../bpmnDocumentCopyGuard";
import { projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { snapshotNativeNamespaces } from "../bpmnDocumentNamespaceContext";
import { nativeBooleanAttribute, nativeImportSignature, nativeXml } from "../bpmnDocumentXml";
import { authoring, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

describe("canonical native copy prerequisite", () => {
  it.each(["work", "work-di", "flow-di", "process"])("refuses projected copies of %s that need canonical payload ownership", async id => {
    const source = owner(), original = structuredClone(source), { projection, baseline } = await modeled(source);
    const snapshot = snapshotNativeNamespaces(authoring, projection, id);
    const next = edit(baseline.xml, (_document, index) => {
      const copied = index.get(id)!.cloneNode(true) as Element; copied.setAttribute("id", "copy");
      index.get(id)!.parentElement!.append(copied);
    });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "copy" }], namespaceOrigins: [{ elementId: "copy", snapshot }] }, next)).toThrow("canonical XML/configuration ownership");
    expect(source).toEqual(original);
  });

  it("refuses bound-only payload and comments even when the projected XML otherwise looks complete", async () => {
    for (const xml of [rawXml.replace('<bpmn:task id="inner"/>', '<bpmn:task id="inner"><!-- retained copy payload --></bpmn:task>'), rawXml]) {
      const source = owner(xml);
      if (xml === rawXml) {
        source.structure!.payload.bindings = [{ elementId: "inner", childNodeId: "work-native", listenerNodeId: "listener-native" }];
        source.structure!.payload.scopeHeaders = [{ scopeElementId: "sub", nodeId: "sub-native", activityVersionId: "bpmn@1", inputs: [], outputs: [], variables: [] }];
      }
      const { projection, baseline } = await modeled(source);
      const next = edit(baseline.xml, (_document, index) => { const copy = index.get("inner")!.cloneNode(true) as Element; copy.setAttribute("id", "copy"); index.get("sub")!.append(copy); });
      expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "copy", parentId: "sub" }], namespaceOrigins: [{ elementId: "copy", snapshot: snapshotNativeNamespaces(authoring, projection, "inner") }] }, next)).toThrow("canonical XML/configuration ownership");
    }
  });

  it("retains ordinary clean native copies without claiming configured-copy acceptance", async () => {
    const source = owner(), { projection, baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, index) => { const copy = index.get("inner")!.cloneNode(true) as Element; copy.setAttribute("id", "copy"); index.get("sub")!.append(copy); });
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "copy", parentId: "sub" }], namespaceOrigins: [{ elementId: "copy", snapshot: snapshotNativeNamespaces(authoring, projection, "inner") }] }, next);
    expect(authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands)).byId.get("copy")?.localName).toBe("task");
  });

  it("follows a pool's actual processRef to its physical invocation rather than inspecting only its shell", () => {
    const source = owner(rawXml.replace('<bpmn:process id="process">', '<bpmn:collaboration id="collab"><bpmn:participant id="pool" processRef="q:process"/><bpmn:participant id="independent-pool" processRef="q:other"/></bpmn:collaboration><bpmn:process id="process">'));
    const projection = projectBpmnNativeDocument(authoring, source, scope);
    const origin = (id: string) => [{ elementId: "copy", snapshot: snapshotNativeNamespaces(authoring, projection, id) }];
    expect(() => guardBpmnNativeCopies(authoring, projection, origin("pool"))).toThrow("canonical XML/configuration ownership");
    expect(() => guardBpmnNativeCopies(authoring, projection, origin("independent-pool"))).not.toThrow();
  });

  it("requires the owning bridge for compact scope headers even without bound descendants", () => {
    const source = owner(); source.structure!.payload.scopeHeaders = [{ scopeElementId: "sub", nodeId: "sub-native", activityVersionId: "bpmn@1", inputs: [], outputs: [], variables: [] }];
    const projection = projectBpmnNativeDocument(authoring, source, scope);
    expect(() => guardBpmnNativeCopies(authoring, projection, [{ elementId: "copy", snapshot: snapshotNativeNamespaces(authoring, projection, "sub") }])).toThrow("canonical XML/configuration ownership");
  });

  it("keeps same-named non-schema Boolean attributes outside normalization", () => {
    const document = nativeXml(`<b:task xmlns:b="${authoring.bpmnNamespaces.model}" isSequential=" 1 "/>`);
    expect(nativeBooleanAttribute(document.documentElement, "isSequential")).toBe(false);
    const changed = nativeXml(`<b:task xmlns:b="${authoring.bpmnNamespaces.model}" isSequential="true"/>`);
    expect(nativeImportSignature(document.documentElement, authoring.bpmnNamespaces.model)).not.toBe(nativeImportSignature(changed.documentElement, authoring.bpmnNamespaces.model));
  });
});
