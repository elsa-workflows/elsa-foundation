import { describe, expect, it } from "vitest";
import {
  applyActivityDuplicatePlan,
  allocateActivityNodeId,
  matchActivityClipboardShortcut,
  pasteActivitiesIntoDraft,
  planActivityDuplicate,
  snapshotSelectedActivities,
  uniqueCopiedDisplayName
} from "../workflow-editor/duplicateActivities";
import {
  flowchartActivity,
  flowchartNode,
  forEachActivity,
  forEachNode,
  leafNode,
  sequenceActivity,
  sequenceNode,
  writeLine
} from "./fixtures";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";

const catalog = new Map([
  [writeLine.activityVersionId, writeLine],
  [flowchartActivity.activityVersionId, flowchartActivity],
  [forEachActivity.activityVersionId, forEachActivity],
  [sequenceActivity.activityVersionId, sequenceActivity]
]);

function sequentialIds() {
  let index = 0;
  return (prefix: string, usedIds: Set<string>) =>
    allocateActivityNodeId(prefix, usedIds, () => {
      index += 1;
      return `copy${index}`;
    });
}

function draftOf(root: ActivityNode, extras: Partial<WorkflowDraft> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { rootActivity: root },
    layout: [],
    activityPresentation: [],
    validationErrors: [],
    ...extras
  };
}

function connectedFlowchart(children: ActivityNode[], connections: Array<{ from: string; to: string; id?: string }>): ActivityNode {
  const root = flowchartNode("flow-root", children);
  root.structure!.payload.connections = connections.map((connection, index) => ({
    id: connection.id ?? `flow-${index}`,
    source: { nodeId: connection.from, port: "Done" },
    target: { nodeId: connection.to }
  }));
  root.structure!.payload.startNodeId = children[0]?.nodeId ?? null;
  return root;
}

describe("activity clipboard shortcuts", () => {
  it("matches copy, paste, and duplicate only with Ctrl/Cmd and without Shift or Alt", () => {
    const modifiers = { metaKey: false, ctrlKey: true, altKey: false, shiftKey: false };
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "c" })).toBe("copy");
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "V" })).toBe("paste");
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "d" })).toBe("duplicate");
    expect(matchActivityClipboardShortcut({ ...modifiers, shiftKey: true, key: "d" })).toBeNull();
    expect(matchActivityClipboardShortcut({ ...modifiers, altKey: true, key: "c" })).toBeNull();
    expect(matchActivityClipboardShortcut({ metaKey: false, ctrlKey: false, altKey: false, shiftKey: false, key: "d" })).toBeNull();
  });
});

describe("copied display names", () => {
  it("assigns (copy) then increments", () => {
    const taken = new Set(["write line"]);
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy)");
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy 2)");
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy 3)");
  });
});

