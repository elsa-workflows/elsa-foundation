import { bpmnInitialConnectionPoints } from "../bpmn/bpmnDocumentCreateOperations";
import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, projectBpmnDocumentScene, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnCreateConnection, planBpmnCreateShape } from "../bpmn/bpmnDocumentCreateOperations";
import { bpmnDocumentToolboxShapes } from "../bpmn/bpmnDocumentToolbox";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("complete-source toolbox creation plans", () => {
  it.each(bpmnDocumentToolboxShapes.filter(entry => entry.type !== "lane"))("creates $id in its actual standard context with DI and retains native/opaque data", entry => {
    const owner = operationOwner(), before = structuredClone(owner);
    const scope = entry.eventSubprocessOnly ? { ...operationScope, scopeElementId: "ES", diagramId: "EventDiagram" }
      : entry.transactionOnly && entry.type !== "boundaryEvent" ? { ...operationScope, scopeElementId: "TX", diagramId: "TxDiagram" } : operationScope;
    const plan = planBpmnCreateShape(owner, scope, { toolId: entry.id, position: { x: 1000.125, y: 50.75 },
      ...(entry.type === "boundaryEvent" ? { attachedToElementId: entry.transactionOnly ? "TX" : "A" } : {}) });
    const next = applyBpmnDocumentTransaction(owner, plan.transaction), parsed = readBpmnDocument(next);
    expect(owner).toEqual(before);
    expect(parsed.byId.get(plan.selection!.elementId)?.localName).toBe(entry.type);
    expect(parsed.byId.get(plan.selection!.diId!)?.getAttribute("bpmnElement")).toBe(plan.selection!.elementId);
    expect(parsed.payload.activities).toEqual(readBpmnDocument(before).payload.activities);
    expect(parsed.payload.bindings).toEqual(readBpmnDocument(before).payload.bindings);
    expect(parsed.document.getElementsByTagNameNS("urn:vendor", "keep")[0].innerHTML).toBe(readBpmnDocument(before).document.getElementsByTagNameNS("urn:vendor", "keep")[0].innerHTML);
    expect(parsed.document.documentElement.hasAttribute("id")).toBe(false);
  });

  it("uses standard timer/conditional/link members rather than an incomplete event definition", () => {
    for (const [toolId, child] of [["intermediateCatchEvent-timer", "timeDuration"], ["intermediateCatchEvent-conditional", "condition"]]) {
      const plan = planBpmnCreateShape(operationOwner(), operationScope, { toolId, position: { x: 0, y: 0 } });
      const element = readBpmnDocument(applyBpmnDocumentTransaction(operationOwner(), plan.transaction)).byId.get(plan.selection!.elementId)!;
      expect(element.getElementsByTagNameNS(ns.model, child)).toHaveLength(1);
    }
    const plan = planBpmnCreateShape(operationOwner(), operationScope, { toolId: "intermediateThrowEvent-link", name: "Join", position: { x: 0, y: 0 } });
    expect(readBpmnDocument(applyBpmnDocumentTransaction(operationOwner(), plan.transaction)).byId.get(plan.selection!.elementId)!.firstElementChild!.getAttribute("name")).toBe("Join");
  });

  it("docks a new boundary to the actual activity DI and opens a real child plane for collapsed-body content", () => {
    const owner = operationOwner(), plan = planBpmnCreateShape(owner, operationScope, { toolId: "boundaryEvent-timer", attachedToElementId: "A", position: { x: 120, y: 50 } });
    const scene = projectBpmnDocumentScene(applyBpmnDocumentTransaction(owner, plan.transaction), "Diagram"), boundary = scene.shapes.find(shape => shape.shapeId === plan.selection!.diId)!;
    expect(boundary.bounds.y + boundary.bounds.height / 2).toBe(100.5);
    expect(() => planBpmnCreateShape(owner, operationScope, { toolId: "task", parentElementId: "S", position: { x: 10, y: 10 } })).toThrow("child scope");
    const child = planBpmnCreateShape(owner, { ...operationScope, scopeElementId: "S", diagramId: "SubDiagram" }, { toolId: "task", position: { x: 150.125, y: 100.5 } });
    expect(readBpmnDocument(applyBpmnDocumentTransaction(owner, child.transaction)).byId.get(child.selection!.elementId)!.parentElement!.id).toBe("S");
  });

  it("retargets a process diagram to collaboration without moving its existing content or editing process identity", () => {
    const owner = operationOwner(), before = projectBpmnDocumentScene(owner, "Diagram");
    const plan = planBpmnCreateShape(owner, operationScope, { toolId: "participant-expanded", position: { x: 1200, y: 800 } });
    const next = applyBpmnDocumentTransaction(owner, plan.transaction), parsed = readBpmnDocument(next), scene = projectBpmnDocumentScene(next, "Diagram");
    expect(plan.nextScope?.scopeElementId).toBe(scene.scopeId);
    expect(parsed.payload.processId).toBe("P");
    expect(parsed.byId.get(plan.selection!.elementId)?.getAttribute("processRef")).toBeTruthy();
    expect(scene.shapes.find(shape => shape.elementId === "A")!.bounds).toEqual(before.shapes.find(shape => shape.elementId === "A")!.bounds);
    const wrapper = scene.shapes.find(shape => shape.element.localName === "participant" && shape.element.getAttribute("processRef") === "P")!;
    expect(wrapper.bounds.x).toBe(-20); // Content only; it does not grow towards the newly requested pool.
    expect(wrapper.bounds.y).toBe(0);
  });

  it("creates lane membership and splits an existing lane with fractional bounds, without duplicate lane sets", () => {
    const poolPlan = planBpmnCreateShape(operationOwner(), operationScope, { toolId: "participant-expanded", position: { x: 1200, y: 800 } });
    const pooled = applyBpmnDocumentTransaction(operationOwner(), poolPlan.transaction), scope = poolPlan.nextScope!;
    const pool = projectBpmnDocumentScene(pooled, "Diagram").shapes.find(shape => shape.element.getAttribute("processRef") === "P")!;
    const first = planBpmnCreateShape(pooled, scope, { toolId: "lane", parentElementId: pool.elementId, position: { x: 0, y: 0 } });
    const one = applyBpmnDocumentTransaction(pooled, first.transaction);
    expect(readBpmnDocument(one).byId.get(first.selection!.elementId)!.getElementsByTagNameNS(ns.model, "flowNodeRef")).toHaveLength(6);
    const second = planBpmnCreateShape(one, scope, { toolId: "lane", parentElementId: pool.elementId, position: { x: 0, y: 0 } });
    const two = readBpmnDocument(applyBpmnDocumentTransaction(one, second.transaction));
    expect(two.process.getElementsByTagNameNS(ns.model, "laneSet")).toHaveLength(1);
    expect(two.process.getElementsByTagNameNS(ns.model, "lane")).toHaveLength(2);
    expect([...two.process.getElementsByTagNameNS(ns.model, "flowNodeRef")].map(element => element.textContent)).toContain("A");
  });

  it("creates sequence references and real owned data I/O, preserving the public display endpoints", () => {
    const owner = operationOwner();
    const sequence = planBpmnCreateConnection(owner, operationScope, { kind: "sequenceFlow", sourceId: "B", targetId: "G", points: [{ x: 450.25, y: 140.5 }, { x: 550, y: 145 }] });
    const next = applyBpmnDocumentTransaction(owner, sequence.transaction), parsed = readBpmnDocument(next);
    expect(parsed.byId.get("B")!.getElementsByTagNameNS(ns.model, "outgoing")[0].textContent).toBe(sequence.selection!.elementId);
    const association = planBpmnCreateConnection(next, operationScope, { kind: "dataInputAssociation", sourceId: "Data", targetId: "B", points: [{ x: 275, y: 84 }, { x: 400, y: 100.5 }] });
    const data = readBpmnDocument(applyBpmnDocumentTransaction(next, association.transaction)), edge = data.byId.get(association.selection!.elementId)!;
    const targetId = edge.getElementsByTagNameNS(ns.model, "targetRef")[0].textContent!;
    expect(data.byId.get(targetId)!.localName).toBe("dataInput");
    expect(data.byId.get("B")!.contains(data.byId.get(targetId)!)).toBe(true);
    expect(data.byId.get("B")!.getElementsByTagNameNS(ns.model, "inputSet")[0].textContent).toBe(targetId);
    expect(data.byId.get("B")!.getElementsByTagNameNS(ns.model, "outputSet")).toHaveLength(1);
    expect(edge.parentElement).toBe(data.byId.get("B"));
  });

  it("creates legal self-loop references in the FlowNode schema order", () => {
    const owner = operationOwner(), plan = planBpmnCreateConnection(owner, operationScope, { kind: "sequenceFlow", sourceId: "G", targetId: "G", points: [{ x: 550, y: 145 }, { x: 575, y: 80 }, { x: 575, y: 120 }] });
    const parsed = readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction));
    expect([...parsed.byId.get("G")!.children].map(child => child.localName)).toEqual(["incoming", "outgoing"]);
  });

  it("refuses missing boundary context, outside-plane endpoints and wrong-owner data references atomically", () => {
    const owner = operationOwner(), before = structuredClone(owner);
    expect(() => planBpmnCreateShape(owner, operationScope, { toolId: "boundaryEvent-timer", position: { x: 0, y: 0 } })).toThrow("real activity");
    expect(() => planBpmnCreateConnection(owner, operationScope, { kind: "sequenceFlow", sourceId: "Child", targetId: "Child", points: [{ x: 0, y: 0 }, { x: 1, y: 1 }] })).toThrow("hidden by a collapsed");
    expect(() => planBpmnCreateConnection(owner, operationScope, { kind: "dataInputAssociation", sourceId: "Data", targetId: "B", targetRef: "Child", points: [{ x: 0, y: 0 }, { x: 1, y: 1 }] })).toThrow("actual owner input");
    expect(owner).toEqual(before);
  });
});

