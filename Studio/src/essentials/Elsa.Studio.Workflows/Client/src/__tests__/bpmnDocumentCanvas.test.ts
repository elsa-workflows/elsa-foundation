import { describe, expect, it } from "vitest";
import { applyBpmnDocumentCommands, bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnDocumentMove, planBpmnDocumentResize, projectBpmnDocumentCanvas } from "../bpmn/bpmnDocumentCanvas";
import { documentCanvasOwner, documentCanvasScope } from "./bpmnDocumentCanvasFixture";

function canvas() { return projectBpmnDocumentCanvas(documentCanvasOwner(), documentCanvasScope); }

describe("retained unresolved routes and XML lexical flags", () => {
  it("fences a host move when its missing boundary shape still owns a retained route", () => {
    const owner = documentCanvasOwner(xml => xml.replace(/<bpmndi:BPMNShape id="BDI"[\s\S]*?<\/bpmndi:BPMNShape>/, "")
      .replace('<bpmn:userTask id="O"/>', '<bpmn:userTask id="O"/><bpmn:sequenceFlow id="BF" sourceRef="B" targetRef="O"/>')
      .replace('</bpmndi:BPMNPlane>', '<bpmndi:BPMNEdge id="BFDI" bpmnElement="BF"><di:waypoint x="200" y="180"/><di:waypoint x="600" y="440"/></bpmndi:BPMNEdge></bpmndi:BPMNPlane>'));
    const before = JSON.stringify(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.unresolvedEdgeIds).toContain("BFDI");
    const host = view.nodes.find(node => node.id === "TDI")!;
    expect(() => planBpmnDocumentMove(view, new Map([[host.id, { x: host.position.x + 20, y: host.position.y }]]))).toThrow("missing endpoint DI");
    expect(JSON.stringify(owner)).toBe(before);
  });

  it.each(["SubDI", "LaneADI", "PoolDI"])("fences enclosing %s moves when inner endpoint shapes are absent", id => {
    const owner = documentCanvasOwner(xml => xml.replace(/<bpmndi:BPMNShape id="(?:IDI|IEDI)"[\s\S]*?<\/bpmndi:BPMNShape>/g, ""));
    const before = JSON.stringify(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.unresolvedEdgeIds).toContain("IFDI");
    const node = view.nodes.find(node => node.id === id)!;
    expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]]))).toThrow("missing endpoint DI");
    expect(planBpmnDocumentMove(view, new Map([[id, node.position]]))).toEqual([]);
    const unrelated = view.nodes.find(node => node.id === "Pool2DI")!;
    expect(() => planBpmnDocumentMove(view, new Map([[unrelated.id, { x: unrelated.position.x + 20, y: unrelated.position.y }]]))).not.toThrow();
    expect(JSON.stringify(owner)).toBe(before);
  });

  it("reads whitespace-collapsed DI flags without hiding an expanded scope or rewriting source", () => {
    const owner = documentCanvasOwner(xml => xml.replaceAll('isExpanded="true"', 'isExpanded=" true "')
      .replace('isHorizontal="true"', 'isHorizontal=" 1 " isMarkerVisible=" false "'));
    const before = JSON.stringify(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.scene.shapes.find(shape => shape.shapeId === "PoolDI")).toMatchObject({ isExpanded: true, isHorizontal: true, isMarkerVisible: false });
    expect(view.nodes.find(node => node.id === "IDI")!.hidden).toBe(false);
    expect(JSON.stringify(owner)).toBe(before);
  });
});
function sourceElement(owner: ReturnType<typeof documentCanvasOwner>, id: string) { return readBpmnDocument(owner).byId.get(id)!; }
function laneDependencyOwner(edit: (document: Document, byId: (id: string) => Element) => void) {
  return documentCanvasOwner(xml => {
    const document = new DOMParser().parseFromString(xml, "application/xml");
    const byId = (id: string) => [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
    [...document.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNEdge")].forEach(edge => edge.remove());
    edit(document, byId);
    return new XMLSerializer().serializeToString(document);
  });
}
function ownerWithThreePointRoute(vertical = false, collinear = false) {
  return documentCanvasOwner(xml => {
    const document = new DOMParser().parseFromString(xml, "application/xml");
    const byId = (id: string) => [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
    const edge = byId("FDI");
    [...edge.getElementsByTagNameNS(bpmnNamespaces.coordinates, "waypoint")].forEach(point => point.remove());
    const points = vertical ? [[200.5, 140.75], [200.5, 440.75], [600.5, 440.75]]
      : [[200.5, 140.75], [collinear ? 400.5 : 600.5, 140.75], [600.5, collinear ? 140.75 : 440.75]];
    points.forEach(([x, y], index) => {
      const point = document.createElementNS(bpmnNamespaces.coordinates, "di:waypoint");
      point.setAttribute("x", String(x)); point.setAttribute("y", String(y)); point.setAttributeNS("urn:vendor", "vendor:vertex", String(index));
      edge.insertBefore(point, edge.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNLabel")[0]);
    });
    if (collinear) {
      byId("ODI").children[0].setAttribute("y", "100.75");
      [...document.getElementsByTagNameNS(bpmnNamespaces.model, "flowNodeRef")].filter(reference => reference.textContent === "O").forEach(reference => reference.remove());
    }
    return new XMLSerializer().serializeToString(document);
  });
}

describe("complete document canvas projection", () => {
  it.each(["missing", "ambiguous"])("does not invent a pool parent when the declared inner lane DI is %s", state => {
    const owner = laneDependencyOwner((_document, byId) => {
      const lane = byId("LaneB1DI");
      if (state === "missing") lane.remove();
      else { const copy = lane.cloneNode(true) as Element; copy.setAttribute("id", "LaneB1CopyDI"); lane.after(copy); }
    });
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.nodes.find(node => node.id === "ODI")!.parentId).toBeUndefined();
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "missing-parent-di", diId: "ODI",
      relatedDiIds: expect.arrayContaining(["LaneBDI", "PoolDI"]) }));
    for (const id of ["ODI", "LaneBDI", "PoolDI", ...(state === "ambiguous" ? ["LaneB1DI", "LaneB1CopyDI"] : [])]) {
      const node = view.nodes.find(item => item.id === id)!;
      expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]]))).toThrow(/parent DI/);
    }
    const pool = view.nodes.find(node => node.id === "PoolDI")!.data.absoluteBounds;
    expect(() => planBpmnDocumentResize(view, "PoolDI", { ...pool, height: pool.height + 40 })).toThrow(/parent DI/);
    const lane = view.nodes.find(node => node.id === "LaneADI")!.data.absoluteBounds;
    expect(() => planBpmnDocumentResize(view, "LaneADI", { ...lane, height: lane.height + 20 })).toThrow(/parent DI/);
    expect(planBpmnDocumentMove(view, new Map(view.nodes.map(node => [node.id, node.position])))).toEqual([]);
    const independentLane = view.nodes.find(node => node.id === "LaneADI")!;
    const laneCommands = planBpmnDocumentMove(view, new Map([[independentLane.id, { x: independentLane.position.x + 20, y: independentLane.position.y }]]));
    expect(laneCommands.some(command => command.kind === "bounds" && ["ODI", "LaneBDI"].includes(command.shapeId))).toBe(false);
    const unrelated = view.nodes.find(node => node.id === "Pool2DI")!;
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentMove(view, new Map([[unrelated.id, { x: unrelated.position.x + 20, y: unrelated.position.y }]])));
    expect(sourceElement(next, "ODI").outerHTML).toBe(sourceElement(owner, "ODI").outerHTML);
    expect(sourceElement(next, "P").outerHTML).toBe(sourceElement(owner, "P").outerHTML);
    expect(next.structure!.payload.activities).toBe(owner.structure!.payload.activities);
    expect(owner).toEqual(before);
  });

  it.each(["equal-depth", "different-depth"])("fences disjoint %s lane memberships and their related container geometry", state => {
    const owner = laneDependencyOwner((document, byId) => {
      const add = (laneId: string) => { const reference = document.createElementNS(bpmnNamespaces.model, "bpmn:flowNodeRef"); reference.textContent = "O"; byId(laneId).prepend(reference); };
      add("LaneA");
      if (state === "equal-depth") {
        [...byId("LaneB1").children].find(element => element.localName === "flowNodeRef" && element.textContent === "O")!.remove();
        add("LaneB");
      }
    });
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.nodes.find(node => node.id === "ODI")!.parentId).toBeUndefined();
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "ambiguous-lane-membership", diId: "ODI",
      relatedDiIds: expect.arrayContaining(["LaneADI", "LaneBDI", "PoolDI"]) }));
    for (const id of ["ODI", "LaneADI", "LaneBDI", "PoolDI", ...(state === "different-depth" ? ["LaneB1DI"] : [])]) {
      const node = view.nodes.find(item => item.id === id)!;
      expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]]))).toThrow(/ambiguous/);
    }
    const pool = view.nodes.find(node => node.id === "PoolDI")!.data.absoluteBounds;
    expect(() => planBpmnDocumentResize(view, "PoolDI", { ...pool, height: pool.height + 40 })).toThrow(/ambiguous/);
    const lane = view.nodes.find(node => node.id === "LaneADI")!.data.absoluteBounds;
    expect(() => planBpmnDocumentResize(view, "LaneADI", { ...lane, height: lane.height + 20 })).toThrow(/ambiguous/);
    expect(planBpmnDocumentMove(view, new Map(view.nodes.map(node => [node.id, node.position])))).toEqual([]);
    const independentTask = view.nodes.find(node => node.id === "TDI")!;
    const taskCommands = planBpmnDocumentMove(view, new Map([[independentTask.id, { x: independentTask.position.x + 20, y: independentTask.position.y }]]));
    expect(taskCommands.some(command => command.kind === "bounds" && command.shapeId === "ODI")).toBe(false);
    const unrelated = view.nodes.find(node => node.id === "Pool2DI")!;
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentMove(view, new Map([[unrelated.id, { x: unrelated.position.x + 20, y: unrelated.position.y }]])));
    expect(sourceElement(next, "ODI").outerHTML).toBe(sourceElement(owner, "ODI").outerHTML);
    expect(sourceElement(next, "P").outerHTML).toBe(sourceElement(owner, "P").outerHTML);
    expect(next.structure!.payload.activities).toBe(owner.structure!.payload.activities);
    expect(owner).toEqual(before);
  });

  it("accepts genuine ancestor-chain lane membership and translates its complete dependent shape family", () => {
    const owner = documentCanvasOwner(xml => xml.replace('<bpmn:lane id="LaneB">', '<bpmn:lane id="LaneB"><bpmn:flowNodeRef>O</bpmn:flowNodeRef>'));
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope), lane = view.nodes.find(node => node.id === "LaneBDI")!;
    expect(view.nodes.find(node => node.id === "ODI")).toMatchObject({ parentId: "LaneB1DI", data: { laneIds: ["LaneB1", "LaneB"] } });
    expect(view.diagnostics.some(diagnostic => diagnostic.code === "ambiguous-lane-membership" && diagnostic.diId === "ODI")).toBe(false);
    const commands = planBpmnDocumentMove(view, new Map([[lane.id, { x: lane.position.x + 20, y: lane.position.y }]]));
    for (const id of ["LaneBDI", "LaneB1DI", "ODI", "CDI", "HDI", "HEDI"]) {
      const previous = view.nodes.find(node => node.id === id)!.data.absoluteBounds;
      expect(commands).toContainEqual({ kind: "bounds", shapeId: id, bounds: { ...previous, x: previous.x + 20 } });
    }
    const next = applyBpmnDocumentCommands(owner, commands);
    for (const id of ["TDI", "Pool2DI", "ChildDiagram", "P"]) expect(sourceElement(next, id).outerHTML).toBe(sourceElement(owner, id).outerHTML);
    expect(next.structure!.payload.activities).toBe(owner.structure!.payload.activities);
    expect(owner).toEqual(before);
  });

  it("keeps ancestor lane dependencies when a boundary host and its subprocess both lack DI", () => {
    const owner = documentCanvasOwner(xml => {
      const document = new DOMParser().parseFromString(xml, "application/xml");
      const byId = (id: string) => [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
      [...document.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNEdge")].forEach(edge => edge.remove());
      const membership = document.createElementNS(bpmnNamespaces.model, "bpmn:flowNodeRef"); membership.textContent = "Sub"; byId("LaneA").append(membership);
      byId("Sub").append(byId("SB")); byId("SB").setAttribute("attachedToRef", "I");
      for (const id of ["SubDI", "IDI", "IEDI"]) byId(id).remove();
      return new XMLSerializer().serializeToString(document);
    });
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope), lane = view.nodes.find(node => node.id === "LaneADI")!;
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "missing-attachment-di", diId: "SBDI", relatedDiIds: expect.arrayContaining(["LaneADI"]) }));
    expect(() => planBpmnDocumentMove(view, new Map([[lane.id, { x: lane.position.x + 20, y: lane.position.y }]]))).toThrow("attachment DI");
  });

  it.each(["subprocess", "lane"])("refuses moving an outer scope when a child lacks its %s parent DI", missing => {
    const owner = documentCanvasOwner(xml => {
      const document = new DOMParser().parseFromString(xml, "application/xml");
      const byId = (id: string) => [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
      [...document.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNEdge")].forEach(edge => edge.remove());
      if (missing === "subprocess") {
        const membership = document.createElementNS(bpmnNamespaces.model, "bpmn:flowNodeRef"); membership.textContent = "Sub"; byId("LaneA").append(membership);
        byId("SubDI").remove(); byId("SBDI").remove();
      } else byId("LaneBDI").remove();
      return new XMLSerializer().serializeToString(document);
    });
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "missing-parent-di", diId: missing === "subprocess" ? "IDI" : "LaneB1DI" }));
    const id = missing === "subprocess" ? "LaneADI" : "PoolDI", node = view.nodes.find(item => item.id === id)!;
    expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]]))).toThrow("parent DI");
    expect(owner).toEqual(before);
  });

  it("refuses container movement dependent on a missing boundary host DI while allowing an unrelated pool", () => {
    const owner = documentCanvasOwner(xml => {
      const document = new DOMParser().parseFromString(xml, "application/xml");
      [...document.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNEdge")].forEach(edge => edge.remove());
      [...document.getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNShape")].find(shape => shape.getAttribute("id") === "TDI")!.remove();
      return new XMLSerializer().serializeToString(document);
    });
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const move = (id: string) => { const node = view.nodes.find(item => item.id === id)!; return planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]])); };
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "missing-attachment-di", diId: "BDI" }));
    expect(() => move("PoolDI")).toThrow("attachment DI");
    expect(() => move("LaneADI")).toThrow("attachment DI");
    expect(() => move("Pool2DI")).not.toThrow();
    expect(owner).toEqual(before);
  });

  it.each([100, 200])("refuses equally close implicit endpoint instances even when their areas differ (%s)", width => {
    const owner = documentCanvasOwner(xml => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', `<bpmndi:BPMNShape id="TCopyDI" bpmnElement="T"><dc:Bounds x="100.5" y="100.75" width="${width}" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>`));
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.unresolvedEdgeIds).toContain("FDI");
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "ambiguous-edge-endpoint-di", diId: "FDI", relatedDiIds: expect.arrayContaining(["TDI", "TCopyDI"]) }));
    for (const id of ["TDI", "TCopyDI"]) {
      const node = view.nodes.find(item => item.id === id)!;
      expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]]))).toThrow("endpoint DI");
    }
    expect(owner).toEqual(before);
  });

  it.each([["T2", "T2DI"], ["I", "IDI"]])("does not parent or move a boundary with a host in another scope %s", (hostId, hostDiId) => {
    const owner = documentCanvasOwner(xml => xml.replace('id="B" attachedToRef="T"', `id="B" attachedToRef="${hostId}"`));
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const boundary = view.nodes.find(node => node.id === "BDI")!, host = view.nodes.find(node => node.id === hostDiId)!;
    expect(boundary.parentId).toBeUndefined();
    expect(boundary.data.attachmentHostId).toBeUndefined();
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "invalid-boundary-attachment", diId: "BDI" }));
    const commands = planBpmnDocumentMove(view, new Map([[hostDiId, { x: host.position.x + 10, y: host.position.y }]]));
    expect(commands.some(command => command.kind === "bounds" && command.shapeId === "BDI")).toBe(false);
    expect(() => planBpmnDocumentMove(view, new Map([["BDI", { x: boundary.position.x + 10, y: boundary.position.y }]]))).toThrow("attachment");
    expect(owner).toEqual(before);
  });

  it("honors explicit QName DI endpoints before proximity and moves only their authored instance", () => {
    const owner = documentCanvasOwner(xml => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '<bpmndi:BPMNShape id="TCopyDI" bpmnElement="T"><dc:Bounds x="800" y="500" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>')
      .replace('id="FDI" bpmnElement="F"', 'id="FDI" bpmnElement="F" sourceElement="tns:TCopyDI" targetElement="tns:ODI"'));
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.edges.find(edge => edge.id === "FDI")).toMatchObject({ source: "TCopyDI", target: "ODI" });
    const move = (id: string) => { const node = view.nodes.find(item => item.id === id)!; return planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 20, y: node.position.y }]])); };
    expect(move("TDI").some(command => command.kind === "waypoints" && command.edgeId === "FDI")).toBe(false);
    expect(move("TCopyDI").some(command => command.kind === "waypoints" && command.edgeId === "FDI")).toBe(true);
    expect(owner).toEqual(before);
  });

  it.each(["vendor:TDI", "tns:Missing", "tns:ODI", "tns:ChildTDI"])("retains invalid explicit endpoint %s and refuses geometry changes needing it", reference => {
    const owner = documentCanvasOwner(xml => xml.replace('id="FDI" bpmnElement="F"', `id="FDI" bpmnElement="F" sourceElement="${reference}"`)
      .replace('<bpmndi:BPMNPlane id="ChildPlane" bpmnElement="C">', '<bpmndi:BPMNPlane id="ChildPlane" bpmnElement="C"><bpmndi:BPMNShape id="ChildTDI" bpmnElement="T"><dc:Bounds x="1" y="1" width="100" height="80"/></bpmndi:BPMNShape>'));
    const before = structuredClone(owner), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.unresolvedEdgeIds).toContain("FDI");
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "invalid-edge-endpoint-di", elementId: "F", diId: "FDI" }));
    expect(() => planBpmnDocumentMove(view, new Map([["TDI", { x: 200, y: 200 }]]))).toThrow("endpoint DI");
    expect(owner).toEqual(before);
  });

  it("honors an explicit secondary data source without reducing the retained source list", () => {
    const owner = documentCanvasOwner(xml => xml.replace('id="IADI" bpmnElement="IA"', 'id="IADI" bpmnElement="IA" sourceElement="tns:DR2DI"'));
    const edge = projectBpmnDocumentCanvas(owner, documentCanvasScope).edges.find(item => item.id === "IADI")!;
    expect(edge).toMatchObject({ source: "DR2DI", data: { semanticSource: { referenceIds: ["DR", "DR2"], displayElementId: "DR2" } } });
  });

  it("keeps the owner exact and uses DI identities with semantic selection and native bindings separate", () => {
    const owner = documentCanvasOwner(), before = structuredClone(owner);
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const task = view.nodes.find(node => node.id === "TDI")!;
    expect(task.data.semanticElementId).toBe("T");
    expect(task.data.element.namespaceURI).toBe(bpmnNamespaces.model);
    expect(task.data.binding).toBe((owner.structure!.payload.bindings as unknown[])[0]);
    expect(task.data.notation).toMatchObject({ type: "serviceTask", width: 100, height: 80, name: "Native task" });
    expect(view.nodes.some(node => ["native", "native-leaf", "listener", "T"].includes(node.id))).toBe(false);
    expect(owner).toEqual(before);
  });

  it("projects parent-first fractional coordinates without turning lane membership into XML containment", () => {
    const view = canvas(), nodes = new Map(view.nodes.map(node => [node.id, node]));
    expect(nodes.get("LaneADI")).toMatchObject({ parentId: "PoolDI", position: { x: 30, y: 0 } });
    expect(nodes.get("TDI")).toMatchObject({ parentId: "LaneADI", position: { x: 60.25, y: 80.25 }, origin: [0, 0], expandParent: false });
    expect(nodes.get("ODI")).toMatchObject({ parentId: "LaneB1DI", position: { x: 530.25, y: 80.25 } });
    expect(nodes.get("IDI")).toMatchObject({ parentId: "SubDI", position: { x: 20, y: 30 } });
    expect(nodes.get("BDI")).toMatchObject({ parentId: "TDI", position: { x: 82, y: 62 }, data: { attachmentHostId: "T" } });
    expect(nodes.get("TDI")!.data.element.parentElement!.getAttribute("id")).toBe("P");
    for (const node of view.nodes) if (node.parentId) expect(view.nodes.findIndex(parent => parent.id === node.parentId)).toBeLessThan(view.nodes.indexOf(node));
  });

  it("hides collapsed standard bodies and pools but leaves attached boundary events visible", () => {
    const owner = documentCanvasOwner(xml => xml.replace('bpmnElement="Sub" isExpanded="true"', 'bpmnElement="Sub" isExpanded="false"'));
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const hidden = (id: string) => view.nodes.find(node => node.id === id)?.hidden;
    expect(hidden("HDI")).toBe(true); expect(hidden("T2DI")).toBe(true);
    expect(hidden("IDI")).toBe(true); expect(hidden("SBDI")).toBe(false);
    expect(view.edges.find(edge => edge.id === "HFDI")?.hidden).toBe(true);
    expect(view.scene.shapes.some(shape => shape.shapeId === "HDI")).toBe(true);
  });

  it("opens actual standard/independent scopes without changing selected execution process or creating native nodes", () => {
    const owner = documentCanvasOwner();
    const second = projectBpmnDocumentCanvas(owner, { ...documentCanvasScope, scopeElementId: "P2" });
    expect(second.nodes.map(node => node.id)).toEqual(["T2DI"]);
    expect(second.nodes[0].parentId).toBeUndefined(); expect(second.nodes[0].hidden).toBe(false);
    const child = projectBpmnDocumentCanvas(owner, { ...documentCanvasScope, diagramId: "ChildDiagram", scopeElementId: "C" });
    expect(child.nodes.map(node => node.id)).toEqual(["ChildHDI", "ChildHEDI"]);
    expect(child.nodes[0].position).toEqual({ x: 20.25, y: 30.5 });
    expect(owner.structure!.payload.processId).toBe("P");
    expect(() => projectBpmnDocumentCanvas(owner, { ...documentCanvasScope, documentOwnerNodeId: "native" })).toThrow("stale");
    expect(() => projectBpmnDocumentCanvas(owner, { ...documentCanvasScope, diagramId: "ChildDiagram", scopeElementId: "P2" })).toThrow("not represented");
  });

  it("preserves every data-association reference while drawing I/O endpoints at the owning activity", () => {
    const view = canvas(), input = view.edges.find(edge => edge.id === "IADI")!, output = view.edges.find(edge => edge.id === "OADI")!;
    expect(input).toMatchObject({ source: "DRDI", target: "TDI", data: { semanticSource: { referenceIds: ["DR", "DR2"] }, semanticTarget: { referenceIds: ["Input"], displayElementId: "T" } } });
    expect(output).toMatchObject({ source: "TDI", target: "DRDI", data: { semanticSource: { referenceIds: ["Output"], displayElementId: "T" }, semanticTarget: { referenceIds: ["DR"] } } });
  });

  it("keeps duplicate semantic DI instances distinct and picks the nearby instance for attachment/edge display", () => {
    const owner = documentCanvasOwner(xml => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '<bpmndi:BPMNShape id="TCopyDI" bpmnElement="T"><dc:Bounds x="800" y="500" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>'));
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.nodes.filter(node => node.data.semanticElementId === "T").map(node => node.id)).toEqual(["TDI", "TCopyDI"]);
    expect(view.nodes.find(node => node.id === "BDI")?.parentId).toBe("TDI");
    expect(view.edges.find(edge => edge.id === "FDI")?.source).toBe("TDI");
  });

  it("diagnoses overlapping parent instances with different areas and refuses movement through an invented relationship", () => {
    const owner = documentCanvasOwner(xml => xml.replace('</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>', '<bpmndi:BPMNShape id="SubCopyDI" bpmnElement="Sub" isExpanded="true"><dc:Bounds x="290.5" y="90.75" width="280" height="200"/></bpmndi:BPMNShape></bpmndi:BPMNPlane></bpmndi:BPMNDiagram>'));
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.diagnostics).toContainEqual(expect.objectContaining({ code: "ambiguous-di-parent", diId: "IDI" }));
    expect(view.nodes.find(node => node.id === "IDI")!.parentId).toBeUndefined();
    for (const id of ["SubDI", "SubCopyDI", "IDI"]) {
      const node = view.nodes.find(candidate => candidate.id === id)!;
      expect(() => planBpmnDocumentMove(view, new Map([[id, { x: node.position.x + 10, y: node.position.y }]]))).toThrow("ambiguous");
    }
    expect(owner.structure!.payload.documentXml).toContain('id="SubCopyDI"');
  });

  it("retains missing endpoints and ambiguous lane membership as explicit diagnostics instead of writing repairs", () => {
    const owner = documentCanvasOwner(xml => xml.replace('<bpmn:lane id="LaneB">', '<bpmn:lane id="PeerLane"><bpmn:flowNodeRef>T</bpmn:flowNodeRef></bpmn:lane><bpmn:lane id="LaneB">')
      .replace('bpmnElement="DR"', 'bpmnElement="Data"'));
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    expect(view.diagnostics).toEqual(expect.arrayContaining([expect.objectContaining({ code: "ambiguous-lane-membership", elementId: "T" }), expect.objectContaining({ code: "missing-edge-di", elementId: "IA" })]));
    expect(view.unresolvedEdgeIds).toEqual(expect.arrayContaining(["IADI", "OADI"]));
    expect(view.nodes.find(node => node.id === "TDI")?.data.laneIds).toEqual(["LaneA", "PeerLane"]);
    expect(owner.structure!.payload.documentXml).toContain('bpmnElement="Data"');
    const task = view.nodes.find(node => node.id === "TDI")!;
    expect(() => planBpmnDocumentMove(view, new Map([["TDI", { x: task.position.x + 20, y: task.position.y }]]))).toThrow("missing endpoint DI");
  });
});

