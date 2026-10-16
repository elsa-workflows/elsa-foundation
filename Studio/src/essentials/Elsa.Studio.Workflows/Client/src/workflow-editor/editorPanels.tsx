import { AlertCircle, Check, Flag, MapPin, Wrench } from "lucide-react";
import type { StudioEndpointContext, StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ValidationError, WorkflowDraft, WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import type { WorkflowTestRunState, WorkflowTestRunWaitDraftState } from "./editorTypes";
import {
  STRUCTURAL_NO_START_TYPE,
  STRUCTURAL_START_TRIGGER_TYPE,
  collectVariableRepairItems,
  parseValidationErrorPath
} from "../validationDiagnostics";
import type { ValidationActivityLabels } from "../validationActivityLabels";
import { formatDate } from "../workflowFormatting";
import { isRejectedTestRun } from "./editorHelpers";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { WorkflowTestRunControls } from "./WorkflowTestRunControls";

/**
 * Renders the unified draft-validation surface (issue #453). `errors` (server-derived + reconciled +
 * structural, merged by the caller) overrides the draft's own list when provided, so the footer never
 * says "No validation errors" while the promotion gate would refuse. Each error offers a "Repair" jump
 * for unresolved-variable references and a "Go to" jump for any error that resolves to a node.
 */
export function ValidationPanel({
  draft,
  errors: providedErrors,
  onRepair,
  onSelectNode,
  onSetAsStartNode,
  unavailable
}: {
  draft: WorkflowDraft;
  errors?: ValidationError[];
  onRepair(nodeId: string | null): void;
  onSelectNode?(nodeId: string): void;
  /**
   * Applies the fix for a start-node fault: the two structural start errors report a node that cannot be
   * reached, and the fix is to make it the start. Absent when the canvas is not a Flowchart.
   */
  onSetAsStartNode?(nodeId: string): void;
  unavailable?: boolean;
}) {
  const errors = providedErrors ?? (Array.isArray(draft.validationErrors) ? draft.validationErrors : []);
  if (!errors.length) {
    return (
      <div className="wf-validation ok">
        <Check size={14} /> No validation errors
        {unavailable ? <span className="wf-validation-note"> (server validation unavailable on this backend)</span> : null}
      </div>
    );
  }

  const repairItems = collectVariableRepairItems(errors);
  const repairByError = new Map(repairItems.map(item => [item.error, item]));

  return (
    <div className="wf-validation">
      <div className="wf-validation-summary">
        <AlertCircle size={14} />
        {errors.length} validation issue{errors.length === 1 ? "" : "s"}
        {repairItems.length > 0 ? (
          <span className="wf-validation-variable-count"> · {repairItems.length} invalid variable reference{repairItems.length === 1 ? "" : "s"}</span>
        ) : null}
      </div>
      <ul className="wf-validation-list">
        {errors.map((error, index) => {
          const repair = repairByError.get(error);
          const nodeId = repair?.path.nodeId ?? parseValidationErrorPath(error.path).nodeId;
          const startNodeFix = onSetAsStartNode && nodeId && isStartNodeError(error) ? nodeId : null;
          return (
            <li key={index} className={repair ? "wf-validation-item repairable" : "wf-validation-item"}>
              <span className="wf-validation-message" title={nodeId ?? undefined}>{error.message ?? "Validation issue."}</span>
              {startNodeFix ? (
                <button type="button" className="wf-validation-repair" onClick={() => onSetAsStartNode?.(startNodeFix)}>
                  <Flag size={12} /> Set as start
                </button>
              ) : null}
              {repair?.path.nodeId ? (
                <button type="button" className="wf-validation-repair wf-validation-node-action" onClick={() => onRepair(repair.path.nodeId)}>
                  <Wrench size={12} /> Repair
                </button>
              ) : nodeId && onSelectNode ? (
                <button type="button" className="wf-validation-repair wf-validation-node-action" onClick={() => onSelectNode(nodeId)}>
                  <MapPin size={12} /> Go to
                </button>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/** The two structural faults a start-node change fixes (see draftValidation). */
function isStartNodeError(error: ValidationError) {
  return error.type === STRUCTURAL_START_TRIGGER_TYPE || error.type === STRUCTURAL_NO_START_TYPE;
}

export function TestRunStatus({
  testRun,
  stale = false,
  onOpenDetails
}: {
  testRun: WorkflowTestRunView;
  stale?: boolean;
  onOpenDetails(): void;
}) {
  const rejected = isRejectedTestRun(testRun);
  const acceptedWithIncidents = !rejected && hasAcceptedRuntimeIssue(testRun);
  return (
    <div className="wf-test-run-status" data-state={rejected ? "rejected" : acceptedWithIncidents ? "accepted-with-incidents" : "accepted"}>
      <button
        type="button"
        className="wf-test-run-trigger"
        onClick={onOpenDetails}
      >
        {rejected || acceptedWithIncidents ? <AlertCircle size={16} /> : <Check size={16} />}
        {stale ? "Earlier draft · " : ""}{rejected ? "Test run rejected" : acceptedWithIncidents ? acceptedRuntimeIssueLabel(testRun) : "Test run dispatched"}
      </button>
    </div>
  );
}

export function WorkflowRuntimePanel({ testRun, context, currentDraftSignature, stimulusEditors, publishedEquivalent, onOpenRun, activityLabels, onRunViewUpdated, onWaitDraftUpdated }: {
  testRun: WorkflowTestRunState | WorkflowTestRunView | null;
  context?: StudioEndpointContext;
  currentDraftSignature?: string;
  stimulusEditors?: StudioTestStimulusEditorContribution[];
  publishedEquivalent?: WorkflowExecutableSummary | null;
  onOpenRun(workflowExecutionId: string, initialTab?: "issues"): void;
  activityLabels?: ValidationActivityLabels;
  onRunViewUpdated?(view: WorkflowTestRunView): void;
  onWaitDraftUpdated?(waitId: string, draftState: WorkflowTestRunWaitDraftState): void;
}) {
  if (!testRun) {
    return (
      <div className="wf-runtime-panel">
        <div className="wf-empty">Run the draft to see Runtime Evidence.</div>
      </div>
    );
  }

  const runState: WorkflowTestRunState = "view" in testRun
    ? testRun
    : { draftSignature: "", view: testRun, startLabel: "Direct start", workflowInputNames: [] };
  const view = runState.view;
  const stale = Boolean(currentDraftSignature && runState.draftSignature && runState.draftSignature !== currentDraftSignature);
  const actionEditor = runState.driverActionResult
    ? stimulusEditors?.find(editor => editor.id === runState.driverActionResult?.contributionId)
    : undefined;
  const ActionResult = actionEditor?.actionResultComponent;
  const rejected = isRejectedTestRun(view);
  const workflowExecutionId = view.workflowExecutionId;
  const triggerPrepared = view.status === "Prepared" && !workflowExecutionId;
  const acceptedWithIncidents = !rejected && hasAcceptedRuntimeIssue(view);
  // The equivalence signal resolves asynchronously; re-check the artifact id so a match computed for an
  // earlier test run never captions a newer one. A rejected dispatch can still mint a valid artifact id,
  // but pairing a green "identical" banner with a rejection reason reads as contradictory — suppress it.
  const equivalent = !stale && !rejected && publishedEquivalent && publishedEquivalent.artifactId === view.artifactId
    ? publishedEquivalent
    : null;
  return (
    <div className="wf-runtime-panel">
      <section className="wf-runtime-card" data-state={rejected ? "rejected" : acceptedWithIncidents ? "accepted-with-incidents" : "accepted"}>
        <header>
          <div>
            <span>{stale ? "Earlier draft" : "Latest Test Run"}</span>
            <h3>{rejected ? "Rejected by the server" : triggerPrepared ? "Trigger prepared" : "Transient run accepted"}</h3>
          </div>
          <WorkflowStatusBadge status={view.status} subStatus={view.commandDispatchStatus ?? undefined} />
        </header>
        <p>Ephemeral - not saved, promoted, or published.</p>
        {stale ? <div className="wf-test-drive-stale" role="status">This result belongs to an earlier draft snapshot.</div> : null}
        {runState.driverActionError ? <div className="wf-test-drive-error" role="alert">{runState.driverActionError}</div> : null}
        <p>Start: {runState.startLabel ?? "Direct start"}</p>
        {runState.workflowInputNames?.length ? <p>Inputs: {runState.workflowInputNames.join(", ")}</p> : null}
        {rejected && view.reason ? <RejectedReason reason={view.reason} activityLabels={activityLabels} /> : null}
        {acceptedWithIncidents ? (
          <div className="wf-runtime-incident-warning" role="status">
            <AlertCircle size={14} aria-hidden="true" />
            <span>{view.incidentCount != null && view.incidentCount > 0
              ? `The test run was accepted and recorded ${view.incidentCount} incident${view.incidentCount === 1 ? "" : "s"}. Review the incident details on the linked Run.`
              : "The test run was accepted with a runtime issue. Incident details may be unavailable in this response; review the linked Run."}</span>
            {workflowExecutionId ? <button type="button" onClick={() => onOpenRun(workflowExecutionId, "issues")}>Review incidents</button> : null}
          </div>
        ) : null}
        {equivalent ? (
          <div className="wf-runtime-equivalence">
            <Check size={14} /> Current draft is behaviorally identical to published v{equivalent.artifactVersion}.
          </div>
        ) : null}
        <dl className="wf-runtime-meta">
          <div><dt>Dispatch</dt><dd title={view.commandDispatchStatus ?? view.status}>{view.commandDispatchStatus ?? view.status}</dd></div>
          <div><dt>Test Run</dt><dd title={view.testRunId}>{view.testRunId}</dd></div>
          <div><dt>Artifact</dt><dd title={view.artifactId ?? "None"}>{view.artifactId ?? "None"}</dd></div>
          <div>
            <dt>Run / Instance</dt>
            <dd title={workflowExecutionId ?? "None"}>
              {workflowExecutionId ? (
                <button type="button" onClick={() => onOpenRun(workflowExecutionId)}>{workflowExecutionId}</button>
              ) : "None"}
            </dd>
          </div>
          <div><dt>Activities</dt><dd>{formatEvidenceCount(view.activityCount, "activity")}</dd></div>
          <div><dt>Incidents</dt><dd>{formatEvidenceCount(view.incidentCount, "incident")}</dd></div>
          <div><dt>Expires</dt><dd title={view.expiresAt ? formatDate(view.expiresAt) : "None"}>{view.expiresAt ? formatDate(view.expiresAt) : "None"}</dd></div>
        </dl>
        {ActionResult && runState.driverActionResult ? <ActionResult action={view.driverAction ?? { kind: runState.driverActionResult.value.kind }} result={runState.driverActionResult.value} /> : null}
        {context && onRunViewUpdated && "view" in testRun ? <WorkflowTestRunControls context={context} testRun={runState} stimulusEditors={stimulusEditors ?? []} onRunViewUpdated={onRunViewUpdated} onWaitDraftUpdated={onWaitDraftUpdated} /> : null}
      </section>
    </div>
  );
}

function hasAcceptedRuntimeIssue(testRun: WorkflowTestRunView) {
  return testRun.commandDispatchStatus === "AcceptedButFaulted" || (typeof testRun.incidentCount === "number" && testRun.incidentCount > 0);
}

function acceptedRuntimeIssueLabel(testRun: WorkflowTestRunView) {
  return typeof testRun.incidentCount === "number" && testRun.incidentCount > 0
    ? "Test run accepted with incidents"
    : "Test run accepted with a runtime issue";
}

function formatEvidenceCount(count: number | null | undefined, label: string) {
  if (typeof count !== "number") return "Available on linked Run";
  return `${count} ${label}${count === 1 ? "" : "s"}`;
}

function RejectedReason({
  reason,
  activityLabels
}: {
  reason: string;
  activityLabels?: ValidationActivityLabels;
}) {
  const humanized = activityLabels?.humanizeMessage(reason) ?? { message: reason };
  return (
    <div className="wf-runtime-reason" title={reason}>
      <AlertCircle size={14} />
      <span className="wf-runtime-reason-text">{humanized.message}</span>
      {humanized.code ? <span className="wf-alert-code">{humanized.code}</span> : null}
    </div>
  );
}
