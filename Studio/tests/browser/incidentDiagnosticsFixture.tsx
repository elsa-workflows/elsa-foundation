import React from "react";
import { WorkflowIncidentList } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import type { IncidentStateSummary, WorkflowInstanceSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import type { IncidentNextStepGuidance } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useOperatorActions";

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

const summary: WorkflowInstanceSummary = {
  workflowExecutionId: "workflow-1",
  artifactId: "artifact-pinned",
  definitionId: `definition-${"0123456789abcdef".repeat(18)}`,
  definitionVersionId: "version-1",
  artifactVersion: "1.0.0",
  artifactHash: "sha256:artifact",
  status: "Running",
  createdAt: "2026-10-17T20:00:00Z",
  activityCount: 1,
  incidentCount: 1
};

const guidance: IncidentNextStepGuidance = {
  actionAvailability: "unknown",
  canAct: false,
  pendingIncidentId: null,
  requesterCancelled: false,
  retryReviewEligibility: { status: "unknown", reason: "source-not-loaded" }
};

export function IncidentDiagnosticsFixture() {
  return <main className="wf-tokens" style={{ width: "min(100%, 720px)", minHeight: "100vh", padding: 16, background: "var(--background)", color: "var(--foreground)" }}>
    <WorkflowIncidentList incidents={[incident]} summary={summary} guidanceForIncident={() => guidance} />
  </main>;
}
