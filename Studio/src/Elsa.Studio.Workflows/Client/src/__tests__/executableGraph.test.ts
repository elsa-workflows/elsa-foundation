import { describe, expect, it } from "vitest";
import { buildExecutableActivityGraph, ghostNodeLabel, isGhostFact } from "../executableGraph";
import { buildExecutableInspectorCanvas } from "../workflow-editor/WorkflowExecutableInspector";
import { getChildSlots } from "../workflowAdapter";
import type { ActivityNode, WorkflowExecutableNode } from "../workflowTypes";
import { flowchartActivity, forEachActivity, sequenceActivity, writeLine } from "./fixtures";

// The Executable Inspector adapts the Execution Material wire tree (executable nodes + named child
// slots, no structure payload) back into the authored ActivityNode shape so buildCanvas/resolveScope
// can render it. These tests pin the adaptation: facet-based slot mapping, generic fallback, ghost
// nodes for catalog misses, and Layout Sidecar geometry with auto-layout fallback.

const catalog = [flowchartActivity, sequenceActivity, forEachActivity, writeLine];

function executableNode(overrides: Partial<WorkflowExecutableNode> = {}): WorkflowExecutableNode {
  return {
    executableNodeId: "exec-1",
    authoredActivityId: "write-line-1",
    activityType: writeLine.activityTypeKey,
    activityTypeVersion: "1.0.0",
    structureKind: null,
    inputBindings: [],
    childSlots: [],
    ...overrides
  };
}

function sequenceRoot(children: WorkflowExecutableNode[]): WorkflowExecutableNode {
  return executableNode({
    executableNodeId: "exec-root",
    authoredActivityId: "root",
    activityType: sequenceActivity.activityTypeKey,
    structureKind: "elsa.sequence.structure",
    childSlots: [{ name: "Sequence.Activities", activities: children }]
  });
}

function flowchartRoot(children: WorkflowExecutableNode[]): WorkflowExecutableNode {
  return executableNode({
    executableNodeId: "exec-root",
    authoredActivityId: "root",
    activityType: flowchartActivity.activityTypeKey,
    structureKind: "elsa.flowchart.structure",
    childSlots: [{ name: "Flowchart.Activities", activities: children }]
  });
}

