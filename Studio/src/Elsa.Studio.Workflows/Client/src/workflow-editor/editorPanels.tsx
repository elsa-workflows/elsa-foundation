import { AlertCircle, Check, Wrench } from "lucide-react";
import type { WorkflowDraft, WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import { collectVariableRepairItems } from "../validationDiagnostics";
import { formatDate } from "../workflowFormatting";
import { isRejectedTestRun } from "./editorHelpers";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";

export function ValidationPanel({ draft, onRepair }: { draft: WorkflowDraft; onRepair(nodeId: string | null): void }) {
  const errors = draft.validationErrors;
  if (!errors.length) {
    return <div className="wf-validation ok"><Check size={14} /> No validation errors</div>;
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
          return (
            <li key={index} className={repair ? "wf-validation-item repairable" : "wf-validation-item"}>
              <span className="wf-validation-message">{error.message ?? "Validation issue."}</span>
              {repair?.path.nodeId ? (
                <button type="button" className="wf-validation-repair" onClick={() => onRepair(repair.path.nodeId)}>
                  <Wrench size={12} /> Repair
                </button>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function TestRunStatus({
  testRun,
  onOpenDetails
}: {
  testRun: WorkflowTestRunView;
  onOpenDetails(): void;
}) {
  const rejected = isRejectedTestRun(testRun);
  return (
    <div className="wf-test-run-status" data-state={rejected ? "rejected" : "accepted"}>
      <button
        type="button"
        className="wf-test-run-trigger"
        onClick={onOpenDetails}
      >
        {rejected ? <AlertCircle size={16} /> : <Check size={16} />}
        {rejected ? "Test run rejected" : "Test run dispatched"}
      </button>
    </div>
  );
}

export function WorkflowRuntimePanel({ testRun, publishedEquivalent, onOpenRun }: {
  testRun: WorkflowTestRunView | null;
  publishedEquivalent?: WorkflowExecutableSummary | null;
  onOpenRun(workflowExecutionId: string): void;
}) {
  if (!testRun) {
    return (
      <div className="wf-runtime-panel">
        <div className="wf-empty">Run the draft to see Runtime Evidence.</div>
      </div>
    );
  }

  const rejected = isRejectedTestRun(testRun);
  const workflowExecutionId = testRun.workflowExecutionId;
  // The equivalence signal resolves asynchronously; re-check the artifact id so a match computed for an
  // earlier test run never captions a newer one. A rejected dispatch can still mint a valid artifact id,
  // but pairing a green "identical" banner with a rejection reason reads as contradictory — suppress it.
  const equivalent = !rejected && publishedEquivalent && publishedEquivalent.artifactId === testRun.artifactId
    ? publishedEquivalent
    : null;
  return (
    <div className="wf-runtime-panel">
      <section className="wf-runtime-card" data-state={rejected ? "rejected" : "accepted"}>
        <header>
          <div>
            <span>Latest Test Run</span>
            <h3>{rejected ? "Rejected by the server" : "Transient run accepted"}</h3>
          </div>
          <WorkflowStatusBadge status={testRun.status} subStatus={testRun.commandDispatchStatus ?? undefined} />
        </header>
        <p>Ephemeral - not saved, promoted, or published.</p>
        {rejected && testRun.reason ? <div className="wf-runtime-reason"><AlertCircle size={14} /> {testRun.reason}</div> : null}
        {equivalent ? (
          <div className="wf-runtime-equivalence">
            <Check size={14} /> Current draft is behaviorally identical to published v{equivalent.artifactVersion}.
          </div>
        ) : null}
        <dl className="wf-runtime-meta">
          <div><dt>Dispatch</dt><dd title={testRun.commandDispatchStatus ?? testRun.status}>{testRun.commandDispatchStatus ?? testRun.status}</dd></div>
          <div><dt>Test Run</dt><dd title={testRun.testRunId}>{testRun.testRunId}</dd></div>
          <div><dt>Artifact</dt><dd title={testRun.artifactId ?? "None"}>{testRun.artifactId ?? "None"}</dd></div>
          <div>
            <dt>Run / Instance</dt>
            <dd title={workflowExecutionId ?? "None"}>
              {workflowExecutionId ? (
                <button type="button" onClick={() => onOpenRun(workflowExecutionId)}>{workflowExecutionId}</button>
              ) : "None"}
            </dd>
          </div>
          <div><dt>Activities</dt><dd>{formatEvidenceCount(testRun.activityCount, "activity")}</dd></div>
          <div><dt>Incidents</dt><dd>{formatEvidenceCount(testRun.incidentCount, "incident")}</dd></div>
          <div><dt>Expires</dt><dd title={testRun.expiresAt ? formatDate(testRun.expiresAt) : "None"}>{testRun.expiresAt ? formatDate(testRun.expiresAt) : "None"}</dd></div>
        </dl>
      </section>
    </div>
  );
}

function formatEvidenceCount(count: number | null | undefined, label: string) {
  if (typeof count !== "number") return "Available on linked Run";
  return `${count} ${label}${count === 1 ? "" : "s"}`;
}