describe("initial actual-DI connection docking", () => {
  it("uses side-midpoint contour anchors for circles/diamonds as well as activity rectangles", () => {
    const route = bpmnInitialConnectionPoints({ x: 0, y: 0, width: 36, height: 36 }, { x: 180, y: 70, width: 50, height: 50 });
    expect(route[0]).toEqual({ x: 36, y: 18 }); expect(route.at(-1)).toEqual({ x: 180, y: 95 });
    for (let index = 1; index < route.length; index++) expect(route[index].x === route[index - 1].x || route[index].y === route[index - 1].y).toBe(true);
  });
  it("creates an explicit orthogonal self-loop outside the actual shape instead of a zero-length center edge", () => {
    const shape = { x: 100.25, y: 80.5, width: 100, height: 80 }, route = bpmnInitialConnectionPoints(shape, shape, true);
    expect(route[0]).toEqual({ x: 200.25, y: 120.5 }); expect(route.at(-1)).toEqual({ x: 150.25, y: 80.5 });
    expect(route[1].x).toBeGreaterThan(shape.x + shape.width); expect(route[2].y).toBeLessThan(shape.y);
  });
  it("refuses overlapping independent endpoints when a safe initial route is ambiguous", () => {
    expect(() => bpmnInitialConnectionPoints({ x: 0, y: 0, width: 100, height: 80 }, { x: 10, y: 0, width: 100, height: 80 })).toThrow("explicit route");
  });
});
