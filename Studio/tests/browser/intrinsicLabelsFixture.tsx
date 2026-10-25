import React, { useState } from "react";
import { ReactFlow } from "@xyflow/react";
import { buildExecutableActivityGraph } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/executableGraph";
import { buildExecutableInspectorCanvas } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowExecutableInspector";
import { buildInstanceCanvas } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import { nodeTypes, edgeTypes } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/graph";
import { WorkflowExecutionTimeline } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowInstanceTimeline";
import { sequenceActivity, setVariableActivity, setOutputActivity, writeLine } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import type { WorkflowExecutableNode, WorkflowInstanceDetails, WorkflowDefinitionVersionDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

function leaf(id: string, type: string): WorkflowExecutableNode {
  return { executableNodeId: id, authoredActivityId: id, activityType: type, activityTypeVersion: "1.0.0", childSlots: [] };
}
const children = [leaf("set", "elsa.intrinsic.set"), leaf("output", "elsa.intrinsic.setoutput"),
  leaf("print", writeLine.activityTypeKey), leaf("custom", "elsa.intrinsic.set")];
const executable: WorkflowExecutableNode = {
  ...leaf("root", sequenceActivity.activityTypeKey), structureKind: "elsa.sequence.structure",
  childSlots: [{ name: "Sequence.Activities", activities: children }]
};
const details: WorkflowInstanceDetails = {
  instance: { workflowExecutionId: "run", artifactId: "artifact", artifactHash: "hash", definitionId: "definition",
    definitionVersionId: "version", artifactVersion: "1.0.0", status: "Completed", createdAt: "2026-10-25T10:00:00Z",
    activityCount: 4, incidentCount: 0 },
  incidents: [],
  activities: children.map((node, index) => ({
    activityExecutionId: `activity-${index}`, workflowExecutionId: "run", executableNodeId: node.executableNodeId,
    authoredActivityId: node.authoredActivityId, activityType: node.activityType, activityTypeVersion: node.activityTypeVersion,
    status: "Completed", subStatus: null, scheduledAt: "2026-10-25T10:00:00Z", startedAt: "2026-10-25T10:00:00Z",
    completedAt: "2026-10-25T10:00:01Z", bookmarkIds: [], incidentIds: [], faultCount: 0, aggregateFaultCount: 0, metadata: {}
  }))
};

export function IntrinsicLabelsFixture() {
  const [missing, setMissing] = useState(false);
  const catalog = missing ? [sequenceActivity, writeLine] : [sequenceActivity, writeLine, setVariableActivity, setOutputActivity];
  const graph = buildExecutableActivityGraph(executable, catalog, [], [], null,
    [{ executableNodeId: "set", description: "Updates the counter" }, { executableNodeId: "custom", displayName: "Increment counter" }]);
  const definition: WorkflowDefinitionVersionDetails = {
    id: "version", version: "1.0.0", definition: { id: "definition", name: "Looping", createdAt: "2026-10-25T10:00:00Z" },
    state: { rootActivity: graph.root }, layout: [], activityPresentation: graph.activityPresentation
  };
  const runs = buildInstanceCanvas(definition, catalog, details, null, [], () => {}, undefined, details.activities, graph);
  const inspector = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {});
  const canvases = [{ name: "Run", model: runs }, { name: "Executable Inspector", model: inspector }];
  return <main className="wf-editor wf-page">
    <h1>Runtime activity labels</h1>
    <label><input type="checkbox" checked={missing} onChange={event => setMissing(event.target.checked)} /> Historical catalog unavailable</label>
    <div style={{ display: "flex", gap: 16 }}>
      {canvases.map(({ name, model }) => {
        return <section key={name} aria-label={name} style={{ flex: 1 }}>
          <h2>{name}</h2>
          <div className="wf-canvas" style={{ height: 530 }}>
            <ReactFlow key={`${name}-${missing}`} nodes={model.nodes} edges={model.edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
              fitView nodesDraggable={false} nodesConnectable={false} />
          </div>
        </section>;
      })}
    </div>
    <WorkflowExecutionTimeline activities={details.activities} activityCatalog={catalog} executableGraph={graph} />
  </main>;
}