describe("in-workflow activity duplicate remapping", () => {
  it("assigns a fresh id and name to a selected leaf", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [{ nodeId: "write-one", x: 80, y: 40 }],
      presentation: []
    });
    expect(snapshot).not.toBeNull();

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root, { layout: [{ nodeId: "write-one", x: 80, y: 40 }] }),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    expect(result).not.toBeNull();
    const activities = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    expect(activities.map(activity => activity.nodeId)).toEqual(["write-one", "writeone-copy1"]);
    expect(result!.selectedNodeIds).toEqual(["writeone-copy1"]);
    expect(result!.draft.activityPresentation).toEqual([
      { nodeId: "writeone-copy1", displayName: "Write Line (copy)", description: undefined }
    ]);
    expect(result!.draft.layout).toEqual([
      { nodeId: "write-one", x: 80, y: 40 },
      { nodeId: "writeone-copy1", x: 120, y: 80 }
    ]);
  });

  it("copies only connections between selected siblings", () => {
    const left = leafNode("left");
    const right = leafNode("right");
    const extra = leafNode("extra");
    const root = connectedFlowchart([left, right, extra], [
      { from: "left", to: "right", id: "kept" },
      { from: "right", to: "extra", id: "outside" }
    ]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["left", "right"],
      scopeActivities: [left, right, extra],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });

    expect(snapshot?.siblingConnections).toHaveLength(1);
    expect(snapshot?.siblingConnections[0]).toMatchObject({
      source: { nodeId: "left" },
      target: { nodeId: "right" }
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const connections = result!.draft.state.rootActivity?.structure?.payload.connections as Array<Record<string, unknown>>;
    expect(connections).toHaveLength(3);
    expect(connections[2]).toMatchObject({
      source: { nodeId: "left-copy1" },
      target: { nodeId: "right-copy2" }
    });
    expect(connections[2]?.id).not.toBe("kept");
    expect(connections.some(connection => {
      const target = connection.target as { nodeId?: string };
      return target.nodeId === "extra" && connection.id !== "outside";
    })).toBe(false);
  });

  it("remaps nested container children, start node, and internal connections", () => {
    const nested = connectedFlowchart(
      [leafNode("inner-start"), leafNode("inner-end")],
      [{ from: "inner-start", to: "inner-end", id: "inner-flow" }]
    );
    nested.nodeId = "inner-flow";
    const root = flowchartNode("flow-root", [nested]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["inner-flow"],
      scopeActivities: [nested],
      scopeOwner: root,
      catalog,
      layout: [
        { nodeId: "inner-flow", x: 10, y: 10 },
        { nodeId: "inner-start", x: 0, y: 0 },
        { nodeId: "inner-end", x: 200, y: 0 }
      ],
      presentation: [{ nodeId: "inner-start", displayName: "Start here" }]
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root, {
        layout: [
          { nodeId: "inner-flow", x: 10, y: 10 },
          { nodeId: "inner-start", x: 0, y: 0 },
          { nodeId: "inner-end", x: 200, y: 0 }
        ],
        activityPresentation: [{ nodeId: "inner-start", displayName: "Start here" }]
      }),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const copies = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    expect(copies).toHaveLength(2);
    const copy = copies[1];
    expect(copy.nodeId).toBe("innerflow-copy1");
    expect(copy.nodeId).not.toBe("inner-flow");
    const children = copy.structure?.payload.activities as ActivityNode[];
    expect(children.map(child => child.nodeId)).toEqual(["innerstart-copy2", "innerend-copy3"]);
    expect(copy.structure?.payload.startNodeId).toBe("innerstart-copy2");
    expect(copy.structure?.payload.connections).toEqual([
      expect.objectContaining({
        source: { nodeId: "innerstart-copy2", port: "Done" },
        target: { nodeId: "innerend-copy3" }
      })
    ]);
    expect(result!.draft.layout.find(record => record.nodeId === "innerflow-copy1")).toEqual({
      nodeId: "innerflow-copy1",
      x: 50,
      y: 50
    });
    expect(result!.draft.layout.find(record => record.nodeId === "innerstart-copy2")).toEqual({
      nodeId: "innerstart-copy2",
      x: 0,
      y: 0
    });
    expect(result!.draft.activityPresentation?.map(record => record.displayName)).toEqual([
      "Start here",
      "Flowchart (copy)",
      "Start here"
    ]);
    const allIds = copies.flatMap(activity => [
      activity.nodeId,
      ...((activity.structure?.payload.activities as ActivityNode[] | undefined)?.map(child => child.nodeId) ?? [])
    ]);
    expect(new Set(allIds).size).toBe(allIds.length);
  });

  it("remaps a ForEach body child so the copy has no duplicate node id", () => {
    const container = forEachNode("each-1", leafNode("body-1"));
    const root = flowchartNode("flow-root", [container]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["each-1"],
      scopeActivities: [container],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const copies = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    const originalBody = (copies[0].structure?.payload.body as ActivityNode).nodeId;
    const copiedBody = (copies[1].structure?.payload.body as ActivityNode).nodeId;
    expect(originalBody).toBe("body-1");
    expect(copiedBody).toBe("body1-copy2");
    expect(copiedBody).not.toBe(originalBody);
    expect(copies[1].nodeId).toBe("each1-copy1");
  });

  it("applies the pasted activities in one draft so undo can treat it as a single step", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one"), leafNode("write-two")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one", "write-two"],
      scopeActivities: [leafNode("write-one"), leafNode("write-two")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });
    const original = draftOf(root);
    const plan = planActivityDuplicate({
      snapshot: snapshot!,
      draft: original,
      frames: [],
      catalog,
      allocateId: sequentialIds()
    });
    const next = applyActivityDuplicatePlan(original, [], plan!, catalog);

    expect(next).not.toBe(original);
    expect((original.state.rootActivity?.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["write-one", "write-two"]);
    expect((next!.state.rootActivity?.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["write-one", "write-two", "writeone-copy1", "writetwo-copy2"]);
  });

  it("does not write a connections list when pasting into a Sequence", () => {
    const left = leafNode("left");
    const right = leafNode("right");
    const flowchart = connectedFlowchart([left, right], [{ from: "left", to: "right", id: "kept" }]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["left", "right"],
      scopeActivities: [left, right],
      scopeOwner: flowchart,
      catalog,
      layout: [],
      presentation: []
    });
    expect(snapshot?.siblingConnections).toHaveLength(1);

    const sequence = sequenceNode("sequence-root", [leafNode("existing")]);
    const result = pasteActivitiesIntoDraft({
      draft: draftOf(sequence),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const payload = result!.draft.state.rootActivity?.structure?.payload as Record<string, unknown>;
    expect(Object.prototype.hasOwnProperty.call(payload, "connections")).toBe(false);
    expect((payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["existing", "left-copy1", "right-copy2"]);
  });

  it("assigns unique names when the same snapshot is pasted into the latest draft twice", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });
    const first = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });
    const second = pasteActivitiesIntoDraft({
      draft: first!.draft,
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    expect(second!.draft.activityPresentation?.map(record => record.displayName)).toEqual([
      "Write Line (copy)",
      "Write Line (copy 2)"
    ]);
  });

  it("returns null when nothing in the current scope is selected", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    expect(snapshotSelectedActivities({
      selectedNodeIds: ["missing"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    })).toBeNull();
  });
});
