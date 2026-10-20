import React, { useMemo, useState } from "react";
import type { Node } from "@xyflow/react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { CompensationScopeDesigner } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/CompensationScopeDesigner";
import { useOperatorActions, type CompensationJournals } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useOperatorActions";
import { WorkflowIncidentList } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import {
  compensationScopeStructureKind,
  readCompensationSteps,
  withCompensationSteps
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/compensationScope";
import type { CompensationScopeRuntime } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/compensationRuntime";
import type { WorkflowNodeData } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import type { ActivityNode, IncidentStateSummary, WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

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

// Spec 258: the outer scope also undoes the hotel booking that the inner "Book hotel" scope handed over when it completed.
const nestedRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-outer",
  phase: "Compensating",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Pending" } },
    { step: "Reserve delivery", forward: { state: "Completed", activityExecutionId: "ae-2" }, recovery: { state: "Compensated", activityExecutionId: "ae-5", order: 2 } },
    { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ],
  received: [{
    step: "Reserve hotel",
    forward: { state: "Completed", activityExecutionId: "ae-hotel" },
    recovery: { state: "Compensated", activityExecutionId: "ae-cancel-hotel", order: 1 },
    origin: { scopeNodeId: "inner", label: "Book hotel", activityExecutionId: "ae-inner", iterationId: "it-2" }
  }],
  cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "The order was rejected." }
};

// Spec 258: a completed inner scope. A Confirm made the inventory final; the delivery is left for the enclosing scope.
const confirmedRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-inner",
  phase: "Completed",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Confirmed", confirmedBy: "ae-confirm" } },
    { step: "Reserve delivery", forward: { state: "Completed", activityExecutionId: "ae-2" },
      recovery: { state: "HandedOver", handedOverTo: { scopeNodeId: "outer", label: "Travel booking", activityExecutionId: "ae-outer" } } },
    { step: "Confirm order", forward: { state: "Completed", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ]
};

// Spec 258: a scope inside a loop. A Compensate undid the first occurrence; the run was cancelled during the second.
const loopOccurrences: CompensationScopeRuntime["occurrences"] = [
  { activityExecutionId: "ae-scope-1", label: "Occurrence 1 · iteration it-1", status: "Compensated", iterationId: "it-1" },
  { activityExecutionId: "ae-scope-2", label: "Occurrence 2 · iteration it-2", status: "Cancelled", iterationId: "it-2" }
];

