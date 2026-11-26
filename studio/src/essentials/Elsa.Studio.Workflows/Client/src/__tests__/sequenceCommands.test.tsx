import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Edge, Node } from "@xyflow/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useGraphCanvasInteractions, type GraphCanvasCommitOptions, type GraphCanvasMode } from "../graph-authoring/useGraphCanvasInteractions";
import {
  buildSequenceEdges,
  sequenceStructureKind,
  syncCanvasToScope,
  type CanvasScope,
  type WorkflowNodeData
} from "../workflowAdapter";
import type { ConnectMenuState, WorkflowEdge } from "../workflow-editor/editorTypes";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

type Interactions = ReturnType<typeof useGraphCanvasInteractions>;
type Commit = { nodes: Node<WorkflowNodeData>[]; edges: WorkflowEdge[]; options?: GraphCanvasCommitOptions };

const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];

beforeEach(() => {
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    callback(0);
    return 1;
  });
  vi.stubGlobal("cancelAnimationFrame", vi.fn());
});

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  vi.unstubAllGlobals();
});

describe("Sequence commands", () => {
  it.each([
    { index: 0, expected: ["Inserted", "a", "b", "c"] },
    { index: 2, expected: ["a", "b", "Inserted", "c"] },
    { index: 3, expected: ["a", "b", "c", "Inserted"] }
  ])("inserts at boundary $index", ({ index, expected }) => {
    const fixture = renderInteractions({ nodes: [node("a", 300), node("b", 200), node("c", 100)] });

    fixture.act(interactions => interactions.insertSequenceActivity(activityDescriptor("inserted"), index));

    expect(fixture.nodes().map(item => item.data.label)).toEqual(expected);
    expect(fixture.commits).toHaveLength(1);
  });

  it.each([
    { activityId: "d", boundary: 0, expected: ["d", "a", "b", "c"] },
    { activityId: "a", boundary: 3, expected: ["b", "c", "a", "d"] },
    { activityId: "b", boundary: 4, expected: ["a", "c", "d", "b"] }
  ])("moves $activityId to insertion boundary $boundary", ({ activityId, boundary, expected }) => {
    const fixture = renderInteractions({ nodes: [node("a"), node("b"), node("c"), node("d")] });

    fixture.act(interactions => interactions.moveSequenceActivity(activityId, boundary));

    expect(fixture.nodes().map(item => item.id)).toEqual(expected);
    expect(fixture.edges().map(({ source, target }) => [source, target])).toEqual(
      expected.slice(0, -1).map((id, index) => [id, expected[index + 1]])
    );
    expect(fixture.commits).toHaveLength(1);
    expect(fixture.select).toHaveBeenCalledWith(activityId);
  });

  it("reconnects the implicit chain when deleting a middle Sequence step", () => {
    const fixture = renderInteractions({ nodes: [node("a"), node("b"), node("c")] });
    fixture.act(interactions => interactions.onNodesDelete([node("b")]));
    expect(fixture.nodes().map(item => item.id)).toEqual(["a", "c"]);
    expect(fixture.edges().map(({ source, target }) => [source, target])).toEqual([["a", "c"]]);
    expect(fixture.commits).toHaveLength(1);
    expect(fixture.commits[0].options?.removedNodeIds).toEqual(new Set(["b"]));
  });

  it.each([
    { activityId: "a", boundary: 0 },
    { activityId: "a", boundary: 1 },
    { activityId: "b", boundary: 1 },
    { activityId: "b", boundary: 2 },
    { activityId: "a", boundary: -1 },
    { activityId: "a", boundary: 4 },
    { activityId: "a", boundary: 1.5 },
    { activityId: "missing", boundary: 1 }
  ])("does not commit a no-op or invalid move ($activityId, $boundary)", ({ activityId, boundary }) => {
    const fixture = renderInteractions({ nodes: [node("a"), node("b"), node("c")] });

    fixture.act(interactions => interactions.moveSequenceActivity(activityId, boundary));

    expect(fixture.nodes().map(item => item.id)).toEqual(["a", "b", "c"]);
    expect(fixture.commits).toHaveLength(0);
  });

  it.each([
    { label: "read-only sequence", mode: "sequence" as const, canAddActivities: false },
    { label: "non-sequence scope", mode: "flowchart" as const, canAddActivities: true }
  ])("guards insertion and movement in a $label", ({ mode, canAddActivities }) => {
    const fixture = renderInteractions({ nodes: [node("a"), node("b")], mode, canAddActivities });

    fixture.act(interactions => {
      interactions.openSequenceInsertMenu(1, { clientX: 40, clientY: 60 });
      interactions.insertSequenceActivity(activityDescriptor("inserted"), 1);
      interactions.moveSequenceActivity("a", 2);
    });

    expect(fixture.menu()).toBeNull();
    expect(fixture.nodes().map(item => item.id)).toEqual(["a", "b"]);
    expect(fixture.commits).toHaveLength(0);
  });

  it("does not place from an insertion picker opened in a stale scope", () => {
    const fixture = renderInteractions({ nodes: [node("a"), node("b")] });
    fixture.act(interactions => interactions.openSequenceInsertMenu(1, { clientX: 40, clientY: 60 }));
    expect(fixture.menu()).toMatchObject({ kind: "sequenceInsert", index: 1, scopeKey: "root" });

    fixture.render({ scopeKey: "root/nested" });
    fixture.act(interactions => interactions.onConnectMenuPick(activityDescriptor("inserted")));

    expect(fixture.nodes().map(item => item.id)).toEqual(["a", "b"]);
    expect(fixture.commits).toHaveLength(0);
    expect(fixture.menu()).toBeNull();
  });

  it("commits an insertion once and syncs list order without changing authored inputs or connections", () => {
    const first = activityNode("a", "First input");
    const second = activityNode("b", "Second input");
    const third = activityNode("c", "Third input");
    const owner: ActivityNode = {
      nodeId: "sequence-root",
      activityVersionId: "sequence@1",
      inputs: [{ referenceKey: "rootInput", value: { expressionType: "Literal", value: "root value" } }],
      outputs: [],
      structure: {
        kind: sequenceStructureKind,
        schemaVersion: "1.0.0",
        payload: { activities: [first, second, third], variables: [{ name: "preserved" }] }
      }
    };
    const scope: CanvasScope = {
      owner,
      slot: {
        id: `${sequenceStructureKind}:activities`,
        label: "Activities",
        property: "activities",
        cardinality: "many",
        mode: "sequence",
        activities: [first, second, third]
      }
    };
    const newInput = [{ referenceKey: "message", value: { expressionType: "Literal", value: "New activity input" } }];
    const descriptor = activityDescriptor("new", {
      authoringTemplate: {
        nodeId: "template",
        activityVersionId: "new@1",
        inputs: newInput,
        outputs: []
      }
    });
    const fixture = renderInteractions({ nodes: [node("a", 300), node("b", 200), node("c", 100)] });

    fixture.act(interactions => interactions.insertSequenceActivity(descriptor, 1));

    expect(fixture.commits).toHaveLength(1);
    const [commit] = fixture.commits;
    const created = commit.options?.createdActivities;
    expect(created).toHaveLength(1);
    expect(created?.[0]?.inputs).toEqual(newInput);
    expect(commit.nodes.map(item => item.id)).toEqual(["a", created?.[0]?.nodeId, "b", "c"]);
    expect(commit.edges.map(({ source, target }) => [source, target])).toEqual([
      ["a", created?.[0]?.nodeId],
      [created?.[0]?.nodeId, "b"],
      ["b", "c"]
    ]);

    const updated = syncCanvasToScope(scope, commit.nodes, commit.edges, created);
    const payload = updated.structure?.payload;
    const activities = payload?.activities as ActivityNode[];
    expect(activities.map(item => item.nodeId)).toEqual(["a", created?.[0]?.nodeId, "b", "c"]);
    expect(activities.map(item => item.inputs)).toEqual([first.inputs, newInput, second.inputs, third.inputs]);
    expect(updated.inputs).toEqual(owner.inputs);
    expect(payload?.variables).toEqual([{ name: "preserved" }]);
    expect(payload).not.toHaveProperty("connections");
  });
});

