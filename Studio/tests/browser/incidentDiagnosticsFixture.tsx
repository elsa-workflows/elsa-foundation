import React from "react";
import { WorkflowIncidentList } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import type { IncidentStateSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const incident: IncidentStateSummary = {
  incidentId: `incident-${"0123456789abcdef".repeat(12)}`,
  workflowExecutionId: "workflow-1",
  activityExecutionId: "activity-1",
  executableNodeId: "response",
  severity: "Error",
  status: "Blocking",
  resolutionOutcome: null,
  failureType: "SchedulerWorkPoisoned",
  message: `Scheduler failed to evaluate the input expression. Fingerprint: sha256:${"abcdef0123456789".repeat(50)}`,
  createdAt: "2026-10-17T21:00:00Z",
  resolvedAt: null,
  isBlocking: true,
  stackTrace: `at Example.Runtime.${"LongMethodName".repeat(50)}()\n at Example.Dispatch()`,
  metadata: {
    "runtime.faultInnerMessage": "Cannot read properties of undefined (reading '0')",
    "runtime.inputKey": "Body",
    "runtime.expressionLanguage": "JavaScript",
    "runtime.inputFailureCode": "ExpressionEvaluationFailed"
  }
};

export function IncidentDiagnosticsFixture() {
  return <main className="wf-tokens" style={{ width: "min(100%, 360px)", padding: 16 }}>
    <WorkflowIncidentList incidents={[incident]} />
  </main>;
}
