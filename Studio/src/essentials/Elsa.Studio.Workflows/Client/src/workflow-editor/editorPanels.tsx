import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { Suspense, useEffect, useState, type ReactNode } from "react";
import { AlertCircle, AlertTriangle, Check, ChevronDown, Flag, Layers, MapPin, Wrench } from "lucide-react";
import { CopyableIdentifier, DisclosureFold, StudioButton, TechnicalDetails } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext, StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ValidationError, WorkflowDraft, WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import type { WorkflowTestRunState, WorkflowTestRunWaitDraftState } from "./editorTypes";
import {
  STRUCTURAL_NO_START_TYPE,
  STRUCTURAL_START_TRIGGER_TYPE,
  collectVariableRepairItems,
  parseValidationErrorPath,
  validationHint
} from "../validationDiagnostics";
import type { ValidationActivityLabels } from "../validationActivityLabels";
import { formatDate } from "../workflowFormatting";
import { isRejectedTestRun } from "./editorHelpers";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { LazyWorkflowTestRunControls } from "./lazyTestDriveUi";
import type { TestRunObservation } from "./useTestRunObservation";
import { StudioLink } from "../StudioLink";
import { isWarningSeverity } from "./publicationReview";
import "./editorPanels.css";

/**
 * Renders the unified draft-validation surface. The caller passes the merged validation set so the
 * footer agrees with the promotion gate; details stay collapsed until requested.
 */
