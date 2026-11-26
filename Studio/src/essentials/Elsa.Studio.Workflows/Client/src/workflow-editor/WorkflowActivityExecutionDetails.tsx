import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { getNotifications } from "./notifications";
import { workflowStateEffectLabels } from "../workflowInstanceInspection";
import { Component, lazy, Suspense, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Activity as ActivityIcon, ChevronRight, Info, Undo2 } from "lucide-react";
import { StudioButton, StudioTabPanel, StudioTabs, type StudioTabItem } from "@elsa-workflows/studio-ui";
import type { StudioBackendSession, StudioEndpointContext, StudioExpressionEditorContribution, StudioExpressionSourceRendererContext, StudioWorkflowRunRelatedLinkContribution } from "@elsa-workflows/studio-sdk";
import { WorkflowRunRelatedLinks } from "./WorkflowRunRelatedLinks";
import { getActivityExecutionInspection } from "../api/runtime";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot, ActivityExecutionStateSummary } from "../workflowTypes";
import type { ExecutableGraphNodeFacts } from "../executableGraph";
import { resolveRuntimeActivityLabel as resolveActivityLabel } from "../runtimeActivityDisplay";
import { readPortableExpressionDefinition } from "../activityProperties";
import { isMaskedInput, readSecretReference } from "../maskedInput";
import { isCompensationHandlerExecution } from "../compensationRuntime";
import { buildInputInspectionRows, evaluationPhase, evaluationSequence, inputRuntimeSnapshot, readDeclaredActivityInputs, type InputInspectionRow, type InputInspectionState } from "../inputInspectionRows";
import { formatActivityVersion, formatDate, formatDuration, shortTypeName } from "../workflowFormatting";
import { CopyValueButton } from "./executableShared";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { formatCaptureMode, formatSnapshotPayload, runtimeValueContextId, RuntimeValueEvidenceCard, RuntimeValueEvidenceContent, RuntimeValueEvidenceResolutionProvider } from "./RuntimeValueEvidence";
import "./activityInspection.css";
import type { PreparedAttachedExecution } from "./prepareAttachedExecution";
import { InputRefreshHistory, LostUpdateBadge, VariableWriteDiagnosticsSection } from "./VariableLifecycleEvidence";
import { ExecutionWarningBadge, ExecutionWarningsSection } from "./ExecutionWarnings";
import { countLostUpdates, lostUpdateBadgeLabel } from "../variableLifecycle";
import type { RunHistorySelection } from "../runtimeHistoryTypes";

const ActivityHistoryJournal = lazy(async () => ({ default: (await import("./run-history/HistoryInspection")).ActivityHistoryJournal }));
const ActivityBookmarkHistory = lazy(async () => ({ default: (await import("./run-history/HistoryInspection")).ActivityBookmarkHistory }));
const HistoryInspection = lazy(async () => ({ default: (await import("./run-history/HistoryInspection")).HistoryInspection }));

const ReusableBoundaryInspector = lazy(async () => ({ default: (await import("./ReusableBoundaryInspector")).ReusableBoundaryInspector }));

export type ActivityExecutionDetailView = "overview" | "inputs" | "outputs" | "events" | "bookmarks";
export type ActivityExecutionInitialView = ActivityExecutionDetailView | "journal" | "data";

