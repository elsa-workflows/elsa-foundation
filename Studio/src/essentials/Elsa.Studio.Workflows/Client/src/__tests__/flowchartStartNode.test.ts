import { describe, expect, it } from "vitest";
import {
  canStartWorkflow,
  findMisplacedStartTriggers,
  flowchartStructureKind,
  normalizeFlowchartStartNode,
  readFlowchartStartNodeId,
  setFlowchartStartNode
} from "../flowchartStartNode";
import type { ActivityNode } from "../workflowTypes";

// The authored Flowchart start node: who assigns it, when it may move, and how a start trigger that
// cannot run is detected.

function node(nodeId: string, extra: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v`, inputs: [], outputs: [], ...extra };
}

/** A node authored the way the designer holds inputs: a top-level wrapped property. */
function trigger(nodeId: string, canStart = true): ActivityNode {
  return node(nodeId, {
    canStartWorkflow: { typeName: "System.Boolean", expression: { type: "Literal", value: canStart } }
  });
}

/** The same node as it arrives off the wire: an `inputs` entry keyed by referenceKey. */
function wireTrigger(nodeId: string, canStart = true): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-v`,
    inputs: [{ referenceKey: "CanStartWorkflow", value: { expression: { type: "Literal", value: canStart } } }],
    outputs: []
  };
}

function flowchart(
  activities: ActivityNode[],
  options: { startNodeId?: string | null; connections?: Array<{ from: string; to: string }> } = {}
): ActivityNode {
  return node("root", {
    structure: {
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: {
        activities,
        connections: (options.connections ?? []).map(edge => ({
          source: { nodeId: edge.from },
          target: { nodeId: edge.to }
        })),
        startNodeId: options.startNodeId === undefined ? null : options.startNodeId
      }
    }
  });
}

describe("canStartWorkflow", () => {
  it("reads the designer's wrapped property and the wire's inputs entry", () => {
    expect(canStartWorkflow(trigger("http"))).toBe(true);
    expect(canStartWorkflow(wireTrigger("http"))).toBe(true);
  });

  it("is false for a literal false, an unauthored input, and an expression that is not a literal true", () => {
    expect(canStartWorkflow(trigger("http", false))).toBe(false);
    expect(canStartWorkflow(wireTrigger("http", false))).toBe(false);
    expect(canStartWorkflow(node("if"))).toBe(false);
    expect(canStartWorkflow(node("http", {
      canStartWorkflow: { expression: { type: "JavaScript", value: "getFlag()" } }
    }))).toBe(false);
  });
});

describe("normalizeFlowchartStartNode", () => {
  it("prefers an activity that can start a workflow over the merely-first one", () => {
    const normalized = normalizeFlowchartStartNode(flowchart([node("if"), trigger("http")]));

    expect(readFlowchartStartNodeId(normalized)).toBe("http");
  });

  it("falls back to the first activity when nothing can start a workflow", () => {
    expect(readFlowchartStartNodeId(normalizeFlowchartStartNode(flowchart([node("if"), node("write")]))))
      .toBe("if");
  });

  it("never moves a start that still resolves, so an explicit choice survives a later trigger", () => {
    const authored = flowchart([node("if"), trigger("http")], { startNodeId: "if" });

    expect(readFlowchartStartNodeId(normalizeFlowchartStartNode(authored))).toBe("if");
  });

  it("reassigns to a start trigger when the current start no longer names an activity", () => {
    const stale = flowchart([node("write"), trigger("http")], { startNodeId: "deleted" });

    expect(readFlowchartStartNodeId(normalizeFlowchartStartNode(stale))).toBe("http");
  });

  it("keeps an explicit null for an empty Flowchart and leaves non-Flowcharts alone", () => {
    expect(readFlowchartStartNodeId(normalizeFlowchartStartNode(flowchart([])))).toBeNull();
    const plain = node("write");
    expect(normalizeFlowchartStartNode(plain)).toBe(plain);
  });
});

describe("setFlowchartStartNode", () => {
  it("points the start at any of the Flowchart's own activities", () => {
    const authored = flowchart([node("if"), trigger("http")], { startNodeId: "if" });

    expect(readFlowchartStartNodeId(setFlowchartStartNode(authored, "http"))).toBe("http");
  });

  it("survives normalization, so the change is not undone on the next edit", () => {
    const moved = setFlowchartStartNode(flowchart([node("if"), trigger("http")], { startNodeId: "if" }), "http");

    expect(readFlowchartStartNodeId(normalizeFlowchartStartNode(moved))).toBe("http");
  });

  it("ignores a node that is not in this Flowchart, and leaves the activity identical", () => {
    const authored = flowchart([node("if")], { startNodeId: "if" });

    expect(setFlowchartStartNode(authored, "elsewhere")).toBe(authored);
    expect(setFlowchartStartNode(authored, "if")).toBe(authored);
    expect(setFlowchartStartNode(node("write"), "write")).toEqual(node("write"));
  });
});

describe("findMisplacedStartTriggers", () => {
  // Spec 238: a start trigger is an entry point when nothing connects into it, and misplaced when something
  // does and it is not the start node — publishing refuses that placement.

  it("reports a start trigger that something connects into and that is not the start node", () => {
    const wired = flowchart([node("if"), trigger("http")], {
      startNodeId: "if",
      connections: [{ from: "if", to: "http" }]
    });

    expect(findMisplacedStartTriggers(wired).map(activity => activity.nodeId)).toEqual(["http"]);
  });

  it("reports nothing once the connected-into trigger is the start node", () => {
    const wired = flowchart([node("if"), trigger("http")], {
      startNodeId: "if",
      connections: [{ from: "if", to: "http" }]
    });

    expect(findMisplacedStartTriggers(setFlowchartStartNode(wired, "http"))).toEqual([]);
  });

  it("reports nothing for a second start trigger heading its own branch, since it is an entry point", () => {
    const multiTrigger = flowchart([trigger("http-a"), node("after-a"), trigger("http-b"), node("after-b")], {
      startNodeId: "http-a",
      connections: [{ from: "http-a", to: "after-a" }, { from: "http-b", to: "after-b" }]
    });

    expect(findMisplacedStartTriggers(multiTrigger)).toEqual([]);
  });

  it("reports nothing for a start trigger that nothing connects into, even when it is not the start node", () => {
    const authored = flowchart([node("if"), trigger("http")], { startNodeId: "if" });

    expect(findMisplacedStartTriggers(authored)).toEqual([]);
  });

  it("ignores a mid-flow trigger that cannot start the workflow, however it is connected", () => {
    const midFlow = flowchart([node("if"), trigger("http", false)], {
      startNodeId: "if",
      connections: [{ from: "if", to: "http" }]
    });

    expect(findMisplacedStartTriggers(midFlow)).toEqual([]);
  });

  it("ignores activities that cannot start a workflow, however disconnected", () => {
    const orphaned = flowchart([node("if"), node("write")], { startNodeId: "if" });

    expect(findMisplacedStartTriggers(orphaned)).toEqual([]);
  });
});