const loopRuntimes: Record<string, CompensationScopeRuntime> = {
  "ae-scope-1": {
    scopeActivityExecutionId: "ae-scope-1",
    phase: "Compensated",
    occurrences: loopOccurrences,
    rows: [
      { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-5", order: 2 } },
      { step: "Reserve delivery", forward: { state: "Uncertain", activityExecutionId: "ae-2" }, recovery: { state: "Compensated", activityExecutionId: "ae-4", order: 1 } },
      { step: "Confirm order", forward: { state: "NotRun" }, recovery: { state: "NotApplicable" } }
    ],
    cause: { kind: "explicit", step: "Reserve delivery", activityExecutionId: "ae-undo", requestedBy: "ae-undo" }
  },
  "ae-scope-2": {
    scopeActivityExecutionId: "ae-scope-2",
    phase: "Cancelled",
    occurrences: loopOccurrences,
    rows: [
      { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-11" }, recovery: { state: "Kept" } },
      { step: "Reserve delivery", forward: { state: "Cancelled", activityExecutionId: "ae-12" }, recovery: { state: "Kept" } },
      { step: "Confirm order", forward: { state: "NotRun" }, recovery: { state: "NotApplicable" } }
    ]
  }
};

// Spec 260: operator actions. A waiting scope from its journal, the result of a resolve, an abandoned scope, the same
// waiting scope for a reader who may not manage the run, a scope without its journal, and a step that waits.
const decisions = ["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"];

const actionsRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-scope",
  phase: "AwaitingIntervention",
  scopeLabel: "Order",
  source: "journal",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Pending" }, handlerNodeId: "release", entryId: "e1", operationKey: "ae-scope/step/0" },
    {
      step: "Reserve delivery",
      forward: { state: "Uncertain", activityExecutionId: "ae-2" },
      recovery: { state: "NeedsAction", activityExecutionId: "ae-11", order: 1, attempts: { count: 3, max: 3 }, incidentId: "inc-5" },
      handlerNodeId: "cancel",
      entryId: "e2",
      operationKey: "ae-scope/step/1",
      actions: { incidentId: "inc-5", sourceActivityExecutionId: "ae-11", decisions, target: "handler" }
    },
    { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", incidentId: "inc-1", failureType: "order.rejected", messageWithheld: true },
  remaining: [{ step: "Reserve delivery", handlerNodeId: "cancel" }, { step: "Reserve inventory", handlerNodeId: "release" }],
  history: [
    { incidentId: "inc-4", kind: "Retry", actor: "alice", appliedAt: "2026-10-20T10:42:00Z", reason: "Delivery service restored", step: "Reserve delivery" }
  ]
};

const resolvedRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-scope",
  phase: "Compensated",
  scopeLabel: "Order",
  source: "derived",
  journalUnavailable: "StateCleared",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Compensated", activityExecutionId: "ae-12", order: 2 } },
    { step: "Reserve delivery", forward: { state: "Uncertain", activityExecutionId: "ae-2" }, recovery: { state: "ResolvedAbsent", activityExecutionId: "ae-11", order: 1, attempts: { count: 3, max: 3 } } },
    { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "The order was rejected." },
  history: [
    { incidentId: "inc-5", kind: "ResolvedAbsent", decision: "compensation.resolve.effect-absent", actor: "bob", appliedAt: "2026-10-20T11:05:00Z", reason: "Refunded manually, ticket FIN-221", step: "Reserve delivery" },
    { incidentId: "inc-4", kind: "Retry", actor: "alice", appliedAt: "2026-10-20T10:42:00Z", reason: "Delivery service restored", step: "Reserve delivery" }
  ]
};

const abandonedRuntime: CompensationScopeRuntime = {
  scopeActivityExecutionId: "ae-scope",
  phase: "Abandoned",
  scopeLabel: "Order",
  source: "journal",
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "NotUndone" } },
    { step: "Reserve delivery", forward: { state: "Uncertain", activityExecutionId: "ae-2" }, recovery: { state: "NotUndone", activityExecutionId: "ae-11", attempts: { count: 3, max: 3 } } },
    { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", message: "The order was rejected." },
  history: [
    { incidentId: "inc-5", kind: "Abandoned", decision: "compensation.abandon", actor: "carol", appliedAt: "2026-10-20T11:30:00Z", reason: "Order written off by finance" }
  ]
};

