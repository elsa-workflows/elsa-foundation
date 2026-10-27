import { Component, lazy, Suspense, useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Activity as ActivityIcon, ChevronRight, Undo2 } from "lucide-react";
import type { StudioActivityInputDescriptor, StudioEndpointContext, StudioExpressionEditorContribution, StudioExpressionSourceRendererContext } from "@elsa-workflows/studio-sdk";
import { getActivityExecutionInspection } from "../api/runtime";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot, ActivityExecutionStateSummary } from "../workflowTypes";
import type { ExecutableGraphNodeFacts } from "../executableGraph";
import { resolveRuntimeActivityLabel as resolveActivityLabel } from "../runtimeActivityDisplay";
import { readPortableExpressionDefinition } from "../activityProperties";
import { isMaskedInput, readSecretReference } from "../maskedInput";
import { isCompensationHandlerExecution } from "../compensationRuntime";
import { buildInputInspectionRows, evaluationPhase, evaluationSequence, type InputInspectionRow, type InputInspectionState } from "../inputInspectionRows";
import { formatActivityVersion, formatDate, formatDuration, shortTypeName } from "../workflowFormatting";
import { CopyValueButton } from "./executableShared";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { formatCaptureMode, formatSnapshotPayload, runtimeValueContextId, RuntimeValueEvidenceCard, RuntimeValueEvidenceContent, RuntimeValueEvidenceResolutionProvider } from "./RuntimeValueEvidence";
import "./activityInspection.css";

const ReusableBoundaryInspector = lazy(async () => ({ default: (await import("./ReusableBoundaryInspector")).ReusableBoundaryInspector }));

type ActivityExecutionInspectionState = {
  activityExecutionId: string | null;
  workflowExecutionId: string | null;
  contextIdentity: number | string | null;
  status: "idle" | "loading" | "ready" | "failed";
  inspection: ActivityExecutionInspection | null;
  error: string;
};

function resolveReportedCount(count: number | null | undefined, legacyIds: readonly unknown[] | null | undefined) {
  return count !== undefined ? count : Array.isArray(legacyIds) ? legacyIds.length : null;
}

// A compensation handler occurrence names the forward occurrence it undid, linked so the operator can go to it.
function CompensatesRow({ forwardActivityExecutionId, stepName, onSelectEvidence }: {
  forwardActivityExecutionId: string;
  stepName?: string;
  onSelectEvidence?(evidenceId: string): void;
}) {
  const target = stepName ? `${stepName} (occurrence ${forwardActivityExecutionId})` : `occurrence ${forwardActivityExecutionId}`;
  return (
    <p className="wf-activity-compensates">
      <Undo2 size={14} aria-hidden="true" /> <span>Compensation · Compensates </span>
      {onSelectEvidence ? (
        <button type="button" className="wf-activity-compensates-link" onClick={() => onSelectEvidence(forwardActivityExecutionId)}>{target}</button>
      ) : <code>{target}</code>}
    </p>
  );
}

