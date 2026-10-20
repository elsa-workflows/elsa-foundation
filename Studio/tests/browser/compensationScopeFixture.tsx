import React, { useState } from "react";
import type { Node } from "@xyflow/react";
import { CompensationScopeDesigner } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/CompensationScopeDesigner";
import {
  compensationScopeStructureKind,
  readCompensationSteps,
  withCompensationSteps
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/compensationScope";
import type { CompensationScopeRuntime } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/compensationRuntime";
import type { WorkflowNodeData } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

// The spec 250 reservation journey: two reservations with handlers, then a confirmation that has none.
const labels: Record<string, string> = {
  reserve: "Reserve inventory",
  release: "Release inventory",
  deliver: "Reserve delivery",
  cancel: "Cancel delivery",
  confirm: "Confirm order"
};

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

const initialScope: ActivityNode = {
  nodeId: "scope",
  activityVersionId: "scope-v1",
  inputs: [],
  outputs: [],
  structure: {
    kind: compensationScopeStructureKind,
    schemaVersion: "1.0.0",
    payload: {
      steps: [
        { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
        { name: "Reserve delivery", activity: leaf("deliver"), compensation: leaf("cancel") },
        { name: "Confirm order", activity: leaf("confirm"), compensation: null }
      ]
    }
  }
};

const runtime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-scope",
  phase: "Compensated",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-5", order: 2 } },
    { step: "Reserve delivery", forward: { state: "Completed", activityExecutionId: "ae-2" }, recovery: { state: "Compensated", activityExecutionId: "ae-4", order: 1 } },
    { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "The order was rejected." }
};

// Spec 251: delivery's response was lost, and its cancel failed on every attempt, so recovery waits for an operator.
const waitingRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-scope",
  phase: "AwaitingIntervention",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Pending" } },
    { step: "Reserve delivery", forward: { state: "Uncertain", activityExecutionId: "ae-2" }, recovery: { state: "NeedsAction", activityExecutionId: "ae-6", order: 1, attempts: { count: 3, max: 3 }, incidentId: "inc-6" } },
    { step: "Confirm order", forward: { state: "NotRun" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "uncertain", step: "Reserve delivery", activityExecutionId: "ae-2", failureType: "delivery.timeout", message: "No response from the delivery service." }
};

function canvasNodes(): Node<WorkflowNodeData>[] {
  return Object.entries(labels).map(([id, label]) => ({
    id,
    type: "workflowActivity",
    position: { x: 0, y: 0 },
    data: { label, activityVersionId: `${id}-v1`, activityTypeKey: `Elsa.${label.replaceAll(" ", "")}`, childSlots: [], acceptsInbound: true, sourcePorts: [] }
  }));
}

export function CompensationScopeFixture({ view }: { view: "author" | "run" | "waiting" }) {
  const [scope, setScope] = useState(initialScope);
  const [selected, setSelected] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [lastSelectedExecution, setLastSelectedExecution] = useState("");
  const [requested, setRequested] = useState("");
  const [openedIncident, setOpenedIncident] = useState("");

  return <main className="wf-tokens" style={{ display: "flex", flexDirection: "column", height: "100vh", background: "var(--studio-bg)" }}>
    <h1 style={{ position: "absolute", left: -10000 }}>Compensation scope fixture</h1>
    <CompensationScopeDesigner
      owner={scope}
      nodes={canvasNodes()}
      selectedNodeId={selected}
      readOnly={view !== "author"}
      onSelect={setSelected}
      onChangeSteps={(edit, nextStatus) => {
        setScope(current => withCompensationSteps(current, edit(readCompensationSteps(current))));
        setStatus(nextStatus);
      }}
      onRequestActivity={target => setRequested(`${target.kind}:${target.index}`)}
      runtime={view === "run" ? runtime : view === "waiting" ? waitingRuntime : undefined}
      onSelectExecution={setLastSelectedExecution}
      onOpenIncident={setOpenedIncident}
    />
    {/* What the host received, for the spec to read; hidden, so it is not part of the page under test. */}
    <div hidden>
      <output data-testid="fixture-status">{status}</output>
      <output data-testid="fixture-requested">{requested}</output>
      <output data-testid="fixture-execution">{lastSelectedExecution}</output>
      <output data-testid="fixture-incident">{openedIncident}</output>
      <output data-testid="fixture-steps">{readCompensationSteps(scope).map(step => step.name).join("|")}</output>
    </div>
  </main>;
}
