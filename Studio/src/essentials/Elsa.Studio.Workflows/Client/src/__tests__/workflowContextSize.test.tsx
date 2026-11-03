import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import { createAgentRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { useWorkflowContextBridge, workflowSnapshotContentLimit } from "../workflow-editor/useWorkflowContextBridge";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { flowchartActivity, writeLine } from "./fixtures";

// Spec 1193 slice C: the disclosed structure must not make a draft that fits the server's 64 KiB snapshot bound today stop
// fitting. Realistic shapes: GUID catalog version ids, editor-style node ids, a chained flowchart with a layout.
const versionedWriteLine: ActivityCatalogItem = { ...writeLine, activityVersionId: "0b9b6c2e-4f0e-4c1e-9f1a-2a6f9d3e7c01" };
const versionedFlowchart: ActivityCatalogItem = { ...flowchartActivity, activityVersionId: "6f1d2a3b-7c4e-4d5f-8a9b-0c1d2e3f4a5b" };

function largeDraft(count: number, description = ""): WorkflowDraft {
  const children: ActivityNode[] = Array.from({ length: count }, (_, index) => ({
    nodeId: `writeline-${index.toString(16).padStart(8, "0")}`, activityVersionId: versionedWriteLine.activityVersionId, inputs: [], outputs: []
  }));
  return {
    id: "draft", definitionId: "definition",
    state: { rootActivity: { nodeId: "flowchart-0000root", activityVersionId: versionedFlowchart.activityVersionId, inputs: [], outputs: [], structure: {
      kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {
        activities: children, startNodeId: children[0]?.nodeId ?? null, nodeMetadata: {}, connectionMetadata: {},
        connections: children.slice(1).map((child, index) => ({ id: `c-${index}`, source: { nodeId: children[index].nodeId, port: "Done" }, target: { nodeId: child.nodeId } }))
      } } } },
    layout: children.map((child, index) => ({ nodeId: child.nodeId, x: 100 + index * 240, y: 200 + (index % 4) * 120 })),
    activityPresentation: description ? children.map(child => ({ nodeId: child.nodeId, description })) : [],
    validationErrors: []
  };
}

function snapshotSizes(draft: WorkflowDraft) {
  const agent = createAgentRegistry();
  const controller = registerWorkflowAgent(agent);
  const catalogByVersion = new Map([versionedWriteLine, versionedFlowchart].map(item => [item.activityVersionId, item]));
  const details = { definition: { id: "definition", name: "Large workflow" } } as WorkflowDefinitionDetails;
  function Probe() {
    useWorkflowContextBridge({ controller, clientId: agent.clientId, details, draft, selectedNode: null, selectedNodeId: null, selectedDescriptor: null,
      inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion });
    return null;
  }
  const container = document.createElement("div");
  const root = createRoot(container);
  flushSync(() => root.render(<Probe />));
  const content = controller.snapshot!.content as Record<string, unknown>;
  flushSync(() => root.unmount());
  // "Before this slice": the same content without the slice C additions.
  const { structure, workingState: _state, secretReferences: _secrets, ...previous } = content;
  return { disclosed: JSON.stringify(content).length, previous: JSON.stringify(previous).length, structured: structure !== undefined };
}

describe("workflow context snapshot size", () => {
  it.each([100, 150, 200, 256])("keeps a %i-activity draft within the bound with its compact structure", count => {
    const sizes = snapshotSizes(largeDraft(count));
    expect(sizes.previous).toBeLessThanOrEqual(workflowSnapshotContentLimit);
    expect(sizes.disclosed).toBeLessThanOrEqual(workflowSnapshotContentLimit);
    // Structure costs a few bytes per activity, not a copy of each activity's identity.
    if (sizes.structured) expect(sizes.disclosed - sizes.previous).toBeLessThan(count * 16 + 1024);
  });

  it("leaves the structure out instead of pushing a draft that fits today over the bound", () => {
    // Long descriptions bring a 200-activity draft just under the bound before this slice.
    const sizes = snapshotSizes(largeDraft(200, "x".repeat(60)));
    expect(sizes.previous).toBeLessThanOrEqual(workflowSnapshotContentLimit);
    expect(sizes.disclosed).toBeLessThanOrEqual(workflowSnapshotContentLimit);
    expect(sizes.structured).toBe(false);
  });
});