export function ValidationPanel({
  draft,
  errors: providedErrors,
  onRepair,
  onSelectNode,
  onSetAsStartNode,
  activityLabels,
  unavailable,
  pending,
  expanded,
  onExpandedChange
}: {
  draft: WorkflowDraft;
  errors?: ValidationError[];
  onRepair(nodeId: string | null): void;
  onSelectNode?(nodeId: string, path?: string): void;
  onSetAsStartNode?(nodeId: string): void;
  activityLabels?: ValidationActivityLabels;
  unavailable?: boolean;
  pending?: boolean;
  expanded?: boolean;
  onExpandedChange?(open: boolean): void;
}) {
  const [localDetailsOpen, setLocalDetailsOpen] = useState(false);
  const detailsOpen = expanded ?? localDetailsOpen;
  const setDetailsOpen = (open: boolean) => {
    if (expanded === undefined) setLocalDetailsOpen(open);
    onExpandedChange?.(open);
  };
  const errors = providedErrors ?? (Array.isArray(draft.validationErrors) ? draft.validationErrors : []);
  useEffect(() => {
    if (!errors.length) {
      if (expanded === undefined) setLocalDetailsOpen(false);
      onExpandedChange?.(false);
    }
  }, [errors.length, expanded, onExpandedChange]);
  if (!errors.length) {
    if (pending) {
      return (
        <div className="wf-validation" role="status">
          Checking validation…
          {unavailable ? <span className="wf-validation-note">Server validation unavailable on this backend.</span> : null}
        </div>
      );
    }
    if (unavailable) {
      return <div className="wf-validation" role="status">Server validation unavailable on this backend.</div>;
    }
    return (
      <div className="wf-validation ok" role="status">
        <Check size={14} /> No validation issues found
      </div>
    );
  }

  const repairItems = collectVariableRepairItems(errors);
  const repairByError = new Map(repairItems.map(item => [item.error, item]));
  const warningCount = errors.filter(error => isWarningSeverity(error.severity)).length;
  const errorCount = errors.length - warningCount;
  const summary = warningCount === errors.length
    ? `${warningCount} warning${warningCount === 1 ? "" : "s"} · publishing is not blocked`
    : warningCount > 0
      ? `${errorCount} error${errorCount === 1 ? "" : "s"} · ${warningCount} warning${warningCount === 1 ? "" : "s"}`
      : `${errors.length} validation issue${errors.length === 1 ? "" : "s"}`;

  return (
    <div className={`wf-validation${detailsOpen ? " expanded" : ""}`}>
      <button
        type="button"
        className="wf-validation-toggle"
        aria-expanded={detailsOpen}
        aria-controls="wf-validation-details"
        onClick={() => setDetailsOpen(!detailsOpen)}
      >
        <span className="wf-validation-summary">
          {errorCount === 0 ? <AlertTriangle size={14} /> : <AlertCircle size={14} />}
          {summary}
          {repairItems.length > 0 ? (
            <span className="wf-validation-variable-count"> · {repairItems.length} invalid variable reference{repairItems.length === 1 ? "" : "s"}</span>
          ) : null}
          {pending ? <span className="wf-validation-note"> · Still checking</span> : null}
          {unavailable ? <span className="wf-validation-note"> · Server validation unavailable</span> : null}
        </span>
        <ChevronDown size={14} aria-hidden="true" />
      </button>
      <div className="wf-validation-details" id="wf-validation-details" hidden={!detailsOpen}>
        <ul className="wf-validation-list" aria-label="Validation findings">
          {errors.map((error, index) => {
            const repair = repairByError.get(error);
            const path = repair?.path ?? parseValidationErrorPath(error.path);
            const nodeId = path.nodeId;
            const startNodeFix = onSetAsStartNode && nodeId && isStartNodeError(error) ? nodeId : null;
            const warning = isWarningSeverity(error.severity);
            const hint = validationHint(error);
            const location = formatValidationLocation(path, activityLabels);
            const inputTarget = path.bag === "inputs" && !!path.referenceKey;
            return (
              <li key={index} className={`wf-validation-item${repair ? " repairable" : ""}${warning ? " warning" : ""}`}>
                <div className="wf-validation-item-heading">
                  <span className="wf-validation-severity" data-severity={warning ? "warning" : "error"}>{warning ? "Warning" : "Error"}</span>
                  {location ? <span className="wf-validation-location">{location}</span> : null}
                </div>
                <p className="wf-validation-message" title={nodeId ?? undefined}>{error.message ?? "Validation issue."}</p>
                {hint ? <p className="wf-validation-hint"><strong>What to try:</strong> {hint}</p> : null}
                <div className="wf-validation-actions">
                  {startNodeFix ? (
                    <button type="button" className="wf-validation-repair" onClick={() => onSetAsStartNode?.(startNodeFix)}>
                      <Flag size={12} /> Set as start
                    </button>
                  ) : null}
                  {repair?.path.nodeId ? (
                    <button
                      type="button"
                      className="wf-validation-repair"
                      onClick={() => inputTarget && onSelectNode
                        ? onSelectNode(repair.path.nodeId!, error.path ?? undefined)
                        : onRepair(repair.path.nodeId)}
                    >
                      <Wrench size={12} /> Repair
                    </button>
                  ) : nodeId && onSelectNode ? (
                    <button type="button" className="wf-validation-repair wf-validation-node-action" onClick={() => onSelectNode(nodeId, error.path ?? undefined)}>
                      <MapPin size={12} /> Go to {inputTarget ? "input" : "activity"}
                    </button>
                  ) : null}
                </div>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}

function formatValidationLocation(
  path: ReturnType<typeof parseValidationErrorPath>,
  activityLabels?: ValidationActivityLabels
): string | null {
  const parts: string[] = [];
  if (path.isWorkflowScope) parts.push("Workflow");
  else if (path.nodeId) parts.push(activityLabels?.labelFor(path.nodeId) ?? path.nodeId);
  if (path.bag) parts.push(path.bag[0].toUpperCase() + path.bag.slice(1));
  if (path.referenceKey) {
    parts.push(path.bag === "inputs"
      ? activityLabels?.inputLabelFor(path.nodeId ?? undefined, path.referenceKey) ?? path.referenceKey
      : path.referenceKey);
  }
  return parts.length ? parts.join(" → ") : null;
}

/** The two structural faults a start-node change fixes (see draftValidation). */
function isStartNodeError(error: ValidationError) {
  return error.type === STRUCTURAL_START_TRIGGER_TYPE || error.type === STRUCTURAL_NO_START_TYPE;
}

export function WorkflowRuntimePanel({ testRun, context, currentDraftSignature, stimulusEditors, publishedEquivalent, onOpenRun, activityLabels, onRunViewUpdated, onWaitDraftUpdated, observation, executionControls }: {
  testRun: WorkflowTestRunState | WorkflowTestRunView | null;
  context?: StudioEndpointContext;
  currentDraftSignature?: string;
  stimulusEditors?: StudioTestStimulusEditorContribution[];
  publishedEquivalent?: WorkflowExecutableSummary | null;
  onOpenRun(workflowExecutionId: string, initialTab?: "issues"): void;
  activityLabels?: ValidationActivityLabels;
  onRunViewUpdated?(view: WorkflowTestRunView): void;
  onWaitDraftUpdated?(waitId: string, draftState: WorkflowTestRunWaitDraftState): void;
  observation?: TestRunObservation;
  executionControls?: ReactNode;
}) {
  useStudioTimeZone();
  const [detailsOpen, setDetailsOpen] = useState(false);

  if (!testRun) {
    return (
      <div className="wf-runtime-panel">
        {executionControls}
        <div className="wf-empty">Run the draft to see its run data.</div>
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
  const execution = observation?.details?.instance;
  const activityCount = execution?.activityCount ?? view.activityCount;
  const incidentCount = execution?.incidentCount ?? view.incidentCount;
  const acceptedWithIncidents = !rejected && (hasAcceptedRuntimeIssue({ ...view, incidentCount }) || execution?.status === "Faulted");
  const summary = runtimeSummary(view, observation, triggerPrepared, acceptedWithIncidents);
  // The equivalence signal resolves asynchronously; re-check the artifact id so a match computed for an
  // earlier test run never captions a newer one. A rejected dispatch can still mint a valid artifact id,
  // but pairing a green "identical" banner with a rejection reason reads as contradictory — suppress it.
  const equivalent = !stale && !rejected && publishedEquivalent && publishedEquivalent.artifactId === view.artifactId
    ? publishedEquivalent
    : null;

  // Build the "Run details" disclosure summary line from activity/incident counts
  const detailsSummary = [
    typeof activityCount === "number" ? formatEvidenceCount(activityCount, "activity") : null,
    typeof incidentCount === "number" ? formatEvidenceCount(incidentCount, "incident") : null
  ].filter(Boolean).join(" · ");

  return (
    <div className="wf-runtime-panel">
      <section className="wf-runtime-card" aria-label="Test run summary" data-state={rejected ? "rejected" : acceptedWithIncidents ? "accepted-with-incidents" : "accepted"}>
        {/* Tier 1 — compact status + outcome */}
        <div className="wf-runtime-status-outcome">
          <WorkflowStatusBadge status={summary.status} />
          <span className="wf-runtime-outcome" aria-live="polite">{summary.description}</span>
        </div>

        {/* Primary action */}
        {workflowExecutionId || view.artifactId ? (
          <div className="wf-runtime-actions">
            {workflowExecutionId ? <StudioButton size="sm" variant="primary" onClick={() => onOpenRun(workflowExecutionId)}>Open run</StudioButton> : null}
            {view.artifactId ? <StudioLink className="wf-runtime-artifact-link" href={`/workflows/executables/${encodeURIComponent(view.artifactId)}`}>View artifact</StudioLink> : null}
          </div>
        ) : null}

        {executionControls}

        {/* Tier 2 — collapsible run details */}
        <DisclosureFold
          className="wf-runtime-details"
          label="Run details"
          summary={detailsSummary || null}
          open={detailsOpen}
          onOpenChange={setDetailsOpen}
        >
          <p className="wf-runtime-note">Temporary test · your draft is unchanged</p>
          {stale ? <div className="wf-test-drive-stale" role="status">This result belongs to an earlier draft snapshot.</div> : null}
          {runState.driverActionError ? <div className="wf-test-drive-error" role="alert">{runState.driverActionError}</div> : null}
          {rejected && view.reason ? <RejectedReason reason={view.reason} activityLabels={activityLabels} /> : null}
          {acceptedWithIncidents ? (
            <div className="wf-runtime-incident-warning" role="status">
              <AlertCircle size={14} aria-hidden="true" />
              <span>{incidentCount != null && incidentCount > 0
                ? `The test run was accepted and recorded ${incidentCount} incident${incidentCount === 1 ? "" : "s"}. Review the incident details on the linked Run.`
                : "The test run was accepted with a runtime issue. Incident details may be unavailable in this response; review the linked Run."}</span>
              {workflowExecutionId ? <StudioButton size="sm" onClick={() => onOpenRun(workflowExecutionId, "issues")}>Review incidents</StudioButton> : null}
            </div>
          ) : null}
          {equivalent ? (
            <div className="wf-runtime-equivalence">
              <Layers size={14} aria-hidden="true" /> Current draft has the same behavior as a published version.
            </div>
          ) : null}
          {ActionResult && runState.driverActionResult ? (
            <Suspense fallback={<p role="status">Loading Test Run response…</p>}>
              <ActionResult action={view.driverAction ?? { kind: runState.driverActionResult.value.kind }} result={runState.driverActionResult.value} />
            </Suspense>
          ) : null}
          {!rejected && context && onRunViewUpdated && "view" in testRun ? (
            <LazyWorkflowTestRunControls
              context={context}
              testRun={runState}
              stimulusEditors={stimulusEditors ?? []}
              onRunViewUpdated={onRunViewUpdated}
              onWaitDraftUpdated={onWaitDraftUpdated}
              observation={observation}
              showExecutionStatus={!execution}
            />
          ) : null}
        </DisclosureFold>

        {/* Tier 3 — technical details */}
        <TechnicalDetails key={view.testRunId}>
          <div className="wf-runtime-identifiers">
            <CopyableIdentifier label="Test run ID" value={view.testRunId} />
            {workflowExecutionId ? <CopyableIdentifier label="Run ID" value={workflowExecutionId} /> : null}
            {view.artifactId ? <CopyableIdentifier label="Executable ID" value={view.artifactId} /> : null}
          </div>
          <dl className="wf-runtime-meta">
            <div><dt>Dispatch</dt><dd>{view.commandDispatchStatus ?? view.status}</dd></div>
            {execution?.subStatus ? <div><dt>Execution detail</dt><dd>{execution.subStatus}</dd></div> : null}
            <div><dt>Started by</dt><dd>{runState.startLabel ?? "Direct start"}</dd></div>
            {runState.workflowInputNames?.length ? <div><dt>Inputs</dt><dd>{runState.workflowInputNames.join(", ")}</dd></div> : null}
            <div><dt>Run data expires</dt><dd>{view.expiresAt ? formatDate(view.expiresAt) : "Not provided"}</dd></div>
          </dl>
        </TechnicalDetails>
      </section>
    </div>
  );
}

function hasAcceptedRuntimeIssue(testRun: WorkflowTestRunView) {
  return testRun.commandDispatchStatus === "AcceptedButFaulted" || (typeof testRun.incidentCount === "number" && testRun.incidentCount > 0);
}

function formatEvidenceCount(count: number, label: string) {
  return `${count} ${count === 1 ? label : label === "activity" ? "activities" : `${label}s`}`;
}

function runtimeSummary(view: WorkflowTestRunView, observation: TestRunObservation | undefined, prepared: boolean, runtimeIssue: boolean) {
  if (isRejectedTestRun(view)) return { status: "Rejected", description: "The server rejected this test run." };
  if (view.status === "Expired" || observation?.status === "expired") return { status: "Expired", description: "Test Run data has expired." };
  const execution = observation?.details?.instance;
  const terminal = ["Completed", "Faulted", "Cancelled"].includes(execution?.status ?? "");
  if (!terminal && (view.status === "StopRequested" || view.status === "Stopped" && execution))
    return { status: "Stopping", description: "Stop requested. Waiting for runtime confirmation." };
  if (view.status === "Stopped" && !execution) return { status: "Stopped", description: "The test run was stopped." };
  if (execution) {
    const status = ["Completed", "Faulted", "Cancelled"].includes(execution.status) ? execution.status
      : observation?.waits.length || execution.status === "Suspended" ? "Waiting" : execution.status;
    const descriptions: Record<string, string> = {
      Completed: "Workflow execution completed.", Faulted: "Workflow execution needs attention.",
      Cancelled: "Workflow execution was cancelled.", Waiting: "Execution is waiting for input.",
      Running: "Workflow execution is running.", Pending: "Workflow execution is pending."
    };
    return { status, description: observation?.status === "paused"
      ? `Last confirmed: ${status.toLowerCase()} · observation paused.`
      : descriptions[status] ?? `Execution status: ${status}.` };
  }
  if (prepared) return { status: "Prepared", description: "Trigger prepared. Waiting for a test event." };
  if (runtimeIssue) return { status: "Needs attention", description: "The test was accepted with a runtime issue." };
  if (observation?.status === "disconnected" || observation?.status === "unavailable")
    return { status: "Unavailable", description: "Execution status is unavailable. Reconnect to refresh." };
  if (observation?.status === "paused") return { status: "Paused", description: "Execution observation is paused." };
  if (view.status === "DispatchDeferred") return { status: "Deferred", description: "Dispatch was deferred. Waiting for execution status." };
  if (view.status === "DispatchDuplicate") return { status: "Duplicate", description: "This start request was already dispatched. Waiting for execution status." };
  if (view.status === "DispatchAccepted") return { status: "Accepted", description: "Test accepted. Waiting for execution status." };
  return { status: view.status, description: `Test run status: ${view.status}.` };
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