function normalizeInitialView(view: ActivityExecutionInitialView): ActivityExecutionDetailView {
  if (view === "journal") return "events";
  if (view === "data") return "overview";
  return view;
}

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
  backendSession,
  runRelatedLinks = [],
  focusInputKey = null,
  onSelectEvidence,
  refreshKey = 0,
  compact = false,
  prepared,
  nodeLabelOf,
  initialView = "overview",
  historyRefreshKey,
  onSelectHistory,
  onFocusHistory,
  onOpenIncident,
  navigate
}: {
  prepared?: PreparedAttachedExecution;
  /** The authored label of an executable node, so a lost-update warning names the other writer by name. */
  nodeLabelOf?(executableNodeId: string): string | undefined;
  initialView?: ActivityExecutionInitialView;
  historyRefreshKey?: string | number;
  onSelectHistory?(selection: RunHistorySelection): void;
  onFocusHistory?(activityExecutionId: string): void;
  onOpenIncident?(incidentId: string): void;
  navigate?(path: string): void;
  compact?: boolean;
  refreshKey?: number;
  executionNavigation?: ReactNode;
  childExecutions?: ReactNode;
  context: StudioEndpointContext;
  activity: ActivityExecutionStateSummary | null;
  activityCatalog: ActivityCatalogItem[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors?: StudioExpressionEditorContribution[];
  backendSession?: StudioBackendSession;
  runRelatedLinks?: StudioWorkflowRunRelatedLinkContribution[];
  focusInputKey?: string | null;
  onSelectEvidence?(evidenceId: string): void;
}) {
  useStudioTimeZone();
  const selectedActivityExecutionId = activity?.activityExecutionId ?? null;
  const selectedWorkflowExecutionId = activity?.workflowExecutionId ?? null;
  const selectedContextIdentity = runtimeValueContextId(context);
  const copyScope = `${selectedContextIdentity}:${selectedWorkflowExecutionId ?? ""}:${selectedActivityExecutionId ?? ""}`;
  const preparedInspection = prepared?.contextIdentity === selectedContextIdentity && prepared?.inspection?.workflowExecutionId === selectedWorkflowExecutionId
    && prepared.inspection.activityExecutionId === selectedActivityExecutionId ? prepared.inspection : null;
  const [inspectionState, setInspectionState] = useState<ActivityExecutionInspectionState>({
    activityExecutionId: null,
    workflowExecutionId: null,
    contextIdentity: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  const [copyStatus, setCopyStatus] = useState("");
  const bookmarkRefreshKey = historyRefreshKey ?? JSON.stringify([activity?.status, activity?.completedAt, activity?.bookmarkCount, activity?.bookmarkIds]);
  const historyIdentity = JSON.stringify([selectedContextIdentity, selectedWorkflowExecutionId, selectedActivityExecutionId]);
  const normalizedInitialView = normalizeInitialView(initialView);
  const [inlineHistorySelection, setInlineHistorySelection] = useState<{ identity: string; selection: RunHistorySelection } | null>(null);
  const activeHistorySelection = inlineHistorySelection?.identity === historyIdentity ? inlineHistorySelection.selection : null;
  const selectHistory = (selection: RunHistorySelection) => {
    if (onSelectHistory) onSelectHistory(selection);
    else setInlineHistorySelection({ identity: historyIdentity, selection });
  };
  const [activeDetailTab, setActiveDetailTab] = useState<ActivityExecutionDetailView>(() => focusInputKey ? "inputs" : normalizedInitialView);
  const detailTabsId = useId();

  useEffect(() => {
    if (focusInputKey) {
      setInlineHistorySelection(null);
      setActiveDetailTab("inputs");
    }
  }, [focusInputKey, historyIdentity]);

  const requestedRevision = useRef(refreshKey); requestedRevision.current = refreshKey;
  const refreshInspection = useRef<(() => void) | null>(null);
  useEffect(() => {
    setCopyStatus("");
    if (!selectedActivityExecutionId || !selectedWorkflowExecutionId) {
      setInspectionState({ activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" });
      return;
    }

    const controller = new AbortController();
    const activityExecutionId = selectedActivityExecutionId;
    const workflowExecutionId = selectedWorkflowExecutionId;
    let running = false;
    let confirmed: number | null = preparedInspection ? requestedRevision.current : null;
    if (preparedInspection) setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "ready", inspection: preparedInspection, error: "" });
    const load = async () => {
      if (running || controller.signal.aborted || confirmed === requestedRevision.current) return;
      running = true;
      try {
        do {
          const revision = requestedRevision.current;
          setInspectionState(previous => previous.status === "ready" && previous.activityExecutionId === activityExecutionId
            && previous.workflowExecutionId === workflowExecutionId && previous.contextIdentity === selectedContextIdentity ? previous
            : { activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" });
          try {
            const inspection = await getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId, controller.signal);
            if (controller.signal.aborted) return;
            if (inspection.workflowExecutionId !== workflowExecutionId || inspection.activityExecutionId !== activityExecutionId) {
              setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity,
                status: "failed", inspection: null, error: "Run identity did not match the selected occurrence." });
            } else setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "ready", inspection, error: "" });
          } catch (error) {
            if (controller.signal.aborted) return;
            setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity,
              status: "failed", inspection: null, error: error instanceof Error ? error.message : String(error) });
          }
          confirmed = revision;
        } while (confirmed !== requestedRevision.current);
      } finally { running = false; }
    };
    const start = () => { void load(); }; refreshInspection.current = start; start();
    return () => { controller.abort(); if (refreshInspection.current === start) refreshInspection.current = null; };
  }, [context, preparedInspection, selectedActivityExecutionId, selectedContextIdentity, selectedWorkflowExecutionId]);
  // Live revisions coalesce behind the current exact read; identity changes still cancel it immediately.
  useEffect(() => { refreshInspection.current?.(); }, [refreshKey]);

  const currentInspectionState: ActivityExecutionInspectionState =
    inspectionState.activityExecutionId === selectedActivityExecutionId &&
    inspectionState.workflowExecutionId === selectedWorkflowExecutionId &&
    inspectionState.contextIdentity === selectedContextIdentity
      ? inspectionState
      : preparedInspection ? { activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId, contextIdentity: selectedContextIdentity, status: "ready", inspection: preparedInspection, error: "" }
      : selectedActivityExecutionId && selectedWorkflowExecutionId
        ? { activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" }
        : { activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" };
  const resolutionScope = useMemo(
    () => selectedActivityExecutionId && selectedWorkflowExecutionId
      ? { context, activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId, preparedValues: preparedInspection ? prepared?.values : undefined }
      : null,
    [context, prepared, preparedInspection, selectedActivityExecutionId, selectedWorkflowExecutionId]);

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
  const inputEvaluations = currentInspectionState.status === "ready"
    ? (currentInspectionState.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === "ActivityInput")
    : [];
  const inputRows = buildInputInspectionRows({
    declarations: declaredInputs,
    authoredSources: executableNodeFacts?.authoredInputs ?? [],
    compiledBindings: executableNodeFacts?.inputBindings ?? [],
    evaluations: inputEvaluations,
    authoredSourceAccess: executableNodeFacts?.authoredInputsAccess
  });
  const outputCount = currentInspectionState.status === "ready"
    ? (currentInspectionState.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === "ActivityOutput").length
    : 0;
  const incidentDetails = currentInspectionState.inspection?.incidents;
  const recordedIncidents = Array.isArray(incidentDetails) ? incidentDetails : null;
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
  const showBookmarksTab = (bookmarkCount ?? 0) > 0 || activity.status === "Suspended" || normalizedInitialView === "bookmarks";
  const detailTabs: StudioTabItem[] = [
    { id: "overview", label: "Overview" },
    {
      id: "inputs",
      label: "Inputs",
      trailing: inputRows.length > 0 ? <span className="wf-activity-tab-count" aria-hidden="true">{inputRows.length}</span> : undefined
    },
    {
      id: "outputs",
      label: "Outputs",
      trailing: outputCount > 0 ? <span className="wf-activity-tab-count" aria-hidden="true">{outputCount}</span> : undefined
    },
    { id: "events", label: "Events" },
    ...(showBookmarksTab ? [{
      id: "bookmarks",
      label: "Bookmarks",
      trailing: (bookmarkCount ?? 0) > 0 ? <span className="wf-activity-tab-count" aria-hidden="true">{bookmarkCount}</span> : undefined
    }] : [])
  ];
  const effectiveDetailTab: ActivityExecutionDetailView = detailTabs.some(tab => tab.id === activeDetailTab) ? activeDetailTab : "overview";
  const executionAttempt = currentInspectionState.status === "ready" ? currentInspectionState.inspection?.attempt : null;
  const executionDetailRows = [
    ...(compact ? [
      { label: "Started", value: startedLabel, copiedLabel: "start time" },
      { label: "Elapsed", value: durationLabel, copiedLabel: "duration" },
      { label: "Incidents", value: incidentCount == null ? "Unavailable" : String(incidentCount), copiedLabel: "incident count" }
    ] : []),
    { label: "Activity Execution ID", value: activity.activityExecutionId, copiedLabel: "activity execution ID", code: true },
    { label: "Authored Activity ID", value: activity.authoredActivityId, copiedLabel: "authored activity ID", code: true },
    { label: "Activity type", value: `${activity.activityType}@${activity.activityTypeVersion}`, copiedLabel: "activity type", code: true },
    { label: "Completed", value: completedLabel, copiedLabel: "completion time" },
    { label: "Current bookmarks", value: bookmarkCount == null ? "Unavailable" : String(bookmarkCount), copiedLabel: "bookmark count" },
    ...(executionAttempt ? [{
      label: "Attempt",
      value: `${executionAttempt.attemptNumber} of ${executionAttempt.totalAttempts ?? "?"}`,
      copiedLabel: "attempt",
      code: true
    }] : [])
  ];
  const markCopied = (label: string) => { setCopyStatus(""); getNotifications().notify({ level: "success", title: `Copied ${label}.` }); };
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
              <h4 title={activityLabel}>{activityLabel}</h4>
              {currentInspectionState.status === "ready" ? <LostUpdateBadge count={countLostUpdates(currentInspectionState.inspection?.variableWrites)} label={lostUpdateBadgeLabel(currentInspectionState.inspection?.variableWrites)} /> : null}
              {currentInspectionState.status === "ready" ? <ExecutionWarningBadge warnings={currentInspectionState.inspection?.warnings} /> : null}
              <CopyValueButton
                scope={copyScope}
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
              scope={copyScope}
              value={statusLabel}
              ariaLabel="Copy activity status"
              copiedLabel="activity status"
              onCopied={markCopied}
              onCopyFailed={markCopyFailed}
            />
          </span>
        </div>

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
      <WorkflowRunRelatedLinks context={context} session={backendSession} activity={activity} contributions={runRelatedLinks} refreshKey={refreshKey} />
      {activeHistorySelection ? <Suspense fallback={<p role="status">Loading recorded history…</p>}><HistoryInspection context={context}
        workflowExecutionId={activity.workflowExecutionId} selection={activeHistorySelection} refreshKey={bookmarkRefreshKey} onSelect={selectHistory}
        onBack={() => setInlineHistorySelection(null)} onFocusTimeline={onFocusHistory} /></Suspense> : (
      <RuntimeValueEvidenceResolutionProvider scope={resolutionScope}>
        {currentInspectionState.status === "ready" && currentInspectionState.inspection ? (
          <VariableWriteDiagnosticsSection inspection={currentInspectionState.inspection} labelOf={nodeLabelOf} />
        ) : null}
        {currentInspectionState.status === "ready" && workflowStateEffectLabels(currentInspectionState.inspection?.workflowStateEffects).length > 0 ? (
          <section className="wf-instance-section" aria-label="Committed workflow changes">
            <h4>Committed workflow changes</h4>
            <p>{workflowStateEffectLabels(currentInspectionState.inspection?.workflowStateEffects).join(", ")}</p>
            <small>Later failed or cancelled work adds no committed changes.</small>
            <ul>{currentInspectionState.inspection?.workflowStateEffectReceipts?.map((receipt, index) => (
              <li key={index}>{receipt.transition === "Suspension" ? "Suspension" : "Completion"} accepted <time dateTime={receipt.acceptedAt}>{formatDate(receipt.acceptedAt)}</time>: {workflowStateEffectLabels(receipt.kinds).join(", ")}{receipt.attemptId ? <small> · Attempt {receipt.attemptId}</small> : null}</li>
            ))}</ul>
          </section>
        ) : null}
        {currentInspectionState.status === "ready" ? <ExecutionWarningsSection warnings={currentInspectionState.inspection?.warnings} /> : null}
        <StudioTabs
          baseId={detailTabsId}
          tabs={detailTabs}
          activeTab={effectiveDetailTab}
          onSelect={tab => setActiveDetailTab(tab as ActivityExecutionDetailView)}
          ariaLabel="Activity execution details"
          className="wf-activity-inspection-tabs"
          compact
        />
        <StudioTabPanel baseId={detailTabsId} index={0} hidden={effectiveDetailTab !== "overview"} className="wf-activity-inspection-tab-panel">
          {!compact ? <dl className="wf-activity-summary-grid">
            <ActivityMetadataValue scope={copyScope} label="Started" value={startedLabel} copiedLabel="start time" onCopied={markCopied} onCopyFailed={markCopyFailed} />
            <ActivityMetadataValue scope={copyScope} label="Duration" value={durationLabel} copiedLabel="duration" onCopied={markCopied} onCopyFailed={markCopyFailed} />
            <ActivityMetadataValue scope={copyScope} label="Incidents" value={incidentCount == null ? "Unavailable" : String(incidentCount)} copiedLabel="incident count" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          </dl> : null}
          {currentInspectionState.status === "ready" && (recordedIncidents === null || recordedIncidents.length > 0) ? <section className="wf-instance-section wf-activity-incidents" aria-label="Activity incidents">
            <header className="wf-runtime-evidence-heading">
              <h4>Incidents</h4>
              {recordedIncidents ? <span className="wf-runtime-evidence-count" aria-label={`${recordedIncidents.length} incidents`}>{recordedIncidents.length}</span> : null}
            </header>
            {recordedIncidents === null ? <p>Incident details are unavailable for this execution.</p> : recordedIncidents.length ? <ul>{recordedIncidents.map(incident => <li key={incident.incidentId}>
              <WorkflowStatusBadge status={incident.status} /><p>{incident.message}</p><small>{incident.failureType}</small>
            </li>)}</ul> : <p>No incidents recorded for this execution.</p>}
          </section> : null}
          <WorkflowActivityOutcomeEvidence state={currentInspectionState} />
          {currentInspectionState.status === "ready" && currentInspectionState.inspection?.boundary ? (
            <ActivityBoundaryDetails
              key={JSON.stringify([selectedContextIdentity, selectedWorkflowExecutionId, selectedActivityExecutionId])}
              context={context}
              inspection={currentInspectionState.inspection}
              activityLabel={activityLabel}
            />
          ) : null}
          <section className="wf-instance-section">
            <details className="wf-activity-inspection-disclosure wf-activity-execution-details">
              <summary>
                <ChevronRight size={14} aria-hidden="true" />
                <span>Execution details</span>
                <small>{executionDetailRows.length} values</small>
              </summary>
              <dl className="wf-activity-detail-list">
                {executionDetailRows.map(row => (
                  <ActivityMetadataValue scope={copyScope} key={row.label} {...row} onCopied={markCopied} onCopyFailed={markCopyFailed} />
                ))}
              </dl>
            </details>
          </section>
        </StudioTabPanel>
        <StudioTabPanel baseId={detailTabsId} index={1} hidden={effectiveDetailTab !== "inputs"} className="wf-activity-inspection-tab-panel">
          {effectiveDetailTab !== "events" && effectiveDetailTab !== "bookmarks" ? (
            <WorkflowActivityInputEvidence
              state={currentInspectionState}
              rows={inputRows}
              executableNodeFacts={executableNodeFacts}
              expressionEditors={expressionEditors}
              focusInputKey={focusInputKey}
            />
          ) : null}
        </StudioTabPanel>
        <StudioTabPanel baseId={detailTabsId} index={2} hidden={effectiveDetailTab !== "outputs"} className="wf-activity-inspection-tab-panel">
          {effectiveDetailTab !== "events" && effectiveDetailTab !== "bookmarks" ? (
            <WorkflowActivityValueEvidence
              state={currentInspectionState}
              subject="ActivityOutput"
              title="Outputs"
              loadingText="Loading recorded outputs..."
              failureText="Recorded outputs are unavailable."
              emptyText="No runtime output snapshots were recorded for this execution."
            />
          ) : null}
        </StudioTabPanel>
        <StudioTabPanel baseId={detailTabsId} index={3} hidden={effectiveDetailTab !== "events"} className="wf-activity-inspection-tab-panel wf-activity-inspection-tab-panel--flush">
          {effectiveDetailTab === "events" ? (
            <Suspense fallback={<p className="wf-instance-note" role="status">Loading activity events…</p>}>
              <ActivityHistoryJournal
                key={historyIdentity}
                context={context}
                workflowExecutionId={activity.workflowExecutionId}
                activityExecutionId={activity.activityExecutionId}
                refreshKey={bookmarkRefreshKey}
                onSelectHistory={selectHistory}
                onOpenTimeline={onFocusHistory}
                onActivity={onSelectEvidence}
                onIncident={onOpenIncident}
                navigate={navigate}
              />
            </Suspense>
          ) : null}
        </StudioTabPanel>
        {showBookmarksTab ? (
          <StudioTabPanel baseId={detailTabsId} index={4} hidden={effectiveDetailTab !== "bookmarks"} className="wf-activity-inspection-tab-panel wf-activity-inspection-tab-panel--flush">
            {effectiveDetailTab === "bookmarks" ? (
              <Suspense fallback={<p className="wf-instance-note" role="status">Loading recorded bookmarks…</p>}>
                <ActivityBookmarkHistory
                  key={historyIdentity}
                  context={context}
                  workflowExecutionId={activity.workflowExecutionId}
                  activityExecutionId={activity.activityExecutionId}
                  refreshKey={bookmarkRefreshKey}
                  onSelect={selectHistory}
                />
              </Suspense>
            ) : null}
          </StudioTabPanel>
        ) : null}
      </RuntimeValueEvidenceResolutionProvider>
      )}
    </>
  );
}