describe("buildExecutableActivityGraph", () => {
  it("maps facet slots onto an authored-style structure and resolves catalog activities", () => {
    const root = sequenceRoot([
      executableNode({ inputBindings: [{ inputName: "Text", source: "Literal", summary: "\"Hello\"" }] }),
      executableNode({
        executableNodeId: "exec-2",
        authoredActivityId: "for-each-1",
        activityType: forEachActivity.activityTypeKey,
        structureKind: "elsa.foreach.structure",
        childSlots: [{
          name: "ForEach.Body",
          activities: [executableNode({ executableNodeId: "exec-3", authoredActivityId: "write-line-2" })]
        }]
      })
    ]);

    const graph = buildExecutableActivityGraph(root, catalog);

    expect(graph.root.nodeId).toBe("root");
    expect(graph.root.activityVersionId).toBe(sequenceActivity.activityVersionId);
    expect(graph.root.structure?.kind).toBe("elsa.sequence.structure");
    const children = graph.root.structure?.payload.activities as ActivityNode[];
    expect(children.map(child => child.nodeId)).toEqual(["write-line-1", "for-each-1"]);

    // The single-cardinality facet slot maps to a single child, not an array, so getChildSlots
    // resolves it through the facet exactly like an authored ForEach.
    const forEach = children[1];
    expect(forEach.structure?.kind).toBe("elsa.foreach.structure");
    expect((forEach.structure?.payload.body as ActivityNode).nodeId).toBe("write-line-2");
    const bodySlot = getChildSlots(forEach, catalog)[0];
    expect(bodySlot.cardinality).toBe("single");
    expect(bodySlot.activities.map(activity => activity.nodeId)).toEqual(["write-line-2"]);

    expect(graph.factsByNodeId.get("write-line-1")).toMatchObject({
      executableNodeId: "exec-1",
      available: true,
      inputBindings: [{ inputName: "Text", source: "Literal", summary: "\"Hello\"" }]
    });
  });

  it("marks activity-catalog misses as ghosts", () => {
    const graph = buildExecutableActivityGraph(sequenceRoot([
      executableNode({ authoredActivityId: "send-email-1", activityType: "Elsa.Activities.Email.SendEmail" })
    ]), catalog);

    const fact = graph.factsByNodeId.get("send-email-1");
    expect(isGhostFact(fact)).toBe(true);
    const ghost = (graph.root.structure?.payload.activities as ActivityNode[])[0];
    expect(ghost.activityVersionId).toBe("executable-missing:Elsa.Activities.Email.SendEmail@1.0.0");
    expect(ghostNodeLabel("Elsa.Activities.Email.SendEmail")).toBe("SendEmail");
  });

  it("falls back to generic slots when the catalog cannot supply a structure facet", () => {
    const graph = buildExecutableActivityGraph(executableNode({
      authoredActivityId: "custom-1",
      activityType: "Acme.Activities.Custom",
      childSlots: [{ name: "Custom.Branches", activities: [executableNode()] }]
    }), catalog);

    expect(graph.root.structure?.kind).toBe("executable:Acme.Activities.Custom");
    expect(Object.keys(graph.root.structure?.payload ?? {})).toEqual(["branches"]);
    const slots = getChildSlots(graph.root, catalog);
    expect(slots).toHaveLength(1);
    expect(slots[0].activities.map(activity => activity.nodeId)).toEqual(["write-line-1"]);
  });

  it("keeps projected connections out of non-flowchart synthesized payloads", () => {
    const graph = buildExecutableActivityGraph({
      ...sequenceRoot([executableNode()]),
      connections: [{
        source: { nodeId: "write-line-1" },
        target: { nodeId: "write-line-2" }
      }]
    }, catalog);

    expect(graph.root.structure?.payload).not.toHaveProperty("connections");
  });
});