describe("complete document canvas geometry source plans", () => {
  it("makes no-op and final parent+child positions exact, without double translation", () => {
    const view = canvas(), child = view.nodes.find(node => node.id === "TDI")!;
    expect(planBpmnDocumentMove(view, new Map())).toEqual([]);
    const commands = planBpmnDocumentMove(view, new Map([["PoolDI", { x: 110.5, y: 70.75 }], ["TDI", child.position]]));
    expect(commands.find(command => command.kind === "bounds" && command.shapeId === "TDI")).toEqual({ kind: "bounds", shapeId: "TDI", bounds: { x: 200.75, y: 151, width: 100, height: 80 } });
    expect(commands.find(command => command.kind === "bounds" && command.shapeId === "BDI")).toMatchObject({ bounds: { x: 282.75, y: 213 } });
  });

  it("does not dirty arbitrary fractional DI or snap an unchanged boundary on a no-op canvas commit", () => {
    const view = projectBpmnDocumentCanvas(documentCanvasOwner(xml => xml.replace('x="100.5" y="100.75"', 'x="100.1" y="100.3"')), documentCanvasScope);
    expect(planBpmnDocumentMove(view, new Map(view.nodes.map(node => [node.id, { ...node.position }])))).toEqual([]);
  });

  it("translates same-plane routes/labels while preserving independent planes, waypoint metadata and exact native trees", () => {
    const owner = documentCanvasOwner(), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const commands = planBpmnDocumentMove(view, new Map([["PoolDI", { x: 110.5, y: 70.75 }]]));
    const next = applyBpmnDocumentCommands(owner, commands);
    expect(sourceElement(next, "ChildDiagram").outerHTML).toBe(sourceElement(owner, "ChildDiagram").outerHTML);
    expect(next.structure!.payload.activities).toBe(owner.structure!.payload.activities);
    expect(next.structure!.payload.bindings).toBe(owner.structure!.payload.bindings);
    expect(next.structure!.payload.opaque).toEqual({ retained: true });
    const before = view.edges.find(edge => edge.id === "FDI")!.data!.points;
    const after = projectBpmnDocumentCanvas(next, documentCanvasScope).edges.find(edge => edge.id === "FDI")!.data!.points;
    expect(after).toEqual(before.map(point => ({ x: point.x + 100.25, y: point.y + 50.25 })));
    expect(sourceElement(next, "FDI").children[0].getAttributeNS("urn:vendor", "keep")).toBe("point");
    expect(projectBpmnDocumentCanvas(next, documentCanvasScope).nodes.find(node => node.id === "TDI")!.data.labelBounds).toMatchObject({ x: 190.75, y: 236 });
    expect(sourceElement(next, "T").getElementsByTagNameNS("urn:vendor", "config")[0].outerHTML).toBe(sourceElement(owner, "T").getElementsByTagNameNS("urn:vendor", "config")[0].outerHTML);
  });

  it("moves only the incident route endpoint and adjacent bend, preserving unaffected authored bends", () => {
    const view = canvas(), node = view.nodes.find(node => node.id === "TDI")!, old = view.edges.find(edge => edge.id === "FDI")!.data!.points;
    const commands = planBpmnDocumentMove(view, new Map([["TDI", { x: node.position.x + 25.5, y: node.position.y + 10.25 }]]));
    const route = commands.find(command => command.kind === "waypoints" && command.edgeId === "FDI");
    expect(route?.kind === "waypoints" && route.points).toEqual([{ x: 226, y: 151 }, { x: 250.5, y: 151 }, ...old.slice(2)]);
    expect(commands.some(command => command.kind === "bounds" && command.shapeId === "ODI")).toBe(false);
  });

  it.each([false, true])("keeps a three-point L route orthogonal with vertical-first=%s, preserving actual waypoint metadata", vertical => {
    const owner = ownerWithThreePointRoute(vertical), view = projectBpmnDocumentCanvas(owner, documentCanvasScope), task = view.nodes.find(node => node.id === "TDI")!;
    const commands = planBpmnDocumentMove(view, new Map([[task.id, { x: task.position.x + (vertical ? 25.5 : 0), y: task.position.y + (vertical ? 0 : 25.5) }]]));
    const next = applyBpmnDocumentCommands(owner, commands), route = projectBpmnDocumentCanvas(next, documentCanvasScope).edges.find(edge => edge.id === "FDI")!.data!.points;
    expect(route).toEqual(vertical ? [{ x: 226, y: 140.75 }, { x: 226, y: 440.75 }, { x: 600.5, y: 440.75 }]
      : [{ x: 200.5, y: 166.25 }, { x: 600.5, y: 166.25 }, { x: 600.5, y: 440.75 }]);
    expect([...sourceElement(next, "FDI").getElementsByTagNameNS(bpmnNamespaces.coordinates, "waypoint")].map(point => point.getAttributeNS("urn:vendor", "vertex"))).toEqual(["0", "1", "2"]);
  });

  it("refuses conflicting constraints on one shared bend instead of introducing a diagonal", () => {
    const owner = ownerWithThreePointRoute(false, true), view = projectBpmnDocumentCanvas(owner, documentCanvasScope), task = view.nodes.find(node => node.id === "TDI")!;
    expect(() => planBpmnDocumentMove(view, new Map([[task.id, { x: task.position.x, y: task.position.y + 25.5 }]]))).toThrow("explicit reroute");
  });

  it.each(["BDI", "IEDI", "DRDI", "TDI", "CDI"])("refuses native resize of fixed-size or collapsed notation %s", id => {
    const view = canvas(), old = view.nodes.find(node => node.id === id)!.data.absoluteBounds;
    expect(() => planBpmnDocumentResize(view, id, { ...old, width: old.width + 20, height: old.height + 20 })).toThrow("resize");
  });

  it("resizes an expanded container without moving ordinary children, keeping boundary centers attached", () => {
    const owner = documentCanvasOwner(), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentResize(view, "SubDI", { x: 280.5, y: 90.75, width: 300, height: 200 }));
    const projected = projectBpmnDocumentCanvas(next, documentCanvasScope);
    expect(projected.nodes.find(node => node.id === "IDI")!.data.absoluteBounds).toEqual(view.nodes.find(node => node.id === "IDI")!.data.absoluteBounds);
    expect(projected.nodes.find(node => node.id === "IDI")!.position).toEqual({ x: 40, y: 40 });
    expect(projected.nodes.find(node => node.id === "SBDI")!.data.absoluteBounds).toMatchObject({ x: 562.5, y: 272.75 });
    expect(sourceElement(next, "ChildDiagram").outerHTML).toBe(sourceElement(owner, "ChildDiagram").outerHTML);
  });

  it("resizes pool lane partitions and nested lanes while keeping ordinary child DI absolute", () => {
    const owner = documentCanvasOwner(), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentResize(view, "PoolDI", { x: 20.5, y: 30.75, width: 1000, height: 700 }));
    const projected = projectBpmnDocumentCanvas(next, documentCanvasScope), get = (id: string) => projected.nodes.find(node => node.id === id)!.data.absoluteBounds;
    expect(get("LaneADI")).toEqual({ x: 50.5, y: 30.75, width: 970, height: 350 });
    expect(get("LaneBDI")).toEqual({ x: 50.5, y: 380.75, width: 970, height: 350 });
    expect(get("LaneB1DI")).toEqual({ x: 80.5, y: 380.75, width: 940, height: 350 });
    expect(get("TDI")).toEqual(view.nodes.find(node => node.id === "TDI")!.data.absoluteBounds);
    expect(get("ODI")).toEqual(view.nodes.find(node => node.id === "ODI")!.data.absoluteBounds);
  });

  it("moves an internal lane divider with its neighboring partition and nested lanes", () => {
    const owner = documentCanvasOwner(), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentResize(view, "LaneADI", { x: 40.25, y: 20.5, width: 870, height: 350 }));
    const projected = projectBpmnDocumentCanvas(next, documentCanvasScope), get = (id: string) => projected.nodes.find(node => node.id === id)!.data.absoluteBounds;
    expect(get("LaneBDI")).toEqual({ x: 40.25, y: 370.5, width: 870, height: 250 });
    expect(get("LaneB1DI")).toEqual({ x: 70.25, y: 370.5, width: 840, height: 250 });
    expect(get("ODI")).toEqual(view.nodes.find(node => node.id === "ODI")!.data.absoluteBounds);
    expect(() => planBpmnDocumentResize(view, "LaneADI", { x: 20, y: 20.5, width: 890, height: 350 })).toThrow("cross-axis");
    expect(() => planBpmnDocumentResize(view, "LaneADI", { x: 40.25, y: 10.5, width: 870, height: 350 })).toThrow("outer lane boundary");
  });

  it("resizes vertical pool partitions along the correct axis", () => {
    const owner = documentCanvasOwner(xml => {
      const document = new DOMParser().parseFromString(xml, "application/xml");
      const byId = (id: string) => [...document.getElementsByTagName("*")].find(element => element.getAttribute("id") === id)!;
      byId("PoolDI").setAttribute("isHorizontal", "false");
      for (const [id, values] of [["LaneADI", [10.25, 50.5, 450, 570]], ["LaneBDI", [460.25, 50.5, 450, 570]], ["LaneB1DI", [460.25, 80.5, 450, 540]]] as const) {
        const bounds = byId(id).children[0];
        ["x", "y", "width", "height"].forEach((name, index) => bounds.setAttribute(name, String(values[index])));
      }
      return new XMLSerializer().serializeToString(document);
    });
    const view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentResize(view, "PoolDI", { x: 10.25, y: 20.5, width: 1000, height: 700 }));
    const projected = projectBpmnDocumentCanvas(next, documentCanvasScope), get = (id: string) => projected.nodes.find(node => node.id === id)!.data.absoluteBounds;
    expect(get("LaneADI")).toEqual({ x: 10.25, y: 50.5, width: 500, height: 670 });
    expect(get("LaneBDI")).toEqual({ x: 510.25, y: 50.5, width: 500, height: 670 });
    expect(get("LaneB1DI")).toEqual({ x: 510.25, y: 80.5, width: 500, height: 640 });
  });

  it("snaps a moved boundary to its existing host without changing semantic attachment", () => {
    const owner = documentCanvasOwner(), view = projectBpmnDocumentCanvas(owner, documentCanvasScope);
    const next = applyBpmnDocumentCommands(owner, planBpmnDocumentMove(view, new Map([["BDI", { x: -23.5, y: 20 }]])));
    expect(sourceElement(next, "B").getAttribute("attachedToRef")).toBe("T");
    expect(projectBpmnDocumentCanvas(next, documentCanvasScope).nodes.find(node => node.id === "BDI")!.data.absoluteBounds).toMatchObject({ x: 82.5, y: 120.75 });
  });

  it("refuses invalid geometry, stale DI IDs and unavailable attachment context before returning a source plan", () => {
    const view = canvas();
    expect(() => planBpmnDocumentMove(view, new Map([["TDI", { x: NaN, y: 1 }]]))).toThrow("finite");
    expect(() => planBpmnDocumentMove(view, new Map([["ChildHDI", { x: 0, y: 0 }]]))).toThrow("outside this scope");
    expect(() => planBpmnDocumentResize(view, "SubDI", { x: 0, y: 0, width: -1, height: 20 })).toThrow("positive");
    const malformed = projectBpmnDocumentCanvas(documentCanvasOwner(xml => xml.replace('attachedToRef="T"', 'attachedToRef="Missing"')), documentCanvasScope);
    expect(() => planBpmnDocumentMove(malformed, new Map([["BDI", { x: 0, y: 0 }]]))).toThrow("attachment context");
  });
});
