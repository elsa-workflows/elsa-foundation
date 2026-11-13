import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { prepareBpmnReparent } from "../bpmn/bpmnDocumentContainmentOperations";
import { planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { prepareBpmnPoolDelete } from "../bpmn/bpmnDocumentPoolOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("retained process roots and containment context", () => {
  it.each([false, true])("retains unrelated idless processes while preparing pool deletion (delete process=%s)", deleteProcess => {
    const original = operationOwner(), pool = planBpmnCreateShape(original, operationScope, { toolId: "participant-expanded", position: { x: 1200, y: 800 } });
    const owner = applyBpmnDocumentTransaction(original, pool.transaction), parsed = readBpmnDocument(owner);
    const originalPool = [...parsed.byId.values()].find(element => element.localName === "participant" && element.getAttribute("processRef") === "P")!.id;
    const replacement = parsed.byId.get(pool.selection!.elementId)!.getAttribute("processRef")!;
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('</b:definitions>', '<b:process/></b:definitions>');
    const before = structuredClone(owner), prepared = prepareBpmnPoolDelete(owner, pool.nextScope!, originalPool, { deleteProcess, ...(deleteProcess ? { nextProcessId: replacement } : {}) });
    if (deleteProcess) {
      expect(prepared).toMatchObject({ kind: "requiresHostTransition", transition: { selectedProcess: { available: [replacement] } } });
    } else {
      if (prepared.kind !== "ready") throw new Error("Participant-only deletion needs no owner transfer");
      const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
      expect([...next.document.documentElement.children].filter(element => element.namespaceURI === ns.model && element.localName === "process" && !element.hasAttribute("id"))).toHaveLength(1);
      expect(next.byId.has("P")).toBe(true);
    }
    expect(owner).toEqual(before);
  });

  it.each(["textAnnotation", "performer"])("moves a flow node before the process's trailing %s", tail => {
    const owner = operationOwner();
    if (tail === "performer") owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:textAnnotation id="Note"><b:text>Original annotation</b:text></b:textAnnotation>', '<b:performer id="Role"/>')
      .replace(/<bd:BPMNShape id="NoteDI"[^>]*>[\s\S]*?<\/bd:BPMNShape>/, "");
    const into = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "TX" });
    if (into.kind !== "ready") throw new Error("Unbound syntax needs no native decision");
    const source = applyBpmnDocumentTransaction(owner, into.plan.transaction);
    const back = prepareBpmnReparent(source, operationScope, { elementIds: ["G"], parentElementId: "P" });
    if (back.kind !== "ready") throw new Error("Unbound syntax needs no native decision");
    const parsed = readBpmnDocument(applyBpmnDocumentTransaction(source, back.plan.transaction)), children = [...parsed.byId.get("P")!.children];
    expect(children.indexOf(parsed.byId.get("G")!)).toBeLessThan(children.indexOf(parsed.byId.get(tail === "performer" ? "Role" : "Note")!));
  });

  it("moves nodes and an idless flow before an ad-hoc completion condition", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:transaction id="TX"/>', '<b:adHocSubProcess id="TX"><b:completionCondition>done</b:completionCondition></b:adHocSubProcess>')
      .replace('<b:exclusiveGateway id="G"/>', '<b:exclusiveGateway id="G"/><b:task id="C"/><b:sequenceFlow sourceRef="G" targetRef="C"/>')
      .replace('<bd:BPMNShape id="GDI"', '<bd:BPMNShape id="CDI" bpmnElement="C"><dc:Bounds x="700" y="120" width="100" height="80"/></bd:BPMNShape><bd:BPMNShape id="GDI"');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G", "C"], parentElementId: "TX" });
    if (prepared.kind !== "ready") throw new Error("Unbound syntax needs no native decision");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, prepared.plan.transaction));
    expect([...next.byId.get("TX")!.children].map(element => element.localName)).toEqual(["exclusiveGateway", "task", "sequenceFlow", "completionCondition"]);
  });

  it.each([false, true])("retains an unchanged cross-scope draft flow during neutral containment (lane=%s)", lane => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:sequenceFlow id="F"', '<b:sequenceFlow id="Unfinished" sourceRef="G" targetRef="Child"/><b:sequenceFlow id="F"');
    if (lane) owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<b:task id="A"', '<b:laneSet id="Lanes"><b:lane id="L1"><b:flowNodeRef>G</b:flowNodeRef></b:lane><b:lane id="L2"/></b:laneSet><b:task id="A"')
      .replace('<bd:BPMNShape id="ADI"', '<bd:BPMNShape id="L1DI" bpmnElement="L1"><dc:Bounds x="0" y="0" width="1000" height="220"/></bd:BPMNShape><bd:BPMNShape id="L2DI" bpmnElement="L2"><dc:Bounds x="0" y="220" width="1000" height="400"/></bd:BPMNShape><bd:BPMNShape id="ADI"');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "P", ...(lane ? { laneId: "L2" } : {}) });
    if (prepared.kind !== "ready") throw new Error("Neutral syntax needs no native decision");
    const nextOwner = applyBpmnDocumentTransaction(owner, prepared.plan.transaction), next = readBpmnDocument(nextOwner);
    expect(next.byId.get("Unfinished")!.getAttribute("targetRef")).toBe("Child");
    if (!lane) expect(nextOwner).toBe(owner);
  });

  it("retains an unchanged misparented flow during a no-op instead of repairing it implicitly", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<b:transaction id="TX"/>', '<b:transaction id="TX"><b:sequenceFlow id="Misparented" sourceRef="G" targetRef="B"/></b:transaction>');
    const prepared = prepareBpmnReparent(owner, operationScope, { elementIds: ["G"], parentElementId: "P" });
    if (prepared.kind !== "ready") throw new Error("Neutral syntax needs no native decision");
    expect(applyBpmnDocumentTransaction(owner, prepared.plan.transaction)).toBe(owner);
  });
});