const stepRuntime: CompensationScopeRuntime = {
  ...actionsRuntime,
  rows: [
    { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Registered" }, handlerNodeId: "release" },
    {
      step: "Reserve delivery",
      forward: { state: "Uncertain", activityExecutionId: "ae-2" },
      recovery: { state: "NeedsAction", incidentId: "inc-2" },
      entryId: "e2",
      operationKey: "ae-scope/step/1",
      actions: { incidentId: "inc-2", sourceActivityExecutionId: "ae-2", decisions, target: "step" }
    },
    { step: "Confirm order", forward: { state: "NotRun" }, recovery: { state: "NotApplicable" } }
  ],
  cause: { kind: "uncertain", step: "Reserve delivery", activityExecutionId: "ae-2", incidentId: "inc-2", failureType: "delivery.timeout", message: "No response from the delivery service." },
  remaining: [],
  history: undefined
};

export type CompensationOperatorFixtureView = "actions" | "resolved" | "abandoned" | "readonly" | "derived" | "step" | "incident";
export type CompensationScopeFixtureView = "author" | "run" | "waiting" | "nested" | "confirmed" | "loop" | CompensationOperatorFixtureView;
export const compensationOperatorFixtureViews: CompensationOperatorFixtureView[] = ["actions", "resolved", "abandoned", "readonly", "derived", "step", "incident"];

const runtimes: Partial<Record<CompensationScopeFixtureView, CompensationScopeRuntime>> = {
  run: runtime,
  waiting: waitingRuntime,
  nested: nestedRuntime,
  confirmed: confirmedRuntime
};

function canvasNodes(): Node<WorkflowNodeData>[] {
  return Object.entries(labels).map(([id, label]) => ({
    id,
    type: "workflowActivity",
    position: { x: 0, y: 0 },
    data: { label, activityVersionId: `${id}-v1`, activityTypeKey: `Elsa.${label.replaceAll(" ", "")}`, childSlots: [], acceptsInbound: true, sourcePorts: [] }
  }));
}

export function CompensationScopeFixture({ view, outcome = "succeeded" }: { view: CompensationScopeFixtureView; outcome?: string }) {
  if ((compensationOperatorFixtureViews as string[]).includes(view)) {
    return <CompensationOperatorFixture view={view as CompensationOperatorFixtureView} outcome={outcome} />;
  }
  return <CompensationScopeRunFixture view={view} />;
}

function CompensationScopeRunFixture({ view }: { view: CompensationScopeFixtureView }) {
  const [scope, setScope] = useState(initialScope);
  const [selected, setSelected] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [lastSelectedExecution, setLastSelectedExecution] = useState("");
  const [requested, setRequested] = useState("");
  const [openedIncident, setOpenedIncident] = useState("");
  // Like the instance view, choosing an occurrence shows that occurrence's runtime.
  const [occurrence, setOccurrence] = useState("ae-scope-2");

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
      runtime={view === "loop" ? loopRuntimes[occurrence] : runtimes[view]}
      onSelectExecution={id => {
        setLastSelectedExecution(id);
        if (view === "loop" && loopRuntimes[id]) setOccurrence(id);
      }}
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

const fixtureJournals: CompensationJournals = { get: () => undefined, request: () => undefined, reload: async () => "unavailable", reset: () => undefined, generation: 0 };

function waitingIncident(incidentId: string, activityExecutionId: string): IncidentStateSummary {
  return {
    incidentId,
    workflowExecutionId: "wf-42",
    activityExecutionId,
    severity: "Error",
    status: "Blocking",
    isBlocking: true,
    failureType: "ActivityReturnedFault",
    message: "The delivery service did not answer.",
    createdAt: "2026-10-20T10:40:00Z",
    metadata: {},
    resolutionOutcome: {
      actionKind: "WaitForIntervention",
      systemSource: "StructuralIntervention",
      metadata: { "runtime.interventionDecisions": JSON.stringify(decisions), "runtime.interventionRequestedBy": "ae-scope", "compensation.maxAttempts": "3" }
    }
  };
}

function runDetails(incidents: IncidentStateSummary[], canManage: boolean): WorkflowInstanceDetails {
  return {
    instance: { workflowExecutionId: "wf-42" } as WorkflowInstanceDetails["instance"],
    activities: [],
    incidents,
    permissions: { canManage, canInspectValues: false }
  };
}

// A backend for the operator flow: the alteration routes, a plan that completes after one poll, and a job that succeeds
// or is refused because someone else acted first. `forbidden` answers the submission with a 403.
function fixtureBackend(outcome: string, submissions: string[]): StudioEndpointContext {
  let planReads = 0;
  const failed = outcome === "refused";
  const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  return {
    baseUrl: `test://compensation-operator-${outcome}`,
    http: {
      async getJson(url: string) {
        if (url.endsWith("capabilities")) {
          return {
            capabilities: [{
              id: "elsa.api.runtime",
              contractVersion: "1",
              links: [
                { rel: "workflow-alteration-plans", href: "runtime/workflows/alteration-plans" },
                { rel: "workflow-alteration-plan", href: "runtime/workflows/alteration-plans/{planId}", templated: true },
                { rel: "workflow-alteration-plan-jobs-page", href: "runtime/workflows/alteration-plans/{planId}/jobs/page", templated: true },
                { rel: "workflow-alteration-job", href: "runtime/workflows/alteration-plans/{planId}/jobs/{jobId}", templated: true }
              ]
            }]
          };
        }
        if (url.includes("/jobs/page")) return { items: [{ jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: failed ? "Failed" : "Succeeded" }] };
        if (url.includes("/jobs/")) {
          return {
            jobId: "job-1",
            planId: "plan-1",
            workflowExecutionId: "wf-42",
            status: failed ? "Failed" : "Succeeded",
            outcomes: failed ? [{ ordinal: 0, kind: "DecideIntervention", schemaVersion: 1, status: "Failed", code: "InterventionNotPending" }] : []
          };
        }
        return { planId: "plan-1", status: planReads++ === 0 ? "Queued" : failed ? "CompletedWithFailures" : "Completed" };
      },
      async requestResponse(_url: string, init?: RequestInit) {
        submissions.push(String((init?.headers as Record<string, string>)["Idempotency-Key"]));
        if (outcome === "forbidden") return json(403, { code: "Forbidden", message: "Forbidden", status: 403 });
        return json(202, { planId: "plan-1", status: "CapturingTargets", submissionDisposition: "Accepted", createdAt: "2026-10-20T11:00:00Z" });
      },
      requestJson: async () => ({}),
      postJson: async () => ({}),
      putJson: async () => ({}),
      deleteJson: async () => ({}),
      postForm: async () => ({})
    }
  } as unknown as StudioEndpointContext;
}

function CompensationOperatorFixture({ view, outcome }: { view: CompensationOperatorFixtureView; outcome: string }) {
  const [submissions] = useState<string[]>(() => []);
  const context = useMemo(() => fixtureBackend(outcome, submissions), [outcome, submissions]);
  const [applied, setApplied] = useState(false);
  const [openedIncident, setOpenedIncident] = useState("");
  const [lastSelectedExecution, setLastSelectedExecution] = useState("");
  const [canManage, setCanManage] = useState(view !== "readonly");
  const waiting = view === "step" ? waitingIncident("inc-2", "ae-2") : waitingIncident("inc-5", "ae-11");
  const details = runDetails(applied ? [{ ...waiting, isBlocking: false, status: "Resolved", resolutionOutcome: { actionKind: "OperatorDecision", metadata: {} } }] : [waiting], canManage);
  const operator = useOperatorActions({
    context,
    workflowExecutionId: "wf-42",
    details,
    root: null,
    activities: [],
    journals: fixtureJournals,
    labelOf: nodeId => (nodeId ? labels[nodeId] : undefined),
    refreshRun: async () => {
      if (outcome === "succeeded") {
        setApplied(true);
        return runDetails([{ ...waiting, isBlocking: false, status: "Resolved", resolutionOutcome: { actionKind: "OperatorDecision", metadata: {} } }], canManage);
      }
      if (outcome === "forbidden") setCanManage(false);
      return runDetails([waiting], outcome !== "forbidden" && canManage);
    }
  });
  const runtime = applied ? resolvedRuntime
    : view === "resolved" ? resolvedRuntime
      : view === "abandoned" ? abandonedRuntime
        : view === "step" ? stepRuntime
          : view === "derived" ? { ...actionsRuntime, source: "derived" as const, journalUnavailable: "Unavailable" as const, remaining: undefined }
            : actionsRuntime;

  return <main className="wf-tokens" style={{ display: "flex", flexDirection: "column", minHeight: "100vh", background: "var(--studio-bg)" }}>
    <h1 style={{ position: "absolute", left: -10000 }}>Compensation operator fixture</h1>
    {operator.status}
    {operator.dialog}
    {view === "incident" ? (
      <WorkflowIncidentList incidents={details.incidents} renderIncidentActions={operator.renderIncidentActions} />
    ) : (
      <CompensationScopeDesigner
        owner={initialScope}
        nodes={canvasNodes()}
        selectedNodeId={null}
        readOnly
        onSelect={() => undefined}
        runtime={runtime}
        onSelectExecution={setLastSelectedExecution}
        onOpenIncident={setOpenedIncident}
        operator={{ canAct: operator.canAct, workflowExecutionId: "wf-42", pending: operator.pending, onAction: operator.open }}
      />
    )}
    <div hidden>
      <output data-testid="fixture-submissions">{submissions.join("|")}</output>
      <output data-testid="fixture-incident">{openedIncident}</output>
      <output data-testid="fixture-execution">{lastSelectedExecution}</output>
    </div>
  </main>;
}