interface HarnessArgs {
  nodes: Node<WorkflowNodeData>[];
  mode?: GraphCanvasMode;
  scopeKey?: string;
  canAddActivities?: boolean;
}

function renderInteractions(initialArgs: HarnessArgs) {
  let args = { mode: "sequence" as GraphCanvasMode, scopeKey: "root", canAddActivities: true, ...initialArgs };
  let interactions: Interactions;
  let renderedNodes: Node<WorkflowNodeData>[] = args.nodes;
  let renderedEdges: WorkflowEdge[] = sequenceEdges(args.nodes);
  const commits: Commit[] = [];
  const select = vi.fn();
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const catalogItem = activityDescriptor("write-line");

  function Harness() {
    const [nodes, setNodes] = useState(args.nodes);
    const [edges, setEdges] = useState<WorkflowEdge[]>(sequenceEdges(args.nodes));
    renderedNodes = nodes;
    renderedEdges = edges;
    interactions = useGraphCanvasInteractions({
      nodes,
      edges,
      setNodes,
      setEdges,
      mode: args.mode,
      scopeKey: args.scopeKey,
      canAddActivities: args.canAddActivities,
      selectedNodeId: null,
      catalogByVersion: new Map([[catalogItem.activityVersionId, catalogItem]]),
      select,
      commitCanvas: (nextNodes, nextEdges, options) => commits.push({ nodes: nextNodes, edges: nextEdges, options }),
      placeActivity: vi.fn(() => null)
    });
    return null;
  }

  const render = (patch: Partial<HarnessArgs> = {}) => {
    args = { ...args, ...patch };
    flushSync(() => root.render(<Harness />));
  };
  render();
  mounted.push({ root, container });

  return {
    act(action: (value: Interactions) => void) {
      flushSync(() => action(interactions));
      render();
    },
    render,
    nodes: () => renderedNodes,
    edges: () => renderedEdges,
    menu: () => interactions.connectMenu as ConnectMenuState | null,
    commits,
    select
  };
}

function node(id: string, x = 0): Node<WorkflowNodeData> {
  return {
    id,
    type: "workflowActivity",
    position: { x, y: 0 },
    data: {
      label: id,
      activityVersionId: "write-line@1",
      activityTypeKey: "Elsa.WriteLine",
      category: "Primitives",
      executionType: "Action",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "Done", displayName: "Done" }]
    }
  };
}

function sequenceEdges(nodes: Node<WorkflowNodeData>[]): WorkflowEdge[] {
  return buildSequenceEdges(nodes) as WorkflowEdge[];
}

function activityDescriptor(id: string, overrides: Partial<ActivityCatalogItem> = {}): ActivityCatalogItem {
  return {
    activityVersionId: `${id}@1`,
    activityTypeKey: `Elsa.${id}`,
    version: "1.0.0",
    category: "Primitives",
    displayName: id === "write-line" ? "Write Line" : id === "inserted" ? "Inserted" : id,
    executionType: "Action",
    inputs: [],
    outputs: [],
    ...overrides
  };
}

function activityNode(nodeId: string, value: string): ActivityNode {
  return {
    nodeId,
    activityVersionId: "write-line@1",
    inputs: [{ referenceKey: "message", value: { expressionType: "Literal", value } }],
    outputs: [],
    structure: null
  };
}