describe("buildExecutableInspectorCanvas", () => {
  const root = sequenceRoot([
    executableNode(),
    executableNode({ executableNodeId: "exec-2", authoredActivityId: "send-email-1", activityType: "Elsa.Activities.Email.SendEmail" })
  ]);
  const graph = buildExecutableActivityGraph(root, catalog);

  it("places nodes from the Layout Sidecar, auto-placing sidecar misses, and renders honest ghosts", () => {
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [{ nodeId: "write-line-1", x: 40, y: 20 }], [], () => {});

    const writeLineNode = canvas.nodes.find(node => node.id === "write-line-1")!;
    expect(writeLineNode.position).toEqual({ x: 40, y: 20 });
    expect(writeLineNode.data.label).toBe("Write Line");
    expect(writeLineNode.data.ghost).toBeUndefined();
    expect(writeLineNode.draggable).toBe(false);
    expect(writeLineNode.deletable).toBe(false);

    // No sidecar record: the second sequence entry auto-places at index * 280.
    const ghostNode = canvas.nodes.find(node => node.id === "send-email-1")!;
    expect(ghostNode.position).toEqual({ x: 280, y: 0 });
    expect(ghostNode.data.ghost).toBe(true);
    expect(ghostNode.data.label).toBe("SendEmail");

    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({ source: "write-line-1", target: "send-email-1", deletable: false });
  });

  it("renders the scope a frame path addresses", () => {
    const nested = buildExecutableActivityGraph(sequenceRoot([
      executableNode({
        executableNodeId: "exec-2",
        authoredActivityId: "for-each-1",
        activityType: forEachActivity.activityTypeKey,
        structureKind: "elsa.foreach.structure",
        childSlots: [{ name: "ForEach.Body", activities: [executableNode({ executableNodeId: "exec-3", authoredActivityId: "write-line-2" })] }]
      })
    ]), catalog);

    const canvas = buildExecutableInspectorCanvas(
      nested,
      catalog,
      [],
      [{ ownerNodeId: "for-each-1", slotId: "elsa.foreach.structure:body", label: "For Each / Body" }],
      () => {}
    );

    expect(canvas.nodes.map(node => node.id)).toEqual(["write-line-2"]);
  });

  it("renders projected flowchart connections with ports while ignoring undeclared routing vertices", () => {
    const rawRoot = {
      ...flowchartRoot([
        executableNode({ authoredActivityId: "write-line-1" }),
        executableNode({ executableNodeId: "exec-2", authoredActivityId: "write-line-2" })
      ]),
      connections: [{
        source: { nodeId: "write-line-1", port: "True" },
        target: { nodeId: "write-line-2", port: "Input" },
        vertices: [{ x: 120, y: 45 }, { x: 180, y: 90 }]
      }]
    } as unknown as WorkflowExecutableNode;
    const graph = buildExecutableActivityGraph(rawRoot, catalog);

    const canvas = buildExecutableInspectorCanvas(graph, catalog, [
      { nodeId: "write-line-1", x: 40, y: 20 },
      { nodeId: "write-line-2", x: 420, y: 180 }
    ], [], () => {});

    expect(canvas.nodes.map(node => ({ id: node.id, position: node.position }))).toEqual([
      { id: "write-line-1", position: { x: 40, y: 20 } },
      { id: "write-line-2", position: { x: 420, y: 180 } }
    ]);
    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({
      id: "flow-0-write-line-1-write-line-2",
      source: "write-line-1",
      target: "write-line-2",
      sourceHandle: "True",
      targetHandle: "Input",
      label: "True",
      deletable: false
    });
    expect(canvas.edges[0].data).toBeUndefined();
  });

  it("treats a null connections projection as no flowchart edges", () => {
    const rawRoot = {
      ...flowchartRoot([executableNode()]),
      connections: null
    } as unknown as WorkflowExecutableNode;

    const graph = buildExecutableActivityGraph(rawRoot, catalog);
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {});

    expect(canvas.edges).toEqual([]);
  });

  it("omits null and non-string endpoint ports", () => {
    const rawRoot = {
      ...flowchartRoot([
        executableNode({ authoredActivityId: "write-line-1" }),
        executableNode({ executableNodeId: "exec-2", authoredActivityId: "write-line-2" })
      ]),
      connections: [{
        source: { nodeId: "write-line-1", port: null },
        target: { nodeId: "write-line-2", port: 42 }
      }]
    } as unknown as WorkflowExecutableNode;

    const graph = buildExecutableActivityGraph(rawRoot, catalog);
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {});

    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({
      source: "write-line-1",
      target: "write-line-2",
      sourceHandle: undefined,
      targetHandle: undefined,
      label: undefined
    });
  });

  it("skips malformed connection entries and missing or invalid endpoints", () => {
    const rawRoot = {
      ...flowchartRoot([
        executableNode({ authoredActivityId: "write-line-1" }),
        executableNode({ executableNodeId: "exec-2", authoredActivityId: "write-line-2" })
      ]),
      connections: [
        null,
        { target: { nodeId: "write-line-2" } },
        { source: { nodeId: "write-line-1" } },
        { source: { nodeId: 42 }, target: { nodeId: "write-line-2" } },
        { source: { nodeId: "   " }, target: { nodeId: "write-line-2" } },
        { source: { nodeId: "write-line-1", port: "Done" }, target: { nodeId: "write-line-2" } }
      ]
    } as unknown as WorkflowExecutableNode;

    const graph = buildExecutableActivityGraph(rawRoot, catalog);
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {});

    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({
      id: "flow-0-write-line-1-write-line-2",
      source: "write-line-1",
      target: "write-line-2",
      sourceHandle: "Done"
    });
  });
});
