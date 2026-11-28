import { describe, expect, it } from "vitest";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { flowchartStructureKind } from "../flowchartStartNode";
import { getChildSlots, resolveScope } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";
import { resolveWorkflowActivityCanvasTarget } from "../workflow-editor/workflowActivityCanvasTarget";

function node(nodeId: string, structure?: ActivityNode["structure"]): ActivityNode {
  return { nodeId, activityVersionId: "activity@1", inputs: [], outputs: [], ...(structure ? { structure } : {}) };
}

function bpmnDraft() {
  const producer = node("producer-activity");
  const consumer = node("consumer-activity");
  const process = node("bpmn-process", {
    kind: bpmnStructureKind,
    schemaVersion: "1.0.0",
    payload: {
      elements: [
        { elementId: "producer-element", elementType: "task", childNodeId: producer.nodeId },
        { elementId: "consumer-element", elementType: "task", childNodeId: consumer.nodeId }
      ],
      sequenceFlows: [{ flowId: "linear-flow", sourceRef: "producer-element", targetRef: "consumer-element" }],
      activities: [producer, consumer]
    }
  });
  return node("workflow-root", {
    kind: flowchartStructureKind,
    schemaVersion: "1.0.0",
    payload: { activities: [process], startNodeId: process.nodeId }
  });
}

describe("workflow activity canvas target", () => {
  it("routes a nested BPMN activity to its element while retaining the activity scope path", () => {
    const root = bpmnDraft();
    const target = resolveWorkflowActivityCanvasTarget(root, "producer-activity", activity => activity.nodeId);

    expect(target?.canvasNodeId).toBe("producer-element");
    expect(target?.canvasNodeId).not.toBe("producer-activity");
    expect(target?.frames).toHaveLength(1);
    expect(resolveScope(root, target!.frames)?.slot.mode).toBe("bpmn");
    expect(resolveScope(root, target!.frames)?.slot.activities.map(activity => activity.nodeId))
      .toEqual(["producer-activity", "consumer-activity"]);
  });

  it("keeps activity identity for non-BPMN canvases", () => {
    const root = node("flowchart-root", {
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities: [node("producer-activity")], startNodeId: "producer-activity" }
    });

    expect(resolveWorkflowActivityCanvasTarget(root, "producer-activity", activity => activity.nodeId))
      .toMatchObject({ canvasNodeId: "producer-activity", frames: [] });
  });

  it("does not invent a BPMN canvas destination when an activity has no bound element", () => {
    const root = bpmnDraft();
    const process = getChildSlots(root)[0]!.activities[0]!;
    process.structure!.payload.elements = [
      { elementId: "consumer-element", elementType: "task", childNodeId: "consumer-activity" }
    ];

    expect(resolveWorkflowActivityCanvasTarget(root, "producer-activity", activity => activity.nodeId)).toBeNull();
  });
});