export function WorkflowActivityExecutionDetails({
  executionNavigation,
  childExecutions,
  context,
  activity,
  activityCatalog,
  executableNodeFacts,
  expressionEditors = [],
  focusInputKey = null,
  onSelectEvidence,
  refreshKey = 0,
  compact = false
}: {
  compact?: boolean;
  refreshKey?: number;
  executionNavigation?: ReactNode;
  childExecutions?: ReactNode;
  context: StudioEndpointContext;
  activity: ActivityExecutionStateSummary | null;
  activityCatalog: ActivityCatalogItem[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors?: StudioExpressionEditorContribution[];
  focusInputKey?: string | null;
  onSelectEvidence?(evidenceId: string): void;
}) {
  const selectedActivityExecutionId = activity?.activityExecutionId ?? null;
  const selectedWorkflowExecutionId = activity?.workflowExecutionId ?? null;
  const selectedContextIdentity = runtimeValueContextId(context);
  const [inspectionState, setInspectionState] = useState<ActivityExecutionInspectionState>({
    activityExecutionId: null,
    workflowExecutionId: null,
    contextIdentity: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  const [copyStatus, setCopyStatus] = useState("");

  useEffect(() => {
    setCopyStatus("");
    if (!selectedActivityExecutionId || !selectedWorkflowExecutionId) {
      setInspectionState({ activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" });
      return;
    }

    const controller = new AbortController();
    const activityExecutionId = selectedActivityExecutionId;
    const workflowExecutionId = selectedWorkflowExecutionId;
    setInspectionState(previous => previous.status === "ready" && previous.activityExecutionId === activityExecutionId
      && previous.workflowExecutionId === workflowExecutionId && previous.contextIdentity === selectedContextIdentity ? previous
      : { activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" });

    getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId, controller.signal).then(
      inspection => {
        if (controller.signal.aborted) return;
        if (inspection.workflowExecutionId !== workflowExecutionId || inspection.activityExecutionId !== activityExecutionId) {
          setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity,
            status: "failed", inspection: null, error: "Execution evidence identity did not match the selected occurrence." });
          return;
        }
        if (!controller.signal.aborted) setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "ready", inspection, error: "" });
      },
      error => {
        if (!controller.signal.aborted) {
          setInspectionState({
            activityExecutionId,
            workflowExecutionId,
            contextIdentity: selectedContextIdentity,
            status: "failed",
            inspection: null,
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
    );

    return () => controller.abort();
  }, [context, refreshKey, selectedActivityExecutionId, selectedContextIdentity, selectedWorkflowExecutionId]);

  const currentInspectionState: ActivityExecutionInspectionState =
    inspectionState.activityExecutionId === selectedActivityExecutionId &&
    inspectionState.workflowExecutionId === selectedWorkflowExecutionId &&
    inspectionState.contextIdentity === selectedContextIdentity
      ? inspectionState
      : selectedActivityExecutionId && selectedWorkflowExecutionId
        ? { activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" }
        : { activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" };
  const resolutionScope = useMemo(
    () => selectedActivityExecutionId && selectedWorkflowExecutionId
      ? { context, activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId }
      : null,
    [context, selectedActivityExecutionId, selectedWorkflowExecutionId]);

  if (!activity) {
    return (
      <section className="wf-instance-section">
        <h4>Activity</h4>
        <p>No activity selected.</p>
      </section>
    );
  }

  const catalogItem = activityCatalog.find(item => item.activityTypeKey === activity.activityType);
  const declaredInputs = readDeclaredActivityInputs(catalogItem?.inputs);
  const activityLabel = resolveActivityLabel(
    executableNodeFacts?.presentation,
    catalogItem,
    activity.activityType);
  const activityDescription = executableNodeFacts?.presentation?.description?.trim();
  const activityTypeLabel = shortTypeName(activity.activityType) ?? activity.activityType;
  const reusableBoundary = currentInspectionState.status === "ready" ? currentInspectionState.inspection?.boundary ?? null : null;
  const overviewVersion = formatActivityVersion(reusableBoundary?.version ?? activity.activityTypeVersion);
  const statusLabel = [activity.status, activity.subStatus].filter(Boolean).join(" · ");
  const startedLabel = formatDate(activity.startedAt);
  const completedLabel = formatDate(activity.completedAt);
  const durationLabel = formatDuration(activity.startedAt, activity.completedAt) || "Unknown";
  const bookmarkCount = resolveReportedCount(activity.bookmarkCount, activity.bookmarkIds);
  const incidentCount = resolveReportedCount(activity.incidentCount, activity.incidentIds);
  const executionAttempt = currentInspectionState.status === "ready" ? currentInspectionState.inspection?.attempt : null;
  const executionDetailRows = [
    ...(compact ? [
      { label: "Started", value: startedLabel, copiedLabel: "start time" },
      { label: "Duration", value: durationLabel, copiedLabel: "duration" },
      { label: "Incidents", value: incidentCount == null ? "Unavailable" : String(incidentCount), copiedLabel: "incident count" }
    ] : []),
    { label: "Activity Execution ID", value: activity.activityExecutionId, copiedLabel: "activity execution ID", code: true },
    { label: "Authored Activity ID", value: activity.authoredActivityId, copiedLabel: "authored activity ID", code: true },
    { label: "Activity type", value: `${activity.activityType}@${activity.activityTypeVersion}`, copiedLabel: "activity type", code: true },
    { label: "Completed", value: completedLabel, copiedLabel: "completion time" },
    { label: "Bookmarks", value: bookmarkCount == null ? "Unavailable" : String(bookmarkCount), copiedLabel: "bookmark count" },
    ...(executionAttempt ? [{
      label: "Attempt",
      value: `${executionAttempt.attemptNumber} of ${executionAttempt.totalAttempts ?? "?"}`,
      copiedLabel: "attempt",
      code: true
    }] : [])
  ];
  const markCopied = (label: string) => setCopyStatus(`Copied ${label}.`);
  const markCopyFailed = (label: string) => setCopyStatus(`Could not copy ${label}.`);

  return (
    <>
      <section className="wf-instance-section wf-activity-overview">
        <div className="wf-activity-overview-heading">
          <span className="wf-activity-overview-icon" aria-hidden="true">
            <ActivityIcon size={18} />
          </span>
          <div className="wf-activity-overview-title">
            <span className="wf-activity-overview-eyebrow">Selected activity</span>
            <div className="wf-activity-copy-line">
              <h4>{activityLabel}</h4>
              <CopyValueButton
                value={activityLabel}
                ariaLabel="Copy activity name"
                copiedLabel="activity name"
                onCopied={markCopied}
                onCopyFailed={markCopyFailed}
              />
            </div>
            {activityDescription
              ? <p className="wf-activity-overview-description">{activityDescription}</p>
              : null}
            <div className="wf-activity-copy-line wf-activity-overview-type">
              {reusableBoundary
                ? <span>Reusable activity <small title={overviewVersion.full}>v{overviewVersion.short}</small></span>
                : <span title={activity.activityType}>{activityTypeLabel} <small title={overviewVersion.full}>v{overviewVersion.short}</small></span>}
            </div>
          </div>
          <span className="wf-activity-overview-status">
            <WorkflowStatusBadge status={activity.status} subStatus={activity.subStatus} />
            <CopyValueButton
              value={statusLabel}
              ariaLabel="Copy activity status"
              copiedLabel="activity status"
              onCopied={markCopied}
              onCopyFailed={markCopyFailed}
            />
          </span>
        </div>

        {!compact ? <dl className="wf-activity-summary-grid">
          <ActivityMetadataValue label="Started" value={startedLabel} copiedLabel="start time" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          <ActivityMetadataValue label="Duration" value={durationLabel} copiedLabel="duration" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          <ActivityMetadataValue label="Incidents" value={incidentCount == null ? "Unavailable" : String(incidentCount)} copiedLabel="incident count" onCopied={markCopied} onCopyFailed={markCopyFailed} />
        </dl> : null}

        {isCompensationHandlerExecution(activity) && activity.provenance?.schedulingActivityExecutionId ? (
          <CompensatesRow
            forwardActivityExecutionId={activity.provenance.schedulingActivityExecutionId}
            stepName={currentInspectionState.status === "ready" ? currentInspectionState.inspection?.provenance?.metadata?.["compensation.step"] : undefined}
            onSelectEvidence={onSelectEvidence}
          />
        ) : null}

        {copyStatus ? <p className="wf-copy-status" role="status" aria-live="polite">{copyStatus}</p> : null}
      </section>
      {executionNavigation}
      {childExecutions}
      {currentInspectionState.status === "ready" && (!compact || currentInspectionState.inspection?.incidents.length) ? <section className="wf-instance-section" aria-label="Activity incidents">
        <h4>Incidents</h4>
        {currentInspectionState.inspection?.incidents.length ? <ul>{currentInspectionState.inspection.incidents.map(incident => <li key={incident.incidentId}>
          <WorkflowStatusBadge status={incident.status} /><p>{incident.message}</p><small>{incident.failureType}</small>
        </li>)}</ul> : <p>No incidents recorded for this execution.</p>}
      </section> : null}
      <RuntimeValueEvidenceResolutionProvider scope={resolutionScope}>
        <WorkflowActivityInputEvidence
          state={currentInspectionState}
          declarations={declaredInputs}
          executableNodeFacts={executableNodeFacts}
          expressionEditors={expressionEditors}
          focusInputKey={focusInputKey}
        />
        <WorkflowActivityValueEvidence
          state={currentInspectionState}
          subject="ActivityOutput"
          title="Outputs"
          loadingText="Loading runtime output evidence..."
          failureText="Runtime output evidence is unavailable."
          emptyText="No runtime output snapshots were recorded for this execution."
        />
        <WorkflowActivityOutcomeEvidence state={currentInspectionState} />
      </RuntimeValueEvidenceResolutionProvider>
      {currentInspectionState.status === "ready" && currentInspectionState.inspection?.boundary ? (
        <ActivityBoundaryDetails
          key={JSON.stringify([selectedContextIdentity, selectedWorkflowExecutionId, selectedActivityExecutionId])}
          context={context}
          inspection={currentInspectionState.inspection}
          activityLabel={activityLabel}
        />
      ) : null}
      <section className="wf-instance-section">
        <details className="wf-activity-execution-details">
          <summary>
            <span>Execution details</span>
            <small>{executionDetailRows.length} values</small>
          </summary>
          <dl className="wf-activity-detail-list">
            {executionDetailRows.map(row => (
              <ActivityMetadataValue key={row.label} {...row} onCopied={markCopied} onCopyFailed={markCopyFailed} />
            ))}
          </dl>
        </details>
      </section>
    </>
  );
}

function ActivityBoundaryDetails({ context, inspection, activityLabel }: { context: StudioEndpointContext; inspection: ActivityExecutionInspection; activityLabel: string }) {
  const [open, setOpen] = useState(false);

  return (
    <section className="wf-instance-section wf-activity-boundary-details">
      <details onToggle={event => setOpen(event.currentTarget.open)}>
        <summary>
          <ChevronRight size={14} aria-hidden="true" />
          <span>Inside this activity</span>
        </summary>
        {open ? (
          <Suspense fallback={<p role="status">Loading activity steps...</p>}>
            <ReusableBoundaryInspector context={context} inspection={inspection} activityLabel={activityLabel} />
          </Suspense>
        ) : null}
      </details>
    </section>
  );
}

function WorkflowActivityOutcomeEvidence({ state }: { state: ActivityExecutionInspectionState }) {
  if (state.status === "idle") return null;

  if (state.status === "loading") {
    return (
      <section className="wf-instance-section">
        <h4>Outcomes</h4>
        <p>Loading runtime outcomes...</p>
      </section>
    );
  }

  if (state.status === "failed") {
    return (
      <section className="wf-instance-section">
        <h4>Outcomes</h4>
        <p>Runtime outcomes are unavailable.</p>
        {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
      </section>
    );
  }

  const outcomeNames = state.inspection?.outcomeNames ?? [];
  if (outcomeNames.length === 0) {
    return (
      <section className="wf-instance-section">
        <h4>Outcomes</h4>
        <p>No runtime outcomes were recorded for this execution.</p>
      </section>
    );
  }

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          Outcomes
          <span className="wf-runtime-evidence-count" aria-label={`${outcomeNames.length} outcomes`}>
            {outcomeNames.length}
          </span>
        </h4>
      </header>
      <div className="wf-activity-outcome-list" role="list">
        {outcomeNames.map((outcomeName, index) => (
          <span className="wf-activity-outcome-chip" role="listitem" key={`${outcomeName}:${index}`}>{outcomeName}</span>
        ))}
      </div>
    </section>
  );
}

function readDeclaredActivityInputs(inputs: unknown[] | undefined): StudioActivityInputDescriptor[] {
  return (inputs ?? []).filter((input): input is StudioActivityInputDescriptor => {
    if (!input || typeof input !== "object") return false;
    const record = input as Record<string, unknown>;
    return typeof record.name === "string" && typeof record.typeName === "string";
  });
}

function ActivityMetadataValue({
  label,
  value,
  copiedLabel,
  code = false,
  onCopied,
  onCopyFailed
}: {
  label: string;
  value: string;
  copiedLabel: string;
  code?: boolean;
  onCopied(label: string): void;
  onCopyFailed(label: string): void;
}) {
  const content = code ? <code title={value}>{value}</code> : <span title={value}>{value}</span>;

  return (
    <div className="wf-activity-meta-item">
      <dt>{label}</dt>
      <dd>
        <span className="wf-activity-meta-value">{content}</span>
        <CopyValueButton
          value={value}
          ariaLabel={`Copy ${copiedLabel}`}
          copiedLabel={copiedLabel}
          onCopied={onCopied}
          onCopyFailed={onCopyFailed}
        />
      </dd>
    </div>
  );
}

function WorkflowActivityInputEvidence({
  state,
  declarations,
  executableNodeFacts,
  expressionEditors,
  focusInputKey
}: {
  state: ActivityExecutionInspectionState;
  declarations: StudioActivityInputDescriptor[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors: StudioExpressionEditorContribution[];
  focusInputKey: string | null;
}) {
  if (state.status === "idle") return null;

  const evaluations = state.status === "ready"
    ? (state.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === "ActivityInput")
    : [];
  const rows = buildInputInspectionRows({
    declarations,
    authoredSources: executableNodeFacts?.authoredInputs ?? [],
    compiledBindings: executableNodeFacts?.inputBindings ?? [],
    evaluations,
    authoredSourceAccess: executableNodeFacts?.authoredInputsAccess
  });

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          Inputs
          {rows.length > 0 ? <span className="wf-runtime-evidence-count" aria-label={`${rows.length} inputs`}>{rows.length}</span> : null}
        </h4>
        <span className="wf-input-inspection-caption">Evaluated at runtime</span>
      </header>
      {state.status === "loading" ? <p>Loading runtime input evidence...</p> : null}
      {state.status === "failed" ? (
        <>
          <p>Runtime input evidence is unavailable. Authored source remains independently inspectable where permitted.</p>
          {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
        </>
      ) : null}
      {rows.length === 0 && state.status === "ready" ? <p>No declared inputs, pinned bindings, or runtime input evidence are available for this execution.</p> : null}
      {rows.length > 0 ? (
        <div className="wf-runtime-input-list wf-input-inspection-list" role="list">
          {rows.map(row => (
            <InputInspectionRowCard
              key={row.rowKey}
              row={row}
              sourceAccess={executableNodeFacts?.authoredInputsAccess}
              expressionEditors={expressionEditors}
              focusInput={!!focusInputKey && row.inputKey === focusInputKey}
              inspectionStatus={state.status}
            />
          ))}
        </div>
      ) : null}
    </section>
  );
}

function InputInspectionRowCard({
  row,
  sourceAccess,
  expressionEditors,
  focusInput = false,
  inspectionStatus
}: {
  row: InputInspectionRow;
  sourceAccess?: string | null;
  expressionEditors: StudioExpressionEditorContribution[];
  focusInput?: boolean;
  inspectionStatus: ActivityExecutionInspectionState["status"];
}) {
  const latest = row.latestEvaluation;
  const sourceKind = row.authoredSource?.expressionType || row.compiledBinding?.source || "No source";
  // The input's declaration counts as much as the per-record flags: a masked input (a password hint, or declared
  // sensitive or secret-only) shows nothing of its authored source but the name of a Secret Reference it is bound
  // to, even when the backend did not flag the record. Any other source stays hidden, and so does a masked input's
  // compiled binding, whose fields Studio cannot vet. A record the backend flagged stays hidden.
  const masked = !!row.declaration && isMaskedInput(row.declaration);
  const secretReference = masked ? readSecretReference(row.authoredSource?.expressionType, row.authoredSource?.value) : null;
  const shownSource = secretReference && row.authoredSource ? { ...row.authoredSource, value: secretReference.name } : row.authoredSource;
  const sourceProtected = isProtectedSourceAccess(sourceAccess) || isProtectedSourceAccess(row.authoredSource?.accessState ?? row.authoredSource?.access) || !!row.authoredSource?.isSensitive || !!row.compiledBinding?.isSensitive || !!latest?.isSensitive
    || (masked && !secretReference);
  const regionId = useId();
  const detailsRef = useRef<HTMLDetailsElement>(null);
  const summaryRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!focusInput) return;
    const details = detailsRef.current;
    const summary = summaryRef.current;
    if (!details || !summary) return;
    details.open = true;
    const frame = requestAnimationFrame(() => {
      summary.focus();
      summary.scrollIntoView?.({ block: "nearest" });
    });
    return () => cancelAnimationFrame(frame);
  }, [focusInput, row.rowKey]);

  const sensitiveInput = masked || row.authoredSource?.isSensitive || row.compiledBinding?.isSensitive;
  const runtimeSnapshot = latest && sensitiveInput && !latest.isSensitive ? { ...latest, isSensitive: true } : latest;

  return (
    <div className="wf-runtime-input wf-input-inspection-row" role="listitem">
      <div className="wf-input-inspection-value-row">
        <div className="wf-input-inspection-identity">
          <strong className="wf-input-inspection-name">{row.name}</strong>
          <small className="wf-input-inspection-type" title={row.declaredType}>{shortTypeName(row.declaredType) || "Unknown type"}</small>
        </div>
        <div className="wf-input-inspection-value" aria-label={`${row.name} runtime value`}>
          {runtimeSnapshot ? <RuntimeValueEvidenceContent snapshot={runtimeSnapshot} presentation="input" /> : (
            <p className="wf-input-inspection-unavailable">
              {inspectionStatus === "loading" ? "Loading captured value…" : inspectionStatus === "failed" ? "Runtime evidence unavailable" : "No evaluation evidence recorded"}
            </p>
          )}
        </div>
      </div>
      <details ref={detailsRef} className="wf-input-inspection-disclosure">
        <summary ref={summaryRef} tabIndex={0} className="wf-input-inspection-summary" aria-controls={regionId}>
          <ChevronRight size={14} aria-hidden="true" />
          <span>Source &amp; diagnostics</span>
          <small>{sourceProtected ? (isProtectedSourceAccess(sourceAccess) ? "Source hidden" : "Protected source") : sourceKind}</small>
        </summary>
        <div id={regionId} className="wf-input-inspection-content">
          <section className="wf-input-inspection-detail wf-input-inspection-source" aria-label={`${row.name} authored source`}>
            <h5>Authored source</h5>
            <AuthoredInputSource
              row={row}
              source={shownSource}
              compiledBindingShown={!masked}
              sourceAccess={sourceAccess}
              protectedSource={sourceProtected}
              expressionEditors={expressionEditors}
            />
          </section>
          <section className="wf-input-inspection-detail" aria-label={`${row.name} runtime evidence`}>
            <h5>Capture details</h5>
            {latest ? (
              <dl className="wf-input-inspection-metadata">
                {[
                  ["Type", row.declaredType || "Unknown type"],
                  ["Capture", formatCaptureMode(latest.captureMode)],
                  ["Captured", formatDate(latest.capturedAt)],
                  ...(evaluationPhase(latest) ? [["Phase", formatCaptureMode(evaluationPhase(latest)!)]] : []),
                  ...(typeof evaluationSequence(latest) === "number" ? [["Sequence", String(evaluationSequence(latest))]] : [])
                ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
              </dl>
            ) : <p>No runtime evaluation was recorded.</p>}
            {row.states.length > 0 ? <p className="wf-instance-note">{row.states.map(formatInputInspectionState).join(" · ")}</p> : null}
            {row.evaluations.length > 1 ? <InputEvaluationHistory evaluations={row.evaluations} /> : null}
          </section>
        </div>
      </details>
    </div>
  );
}

function InputEvaluationHistory({ evaluations }: { evaluations: ActivityExecutionInspectionValueSnapshot[] }) {
  return (
    <details>
      <summary>Evaluation history ({evaluations.length})</summary>
      <ol>
        {evaluations.map((evaluation, index) => (
          <li key={evaluation.evaluationId || `${evaluation.capturedAt}:${index}`}>
            <strong>{evaluationPhase(evaluation) ? formatCaptureMode(evaluationPhase(evaluation)!) : "Unknown phase"}</strong>
            {typeof evaluationSequence(evaluation) === "number" ? ` · sequence ${evaluationSequence(evaluation)}` : " · legacy sequence"}
            {` · ${formatDate(evaluation.capturedAt)}`}
            {evaluation.failure?.code ? ` · ${evaluation.failure.code}` : ""}
          </li>
        ))}
      </ol>
    </details>
  );
}

function AuthoredInputSource({
  row,
  source,
  compiledBindingShown,
  sourceAccess,
  protectedSource,
  expressionEditors
}: {
  row: InputInspectionRow;
  source: InputInspectionRow["authoredSource"];
  compiledBindingShown: boolean;
  sourceAccess?: string | null;
  protectedSource: boolean;
  expressionEditors: StudioExpressionEditorContribution[];
}) {
  if (protectedSource) {
    return <p>{isProtectedSourceAccess(sourceAccess) ? "Authored source is hidden by source permissions." : "Authored source is protected because this input is sensitive."}</p>;
  }

  const expressionType = source?.expressionType || "Unknown";
  const portableDefinition = source
    ? readPortableExpressionDefinition(source.value, expressionType)
    : null;
  const sourceValue = portableDefinition?.source ?? source?.value;
  const fallback = source ? <GenericExpressionSource expressionType={expressionType} value={sourceValue} expanded /> : null;
  const renderer = source ? resolveExpressionSourceRenderer(expressionEditors, row, expressionType) : undefined;
  const Renderer = renderer?.sourceRenderer?.expanded;
  const rendererContext: StudioExpressionSourceRendererContext | undefined = source ? {
    expressionType,
    value: sourceValue,
    metadata: {},
    isSensitive: !!source.isSensitive,
    surface: "expanded"
  } : undefined;

  return (
    <>
      {source && Renderer && rendererContext ? (
        <ExpressionSourceRendererBoundary fallback={fallback}>
          <Renderer context={rendererContext} />
        </ExpressionSourceRendererBoundary>
      ) : fallback ?? <p>No authored source is available for this pinned Source Reference.</p>}
      {!row.compiledBinding ? (
        <p className="wf-instance-note">No compiled binding is available.</p>
      ) : compiledBindingShown ? (
        <details>
          <summary>Compiled binding ({row.compiledBinding.source || "Unknown"})</summary>
          <GenericExpressionSource expressionType={row.compiledBinding.source || "Unknown"} value={compiledBindingDetails(row.compiledBinding)} expanded />
        </details>
      ) : <p className="wf-instance-note">The compiled binding of a masked input is not shown.</p>}
    </>
  );
}

class ExpressionSourceRendererBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}

function resolveExpressionSourceRenderer(
  expressionEditors: StudioExpressionEditorContribution[],
  row: InputInspectionRow,
  expressionType: string
) {
  const descriptor = row.declaration ?? {
    name: row.name,
    displayName: row.name,
    typeName: row.declaredType || "System.Object",
    referenceKey: row.inputKey
  };
  const context = {
    syntax: expressionType,
    surface: "expanded" as const,
    descriptor,
    activity: null,
    expressionDescriptors: [],
    readOnly: true
  };

  return expressionEditors.find(editor => {
    if (!editor.sourceRenderer) return false;
    try {
      return editor.supports(context);
    } catch {
      return false;
    }
  });
}

function GenericExpressionSource({ expressionType, value, expanded = false }: { expressionType: string; value: unknown; expanded?: boolean }) {
  const text = formatSnapshotPayload(value);
  const bounded = text.length > 4_000 ? `${text.slice(0, 3_997)}...` : text;
  return (
    <div className="wf-input-inspection-expression">
      <small>{expressionType || "Unknown expression"}</small>
      {expanded && (bounded.includes("\n") || bounded.length > 160) ? <pre>{bounded}</pre> : <code>{bounded}</code>}
    </div>
  );
}

function compiledBindingDetails(binding: ExecutableGraphNodeFacts["inputBindings"][number]) {
  const { inputName: _inputName, inputKey: _inputKey, summary: _summary, ...details } = binding;
  return details;
}

function isProtectedSourceAccess(access: string | null | undefined) {
  if (!access) return false;
  const normalized = access.toLowerCase();
  return normalized !== "visible" && normalized !== "allowed";
}

function formatInputInspectionState(state: InputInspectionState) {
  const labels: Record<InputInspectionState, string> = {
    missingDeclaration: "Declaration unavailable",
    missingAuthoredSource: "Authored source unavailable",
    missingCompiledBinding: "Compiled binding unavailable",
    noEvaluation: "Evaluation evidence not recorded",
    orphanEvidence: "Orphan runtime evidence",
    compiledOnly: "Compiled source only",
    sourceUnavailable: "Source unavailable",
    duplicateKey: "Duplicate input key",
    legacyIdentity: "Legacy name-only identity",
    redacted: "Redacted",
    permissionHidden: "Permission hidden",
    metadataOnly: "Metadata only",
    unavailable: "Unavailable",
    unsupported: "Unsupported snapshot",
    truncated: "Truncated snapshot",
    failedEvaluation: "Evaluation failed"
  };
  return labels[state];
}

function WorkflowActivityValueEvidence({
  state,
  subject,
  title,
  loadingText,
  failureText,
  emptyText
}: {
  state: ActivityExecutionInspectionState;
  subject: "ActivityInput" | "ActivityOutput";
  title: string;
  loadingText: string;
  failureText: string;
  emptyText: string;
}) {
  if (state.status === "idle") return null;

  if (state.status === "loading") {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{loadingText}</p>
      </section>
    );
  }

  if (state.status === "failed") {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{failureText}</p>
        {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
      </section>
    );
  }

  const snapshots = (state.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === subject);
  if (snapshots.length === 0) {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{emptyText}</p>
      </section>
    );
  }

  const captureModeByKey = new Map(snapshots.map(snapshot => [formatCaptureMode(snapshot.captureMode).toLowerCase(), snapshot.captureMode]));
  const captureModes = [...captureModeByKey.values()];
  const homogeneousCaptureMode = captureModes.length === 1;
  const captureModeLabel = homogeneousCaptureMode ? formatCaptureMode(captureModes[0] ?? "Evidence") : "Mixed evidence";

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          {title}
          <span className="wf-runtime-evidence-count" aria-label={`${snapshots.length} ${title.toLowerCase()}`}>
            {snapshots.length}
          </span>
        </h4>
        <span className="wf-runtime-capture-mode">{captureModeLabel}</span>
      </header>
      <div className={`wf-runtime-input-list${subject === "ActivityOutput" ? " wf-activity-output-list" : ""}`} role="list">
        {snapshots.map((snapshot, index) => (
          <RuntimeValueEvidenceCard
            key={snapshot.evidenceId || `${snapshot.name}:${snapshot.capturedAt}:${snapshot.captureMode}:${index}`}
            snapshot={snapshot}
            compactObjects={subject === "ActivityOutput"}
            showCaptureMode={!homogeneousCaptureMode}
          />
        ))}
      </div>
    </section>
  );
}