function ActivityBoundaryDetails({ context, inspection, activityLabel }: { context: StudioEndpointContext; inspection: ActivityExecutionInspection; activityLabel: string }) {

  const [open, setOpen] = useState(false);

  return (
    <section className="wf-instance-section wf-activity-boundary-details">
      <details className="wf-activity-inspection-disclosure" onToggle={event => setOpen(event.currentTarget.open)}>
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
  const outcomeNames = state.status === "ready" ? state.inspection?.outcomeNames ?? [] : [];
  const presentation = state.status === "ready"
    ? outcomeNames.length > 0 ? "recorded" : "empty"
    : state.status === "loading" ? "loading" : "unavailable";

  return (
    <section className="wf-instance-section wf-activity-outcomes" data-state={presentation}>
      <h4 className="wf-activity-outcomes-heading">Outcomes</h4>
      {state.status === "loading" ? <p className="wf-activity-outcome-status">Loading runtime outcomes...</p>
        : state.status === "failed" ? <>
          <p className="wf-activity-outcome-status">Runtime outcomes are unavailable.</p>
          {state.error ? <p className="wf-instance-note wf-activity-outcome-error">{state.error}</p> : null}
        </>
          : outcomeNames.length === 0 ? <p className="wf-activity-outcome-status">No runtime outcomes were recorded for this execution.</p>
            : <div className="wf-activity-outcome-list" role="list">
              {outcomeNames.map((outcomeName, index) => (
                <span className="wf-activity-outcome-chip" role="listitem" key={`${outcomeName}:${index}`}>{outcomeName}</span>
              ))}
            </div>}
    </section>
  );
}

function ActivityMetadataValue({
  scope,
  label,
  value,
  copiedLabel,
  code = false,
  onCopied,
  onCopyFailed
}: {
  scope?: object | string;
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
          scope={scope}
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
  rows,
  executableNodeFacts,
  expressionEditors,
  focusInputKey
}: {
  state: ActivityExecutionInspectionState;
  rows: InputInspectionRow[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors: StudioExpressionEditorContribution[];
  focusInputKey: string | null;
}) {

  if (state.status === "idle") return null;

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          Inputs
          {rows.length > 0 ? <span className="wf-runtime-evidence-count" aria-label={`${rows.length} inputs`}>{rows.length}</span> : null}
        </h4>
        <span className="wf-input-inspection-caption">Runtime values</span>
      </header>
      {state.status === "loading" ? <p>Loading recorded inputs...</p> : null}
      {state.status === "failed" ? (
        <>
          <p>Recorded inputs are unavailable. Authored source remains independently inspectable where permitted.</p>
          {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
        </>
      ) : null}
      {rows.length === 0 && state.status === "ready" ? <p>No declared inputs, pinned bindings, or recorded inputs are available for this execution.</p> : null}
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
      {state.status === "ready" ? <InputRefreshHistory refreshes={state.inspection?.inputRefreshes} /> : null}
    </section>
  );
}

function sourceValueFallback(_row: InputInspectionRow, source: InputInspectionRow["authoredSource"], sourceProtected: boolean) {
  if (sourceProtected || !source || !source.expressionType) return false;
  const portableDefinition = readPortableExpressionDefinition(source.value, source.expressionType);
  return portableDefinition?.source ?? source.value;
}

function AuthoredSourceValueFallback({ source }: { source: InputInspectionRow["authoredSource"] }) {
  const expressionType = source?.expressionType || "Unknown";
  const portableDefinition = source ? readPortableExpressionDefinition(source.value, expressionType) : null;
  const sourceValue = portableDefinition?.source ?? source?.value;
  const text = formatSnapshotPayload(sourceValue);
  const bounded = text.length > 4_000 ? `${text.slice(0, 3_997)}...` : text;

  return (
    <div className="wf-input-inspection-authored-fallback">
      <small>Authored value</small>
      {bounded.includes("\n") || bounded.length > 160 ? <pre>{bounded}</pre> : <code>{bounded}</code>}
    </div>
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
  useStudioTimeZone();
  const latest = row.latestEvaluation;
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
  const rowRef = useRef<HTMLDivElement>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);

  const focusDetailsButton = () => rowRef.current?.querySelector<HTMLButtonElement>(".wf-input-inspection-info")?.focus();
  const closeDetailsOnEscape = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key !== "Escape" || event.defaultPrevented || !detailsOpen) return;
    event.preventDefault();
    event.stopPropagation();
    setDetailsOpen(false);
    focusDetailsButton();
  };

  useEffect(() => {
    if (!focusInput) return;
    setDetailsOpen(true);
    const frame = requestAnimationFrame(() => {
      const button = rowRef.current?.querySelector<HTMLButtonElement>(".wf-input-inspection-info");
      button?.focus();
      button?.scrollIntoView?.({ block: "nearest" });
    });
    return () => cancelAnimationFrame(frame);
  }, [focusInput, row.rowKey]);

  const runtimeSnapshot = inputRuntimeSnapshot(row);

  return (
    <div ref={rowRef} className="wf-runtime-input wf-input-inspection-row" role="listitem">
      <div className="wf-input-inspection-value-row">
        <div className="wf-input-inspection-identity">
          <strong className="wf-input-inspection-name">{row.name}</strong>
        </div>
        <div className="wf-input-inspection-value" aria-label={`${row.name} runtime value`}>
          {runtimeSnapshot ? <RuntimeValueEvidenceContent snapshot={runtimeSnapshot} presentation="input" /> : (
            inspectionStatus === "loading" ? <p className="wf-input-inspection-unavailable">Loading captured value…</p>
              : inspectionStatus === "failed" ? <p className="wf-input-inspection-unavailable">Run data unavailable</p>
              : sourceValueFallback(row, shownSource, sourceProtected)
                ? <AuthoredSourceValueFallback source={shownSource} />
                : <p className="wf-input-inspection-unavailable">No evaluation data recorded</p>
          )}
        </div>
        <StudioButton
          variant="ghost"
          size="sm"
          className="wf-input-inspection-info"
          aria-label={`Technical details for ${row.name}`}
          title={`Technical details for ${row.name}`}
          aria-expanded={detailsOpen}
          aria-controls={regionId}
          onClick={() => setDetailsOpen(open => !open)}
          onKeyDown={closeDetailsOnEscape}
        >
          <Info size={16} aria-hidden="true" />
        </StudioButton>
      </div>
      <div
        id={regionId}
        role="region"
        aria-label={`${row.name} technical details`}
        className="wf-input-inspection-content"
        hidden={!detailsOpen}
        onKeyDown={closeDetailsOnEscape}
      >
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
        <section className="wf-input-inspection-detail" aria-label={`${row.name} run data`}>
          <h5>Capture details</h5>
          <dl className="wf-input-inspection-metadata">
            {[
              ["Type", row.declaredType || "Unknown type"],
              ...(latest ? [
                ["Capture", formatCaptureMode(latest.captureMode)],
                ["Captured", formatDate(latest.capturedAt)],
                ...(evaluationPhase(latest) ? [["Phase", formatCaptureMode(evaluationPhase(latest)!)]] : []),
                ...(typeof evaluationSequence(latest) === "number" ? [["Sequence", String(evaluationSequence(latest))]] : [])
              ] : [])
            ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
          </dl>
          {!latest ? <p>No runtime evaluation was recorded.</p> : null}
          {row.states.length > 0 ? <p className="wf-instance-note">{row.states.map(formatInputInspectionState).join(" · ")}</p> : null}
          {row.evaluations.length > 1 ? <InputEvaluationHistory evaluations={row.evaluations} /> : null}
        </section>
      </div>
    </div>
  );
}

function InputEvaluationHistory({ evaluations }: { evaluations: ActivityExecutionInspectionValueSnapshot[] }) {
  useStudioTimeZone();
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
    noEvaluation: "No evaluation data recorded",
    orphanEvidence: "Unmatched run data",
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
  const captureModeLabel = homogeneousCaptureMode ? formatCaptureMode(captureModes[0] ?? "Run data") : "Mixed capture modes";

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

