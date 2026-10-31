import { loadExecutionScope, activityExecutionSummaryFromInspection, type IncidentActivityInspection } from "./executionScope";
export { activityExecutionSummaryFromInspection } from "./executionScope";
import { WorkflowActivityExecutionDetails } from "./WorkflowActivityExecutionDetails";
export { WorkflowActivityExecutionDetails } from "./WorkflowActivityExecutionDetails";
import "./activityInspection.css";
import { latestActivityExecution } from "../runtimeActivityOrder";
import { useScopedCanvasActivities } from "./scopedExecutionEvidence";
import { browseActivityExecutions, supportsActivityExecutionBrowsing, type ActivityExecutionBrowseEntry } from "../api/runtime";
import { createCursorHistory, getPreviousCursor, moveCursorHistoryNext, moveCursorHistoryPrevious, reconcileCursorHistory } from "./cursorHistory";
import { lazy, Suspense, useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { applyNodeChanges, ReactFlow, Background, Controls, MiniMap, type Edge, type Node, type NodeChange, type ReactFlowInstance } from "@xyflow/react";
import { Activity as ActivityIcon, AlertCircle, Boxes, ChevronLeft, ChevronRight, ListTree, Maximize2, Minimize2, RotateCcw, SlidersHorizontal, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityInputDescriptor, StudioAgentRegistry, StudioEndpointContext, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { listActivities } from "../api/activityDesign";
import { deleteWorkflowInstance, getActivityExecutionInspection, getExecutable, getExecutableInputSources, listWorkflowInstances, supportsActivityExecutionInspection, supportsWorkflowInstanceDeletion, supportsWorkflowInstanceHealthFilter, WorkflowInstanceHealthFilterUnavailableError, type WorkflowInstanceListPage } from "../api/runtime";
import { formatNamedIdentityTitle, loadDefinitionDisplayNames, resolveNamedIdentity } from "./definitionDisplayNames";
import type { ActivityCatalogItem, ActivityExecutionLayout, ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowDefinitionVersionDetails, WorkflowExecutableDetails, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import { formatActivitySummary } from "../activitySummary";
import { resolveRuntimeActivityLabel as resolveActivityLabel } from "../runtimeActivityDisplay";
import {
  buildCanvas,
  buildUnsupportedActivityCanvas,
  findActivityNode,
  findNodeScopePath,
  getActivityDesignerSupport,
  getChildSlots,
  planSlotNavigation,
  resolveScope,
  slotCrumbLabel,
  type ChildSlot,
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "../workflowAdapter";
import { applyRuntimeOverlays, isActiveIncident } from "../workflowRuntimeOverlays";
import { buildExecutableActivityGraph, findExecutableGraphNodeId, findExecutableNodeFacts, type ExecutableActivityGraph, type ExecutableGraphNodeFacts } from "../executableGraph";
import { formatDate, formatDuration, resolutionOutcomeLabel, shortTypeName } from "../workflowFormatting";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { PanelTabList } from "./PanelTabList";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { WorkflowInstanceInspectionFacts, WorkflowInstanceInspectionNote } from "./WorkflowInstanceInspectionFacts";
import { nodeTypes, edgeTypes } from "./graph";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { ReadOnlyWorkflowSequence } from "./ReadOnlyWorkflowSequence";
import { decorateExecutionMotion, useConfirmedExecutionMotion } from "./executionMotion";
import { CompensationScopeDesigner, type CompensationOperatorControls } from "../graph-authoring/CompensationScopeDesigner";
import { compensationStepsSlotId } from "../compensationScope";
import { deriveCompensationRuntime, type CompensationJournalSource, type CompensationScopeRuntime } from "../compensationRuntime";
import { useCompensationJournals, useOperatorActions, type CompensationJournals, type OperatorActionsApi } from "./useOperatorActions";
import { canReviewDefinition, IncidentNextStep, useDefinitionReviewAccess, type DefinitionAccessObservation } from "./IncidentNextStep";
import { writeRunSelection } from "./runSelectionUrl";
import { buildBpmnCanvas, type BpmnNodeData } from "../bpmn/bpmnAdapter";
import { CopyValueButton } from "./executableShared";
import { useDialogFocus } from "./useDialogFocus";
import { getDialogs } from "./dialogs";
import { isTerminalExecution, readObservedExecution, useExecutionObservation, type ExecutionObservationStatus } from "./useExecutionObservation";
export { formatSnapshotPayload } from "./RuntimeValueEvidence";
import type { InstanceInspectorTab, WorkflowEditorPanelTab, WorkflowInstanceInspectionData } from "./editorTypes";
import {
  activityNodeKey,
  dispatchAiAction,
  findAiAction,
  formatRunKind,
  formatWorkflowRunLoadError
} from "./editorHelpers";
import { runDetailMinInspectorWidth, useRunDetailLayout, type RunDetailLayoutMode } from "./useRunDetailLayout";
import { activateWorkflowCanvasNode, decorateWorkflowCanvasElements } from "./workflowAccessibility";
import { AdvancedSection, CopyableIdentifier, OverflowMenu, StudioPageHeader, StudioButton } from "@elsa-workflows/studio-ui";
import { RunWorkflowPicker } from "./RunWorkflowPicker";
import { WorkflowListIdentifiers } from "./WorkflowListIdentifiers";
import "./cleanListTables.css";

type WorkflowRunFlowInstance = ReactFlowInstance<Node<WorkflowNodeData>, Edge<WorkflowEdgeData>>;

const runHistoryPageSizes = [10, 25, 50, 100] as const;
const ActivityExecutionInspectionPanel = lazy(async () => ({ default: (await import("./ActivityExecutionBrowser")).ActivityExecutionInspectionPanel }));
const ExecutionScopeNavigator = lazy(async () => ({ default: (await import("./ActivityExecutionBrowser")).ExecutionScopeNavigator }));
const HistoricalBoundaryLayout = lazy(async () => ({ default: (await import("./ReusableBoundaryInspector")).HistoricalBoundaryLayout }));

interface RunHistoryFilters {
  status: string;
  runKind: string;
  definitionId: string;
  workflowExecutionId: string;
  artifactId: string;
  correlationId: string;
  from: string;
  to: string;
  incidentHealth: "" | "active" | "blocking" | "none";
}

interface RunHistoryLocation {
  filters: RunHistoryFilters;
  pageSize: number;
  cursor: string | null;
}

const emptyRunHistoryPage: WorkflowInstanceListPage = {
  items: [],
  previousCursor: null,
  nextCursor: null,
  hasPrevious: false,
  hasNext: false,
  count: 0,
  totalCount: 0
};

export function WorkflowInstances({ context, navigate }: {
  context: StudioEndpointContext;
  navigate(path: string): void;
}) {
  const healthFilterHelpId = useId();
  const [location, setLocation] = useState<RunHistoryLocation>(readRunHistoryLocation);
  const [draftFilters, setDraftFilters] = useState<RunHistoryFilters>(location.filters);
  const [state, setState] = useState<"loading" | "ready" | "failed" | "unsupported">("loading");
  const [error, setError] = useState("");
  const [page, setPage] = useState<WorkflowInstanceListPage>(emptyRunHistoryPage);
  const [definitionNames, setDefinitionNames] = useState<Map<string, string>>(() => new Map());
  const [healthFilterSupported, setHealthFilterSupported] = useState<boolean | null>(null);
  const [deleteCapability, setDeleteCapability] = useState<{ context: StudioEndpointContext; supported: boolean } | null>(null);
  const [selectedRunIds, setSelectedRunIds] = useState<Set<string>>(() => new Set());
  const [deletionPhase, setDeletionPhase] = useState<"idle" | "confirming" | "deleting">("idle");
  const deletingRuns = deletionPhase !== "idle";
  const deletionTrigger = useRef<{ context: StudioEndpointContext; element: HTMLElement | null } | null>(null);
  const refreshButton = useRef<HTMLButtonElement>(null);
  const [deleteFeedback, setDeleteFeedback] = useState<{ message: string; failures: Array<{ id: string; message: string }> } | null>(null);
  const requestSequence = useRef(0);
  const definitionNamesAbort = useRef<AbortController | null>(null);
  const deletionSequence = useRef(0);
  const deletionController = useRef<AbortController | null>(null);
  const deletionScope = useRef({ context, location });
  deletionScope.current = { context, location };
  const navigateRef = useRef(navigate);
  navigateRef.current = navigate;
  const selectAllRef = useRef<HTMLInputElement>(null);
  const cursorScope = { backend: context, query: buildRunHistoryUrl({ ...location, cursor: null }) };
  const cursorHistory = useRef(createCursorHistory(cursorScope, location.cursor));
  const previousPage = getPreviousCursor(cursorHistory.current, cursorScope, location.cursor, page);
  const canFilterByHealth = healthFilterSupported === true;
  const canDeleteRuns = deleteCapability?.context === context && deleteCapability.supported;
  const visibleSelectedCount = page.items.reduce((count, instance) => count + Number(selectedRunIds.has(instance.workflowExecutionId)), 0);
  const allVisibleSelected = page.items.length > 0 && visibleSelectedCount === page.items.length;
  const someVisibleSelected = visibleSelectedCount > 0 && !allVisibleSelected;

  useEffect(() => {
    const trigger = deletionTrigger.current;
    if (deletingRuns || !trigger) return;
    deletionTrigger.current = null;
    if (trigger.context !== context) return;
    const element = trigger.element?.isConnected && trigger.element.tagName === "BUTTON" ? trigger.element : refreshButton.current;
    element?.focus();
  }, [context, deletingRuns]);

  useEffect(() => {
    if (selectAllRef.current) selectAllRef.current.indeterminate = someVisibleSelected;
  }, [someVisibleSelected]);

  useEffect(() => {
    setSelectedRunIds(new Set());
    setDeleteFeedback(null);
    setDeletionPhase("idle");
    deletionSequence.current += 1;
    deletionController.current?.abort();
    deletionController.current = null;
    return () => {
      deletionSequence.current += 1;
      deletionController.current?.abort();
      deletionController.current = null;
    };
  }, [context]);

  const load = useCallback(async () => {
    const requestId = ++requestSequence.current;
    definitionNamesAbort.current?.abort();
    setState("loading");
    setError("");
    try {
      const filters = location.filters;
      const [supportsHealthFilter, supportsDeletion] = await Promise.all([
        supportsWorkflowInstanceHealthFilter(context),
        supportsWorkflowInstanceDeletion(context)
      ]);
      if (requestId !== requestSequence.current) return;
      setHealthFilterSupported(supportsHealthFilter);
      setDeleteCapability({ context, supported: supportsDeletion });
      if (filters.incidentHealth && !supportsHealthFilter) throw new WorkflowInstanceHealthFilterUnavailableError();
      const nextPage = await listWorkflowInstances(context, {
        status: valueOrUndefined(filters.status),
        runKind: valueOrUndefined(filters.runKind),
        definitionId: valueOrUndefined(filters.definitionId),
        workflowExecutionId: valueOrUndefined(filters.workflowExecutionId),
        artifactId: valueOrUndefined(filters.artifactId),
        correlationId: valueOrUndefined(filters.correlationId),
        from: toUtcIso(filters.from),
        to: toUtcIso(filters.to),
        incidentHealth: filters.incidentHealth || undefined,
        take: location.pageSize,
        cursor: location.cursor ?? undefined
      });
      if (requestId !== requestSequence.current) return;
      if (location.cursor && nextPage.items.length === 0) {
        const firstLocation = { ...location, cursor: null };
        cursorHistory.current = reconcileCursorHistory(cursorHistory.current, {
          backend: context,
          query: buildRunHistoryUrl({ ...firstLocation, cursor: null })
        }, null);
        setSelectedRunIds(new Set());
        setState("loading");
        setLocation(firstLocation);
        navigateRef.current(buildRunHistoryUrl(firstLocation));
        return;
      }
      const visibleIds = new Set(nextPage.items.map(instance => instance.workflowExecutionId));
      setSelectedRunIds(current => new Set([...current].filter(id => visibleIds.has(id))));
      setPage(nextPage);
      setState("ready");
      definitionNamesAbort.current?.abort();
      const namesAbort = new AbortController();
      definitionNamesAbort.current = namesAbort;
      void loadDefinitionDisplayNames(context, [
        ...nextPage.items.map(item => item.definitionId),
        ...(filters.definitionId ? [filters.definitionId] : [])
      ], namesAbort.signal).then(names => {
        if (requestId !== requestSequence.current) return;
        setDefinitionNames(names);
      });
    } catch (e) {
      if (requestId !== requestSequence.current) return;
      setError(e instanceof Error ? e.message : String(e));
      setPage(emptyRunHistoryPage);
      setState(e instanceof WorkflowInstanceHealthFilterUnavailableError ? "unsupported" : "failed");
    }
  }, [context, location]);

  useEffect(() => {
    void load();
    return () => {
      requestSequence.current += 1;
      definitionNamesAbort.current?.abort();
    };
  }, [load]);

  useEffect(() => {
    const restoreLocation = () => {
      const next = readRunHistoryLocation();
      setSelectedRunIds(new Set());
      setDeleteFeedback(null);
      setDeletionPhase("idle");
      deletionSequence.current += 1;
      deletionController.current?.abort();
      deletionController.current = null;
      setLocation(next);
      setDraftFilters(next.filters);
    };
    window.addEventListener("popstate", restoreLocation);
    return () => window.removeEventListener("popstate", restoreLocation);
  }, []);

  const updateLocation = (next: RunHistoryLocation, syncDraft = true) => {
    setSelectedRunIds(new Set());
    setDeleteFeedback(null);
    setDeletionPhase("idle");
    deletionSequence.current += 1;
    deletionController.current?.abort();
    deletionController.current = null;
    cursorHistory.current = reconcileCursorHistory(cursorHistory.current, {
      backend: context, query: buildRunHistoryUrl({ ...next, cursor: null })
    }, next.cursor);
    setState("loading");
    setLocation(next);
    if (syncDraft) setDraftFilters(next.filters);
    navigate(buildRunHistoryUrl(next));
  };

  const applyFilters = () => updateLocation({
    filters: normalizeRunHistoryFilters({ ...draftFilters, definitionId: location.filters.definitionId }),
    pageSize: location.pageSize,
    cursor: null
  });

  const clearFilters = () => updateLocation({
    filters: emptyRunHistoryFilters(),
    pageSize: location.pageSize,
    cursor: null
  });

  const updateAppliedFilter = <K extends keyof RunHistoryFilters,>(name: K, value: RunHistoryFilters[K]) => {
    setDraftFilters(current => ({ ...current, [name]: value }));
    updateLocation({
      filters: { ...location.filters, [name]: value },
      pageSize: location.pageSize,
      cursor: null
    }, false);
  };

  const openInstance = (workflowExecutionId: string) => {
    const returnTo = new URLSearchParams({ returnTo: buildRunHistoryUrl(location) }).toString();
    navigate(`/workflows/instances/${encodeURIComponent(workflowExecutionId)}?${returnTo}`);
  };

  const toggleRunSelection = (workflowExecutionId: string, selected: boolean) => {
    setSelectedRunIds(current => {
      const next = new Set(current);
      if (selected) next.add(workflowExecutionId);
      else next.delete(workflowExecutionId);
      return next;
    });
  };

  const toggleVisibleRunSelection = (selected: boolean) => {
    setSelectedRunIds(current => {
      const next = new Set(current);
      for (const instance of page.items) {
        if (selected) next.add(instance.workflowExecutionId);
        else next.delete(instance.workflowExecutionId);
      }
      return next;
    });
  };

  const deleteRuns = async (workflowExecutionIds: string[]) => {
    if (!canDeleteRuns || deletingRuns || workflowExecutionIds.length === 0) return;
    const ids = [...new Set(workflowExecutionIds)];
    const title = ids.length === 1 ? `Delete workflow run ${ids[0]}?` : `Delete these ${ids.length} workflow runs?`;
    const confirmation = `${ids.join("\n")}\n\nActive runs will be stopped before deletion. This action cannot be undone.`;
    const operationId = ++deletionSequence.current;
    const operationContext = context;
    const operationLocation = location;
    const controller = new AbortController();
    deletionController.current = controller;
    const isCurrentOperation = () => deletionSequence.current === operationId
      && deletionScope.current.context === operationContext
      && deletionScope.current.location === operationLocation;
    deletionTrigger.current = { context, element: document.activeElement instanceof HTMLElement ? document.activeElement : null };
    setDeletionPhase("confirming");
    const confirmed = await getDialogs().confirm({ title, message: confirmation, confirmLabel: "Delete", tone: "danger" });
    if (!isCurrentOperation()) return;
    if (!confirmed) {
      if (deletionController.current === controller) deletionController.current = null;
      setDeletionPhase("idle");
      return;
    }
    setDeletionPhase("deleting");
    setDeleteFeedback(null);

    const results = await mapWithConcurrency(ids, 3, async id => {
      if (controller.signal.aborted) return { id, deleted: false, message: "Deletion was interrupted." };
      try {
        await deleteWorkflowInstance(operationContext, id, controller.signal);
        return { id, deleted: true, message: "" };
      } catch (error) {
        return { id, deleted: false, message: error instanceof Error ? error.message : String(error) };
      }
    });
    if (!isCurrentOperation()) return;
    if (deletionController.current === controller) deletionController.current = null;

    const successfulIds = results.filter(result => result.deleted).map(result => result.id);
    const failures = results.filter(result => !result.deleted).map(result => ({ id: result.id, message: result.message }));
    setSelectedRunIds(current => {
      const next = new Set(current);
      for (const id of successfulIds) next.delete(id);
      for (const { id } of failures) next.add(id);
      return next;
    });
    setDeleteFeedback({
      message: `${successfulIds.length ? `${formatRunCount(successfulIds.length)} deleted.` : "No workflow runs were deleted."}${failures.length ? ` ${formatRunCount(failures.length)} could not be deleted. Review the errors below before retrying.` : ""}`,
      failures
    });
    setDeletionPhase("idle");
    if (successfulIds.length > 0) void load();
  };

  const instances = page.items;
  const appliedFilterChips = buildRunFilterChips(location.filters, definitionNames);
  const advancedAppliedCount = countAdvancedRunFilters(location.filters);
  const activeFilterCount = appliedFilterChips.length;
  const hasDraftFilters = Object.values(draftFilters).some(Boolean);

  return (
    <>
      <StudioPageHeader title="Runs" headingLevel={2} description="Inspect workflow runs, their progress, and execution evidence." actions={<StudioButton ref={refreshButton} onClick={() => void load()}>Refresh</StudioButton>} />
      <form className="wf-run-filters" aria-label="Workflow run filters" onSubmit={event => {
        event.preventDefault();
        applyFilters();
      }}>
        <div className="studio-collection-toolbar">
          <div className="wf-run-primary-controls" aria-label="Primary workflow run filters">
            <button
              type="button"
              aria-pressed={location.filters.incidentHealth === "active"}
              aria-describedby={healthFilterSupported === false ? healthFilterHelpId : undefined}
              disabled={healthFilterSupported === false && location.filters.incidentHealth !== "active"}
              onClick={() => updateAppliedFilter("incidentHealth", location.filters.incidentHealth === "active" ? "" : "active")}
            >Needs attention</button>
            <button
              type="button"
              aria-pressed={location.filters.status === "Running"}
              onClick={() => updateAppliedFilter("status", location.filters.status === "Running" ? "" : "Running")}
            >Running</button>
            {healthFilterSupported === false ? <small id={healthFilterHelpId}>Current incident health filtering is not available from this host.</small> : null}
          </div>
          <RunWorkflowPicker context={context} definitionId={location.filters.definitionId} onChange={definitionId => updateAppliedFilter("definitionId", definitionId)} />
          <button type="button" onClick={clearFilters} disabled={activeFilterCount === 0 && !hasDraftFilters}>Clear filters</button>
        </div>
        {appliedFilterChips.length > 0 ? (
          <div className="wf-run-applied-filters" aria-label="Applied workflow run filters">
            {appliedFilterChips.map(chip => (
              <span className="wf-run-filter-chip" key={chip.name}>
                <span>{chip.label}</span>
                <button type="button" aria-label={`Remove ${chip.removeLabel} filter`} onClick={() => updateAppliedFilter(chip.name, "")}>×</button>
              </span>
            ))}
          </div>
        ) : null}
        <AdvancedSection label="More filters" setCount={advancedAppliedCount} defaultOpen={advancedAppliedCount > 0} allowCollapseWhenSet>
          <div className="wf-toolbar wf-run-advanced-selects">
            <label className="wf-toolbar-field">
              <span>Status</span>
              <select aria-label="Workflow run status" value={draftFilters.status} onChange={event => setDraftFilters(current => ({ ...current, status: event.target.value }))}>
                <option value="">All statuses</option>
                <option value="Pending">Pending</option>
                <option value="Running">Running</option>
                <option value="Suspended">Suspended</option>
                <option value="Completed">Completed</option>
                <option value="Faulted">Faulted</option>
                <option value="Cancelled">Cancelled</option>
              </select>
            </label>
            <label className="wf-toolbar-field">
              <span>Kind</span>
              <select aria-label="Run Kind" value={draftFilters.runKind} onChange={event => setDraftFilters(current => ({ ...current, runKind: event.target.value }))}>
                <option value="">All kinds</option>
                <option value="TestRun">Test Run</option>
                <option value="PublishedRun">Published Run</option>
                <option value="BackgroundWeaverRun">Background Weaver Run</option>
                <option value="Unknown">Unknown / legacy</option>
              </select>
            </label>
            <label className="wf-toolbar-field">
              <span>Incident health</span>
              <select aria-label="Workflow run incident health" value={draftFilters.incidentHealth} disabled={!canFilterByHealth && !draftFilters.incidentHealth} onChange={event => setDraftFilters(current => ({ ...current, incidentHealth: event.target.value as RunHistoryFilters["incidentHealth"] }))}>
                <option value="">All health states</option>
                <option value="active" disabled={!canFilterByHealth}>Active incidents</option>
                <option value="blocking" disabled={!canFilterByHealth}>Needs intervention</option>
                <option value="none" disabled={!canFilterByHealth}>No active incidents</option>
              </select>
            </label>
          </div>
          <div className="wf-run-filter-fields">
            <RunFilterInput label="Execution ID" value={draftFilters.workflowExecutionId} onChange={value => setDraftFilters(current => ({ ...current, workflowExecutionId: value }))} />
            <RunFilterInput label="Artifact" value={draftFilters.artifactId} onChange={value => setDraftFilters(current => ({ ...current, artifactId: value }))} />
            <RunFilterInput label="Correlation" value={draftFilters.correlationId} onChange={value => setDraftFilters(current => ({ ...current, correlationId: value }))} />
            <RunFilterInput label="From" type="datetime-local" value={draftFilters.from} onChange={value => setDraftFilters(current => ({ ...current, from: value }))} />
            <RunFilterInput label="To" type="datetime-local" value={draftFilters.to} onChange={value => setDraftFilters(current => ({ ...current, to: value }))} />
            <div className="wf-run-filter-actions">
              <button type="submit">Apply filters</button>
            </div>
          </div>
        </AdvancedSection>
      </form>
      {state === "failed" ? <WfErrorCard message={error} action={<button type="button" onClick={() => void load()}>Retry</button>} /> : null}
      {state === "unsupported" ? <p className="wf-run-health-unavailable" role="status">{error} No workflow runs were loaded. Clear the incident-health filter to search without it.</p> : null}
      {state === "loading" ? <div role="status" aria-live="polite" aria-label="Loading workflow runs"><WfListSkeleton /></div> : null}
      {canDeleteRuns && state === "ready" && selectedRunIds.size > 0 ? (
        <div className="wf-run-deletion-toolbar" aria-label="Workflow run deletion controls">
          <span aria-live="polite">{formatRunCount(selectedRunIds.size)} selected</span>
          <button type="button" disabled={selectedRunIds.size === 0 || deletingRuns} onClick={() => void deleteRuns([...selectedRunIds])}>
            {deletionPhase === "deleting" ? "Deleting…" : "Delete selected"}
          </button>
          <button type="button" disabled={selectedRunIds.size === 0 || deletingRuns} onClick={() => setSelectedRunIds(new Set())}>
            Clear selection
          </button>
        </div>
      ) : null}
      {canDeleteRuns && deleteFeedback ? (
        <div className="wf-run-deletion-feedback" role={deleteFeedback.failures.length ? "alert" : "status"}>
          <p>{deleteFeedback.message}</p>
          {deleteFeedback.failures.length ? (
            <ul>
              {deleteFeedback.failures.map(failure => <li key={failure.id}><code>{failure.id}</code>: {failure.message}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
      {state === "ready" && instances.length === 0 ? (
        <div role="status" aria-live="polite">
          <WfEmptyState
            icon={<Boxes size={22} />}
            title={activeFilterCount ? "No workflow runs match these filters" : "No workflow runs yet"}
            description={activeFilterCount
              ? "Clear or adjust the operational filters to broaden the result set."
              : "Run a published workflow executable to create execution history here."}
          />
        </div>
      ) : null}
      {state === "ready" && instances.length > 0 ? (
        <div className={`studio-table-container studio-collection-table wf-grid wf-instance-grid${canDeleteRuns ? " wf-instance-grid--deletable" : ""}`} role="table" aria-label="Workflow runs">
          <div className="wf-grid-head" role="row">
            <span role="columnheader" className="wf-run-column-header">
              {canDeleteRuns ? <label className="wf-run-select-all"><input
                ref={selectAllRef}
                type="checkbox"
                aria-label="Select all workflow runs on this page"
                checked={allVisibleSelected}
                disabled={deletingRuns}
                onChange={event => toggleVisibleRunSelection(event.target.checked)}
              /></label> : null}
              <strong>Run</strong>
            </span>
            <span role="columnheader">Execution ID</span>
            <span role="columnheader">Identifiers</span>
            <span role="columnheader">Kind</span>
            <span role="columnheader">Status</span>
            <span role="columnheader">Health</span>
            <span role="columnheader">Activity</span>
            <span role="columnheader">Started</span>
            <span role="columnheader">Duration</span>
            {canDeleteRuns ? <span role="columnheader">Actions</span> : null}
          </div>
          {instances.map(instance => {
            const workflowName = definitionNames.get(instance.definitionId) ?? instance.definitionId;
            const secondaryIdentifiers = [
              { label: "Artifact ID", value: instance.artifactId },
              { label: "Definition ID", value: instance.definitionId },
              { label: "Definition version ID", value: instance.definitionVersionId }
            ].filter(identifier => identifier.value);
            return (
            <div
              className="wf-grid-row"
              role="row"
              tabIndex={0}
              aria-selected={selectedRunIds.has(instance.workflowExecutionId)}
              aria-label={`Inspect workflow run ${instance.workflowExecutionId} · ${workflowName} · ${instance.status}${instance.subStatus ? ` · ${instance.subStatus}` : ""} · ${formatCurrentIncidentHealth(instance)}`}
              key={instance.workflowExecutionId}
              onClick={event => {
                if ((event.target as HTMLElement).closest("button, input, label")) return;
                openInstance(instance.workflowExecutionId);
              }}
              onKeyDown={event => {
                if (event.target !== event.currentTarget) return;
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  openInstance(instance.workflowExecutionId);
                }
              }}
            >
              <div role="cell" className="wf-run-identity-cell">
                {canDeleteRuns ? <label className="wf-run-select"><input
                  type="checkbox"
                  aria-label={`Select workflow run ${instance.workflowExecutionId}`}
                  checked={selectedRunIds.has(instance.workflowExecutionId)}
                  disabled={deletingRuns}
                  onChange={event => toggleRunSelection(instance.workflowExecutionId, event.target.checked)}
                /></label> : null}
                <div className="wf-run-identity-content">
                  <button
                    type="button"
                    className="wf-run-inspect"
                    aria-label={`Inspect workflow run ${instance.workflowExecutionId} · ${workflowName}`}
                    onClick={() => openInstance(instance.workflowExecutionId)}
                  >
                    <strong>{workflowName}</strong>
                  </button>
                </div>
              </div>
              <span role="cell" className="wf-list-primary-id wf-run-primary-identifier"><CopyableIdentifier label="Execution ID" value={instance.workflowExecutionId} /></span>
              <span role="cell" className="wf-run-secondary-identifiers"><WorkflowListIdentifiers kind="Workflow run" name={instance.workflowExecutionId} identifiers={secondaryIdentifiers}>
                <p>{formatCurrentIncidentHealth(instance)}</p>
                <p>{instance.activityCount} activities · {instance.incidentCount} incidents</p>
              </WorkflowListIdentifiers></span>
              <span role="cell">{formatRunKind(instance.runKind)}</span>
              <span role="cell"><WorkflowStatusBadge status={instance.status} subStatus={instance.subStatus} /></span>
              <span role="cell"><CurrentIncidentHealthLabel instance={instance} /></span>
              <span role="cell" className="wf-run-activity-indicator">
                <span aria-hidden="true" title={`${instance.activityCount} activities · ${instance.incidentCount} incidents`}>{instance.activityCount}</span>
                <span className="wf-visually-hidden">{instance.activityCount} activities · {instance.incidentCount} incidents</span>
              </span>
              <span role="cell">{formatDate(instance.startedAt ?? instance.createdAt)}</span>
              <span role="cell">{formatDuration(instance.startedAt ?? instance.createdAt, instance.completedAt ?? instance.updatedAt)}</span>
              {canDeleteRuns ? <span role="cell" className="wf-run-row-actions wf-row-actions">
                <OverflowMenu ariaLabel={`Actions for workflow run ${instance.workflowExecutionId}`} items={[{
                  id: "delete", label: "Delete", destructive: true, disabled: deletingRuns,
                  onSelect: () => { void deleteRuns([instance.workflowExecutionId]); }
                }]} />
              </span> : null}
            </div>
            );
          })}
        </div>
      ) : null}
      {state === "ready" ? (
        <div className="studio-collection-footer" aria-label="Workflow run pagination">
          <span className="wf-pagination-summary" aria-live="polite">
            Showing {page.count} of {page.totalCount} matching runs
          </span>
          <label className="wf-page-size">
            Rows
            <select aria-label="Workflow run page size" value={location.pageSize} onChange={event => updateLocation({
              filters: location.filters,
              pageSize: Number(event.target.value),
              cursor: null
            }, false)}>
              {runHistoryPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
            </select>
          </label>
          <div className="wf-page-controls">
            <button type="button" disabled={!previousPage.available} onClick={() => {
              const previous = moveCursorHistoryPrevious(cursorHistory.current, cursorScope, location.cursor, page);
              if (!previous.destination.available) return;
              cursorHistory.current = previous.history;
              updateLocation({ ...location, cursor: previous.destination.cursor }, false);
            }} aria-label="Previous workflow run page">
              <ChevronLeft size={14} /> Previous
            </button>
            <span role="status">{page.hasNext ? "More results" : "End of results"}</span>
            <button type="button" disabled={!page.hasNext || !page.nextCursor} onClick={() => {
              const next = moveCursorHistoryNext(cursorHistory.current, cursorScope, location.cursor, page.nextCursor);
              if (!next.destination.available) return;
              cursorHistory.current = next.history;
              updateLocation({ ...location, cursor: next.destination.cursor }, false);
            }} aria-label="Next workflow run page">
              Next <ChevronRight size={14} />
            </button>
          </div>
        </div>
      ) : null}
    </>
  );
}

function RunFilterInput({ label, value, type = "text", onChange }: {
  label: string;
  value: string;
  type?: "text" | "datetime-local";
  onChange(value: string): void;
}) {
  return (
    <label className="wf-run-filter-field">
      <span>{label}</span>
      <input aria-label={`Workflow run ${label.toLowerCase()}`} type={type} value={value} onChange={event => onChange(event.target.value)} />
    </label>
  );
}

function emptyRunHistoryFilters(): RunHistoryFilters {
  return {
    status: "",
    runKind: "",
    definitionId: "",
    workflowExecutionId: "",
    artifactId: "",
    correlationId: "",
    from: "",
    to: "",
    incidentHealth: ""
  };
}

type RunHistoryFilterName = keyof RunHistoryFilters;

interface RunHistoryFilterChip {
  name: RunHistoryFilterName;
  label: string;
  removeLabel: string;
}

function buildRunFilterChips(filters: RunHistoryFilters, definitionNames: Map<string, string>): RunHistoryFilterChip[] {
  const chips: RunHistoryFilterChip[] = [];
  if (filters.status) chips.push({ name: "status", label: `Status: ${filters.status}`, removeLabel: "Status" });
  if (filters.runKind) chips.push({ name: "runKind", label: `Kind: ${formatRunKind(filters.runKind)}`, removeLabel: "Kind" });
  if (filters.definitionId) chips.push({
    name: "definitionId",
    label: `Workflow: ${definitionNames.get(filters.definitionId) ?? filters.definitionId}`,
    removeLabel: "Workflow"
  });
  if (filters.workflowExecutionId) chips.push({ name: "workflowExecutionId", label: `Execution ID: ${filters.workflowExecutionId}`, removeLabel: "Execution ID" });
  if (filters.artifactId) chips.push({ name: "artifactId", label: `Artifact: ${filters.artifactId}`, removeLabel: "Artifact" });
  if (filters.correlationId) chips.push({ name: "correlationId", label: `Correlation: ${filters.correlationId}`, removeLabel: "Correlation" });
  if (filters.from) chips.push({ name: "from", label: `From: ${filters.from}`, removeLabel: "From" });
  if (filters.to) chips.push({ name: "to", label: `To: ${filters.to}`, removeLabel: "To" });
  if (filters.incidentHealth) {
    const label = filters.incidentHealth === "active"
      ? "Needs attention"
      : filters.incidentHealth === "blocking" ? "Needs intervention" : "No active incidents";
    chips.push({ name: "incidentHealth", label, removeLabel: label });
  }
  return chips;
}

function countAdvancedRunFilters(filters: RunHistoryFilters) {
  return Number(Boolean(filters.status) && filters.status !== "Running")
    + Number(Boolean(filters.runKind))
    + Number(Boolean(filters.workflowExecutionId))
    + Number(Boolean(filters.artifactId))
    + Number(Boolean(filters.correlationId))
    + Number(Boolean(filters.from))
    + Number(Boolean(filters.to))
    + Number(Boolean(filters.incidentHealth) && filters.incidentHealth !== "active");
}

function formatRunCount(count: number) {
  return `${count} workflow run${count === 1 ? "" : "s"}`;
}

async function mapWithConcurrency<T, TResult>(
  items: readonly T[],
  maxConcurrency: number,
  mapItem: (item: T) => Promise<TResult>
): Promise<TResult[]> {
  const results = new Array<TResult>(items.length);
  let nextIndex = 0;
  const workerCount = Math.min(items.length, Math.max(1, Math.floor(maxConcurrency)));
  await Promise.all(Array.from({ length: workerCount }, async () => {
    while (true) {
      const index = nextIndex++;
      if (index >= items.length) return;
      results[index] = await mapItem(items[index]);
    }
  }));
  return results;
}

function normalizeRunHistoryFilters(filters: RunHistoryFilters): RunHistoryFilters {
  return Object.fromEntries(Object.entries(filters).map(([key, value]) => [key, value.trim()])) as unknown as RunHistoryFilters;
}

export function readRunHistoryLocation(search = window.location.search): RunHistoryLocation {
  const parameters = new URLSearchParams(search);
  const requestedPageSize = Number(parameters.get("pageSize"));
  return {
    filters: {
      status: parameters.get("status") ?? "",
      runKind: parameters.get("runKind") ?? "",
      definitionId: parameters.get("definitionId") ?? "",
      workflowExecutionId: parameters.get("workflowExecutionId") ?? "",
      artifactId: parameters.get("artifactId") ?? "",
      correlationId: parameters.get("correlationId") ?? "",
      from: parameters.get("from") ?? "",
      to: parameters.get("to") ?? "",
      incidentHealth: parseIncidentHealth(parameters.get("incidentHealth"))
    },
    pageSize: runHistoryPageSizes.includes(requestedPageSize as typeof runHistoryPageSizes[number]) ? requestedPageSize : 25,
    cursor: parameters.get("cursor")
  };
}

export function buildRunHistoryUrl(location: RunHistoryLocation) {
  const parameters = new URLSearchParams();
  Object.entries(location.filters).forEach(([name, value]) => {
    if (value) parameters.set(name, value);
  });
  if (location.pageSize !== 25) parameters.set("pageSize", String(location.pageSize));
  if (location.cursor) parameters.set("cursor", location.cursor);
  const query = parameters.toString();
  return `/workflows/instances${query ? `?${query}` : ""}`;
}

export function parseRunHistoryReturnTo(value: string | null | undefined, origin = window.location.origin): string | null {
  if (!value || value.startsWith("//") || value.includes("\\") || value.includes("#")) return null;
  try {
    const base = new URL(origin);
    const target = new URL(value, base);
    const absolutePath = value.match(/^https?:\/\/[^/?#]+([^?#]*)/i)?.[1] || "/";
    const rawPath = value.startsWith("/") ? value.split(/[?#]/, 1)[0] : absolutePath;
    if (target.origin !== base.origin || target.pathname !== "/workflows/instances" || target.username || target.password || rawPath !== target.pathname) return null;
    if (!value.startsWith("/") && !/^https?:\/\//i.test(value)) return null;
    return buildRunHistoryUrl(readRunHistoryLocation(target.search));
  } catch {
    return null;
  }
}

export function getRunHistoryBackLocation(returnTo: string | null | undefined): string {
  return parseRunHistoryReturnTo(returnTo) ?? "/workflows/instances";
}

function valueOrUndefined(value: string) {
  return value || undefined;
}

function parseIncidentHealth(value: string | null): RunHistoryFilters["incidentHealth"] {
  return value === "active" || value === "blocking" || value === "none" ? value : "";
}

export function formatCurrentIncidentHealth(instance: Pick<WorkflowInstanceSummary, "activeIncidentCount" | "blockingIncidentCount">) {
  if (typeof instance.activeIncidentCount !== "number" || typeof instance.blockingIncidentCount !== "number") {
    return "Current health unavailable";
  }
  if (instance.blockingIncidentCount > 0) {
    return `${instance.activeIncidentCount} active · ${instance.blockingIncidentCount} blocking · Needs intervention`;
  }
  if (instance.activeIncidentCount > 0) {
    return `${instance.activeIncidentCount} active · Non-blocking`;
  }
  return "No active incidents";
}

function CurrentIncidentHealthLabel({ instance }: { instance: WorkflowInstanceSummary }) {
  const available = typeof instance.activeIncidentCount === "number" && typeof instance.blockingIncidentCount === "number";
  const health = !available
    ? "unavailable"
    : instance.blockingIncidentCount! > 0
      ? "blocking"
      : instance.activeIncidentCount! > 0
        ? "active"
        : "none";
  const label = health === "blocking" ? "Needs intervention" : health === "active" ? `${instance.activeIncidentCount} active` : health === "none" ? "Healthy" : "Unknown";
  return <span className="wf-run-health-indicator" data-health={health} title={formatCurrentIncidentHealth(instance)}>
    <span aria-hidden="true">{label}</span>
    <span className="wf-visually-hidden">{formatCurrentIncidentHealth(instance)}</span>
  </span>;
}

function toUtcIso(value: string) {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? undefined : date.toISOString();
}

interface WorkflowInstanceWorkbenchData extends WorkflowInstanceInspectionData {
  definitionName?: string | null;
  executableGraph: ExecutableActivityGraph | null;
  incidentActivityExecutions: ActivityExecutionStateSummary[];
  incidentAssociationLookupIncomplete: boolean;
  incidentActivityLookupSupported: boolean;
  pendingIncidentActivityExecutionIds: string[];
  unavailableIncidentActivityFailures: UnavailableIncidentActivityFailure[];
}

const maxIncidentActivityLookups = 50;
const incidentActivityLookupBatchSize = 8;

export function WorkflowInstanceDetailsWorkbench({ context, ai, expressionEditors = [], workflowExecutionId, initialActivityExecutionId = null, initialInspectorTab = "timeline", initialReturnTo = null, navigate }: {
  context: StudioEndpointContext;
  ai: StudioAgentRegistry;
  expressionEditors?: StudioExpressionEditorContribution[];
  workflowExecutionId: string;
  initialActivityExecutionId?: string | null;
  initialInspectorTab?: InstanceInspectorTab;
  initialReturnTo?: string | null;
  navigate(path: string): void;
}) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [loadedData, setData] = useState<WorkflowInstanceWorkbenchData | null>(null);
  const [selectedEvidenceId, setSelectedEvidenceId] = useState<string | null>(null);
  const [activeInspectorTab, setActiveInspectorTab] = useState<InstanceInspectorTab>(initialInspectorTab);
  const [focusedRuntimeNodeId, setFocusedRuntimeNodeId] = useState<string | null>(null);
  const [canvasFocusRequestId, setCanvasFocusRequestId] = useState(0);
  const [focusInputKey, setFocusInputKey] = useState<string | null>(null);
  const [incidentAssociationMessage, setIncidentAssociationMessage] = useState<string | null>(null);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const appliedInitialInspectorTab = useRef<InstanceInspectorTab | null>(null);
  const appliedInitialActivityExecutionId = useRef<string | null>(null);
  const incidentNavigationRequest = useRef(0);
  const loadGeneration = useRef(0);
  const definitionNameAbort = useRef<AbortController | null>(null);
  const incidentActivityLookupSequence = useRef(0);
  const latestIncidentActivityLookupById = useRef(new Map<string, number>());
  const observedArtifact = useRef<{ identity: string; artifactId: string } | null>(null);
  const observation = useExecutionObservation<WorkflowInstanceDetails>({ context, identity: JSON.stringify(["instance", workflowExecutionId]), enabled: !!workflowExecutionId,
    read: async scope => {
      const pinned = observedArtifact.current?.identity === scope.identity ? observedArtifact.current.artifactId : undefined;
      const details = await readObservedExecution(context, workflowExecutionId, pinned, scope.signal);
      if (scope.isCurrent()) observedArtifact.current = { identity: scope.identity, artifactId: details.instance.artifactId };
      return { value: details, terminal: isTerminalExecution(details.instance.status) };
    }
  });
  const browserContext = observation.identity;
  const [browserEvidence, setBrowserEvidence] = useState<{ context: string; items: ActivityExecutionStateSummary[] }>({ context: browserContext, items: [] });
  const [executionScopes, setExecutionScopes] = useState<{ context: string; items: Array<{ execution: ActivityExecutionStateSummary; label: string }> }>({ context: browserContext, items: [] });
  const [scopeNavigation, setScopeNavigation] = useState<{ context: string; loading: boolean; error: string }>({ context: browserContext, loading: false, error: "" });
  const scopeRequest = useRef<AbortController | null>(null);
  const [boundaryCanvas, setBoundaryCanvas] = useState<{ context: string; layout: ActivityExecutionLayout } | null>(null);
  const requestedScope = useRef<{ execution: ActivityExecutionStateSummary; includeSelf: boolean; label?: string } | null>(null);
  const rememberedExecutions = useRef(new Map<string, ActivityExecutionStateSummary>());
  const loadedBrowserContext = useRef<string | null>(null);
  const loadingBrowserContext = useRef<string | null>(null);
  const failedMetadataContext = useRef<string | null>(null);
  const [metadataRevision, setMetadataRevision] = useState(0);
  const reloadDetailsRequested = useRef(false);
  const data = loadedBrowserContext.current === browserContext && observation.status !== "unavailable" ? loadedData : null;
  const hasLoadedData = loadedData !== null;
  const evidenceRevision = loadGeneration.current + observation.revision;
  const observationIsCurrent = observation.isCurrent;
  const refreshObservation = observation.refresh;
  useEffect(() => () => scopeRequest.current?.abort(), [browserContext]);
  const currentExecutionScopes = executionScopes.context === browserContext ? executionScopes.items : [];
  const executionScope = currentExecutionScopes.at(-1)?.execution;
  const associationActivities = combineActivityExecutions(
    combineActivityExecutions(data?.details.activities ?? [], data?.incidentActivityExecutions ?? []),
    browserEvidence.context === browserContext ? browserEvidence.items : []);
  const selectedActivity = findSelectedActivityExecution(associationActivities, selectedEvidenceId, data?.details.incidents ?? []);
  const {
    containerRef,
    mode,
    inspectorWidth,
    inspectorMaxWidth,
    inspectorCollapsed,
    inspectorMaximized,
    inspectorExpanded,
    canResizeInspector,
    mediumDrawerOpen,
    workbenchClassName,
    workbenchStyle,
    closeInspector,
    openInspector,
    showCanvasForSelection,
    toggleInspectorCollapsed,
    toggleInspectorMaximized,
    startInspectorResize,
    handleInspectorResizeKeyDown
  } = useRunDetailLayout({ selectedActivityId: selectedActivity?.activityExecutionId });
  useEffect(() => {
    if (appliedInitialInspectorTab.current === initialInspectorTab) return;
    appliedInitialInspectorTab.current = initialInspectorTab;
    incidentNavigationRequest.current += 1;
    scopeRequest.current?.abort();
    setScopeNavigation(current => current.context === browserContext && current.loading
      ? { context: browserContext, loading: false, error: "Execution scope navigation was interrupted. Retry the scope." } : current);
    setActiveInspectorTab(initialInspectorTab);
    if (initialInspectorTab === "issues" || initialInspectorTab === "activity") openInspector();
  }, [browserContext, initialInspectorTab, openInspector]);
  const instanceAction = findAiAction(ai, "weaver.workflows.explain-instance");
  const journals = useCompensationJournals(context, workflowExecutionId);

  // Spec 260: re-reads the run's details and incidents while an operator action is applied, without resetting the view.
  const refreshRun = useCallback(async () => {
    if (!workflowExecutionId) return null;
    try {
      const details = await readObservedExecution(context, workflowExecutionId, data?.details.instance.artifactId);
      if (!observationIsCurrent()) return null;
      refreshObservation();
      const supported = await supportsActivityExecutionInspection(context).catch(() => false);
      const summaries = await loadActiveIncidentActivitySummaries(
        details,
        supported ? activityExecutionId => getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId) : null);
      if (!observationIsCurrent()) return null;
      setData(current => loadedBrowserContext.current === browserContext && current && current.details.instance.workflowExecutionId === workflowExecutionId
        ? {
          ...current,
          details,
          incidentActivityExecutions: combineActivityExecutions(current.incidentActivityExecutions, summaries.activities),
          pendingIncidentActivityExecutionIds: summaries.pendingActivityExecutionIds,
          incidentAssociationLookupIncomplete: summaries.incomplete
        }
        : current);
      return details;
    } catch {
      return null;
    }
  }, [browserContext, context, data?.details.instance.artifactId, observationIsCurrent, refreshObservation, workflowExecutionId]);

  const rootActivity = data?.definitionVersion?.state.rootActivity ?? null;
  const labelOfNode = useCallback((nodeId: string | undefined) => {
    if (!nodeId || !data?.definitionVersion?.state.rootActivity) return undefined;
    const node = findActivityNode(data.definitionVersion.state.rootActivity, nodeId);
    if (!node) return undefined;
    return resolveActivityLabel(
      data.definitionVersion.activityPresentation?.find(record => record.nodeId === node.nodeId),
      data.activityCatalog.find(item => item.activityVersionId === node.activityVersionId),
      node.activityVersionId);
  }, [data]);
  const operator = useOperatorActions({
    context,
    workflowExecutionId,
    details: observation.canInteract ? data?.details ?? null : null,
    root: rootActivity,
    activities: associationActivities,
    journals,
    labelOf: labelOfNode,
    refreshRun
  });
  const definitionAccess = useDefinitionReviewAccess(
    context,
    data?.details.instance.definitionId,
    state === "ready" && !!data
  );
  const selectedDefinitionId = data?.details.instance.definitionId;
  const selectedDefinitionCanBeReviewed = canReviewDefinition(definitionAccess, context, selectedDefinitionId);

  // The selected occurrence lives in the URL (spec 260 FR-423): an activity execution, or the one an incident names.
  const selectedUrlActivityId = state !== "ready" || !selectedEvidenceId
    ? null
    : associationActivities.some(activity => activity.activityExecutionId === selectedEvidenceId)
      ? selectedEvidenceId
      : (() => {
        const incident = data?.details.incidents?.find(item => item.incidentId === selectedEvidenceId);
        return incident ? readIncidentActivityExecutionId(incident) ?? null : null;
      })();
  useEffect(() => {
    if (state !== "ready") return;
    writeRunSelection(selectedUrlActivityId);
  }, [selectedUrlActivityId, state]);

  const resetJournals = journals.reset;
  const load = useCallback(async (details: WorkflowInstanceDetails, preserveCanvas = false) => {
    const generation = ++loadGeneration.current;
    failedMetadataContext.current = null;
    scopeRequest.current?.abort();
    setScopeNavigation(current => current.context === browserContext && current.loading
      ? { context: browserContext, loading: false, error: "Execution scope resolution was interrupted by refresh. Retry the scope." } : current);
    resetJournals();
    latestIncidentActivityLookupById.current.clear();
    incidentNavigationRequest.current += 1;
    setIncidentAssociationMessage(null);
    setFocusedRuntimeNodeId(null);
    setFocusInputKey(null);
    if (!workflowExecutionId) {
      setError("No workflow execution id was provided.");
      setState("failed");
      return;
    }

    if (!preserveCanvas) setState("loading");
    setError("");
    try {
      const [executableResult, activityCatalog, sourceResult, incidentActivityResult] = await Promise.all([
        getExecutable(context, details.instance.artifactId, details.instance.sourceReferenceId).then(
          executable => ({ executable, error: "" }),
          error => ({ executable: null, error: error instanceof Error ? error.message : String(error) })
        ),
        listActivities(context),
        details.instance.sourceReferenceId
          ? getExecutableInputSources(context, details.instance.artifactId, details.instance.sourceReferenceId).then(
              sources => ({ sources, access: sources.accessState || sources.access || "allowed" }),
              error => ({ sources: null, access: sourceAccessFromError(error) }))
          : Promise.resolve({ sources: null, access: "unavailable" }),
        supportsActivityExecutionInspection(context).then(
          async supported => {
            const fetchInspection = supported
              ? (activityExecutionId: string) => getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId)
              : null;
            const summaries = await loadActiveIncidentActivitySummaries(details, fetchInspection);
            const requestedActivityId = initialActivityExecutionId?.trim();
            const alreadyLoaded = requestedActivityId && (
              details.activities.some(activity => activity.activityExecutionId === requestedActivityId) ||
              summaries.activities.some(activity => activity.activityExecutionId === requestedActivityId));
            const incidentLookupAlreadyCoversRequest = requestedActivityId && (details.incidents ?? []).some(incident =>
              isActiveIncident(incident) && readIncidentActivityExecutionId(incident) === requestedActivityId);
            if (!fetchInspection || !requestedActivityId || alreadyLoaded || incidentLookupAlreadyCoversRequest) {
              return { supported, ...summaries };
            }

            const requestedActivity = await loadExactIncidentActivitySummary(
              workflowExecutionId,
              requestedActivityId,
              fetchInspection);
            return {
              supported,
              ...summaries,
              activities: requestedActivity.status === "found"
                ? [...summaries.activities, requestedActivity.summary]
                : summaries.activities
            };
          },
          async () => ({ supported: false, ...await loadActiveIncidentActivitySummaries(details, null) }))
      ]);
      if (generation !== loadGeneration.current || !observationIsCurrent()) return;
      const catalog = activityCatalog.activities;
      const executableGraph = executableResult.executable
        ? buildExecutableActivityGraph(
            executableResult.executable.rootActivity,
            catalog,
            sourceResult.sources?.authoredInputs ?? [],
            sourceResult.sources?.compiledInputs ?? [],
            sourceResult.access,
            executableResult.executable.chosenReference?.activityPresentation ?? [])
        : null;
      setData({
        details,
        definitionVersion: executableResult.executable
          ? projectPinnedExecutable(executableResult.executable, details, catalog, executableGraph ?? undefined)
          : null,
        definitionVersionError: executableResult.error,
        activityCatalog: catalog,
        executableGraph,
        incidentActivityExecutions: incidentActivityResult.activities,
        incidentAssociationLookupIncomplete: incidentActivityResult.incomplete,
        incidentActivityLookupSupported: incidentActivityResult.supported,
        pendingIncidentActivityExecutionIds: incidentActivityResult.pendingActivityExecutionIds,
        unavailableIncidentActivityFailures: incidentActivityResult.unavailableFailures
      });
      const initialEvidence = resolveInitialActivityEvidenceId(
        combineActivityExecutions(details.activities, incidentActivityResult.activities),
        initialActivityExecutionId);
      if (loadedBrowserContext.current !== browserContext) {
        setSelectedEvidenceId(initialEvidence);
        setFrames([]);
        loadedBrowserContext.current = browserContext;
      } else {
        setSelectedEvidenceId(current => details.incidents?.some(item => item.incidentId === current) ? initialEvidence : current);
        setBrowserEvidence(current => ({ context: browserContext, items: (current.context === browserContext ? current.items : [])
          .map(item => details.activities.find(fresh => fresh.activityExecutionId === item.activityExecutionId) ?? item) }));
      }
      setState("ready");
      if (initialEvidence) {
        requestAnimationFrame(() => requestAnimationFrame(() => focusSelectedCanvasActivity(initialEvidence)));
      }
      definitionNameAbort.current?.abort();
      const namesAbort = new AbortController();
      definitionNameAbort.current = namesAbort;
      void loadDefinitionDisplayNames(context, [details.instance.definitionId], namesAbort.signal).then(
        definitionNames => {
          if (generation !== loadGeneration.current) return;
          const definitionName = definitionNames.get(details.instance.definitionId);
          if (!definitionName) return;
          setData(current => current ? applyDefinitionDisplayName(current, definitionName) : current);
        },
        () => undefined
      );
    } catch (e) {
      if (generation !== loadGeneration.current) return;
      failedMetadataContext.current = browserContext;
      setData(null);
      setError(formatWorkflowRunLoadError(e, workflowExecutionId));
      setState("failed");
    }
  }, [browserContext, context, initialActivityExecutionId, observationIsCurrent, resetJournals, workflowExecutionId]);

  useEffect(() => {
    return () => {
      loadGeneration.current += 1;
      definitionNameAbort.current?.abort();
    };
  }, [load]);

  useEffect(() => {
    const details = observation.value;
    if (!details) {
      if (!hasLoadedData && observation.error) { setError(formatWorkflowRunLoadError(new Error(observation.error), workflowExecutionId)); setState("failed"); }
      if (!workflowExecutionId) { setError("No workflow execution id was provided."); setState("failed"); }
      return;
    }
    if (loadedBrowserContext.current !== browserContext || reloadDetailsRequested.current) {
      if (failedMetadataContext.current === browserContext && !reloadDetailsRequested.current) return;
      if (loadingBrowserContext.current === browserContext) return;
      const preserveCanvas = loadedBrowserContext.current === browserContext && hasLoadedData;
      reloadDetailsRequested.current = false;
      loadingBrowserContext.current = browserContext;
      void load(details, preserveCanvas).finally(() => {
        if (loadingBrowserContext.current !== browserContext) return;
        loadingBrowserContext.current = null;
        if (reloadDetailsRequested.current) setMetadataRevision(value => value + 1);
      });
      return;
    }
    // Reconcile data without rebuilding the pinned graph, resetting scopes or moving the viewport.
    setData(current => current && current.details.instance.workflowExecutionId === workflowExecutionId && current.details !== details
      ? { ...current, details } : current);
    setBrowserEvidence(current => current.context === browserContext ? { ...current, items: current.items
      .map(item => details.activities.find(fresh => fresh.activityExecutionId === item.activityExecutionId) ?? item) } : current);
  }, [browserContext, hasLoadedData, load, metadataRevision, observation.error, observation.revision, observation.value, workflowExecutionId]);

  const goBack = () => {
    navigate(getRunHistoryBackLocation(initialReturnTo));
  };

  const openDefinitionDesigner = (requestedDefinitionId?: string) => {
    const definitionId = requestedDefinitionId ?? data?.details.instance.definitionId;
    if (!definitionId) return;

    navigate(`/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
  };
  const closeResponsiveInspector = () => {
    beginEvidenceNavigation();
    closeInspector();
    requestAnimationFrame(() => focusSelectedCanvasActivity(focusedRuntimeNodeId ?? selectedEvidenceId));
  };

  const navigateToScope = (nextFrames: ScopeFrame[]) => {
    beginEvidenceNavigation();
    setFrames(nextFrames);
  };

  const focusNodePath = useCallback((nodeId: string | null) => {
    if (!nodeId || !data?.definitionVersion?.state.rootActivity) return;
    const path = findNodeScopePath(
      data.definitionVersion.state.rootActivity,
      nodeId,
      activity => resolveActivityLabel(
        data.definitionVersion?.activityPresentation?.find(record => record.nodeId === activity.nodeId),
        data.activityCatalog.find(item => item.activityVersionId === activity.activityVersionId),
        activity.activityVersionId),
      data.activityCatalog
    );
    if (path) setFrames(path);
  }, [data]);



  const ensureIncidentActivityAssociation = useCallback(async (incident: IncidentStateSummary, isCurrent: () => boolean = () => true) => {
    const generation = loadGeneration.current;
    const activities = combineActivityExecutions(data?.details.activities ?? [], data?.incidentActivityExecutions ?? []);
    const existing = resolveIncidentActivityAssociation(incident, activities, data?.executableGraph);
    if (existing?.activityExecution) {
      if (isCurrent()) setIncidentAssociationMessage(null);
      return existing;
    }
    const activityExecutionId = readIncidentActivityExecutionId(incident);
    if (!activityExecutionId || !data) return existing;
    const lookupRequest = ++incidentActivityLookupSequence.current;
    latestIncidentActivityLookupById.current.set(activityExecutionId, lookupRequest);
    const isLatestLookup = () => latestIncidentActivityLookupById.current.get(activityExecutionId) === lookupRequest;
    const finishLookup = () => {
      if (isLatestLookup()) latestIncidentActivityLookupById.current.delete(activityExecutionId);
    };
    let supported: boolean;
    try {
      supported = await supportsActivityExecutionInspection(context);
    } catch {
      if (isLatestLookup() && isCurrent()) setIncidentAssociationMessage(incidentActivityLookupFailureMessage("unavailable"));
      finishLookup();
      return existing;
    }
    if (!isLatestLookup()) return existing;
    if (!supported) {
      if (isCurrent()) setIncidentAssociationMessage(incidentActivityLookupFailureMessage("unsupported"));
      finishLookup();
      return existing;
    }
    const workflowExecutionId = data.details.instance.workflowExecutionId;
    const result = await loadExactIncidentActivitySummary(
      workflowExecutionId,
      activityExecutionId,
      id => getActivityExecutionInspection(context, workflowExecutionId, id));
    if (!isLatestLookup()) return existing;
    if (result.status === "failed") {
      if (isCurrent()) setIncidentAssociationMessage(incidentActivityLookupFailureMessage(result.reason));
      finishLookup();
      return existing;
    }
    const exact = result.summary;
    if (isCurrent()) setIncidentAssociationMessage(null);
    setData(current => {
      if (!current || generation !== loadGeneration.current || current.details.instance.workflowExecutionId !== workflowExecutionId)
        return current;
      const pendingIncidentActivityExecutionIds = current.pendingIncidentActivityExecutionIds
        .filter(id => id !== activityExecutionId);
      return {
        ...current,
        incidentActivityExecutions: combineActivityExecutions(current.incidentActivityExecutions, [exact]),
        pendingIncidentActivityExecutionIds,
        incidentAssociationLookupIncomplete: pendingIncidentActivityExecutionIds.length > 0,
        incidentActivityLookupSupported: true,
        unavailableIncidentActivityFailures: current.unavailableIncidentActivityFailures
          .filter(failure => failure.activityExecutionId !== activityExecutionId)
      };
    });
    finishLookup();
    return resolveIncidentActivityAssociation(incident, combineActivityExecutions(activities, [exact]), data?.executableGraph);
  }, [context, data]);

  const loadMoreIncidentAssociations = async () => {
    if (!data?.incidentActivityLookupSupported || data.pendingIncidentActivityExecutionIds.length === 0) return;
    const generation = loadGeneration.current;
    const workflowExecutionId = data.details.instance.workflowExecutionId;
    const pendingIds = data.pendingIncidentActivityExecutionIds;
    const result = await loadActiveIncidentActivitySummaries(
      { ...data.details, activities: associationActivities },
      activityExecutionId => getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId),
      pendingIds);
    setData(current => {
      if (!current || generation !== loadGeneration.current || current.details.instance.workflowExecutionId !== workflowExecutionId)
        return current;
      const confirmedActivityExecutionIds = new Set(current.incidentActivityExecutions.map(activity => activity.activityExecutionId));
      const pendingIncidentActivityExecutionIds = result.pendingActivityExecutionIds
        .filter(id => !confirmedActivityExecutionIds.has(id));
      const unavailableIncidentActivityFailures = combineUnavailableIncidentActivityFailures(
        current.unavailableIncidentActivityFailures.filter(failure => !confirmedActivityExecutionIds.has(failure.activityExecutionId)),
        result.unavailableFailures.filter(failure => !confirmedActivityExecutionIds.has(failure.activityExecutionId)));
      return {
        ...current,
        incidentActivityExecutions: combineActivityExecutions(
          current.incidentActivityExecutions,
          result.activities.filter(activity => !confirmedActivityExecutionIds.has(activity.activityExecutionId))),
        pendingIncidentActivityExecutionIds,
        incidentAssociationLookupIncomplete: pendingIncidentActivityExecutionIds.length > 0,
        unavailableIncidentActivityFailures
      };
    });
  };

  const beginEvidenceNavigation = () => {
    const requestId = ++incidentNavigationRequest.current;
    if (scopeNavigation.context === browserContext && scopeNavigation.loading) {
      scopeRequest.current?.abort();
      setScopeNavigation({ context: browserContext, loading: false, error: "Execution scope navigation was interrupted. Retry the scope." });
    }
    setIncidentAssociationMessage(null);
    setFocusInputKey(null);
    setFocusedRuntimeNodeId(null);
    return requestId;
  };

  const requestCanvasFocus = (nodeId: string | null) => {
    setFocusedRuntimeNodeId(nodeId);
    if (nodeId) setCanvasFocusRequestId(current => current + 1);
  };

  const selectEvidence = (evidenceId: string | null) => {
    beginEvidenceNavigation();
    setSelectedEvidenceId(evidenceId);
  };

  const selectInspectorTab = (tab: InstanceInspectorTab) => {
    beginEvidenceNavigation();
    setActiveInspectorTab(tab);
  };

  const toggleInspectorCollapsedWithNavigation = () => {
    beginEvidenceNavigation();
    toggleInspectorCollapsed();
  };

  const toggleInspectorMaximizedWithNavigation = () => {
    beginEvidenceNavigation();
    toggleInspectorMaximized();
  };

  const openIncidentInIssues = (incidentId?: string | null, nodeId?: string | null) => {
    const requestId = beginEvidenceNavigation();
    const isCurrent = () => incidentNavigationRequest.current === requestId;
    const incident = data?.details.incidents?.find(item => item.incidentId === incidentId);
    const existingAssociation = incident
      ? resolveIncidentActivityAssociation(incident, associationActivities, data?.executableGraph)
      : null;
    const targetNodeId = nodeId ?? existingAssociation?.nodeId ?? null;
    setSelectedEvidenceId(incidentId ?? null);
    setActiveInspectorTab("issues");
    requestCanvasFocus(targetNodeId);
    focusNodePath(targetNodeId);
    openInspector();

    if (incident) {
      void ensureIncidentActivityAssociation(incident, isCurrent).then(association => {
        if (!isCurrent() || !association?.nodeId || association.nodeId === targetNodeId) return;
        requestCanvasFocus(association.nodeId);
        focusNodePath(association.nodeId);
      });
    }
  };

  const showIncidentActivity = (incident: IncidentStateSummary, showInput: boolean) => {
    const requestId = beginEvidenceNavigation();
    const isCurrent = () => incidentNavigationRequest.current === requestId;
    const existingAssociation = resolveIncidentActivityAssociation(incident, associationActivities, data?.executableGraph);
    setSelectedEvidenceId(incident.incidentId);
    setActiveInspectorTab(existingAssociation?.activityExecution ? "activity" : "issues");
    setFocusInputKey(showInput && existingAssociation?.activityExecution ? readIncidentInputKey(incident) : null);
    requestCanvasFocus(existingAssociation?.nodeId ?? null);
    focusNodePath(existingAssociation?.nodeId ?? null);
    if (showInput) openInspector();
    else showCanvasForSelection();

    void ensureIncidentActivityAssociation(incident, isCurrent).then(association => {
      if (!isCurrent()) return;
      if (!association?.activityExecution && association?.nodeId && !readIncidentActivityExecutionId(incident)) {
        setIncidentAssociationMessage("This incident identifies an activity but not an exact execution, so Studio cannot choose a repeated occurrence.");
      }
      if (association?.activityExecution) {
        setSelectedEvidenceId(association.activityExecution.activityExecutionId);
        void restoreExecutionContext(association.activityExecution, false);
      }
      setActiveInspectorTab(association?.activityExecution ? "activity" : "issues");
      setFocusInputKey(showInput && association?.activityExecution ? readIncidentInputKey(incident) : null);
      if (association?.nodeId !== existingAssociation?.nodeId) {
        requestCanvasFocus(association?.nodeId ?? null);
        focusNodePath(association?.nodeId ?? null);
      }
      if (showInput) openInspector();
      else showCanvasForSelection();
    });
  };

  const labelForExecution = useCallback((execution: ActivityExecutionStateSummary) => resolveActivityLabel(
    findExecutableNodeFacts(data?.executableGraph, execution)?.presentation,
    data?.activityCatalog.find(item => item.activityTypeKey === execution.activityType), execution.activityType), [data?.activityCatalog, data?.executableGraph]);

  const rememberExecution = useCallback((execution: ActivityExecutionStateSummary) => {
    setBrowserEvidence(current => ({ context: browserContext, items: combineActivityExecutions(
      current.context === browserContext ? current.items : [], [execution]) }));
  }, [browserContext]);
  const rememberObservedExecutions = useCallback((executions: ActivityExecutionStateSummary[]) => {
    if (loadedBrowserContext.current !== browserContext) return;
    setBrowserEvidence(current => ({ context: browserContext, items: combineActivityExecutions(current.context === browserContext ? current.items : [], executions) }));
  }, [browserContext]);

  const restoreExecutionContext = useCallback(async (execution: ActivityExecutionStateSummary, includeSelf: boolean, label?: string) => {
    requestedScope.current = { execution, includeSelf, label };
    scopeRequest.current?.abort();
    const controller = new AbortController();
    scopeRequest.current = controller;
    const generation = loadGeneration.current;
    const navigation = incidentNavigationRequest.current;
    try {
      if (!await supportsActivityExecutionBrowsing(context) || controller.signal.aborted || generation !== loadGeneration.current || navigation !== incidentNavigationRequest.current) return;
      setScopeNavigation({ context: browserContext, loading: true, error: "" });
      const result = await loadExecutionScope(context, workflowExecutionId, execution, {
        graph: data?.executableGraph, includeSelf, label, labelFor: labelForExecution, signal: controller.signal,
        isCurrent: () => generation === loadGeneration.current && navigation === incidentNavigationRequest.current
      });
      if (!controller.signal.aborted && navigation === incidentNavigationRequest.current) {
        result.activities.forEach(rememberExecution);
        const pinnedBoundaryLayout = result.layout;
        const chain = result.scopes;
        setBoundaryCanvas(pinnedBoundaryLayout ? { context: browserContext, layout: pinnedBoundaryLayout } : null);
        setExecutionScopes({ context: browserContext, items: chain });
        setScopeNavigation({ context: browserContext, loading: false, error: "" });
        return true;
      }
    } catch {
      if (!controller.signal.aborted && navigation === incidentNavigationRequest.current) setScopeNavigation({ context: browserContext, loading: false, error: "Execution context could not be resolved. Retry the selection to inspect its scope." });
    }
  }, [browserContext, context, data?.executableGraph, labelForExecution, rememberExecution, workflowExecutionId]);

  const selectBrowserExecution = (execution: ActivityExecutionStateSummary, updateScope = true) => {
    rememberExecution(execution);
    rememberedExecutions.current.set(JSON.stringify([browserContext, executionScope?.activityExecutionId, execution.executableNodeId]), execution);
    beginEvidenceNavigation();
    setSelectedEvidenceId(execution.activityExecutionId);
    setActiveInspectorTab("activity");
    const nodeId = findExecutableGraphNodeId(data?.executableGraph, execution) ?? execution.authoredActivityId;
    requestCanvasFocus(nodeId);
    if (updateScope && executionScope?.executableNodeId === execution.executableNodeId && executionScope.activityExecutionId !== execution.activityExecutionId)
      void restoreExecutionContext(execution, true);
    openInspector();
  };

  const selectActivityExecution = (activityExecutionId: string) => {
    const known = associationActivities.find(item => item.activityExecutionId === activityExecutionId);
    if (!known) return;
    selectBrowserExecution(known);
    focusNodePath(findExecutableGraphNodeId(data?.executableGraph, known) ?? known.authoredActivityId);
    void restoreExecutionContext(known, false);
  };

  const enterChildExecution = (entry: ActivityExecutionBrowseEntry) => {
    const execution = entry.execution;
    selectBrowserExecution(execution);
    const nodeId = findExecutableGraphNodeId(data?.executableGraph, execution) ?? execution.authoredActivityId;
    const root = data?.definitionVersion?.state.rootActivity;
    const node = root ? findActivityNode(root, nodeId, data?.activityCatalog) : null;
    const firstChild = node ? getChildSlots(node, data?.activityCatalog)[0]?.activities[0] : null;
    focusNodePath(firstChild?.nodeId ?? nodeId);
    const occurrence = execution.iterationId && entry.iterationPosition && !entry.attempt ? `Iteration ${entry.iterationPosition}` : `Execution ${entry.position}`;
    void restoreExecutionContext(execution, Boolean(firstChild) || !node, `${labelForExecution(execution)} · ${occurrence}`);
  };

  const selectGraphActivity = (nodeId: string) => {
    beginEvidenceNavigation();
    const node = data?.executableGraph?.factsByNodeId.get(nodeId);
    const remembered = rememberedExecutions.current.get(JSON.stringify([browserContext, executionScope?.activityExecutionId, node?.executableNodeId]));
    setSelectedEvidenceId(remembered?.activityExecutionId ?? nodeId);
    setActiveInspectorTab("activity");
    requestCanvasFocus(nodeId);
    openInspector();
  };

  const moveScopeOccurrence = async (execution: ActivityExecutionStateSummary) => {
    const navigation = beginEvidenceNavigation();
    const retainedNode = selectedActivity?.executableNodeId;
    const retainedCanvasNode = selectedActivity ? findExecutableGraphNodeId(data?.executableGraph, selectedActivity) ?? selectedActivity.executableNodeId : null;
    if (!await restoreExecutionContext(execution, true) || navigation !== incidentNavigationRequest.current) return;
    const controller = scopeRequest.current;
    if (!retainedNode || retainedNode === execution.executableNodeId) { selectBrowserExecution(execution, false); return; }
    try {
      const page = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: retainedNode,
        ancestorActivityExecutionId: execution.activityExecutionId, limit: 1 }, controller?.signal);
      if (controller?.signal.aborted || scopeRequest.current !== controller || navigation !== incidentNavigationRequest.current) return;
      if (page.items[0]) selectBrowserExecution(page.items[0].execution);
      else setSelectedEvidenceId(retainedCanvasNode);
    } catch {
      if (!controller?.signal.aborted && navigation === incidentNavigationRequest.current) setScopeNavigation({ context: browserContext, loading: false, error: "This occurrence's activity evidence could not be loaded. Retry the scope." });
    }
  };

  const openExecutionIssues = (execution: ActivityExecutionStateSummary) => {
    rememberExecution(execution);
    beginEvidenceNavigation();
    setSelectedEvidenceId(execution.activityExecutionId);
    setActiveInspectorTab("issues");
    openInspector();
  };

  const browserNodeFacts = findExecutableNodeFacts(data?.executableGraph, selectedActivity)
    ?? (selectedEvidenceId ? data?.executableGraph?.factsByNodeId.get(selectedEvidenceId) : undefined);
  const boundaryNode = boundaryCanvas?.context === browserContext
    ? boundaryCanvas.layout.nodes.find(node => node.executableNodeId === selectedEvidenceId) : undefined;
  const browserNodeIdentity = browserNodeFacts ?? selectedActivity ?? (boundaryNode ? {
    executableNodeId: boundaryNode.executableNodeId, activityType: boundaryNode.activityType ?? "Activity"
  } : undefined);
  const selectedIncident = data?.details.incidents?.find(incident => incident.incidentId === selectedEvidenceId);
  const incidentExecutionId = selectedIncident ? readIncidentActivityExecutionId(selectedIncident) : null;
  const browserExactId = selectedActivity && (selectedActivity.activityExecutionId === selectedEvidenceId || selectedActivity.activityExecutionId === incidentExecutionId)
    ? selectedActivity.activityExecutionId : undefined;
  const browserAncestorId = executionScope?.activityExecutionId === browserExactId
    ? currentExecutionScopes.at(-2)?.execution.activityExecutionId : executionScope?.activityExecutionId;
  const scopeBlocked = scopeNavigation.context === browserContext && (scopeNavigation.loading || Boolean(scopeNavigation.error));
  const browserActivityContent = scopeBlocked ? <p className="wf-instance-note" role="status">{scopeNavigation.loading ? "Resolving execution scope…" : scopeNavigation.error}</p> : browserNodeIdentity && data ? <Suspense fallback={<p role="status">Loading execution browser…</p>}><ActivityExecutionInspectionPanel
    key={`${browserContext}:${browserNodeIdentity.executableNodeId}:${browserAncestorId ?? ""}`}
    context={context} workflowExecutionId={workflowExecutionId} nodeId={browserNodeIdentity.executableNodeId}
    nodeLabel={browserNodeFacts?.presentation?.displayName ?? (shortTypeName(browserNodeIdentity.activityType) ?? browserNodeIdentity.activityType)}
    exactId={browserExactId} seed={browserExactId ? selectedActivity : null} ancestorId={browserAncestorId} refreshKey={evidenceRevision} requireExplicitSelection
    labelFor={labelForExecution} onSelect={selectBrowserExecution} onEnter={enterChildExecution}
    onIncident={openExecutionIssues}
    renderDetails={(execution, navigation, children) => <WorkflowActivityExecutionDetails context={context} activity={execution}
      refreshKey={evidenceRevision}
      activityCatalog={data.activityCatalog} executableNodeFacts={findExecutableNodeFacts(data.executableGraph, execution)}
      expressionEditors={expressionEditors} focusInputKey={focusInputKey} onSelectEvidence={selectActivityExecution}
      executionNavigation={navigation} childExecutions={children} />}
  /></Suspense> : undefined;

  useEffect(() => {
    const id = initialActivityExecutionId?.trim();
    if (!data || !id || appliedInitialActivityExecutionId.current === id) return;
    const activity = associationActivities.find(item => item.activityExecutionId === id);
    if (!activity) return;
    appliedInitialActivityExecutionId.current = id;
    const nodeId = findExecutableGraphNodeId(data.executableGraph, activity) ?? activity.authoredActivityId;
    requestCanvasFocus(nodeId);
    focusNodePath(nodeId);
    void restoreExecutionContext(activity, false);
  }, [associationActivities, data, focusNodePath, initialActivityExecutionId, restoreExecutionContext]);

  const openIncidents = () => {
    const active = choosePreferredIncident((data?.details.incidents ?? []).filter(isActiveIncident));
    openIncidentInIssues(active?.incidentId ?? selectedEvidenceId);
  };
  const refreshWorkspace = () => {
    loadGeneration.current++;
    scopeRequest.current?.abort(); incidentNavigationRequest.current++;
    latestIncidentActivityLookupById.current.clear(); setIncidentAssociationMessage(null);
    setScopeNavigation(current => current.context === browserContext && current.loading
      ? { context: browserContext, loading: false, error: "Execution scope resolution was interrupted by refresh. Retry the scope." } : current);
    setSelectedEvidenceId(current => data?.details.incidents?.some(incident => incident.incidentId === current) ? null : current);
    reloadDetailsRequested.current = true; refreshObservation();
  };

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={goBack}><ChevronLeft size={14} /> Runs</button>
        <IncidentHealthAction incidents={data?.details.incidents} onClick={openIncidents} />
        {selectedDefinitionCanBeReviewed && selectedDefinitionId ? (
          <button type="button" onClick={() => openDefinitionDesigner(selectedDefinitionId)}><WorkflowIcon size={14} /> Review workflow definition</button>
        ) : null}
        <button type="button" onClick={refreshWorkspace}><RotateCcw size={14} /> Refresh</button>
        <button type="button" aria-pressed={observation.following} onClick={() => observation.setFollowing(!observation.following)}
          title="Follow recorded updates without changing the inspected occurrence. Pausing observation does not stop execution.">{observation.following ? "Pause following" : "Follow execution"}</button>
        <span role="status">{observation.status === "connected" ? data ? "Execution on this canvas" : `Execution ${observation.value?.instance.status ?? "observed"}` : observation.status === "paused" ? "Observation paused · last confirmed state"
          : observation.status === "disconnected" ? "Execution observation disconnected · last confirmed evidence" : observation.status === "loading" ? "Connecting to execution…" : "Execution observation unavailable"}</span>
        {observation.error ? <span role="status">{observation.error}</span> : null}
        {data && instanceAction ? (
          <button type="button" onClick={() => dispatchAiAction(ai, instanceAction, data.details)}>
            <Sparkles size={13} /> Explain
          </button>
        ) : null}
        {operator.status}
      </div>
      {operator.dialog}
      {state === "loading" ? <div className="wf-empty">Loading workflow run...</div> : null}
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && data ? (
        <div ref={containerRef} className={workbenchClassName} style={workbenchStyle}>
          {data.incidentAssociationLookupIncomplete ? (
            <div className="wf-run-association-status" role="status">
              <span>
              {data.incidentActivityLookupSupported
                ? `Some affected activities are not shown yet (${data.pendingIncidentActivityExecutionIds.length} remaining). Open Issues and load more.`
                : "Exact affected activity evidence is unavailable from this server."}
              </span>
            </div>
          ) : null}
          <WorkflowInstanceCanvas
            observationIdentity={observation.identity}
            observationStatus={observation.status}
            onSelectExecutionSummary={selectBrowserExecution}
            onExecutionsObserved={rememberObservedExecutions}
            context={context}
            boundaryLayout={boundaryCanvas?.context === browserContext ? boundaryCanvas.layout : undefined}
            executionScope={executionScope}
            refreshKey={evidenceRevision}
            scopeLoading={scopeBlocked}
            executionBreadcrumb={<nav className="wf-execution-scope" aria-label="Execution scope"><button type="button" onClick={() => { scopeRequest.current?.abort(); setBoundaryCanvas(null); requestedScope.current = null; setExecutionScopes({ context: browserContext, items: [] }); setScopeNavigation({ context: browserContext, loading: false, error: "" }); setFrames([]); }}>All executions</button>{currentExecutionScopes.map(scope => <button type="button" key={scope.execution.activityExecutionId} onClick={() => { selectBrowserExecution(scope.execution); focusNodePath(scope.execution.authoredActivityId); void restoreExecutionContext(scope.execution, true); }}><ChevronRight size={13} />{scope.label}</button>)}{executionScope && !scopeBlocked ? <Suspense fallback={null}><ExecutionScopeNavigator context={context} execution={executionScope} refreshKey={evidenceRevision} onIncident={openExecutionIssues} onSelect={execution => void moveScopeOccurrence(execution)} onUnavailable={() => setScopeNavigation({ context: browserContext, loading: false, error: "This execution scope is unavailable in the current context." })} /></Suspense> : null}{scopeNavigation.context === browserContext && (scopeNavigation.loading || scopeNavigation.error) ? <span role="status">{scopeNavigation.loading ? "Resolving execution scope…" : scopeNavigation.error}{scopeNavigation.error ? <button type="button" onClick={() => { const request = requestedScope.current; if (request) void restoreExecutionContext(request.execution, request.includeSelf, request.label); }}>Retry execution scope</button> : null}</span> : null}</nav>}
            definitionVersion={data.definitionVersion}
            definitionVersionError={data.definitionVersionError}
            activityCatalog={data.activityCatalog}
            executableGraph={data.executableGraph}
            details={data.details}
            associationActivities={associationActivities}
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={selectEvidence}
            onOpenIncident={openIncidentInIssues}
            onSelectActivity={selectGraphActivity}
            focusNodeId={focusedRuntimeNodeId}
            focusRequestId={canvasFocusRequestId}
            frames={frames}
            onNavigateToScope={navigateToScope}
            inactive={mode === "medium" && mediumDrawerOpen}
            journals={journals}
            operator={{
              canAct: operator.canAct,
              workflowExecutionId: data.details.instance.workflowExecutionId,
              pending: operator.pending,
              onAction: operator.open
            }}
          />
          {mediumDrawerOpen ? (
            <button type="button" className="wf-run-inspector-backdrop" aria-label="Close run details" onClick={closeResponsiveInspector} />
          ) : null}
          {canResizeInspector ? (
            <div
              className="wf-side-resize-handle right"
              role="separator"
              aria-label="Resize run details panel"
              aria-orientation="vertical"
              aria-valuemin={runDetailMinInspectorWidth}
              aria-valuemax={inspectorMaxWidth}
              aria-valuenow={inspectorWidth}
              tabIndex={0}
              onPointerDown={startInspectorResize}
              onKeyDown={handleInspectorResizeKeyDown}
            />
          ) : <div className="wf-side-resize-spacer" />}
          <WorkflowInstanceInspector
            context={context}
            summary={data.details.instance}
            activityContent={browserActivityContent}
            details={data.details}
            associationActivities={associationActivities}
            associationLookupIncomplete={data.incidentAssociationLookupIncomplete}
            associationLookupSupported={data.incidentActivityLookupSupported}
            associationLookupMessage={incidentAssociationMessage}
            pendingIncidentActivityCount={data.pendingIncidentActivityExecutionIds.length}
            unavailableActivityFailures={data.unavailableIncidentActivityFailures}
            state="ready"
            error=""
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={selectEvidence}
            activeTab={activeInspectorTab}
            onActiveTabChange={selectInspectorTab}
            onShowAffectedActivity={incident => showIncidentActivity(incident, false)}
            onViewInput={incident => showIncidentActivity(incident, true)}
            onLoadMoreIncidentAssociations={loadMoreIncidentAssociations}
            focusInputKey={focusInputKey}
            onSelectActivityExecution={selectActivityExecution}
            renderIncidentActions={operator.renderIncidentActions}
            guidanceForIncident={operator.guidanceForIncident}
            onOpenDefinition={openDefinitionDesigner}
            definitionAccess={definitionAccess}
            activityCatalog={data.activityCatalog}
            executableGraph={data.executableGraph}
            definitionName={data.definitionName ?? data.definitionVersion?.definition.name}
            expressionEditors={expressionEditors}
            graphNodeIds={data.definitionVersion ? collectWorkflowGraphNodeIds(data.definitionVersion, data.activityCatalog) : undefined}
            rootNodeId={data.definitionVersion?.state.rootActivity?.nodeId}
            collapsed={inspectorCollapsed}
            expanded={inspectorExpanded}
            maximized={inspectorMaximized}
            layoutMode={mode}
            responsiveOpen={mode === "medium" ? mediumDrawerOpen : mode === "narrow" && inspectorExpanded}
            onCloseResponsive={closeResponsiveInspector}
            onToggleCollapsed={toggleInspectorCollapsedWithNavigation}
            onToggleMaximized={toggleInspectorMaximizedWithNavigation}
          />
        </div>
      ) : null}
    </>
  );
}

export function applyDefinitionDisplayName<T extends {
  details: Pick<WorkflowInstanceDetails, "instance">;
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionName?: string | null;
}>(data: T, definitionName: string | null | undefined): T & { definitionName: string } {
  const name = resolveNamedIdentity(definitionName, data.details.instance.definitionId).label;
  const nextVersion = data.definitionVersion && data.definitionVersion.definition.name !== name
    ? {
        ...data.definitionVersion,
        definition: {
          ...data.definitionVersion.definition,
          name
        }
      }
    : data.definitionVersion;
  if (data.definitionName === name && nextVersion === data.definitionVersion) return { ...data, definitionName: name };
  return {
    ...data,
    definitionName: name,
    definitionVersion: nextVersion
  };
}

export function projectPinnedExecutable(
  executable: WorkflowExecutableDetails,
  details: WorkflowInstanceDetails,
  catalog: ActivityCatalogItem[],
  executableGraph?: ExecutableActivityGraph,
  definitionName?: string | null
): WorkflowDefinitionVersionDetails {
  const timestamp = executable.createdAt ?? details.instance.createdAt;
  const graph = executableGraph ?? buildExecutableActivityGraph(
    executable.rootActivity,
    catalog,
    [],
    [],
    null,
    executable.chosenReference?.activityPresentation ?? []);
  return {
    id: details.instance.definitionVersionId,
    version: details.instance.artifactVersion,
    definition: {
      id: details.instance.definitionId,
      name: resolveNamedIdentity(definitionName, details.instance.definitionId).label,
      description: `Pinned Runtime executable ${executable.artifactId}`,
      createdAt: timestamp,
      lastModifiedAt: timestamp
    },
    state: { rootActivity: graph.root },
    layout: executable.chosenReference?.layout ?? [],
    activityPresentation: graph.activityPresentation
  };
}

export function resolveInitialActivityEvidenceId(
  activities: ReadonlyArray<{ activityExecutionId: string }>,
  requestedActivityExecutionId: string | null
) {
  return requestedActivityExecutionId &&
    activities.some(activity => activity.activityExecutionId === requestedActivityExecutionId)
    ? requestedActivityExecutionId
    : null;
}

export function combineActivityExecutions(
  pageActivities: ActivityExecutionStateSummary[],
  incidentActivities: ActivityExecutionStateSummary[]
) {
  const byId = new Map(pageActivities.map(activity => [activity.activityExecutionId, activity]));
  for (const activity of incidentActivities) byId.set(activity.activityExecutionId, activity);
  return [...byId.values()];
}

export type IncidentActivityInspectionFetcher = (activityExecutionId: string) => Promise<IncidentActivityInspection>;

export type IncidentActivityLookupFailureReason = "identity-mismatch" | "permission-denied" | "unavailable" | "unsupported";
type UnavailableIncidentActivityFailure = {
  activityExecutionId: string;
  reason: "identity-mismatch" | "permission-denied";
};

export function incidentActivityLookupFailureMessage(reason: IncidentActivityLookupFailureReason) {
  switch (reason) {
    case "identity-mismatch":
      return "Runtime returned a different run or activity execution. Studio kept the incident open and did not open that execution.";
    case "permission-denied":
      return "You do not have permission to inspect this activity execution. The incident remains available here.";
    case "unsupported":
      return "This host does not support exact activity inspection. The incident remains available here.";
    default:
      return "Studio could not load the exact activity execution. Use Show affected activity to try again; the incident remains available here.";
  }
}

export type ExactIncidentActivitySummaryResult =
  | { status: "found"; summary: ActivityExecutionStateSummary }
  | { status: "failed"; reason: Exclude<IncidentActivityLookupFailureReason, "unsupported"> };

export async function loadExactIncidentActivitySummary(
  workflowExecutionId: string,
  activityExecutionId: string,
  fetchInspection: IncidentActivityInspectionFetcher
): Promise<ExactIncidentActivitySummaryResult> {
  try {
    const inspection = await fetchInspection(activityExecutionId);
    if (inspection.activityExecutionId !== activityExecutionId || inspection.workflowExecutionId !== workflowExecutionId) {
      return { status: "failed", reason: "identity-mismatch" };
    }
    return { status: "found", summary: activityExecutionSummaryFromInspection(inspection) };
  } catch (error) {
    const structuredStatus = typeof error === "object" && error !== null && "status" in error
      ? (error as { status?: unknown }).status
      : undefined;
    const status = typeof structuredStatus === "number" && Number.isFinite(structuredStatus)
      ? structuredStatus
      : undefined;
    const message = error instanceof Error ? error.message : String(error);
    const permissionDenied = status !== undefined
      ? status === 401 || status === 403
      : /forbidden|permission|unauthori[sz]ed/i.test(message);
    return {
      status: "failed",
      reason: permissionDenied ? "permission-denied" : "unavailable"
    };
  }
}

export async function loadActiveIncidentActivitySummaries(
  details: WorkflowInstanceDetails,
  fetchInspection: IncidentActivityInspectionFetcher | null,
  requestedActivityExecutionIds?: string[]
) {
  const activitiesById = new Map(details.activities.map(activity => [activity.activityExecutionId, activity]));
  const activeIds = [...new Set((details.incidents ?? [])
    .filter(isActiveIncident)
    .map(readIncidentActivityExecutionId)
    .filter((id): id is string => Boolean(id?.trim())))];
  const requestedIds = requestedActivityExecutionIds
    ? [...new Set(requestedActivityExecutionIds.filter(id => activeIds.includes(id)))]
    : activeIds.filter(id => !activitiesById.get(id)?.authoredActivityId);
  if (requestedIds.length === 0) {
    return {
      activities: [] as ActivityExecutionStateSummary[],
      incomplete: false,
      pendingActivityExecutionIds: [] as string[],
      unavailableFailures: [] as UnavailableIncidentActivityFailure[]
    };
  }
  if (!fetchInspection) {
    return {
      activities: [] as ActivityExecutionStateSummary[],
      incomplete: true,
      pendingActivityExecutionIds: requestedIds,
      unavailableFailures: [] as UnavailableIncidentActivityFailure[]
    };
  }

  const fetched: ActivityExecutionStateSummary[] = [];
  const failedIds: string[] = [];
  const unavailableFailures: UnavailableIncidentActivityFailure[] = [];
  const selectedIds = requestedIds.slice(0, maxIncidentActivityLookups);
  for (let index = 0; index < selectedIds.length; index += incidentActivityLookupBatchSize) {
    const batch = selectedIds.slice(index, index + incidentActivityLookupBatchSize);
    const results = await Promise.all(batch.map(async activityExecutionId => {
      const result = await loadExactIncidentActivitySummary(details.instance.workflowExecutionId, activityExecutionId, fetchInspection);
      return { activityExecutionId, result };
    }));
    for (const result of results) {
      if (result.result.status === "found") fetched.push(result.result.summary);
      else if (result.result.reason === "unavailable") failedIds.push(result.activityExecutionId);
      else unavailableFailures.push({ activityExecutionId: result.activityExecutionId, reason: result.result.reason });
    }
  }

  const pendingActivityExecutionIds = [...requestedIds.slice(selectedIds.length), ...failedIds];
  return {
    activities: fetched,
    incomplete: pendingActivityExecutionIds.length > 0,
    pendingActivityExecutionIds,
    unavailableFailures
  };
}

function combineUnavailableIncidentActivityFailures(
  existing: UnavailableIncidentActivityFailure[],
  next: UnavailableIncidentActivityFailure[]
) {
  const byActivityExecutionId = new Map(existing.map(failure => [failure.activityExecutionId, failure]));
  for (const failure of next) byActivityExecutionId.set(failure.activityExecutionId, failure);
  return [...byActivityExecutionId.values()];
}

function sourceAccessFromError(error: unknown) {
  const status = typeof error === "object" && error !== null && "status" in error
    ? Number((error as { status?: unknown }).status)
    : undefined;
  const message = error instanceof Error ? error.message : String(error);
  return status === 401 || status === 403 || /forbidden|permission|unauthori[sz]ed/i.test(message)
    ? "permissionHidden"
    : "unavailable";
}

function WorkflowInstanceCanvas({
  observationIdentity, observationStatus, context, executionScope, boundaryLayout, refreshKey, scopeLoading, executionBreadcrumb, onSelectExecutionSummary,
  onExecutionsObserved,
  definitionVersion,
  definitionVersionError,
  activityCatalog,
  executableGraph,
  details,
  associationActivities,
  selectedEvidenceId,
  onSelectEvidence,
  onOpenIncident,
  onSelectActivity,
  focusNodeId,
  focusRequestId,
  frames,
  onNavigateToScope,
  inactive = false,
  journals,
  operator
 }: {
  observationIdentity: string;
  observationStatus: ExecutionObservationStatus;
  context: StudioEndpointContext;
  executionScope?: ActivityExecutionStateSummary;
  boundaryLayout?: ActivityExecutionLayout;
  refreshKey: number;
  scopeLoading: boolean;
  executionBreadcrumb: ReactNode;
  onSelectExecutionSummary(execution: ActivityExecutionStateSummary): void;
  onExecutionsObserved(executions: ActivityExecutionStateSummary[]): void;
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
  executableGraph: ExecutableActivityGraph | null;
  details: WorkflowInstanceDetails;
  associationActivities: ActivityExecutionStateSummary[];
  selectedEvidenceId: string | null;
  onSelectEvidence(evidenceId: string | null): void;
  onOpenIncident(incidentId: string, nodeId?: string | null): void;
  onSelectActivity(nodeId: string): void;
  focusNodeId: string | null;
  focusRequestId: number;
  frames: ScopeFrame[];
  onNavigateToScope(frames: ScopeFrame[]): void;
  inactive?: boolean;
  journals?: CompensationJournals;
  operator?: CompensationOperatorControls;
}) {
  const scopeKey = getInstanceScopeKey(frames);
  const [flowInstance, setFlowInstance] = useState<WorkflowRunFlowInstance | null>(null);
  const journalOf = journals?.get;
  const unscopedCanvas = useMemo(
    () => buildInstanceCanvas(definitionVersion, activityCatalog, details, selectedEvidenceId, frames, onNavigateToScope, onOpenIncident, [], executableGraph, journalOf),
    [activityCatalog, definitionVersion, details, executableGraph, frames, journalOf, onNavigateToScope, onOpenIncident, selectedEvidenceId]);
  const scoped = useScopedCanvasActivities(context, details.instance.workflowExecutionId, executionScope?.activityExecutionId,
    boundaryLayout ? boundaryLayout.nodes.map(node => node.executableNodeId) : unscopedCanvas.nodes.map(node => executableGraph?.factsByNodeId.get(nodeRuntimeId(node))?.executableNodeId ?? nodeRuntimeId(node)), refreshKey,
    associationActivities.find(item => item.activityExecutionId === selectedEvidenceId), true);
  const selectedIncident = details.incidents?.find(incident => incident.incidentId === selectedEvidenceId);
  const incidentExecutionId = selectedIncident ? readIncidentActivityExecutionId(selectedIncident) : null;
  const selectedExecution = associationActivities.find(item => (item.activityExecutionId === selectedEvidenceId || item.activityExecutionId === incidentExecutionId)
    && item.workflowExecutionId === details.instance.workflowExecutionId);
  const shownActivities = scopeLoading ? [] : selectedExecution && !scoped.activities.some(item => item.activityExecutionId === selectedEvidenceId)
    ? [...scoped.activities, selectedExecution] : scoped.activities;
  useEffect(() => { if (scoped.activities.length > 0) onExecutionsObserved(scoped.activities); }, [onExecutionsObserved, scoped.activities]);
  const scopedIds = new Set(shownActivities.map(item => item.activityExecutionId));
  const shownIncidents = executionScope || scopeLoading ? (details.incidents ?? []).filter(item => {
    const id = readIncidentActivityExecutionId(item);
    return id ? scopedIds.has(id) : false;
  }) : details.incidents;
  const canvas = buildInstanceCanvas(definitionVersion, activityCatalog, { ...details, incidents: shownIncidents }, selectedEvidenceId,
    frames, onNavigateToScope, onOpenIncident, shownActivities, executableGraph, journalOf);
  const motion = useConfirmedExecutionMotion({ identity: JSON.stringify([observationIdentity, scopeKey, executionScope?.activityExecutionId, selectedEvidenceId]),
    observationStatus, enabled: !inactive && !scopeLoading && !scoped.failed && !boundaryLayout,
    observationRevision: refreshKey, confirmedRevision: scoped.confirmedRevision,
    ready: !scoped.loading, workflowExecutionId: details.instance.workflowExecutionId,
    mode: canvas.mode === "sequence" ? "sequence" : (definitionVersion?.state.rootActivity ? resolveScope(definitionVersion.state.rootActivity, frames, activityCatalog)?.slot.mode : undefined),
    nodes: canvas.nodes, edges: canvas.edges, observations: scoped.observations });
  const animatedCanvas = decorateExecutionMotion(canvas.nodes, canvas.edges, motion);
  // Spec 260: a live scope's rows come from its journal; read it once the shown occurrence is known.
  const shownScopeExecutionId = canvas.mode === "compensation" ? canvas.compensation?.runtime.scopeActivityExecutionId : undefined;
  const shownScopeStatus = shownScopeExecutionId ? associationActivities.find(activity => activity.activityExecutionId === shownScopeExecutionId)?.status : undefined;
  const requestJournal = journals?.request;
  const journalGeneration = journals?.generation;
  useEffect(() => {
    if (shownScopeExecutionId && requestJournal) requestJournal(shownScopeExecutionId, shownScopeStatus);
  }, [journalGeneration, requestJournal, shownScopeExecutionId, shownScopeStatus]);
  const focusCanvasNodeId = focusNodeId
    ? canvas.nodes.find(node => nodeRuntimeId(node) === focusNodeId)?.id ?? null
    : null;

  useEffect(() => {
    if (canvas.mode !== "graph" || !flowInstance || !focusCanvasNodeId) return;
    const frame = requestAnimationFrame(() => {
      flowInstance.fitView({ nodes: [{ id: focusCanvasNodeId }], duration: 220, padding: 0.35 });
      focusSelectedCanvasActivity(focusNodeId);
    });
    return () => cancelAnimationFrame(frame);
  }, [canvas.mode, flowInstance, focusCanvasNodeId, focusNodeId, focusRequestId, scopeKey]);

  return (
    <section className="wf-instance-canvas-shell" aria-label="Workflow run canvas" aria-hidden={inactive || undefined} inert={inactive || undefined}>
      <header>
        <div>
          <span>Pinned Runtime executable</span>
          <h3 title={definitionVersion ? `${definitionVersion.definition.name} (${definitionVersion.definition.id})` : undefined}>{definitionVersion ? (
            <>
              {definitionVersion.definition.name} <small>{definitionVersion.version}</small>
            </>
          ) : (
            <>
              Executable graph unavailable <small>{details.instance.artifactId}</small>
            </>
          )}</h3>
        </div>
        <WorkflowStatusBadge status={details.instance.status} subStatus={details.instance.subStatus} />
      </header>
      {definitionVersion ? (
        <ScopeBreadcrumb className="wf-instance-breadcrumb" frames={frames} onNavigate={onNavigateToScope} />
      ) : null}
      {executionBreadcrumb}
      {(scoped.loading || scoped.failed || scoped.repeated) ? <p className="wf-instance-note" role="status">{scoped.loading ? "Loading visible execution evidence…" : scoped.failed ? "Some visible execution evidence could not be loaded. Refresh to retry." : "Some activities ran more than once. Choose an occurrence to inspect."}</p> : null}
      <div className="wf-instance-canvas" onKeyDownCapture={event => activateWorkflowCanvasNode(event, nodeId => {
        const node = canvas.nodes.find(candidate => candidate.id === nodeId);
        if (node) onSelectActivity(nodeRuntimeId(node));
      })}>
        {!definitionVersion ? (
          <div className="wf-empty">
            The workflow run loaded, but its pinned executable graph could not be resolved.
            {definitionVersionError ? <small>{definitionVersionError}</small> : null}
          </div>
        ) : null}
        {definitionVersion && canvas.mode === "graph" && canvas.nodes.length === 0 ? <div className="wf-empty">No workflow activities are available for this executable.</div> : null}
        {boundaryLayout ? <Suspense fallback={<p role="status">Loading pinned activity structure…</p>}><HistoricalBoundaryLayout layout={boundaryLayout}
          selectedExecutableNodeId={associationActivities.find(item => item.activityExecutionId === selectedEvidenceId)?.executableNodeId}
          statusByNode={new Map(shownActivities.map(item => [item.executableNodeId, item.status]))}
          onSelectNode={nodeId => {
            const execution = shownActivities.find(item => item.executableNodeId === nodeId);
            if (execution) onSelectExecutionSummary(execution);
            else onSelectActivity(nodeId);
          }} /></Suspense> : definitionVersion && canvas.mode === "compensation" && canvas.compensation ? (
          <CompensationScopeDesigner
            key={scopeKey}
            owner={canvas.compensation.owner}
            nodes={animatedCanvas.nodes}
            selectedNodeId={null}
            readOnly
            runtime={canvas.compensation.runtime}
            onSelect={nodeId => nodeId && onSelectActivity(nodeId)}
            onSelectExecution={onSelectEvidence}
            onOpenIncident={incidentId => onOpenIncident(incidentId)}
            onOpenSlot={canvas.compensation.openSlot}
            operator={operator}
          />
        ) : definitionVersion && canvas.mode === "sequence" ? (
          <ReadOnlyWorkflowSequence
            key={scopeKey}
            nodes={animatedCanvas.nodes}
            onSelectActivity={onSelectActivity}
            onSelectEvidence={onSelectEvidence}
            focusNodeId={focusCanvasNodeId}
            focusRequestId={focusRequestId}
          />
        ) : canvas.nodes.length > 0 ? (
          <ReadOnlyWorkflowFlow
            key={scopeKey}
            nodes={animatedCanvas.nodes}
            edges={animatedCanvas.edges}
            onInit={setFlowInstance}
            onSelectActivity={onSelectActivity}
            onSelectEvidence={onSelectEvidence}
          />
        ) : null}
      </div>
    </section>
  );
}

function ReadOnlyWorkflowFlow({
  nodes,
  edges,
  onInit,
  onSelectActivity,
  onSelectEvidence
}: {
  nodes: Node<WorkflowNodeData>[];
  edges: Edge<WorkflowEdgeData>[];
  onInit(instance: WorkflowRunFlowInstance): void;
  onSelectActivity(nodeId: string): void;
  onSelectEvidence(evidenceId: string | null): void;
}) {
  const [measuredNodes, setMeasuredNodes] = useState<Node<WorkflowNodeData>[]>([]);
  const flowNodes = useMemo(() => mergeMeasuredNodeDimensions(nodes, measuredNodes), [measuredNodes, nodes]);

  useEffect(() => {
    setMeasuredNodes(current => {
      const activeTypes = new Map(nodes.map(node => [node.id, node.type]));
      const next = current.filter(node => activeTypes.get(node.id) === node.type);
      return next.length === current.length ? current : next;
    });
  }, [nodes]);

  const onNodesChange = useCallback((changes: NodeChange<Node<WorkflowNodeData>>[]) => {
    const dimensionChanges = changes.filter(change => change.type === "dimensions");
    if (dimensionChanges.length === 0) return;

    setMeasuredNodes(current => {
      return applyNodeChanges(dimensionChanges, mergeMeasuredNodeDimensions(nodes, current));
    });
  }, [nodes]);

  return (
    <ReactFlow
      nodes={flowNodes}
      edges={edges}
      nodeTypes={nodeTypes}
      edgeTypes={edgeTypes}
      fitView
      onInit={onInit}
      onNodesChange={onNodesChange}
      nodesDraggable={false}
      nodesConnectable={false}
      elementsSelectable
      onNodeClick={(_, node) => onSelectActivity(nodeRuntimeId(node))}
      onPaneClick={() => onSelectEvidence(null)}
    >
      <Background />
      <MiniMap pannable zoomable />
      <Controls />
    </ReactFlow>
  );
}

function mergeMeasuredNodeDimensions(
  nodes: Node<WorkflowNodeData>[],
  measuredNodes: Node<WorkflowNodeData>[]
) {
  const measuredById = new Map(measuredNodes.map(node => [node.id, node]));
  return nodes.map(node => {
    const measuredNode = measuredById.get(node.id);
    return measuredNode && measuredNode.type === node.type && measuredNode.measured
      ? { ...node, measured: measuredNode.measured }
      : node;
  });
}

// Builds the read-only run-inspection canvas for the scope addressed by `frames`. Slot badges navigate
// along the same planned frame path as the editor (planSlotNavigation): a slot holding a single canvas
// container is descended THROUGH so the canvas shows the container's contents instead of a one-node
// canvas holding the container. Only the plan's frames are used — selection stays evidence-driven.
export function buildInstanceCanvas(
  definitionVersion: WorkflowDefinitionVersionDetails | null,
  activityCatalog: ActivityCatalogItem[],
  details: WorkflowInstanceDetails,
  selectedEvidenceId: string | null,
  frames: ScopeFrame[],
  onNavigateToScope: (frames: ScopeFrame[]) => void,
  onOpenIncident: (incidentId: string, nodeId?: string | null) => void = () => undefined,
  associationActivities: ActivityExecutionStateSummary[] = details.activities,
  executableGraph: ExecutableActivityGraph | null = null,
  journalOf?: (scopeActivityExecutionId: string | undefined) => CompensationJournalSource | undefined
): {
  nodes: Node<WorkflowNodeData>[];
  edges: Edge<WorkflowEdgeData>[];
  mode: "sequence" | "graph" | "compensation";
  compensation?: { owner: ActivityNode; runtime: CompensationScopeRuntime; openSlot(ownerNodeId: string, ownerLabel: string, slot: ChildSlot): void };
} {
  const root = definitionVersion?.state.rootActivity;
  if (!definitionVersion || !root) return { nodes: [], edges: [], mode: "graph" };

  const scope = resolveScope(root, frames, activityCatalog);
  const scopeOwner = scope?.owner ?? root;
  const scopeOwnerCatalogItem = activityCatalog.find(activity => activity.activityVersionId === scopeOwner.activityVersionId);
  const support = getActivityDesignerSupport(scopeOwner, scopeOwnerCatalogItem);
  const baseCanvas = support === "bpmn" && scope
    ? buildBpmnCanvas(scope, activityCatalog, definitionVersion.layout, definitionVersion.activityPresentation)
    : support === "unsupported" || !scope
    ? buildUnsupportedActivityCanvas(
        scopeOwner,
        activityCatalog,
        definitionVersion.layout,
        formatActivitySummary,
        definitionVersion.activityPresentation)
    : buildCanvas(
        scope,
        activityCatalog,
        definitionVersion.layout,
        formatActivitySummary,
        definitionVersion.activityPresentation);
  const readonlyNodes = baseCanvas.nodes.map(node => {
    const bpmnData = node.data as BpmnNodeData;
    const runtimeNodeId = bpmnData.runtimeNodeId ?? bpmnData.boundActivity?.nodeId ?? node.id;
    const slotOwnerNodeId = bpmnData.boundActivity?.nodeId ?? runtimeNodeId;
    return {
      ...node,
      draggable: false,
      connectable: false,
      deletable: false,
      data: {
        ...node.data,
        runtimeNodeId,
        onEnterSlot: (slot: ChildSlot) => {
          const plan = planSlotNavigation(frames, scopeOwner, slotOwnerNodeId, slot, slotCrumbLabel(node.data.label, slot), activityCatalog);
          if (plan) onNavigateToScope(plan.frames);
        },
        onIncidentClick: (incidentId: string, targetNodeId?: string | null) =>
          onOpenIncident(incidentId, targetNodeId === undefined ? runtimeNodeId : targetNodeId)
      }
    } as Node<WorkflowNodeData>;
  });

  const canvas = decorateWorkflowCanvasElements(
    applyRuntimeOverlays(readonlyNodes, associationActivities, Array.isArray(details.incidents) ? details.incidents : [], selectedEvidenceId, activityCatalog, executableGraph),
    baseCanvas.edges.map(edge => ({ ...edge, deletable: false }))
  );
  if (support !== "unsupported" && scope?.slot.id === compensationStepsSlotId) {
    const incidents = Array.isArray(details.incidents) ? details.incidents : [];
    const options = { complete: !details.activityNextContinuationToken, selectedActivityExecutionId: selectedEvidenceId, root };
    const derived = deriveCompensationRuntime(scopeOwner, associationActivities, incidents, options);
    const journal = journalOf?.(derived.scopeActivityExecutionId);
    // The scope's name as the canvas shows it, for the operator actions' labels.
    const scopeLabel = resolveActivityLabel(
      definitionVersion.activityPresentation?.find(record => record.nodeId === scopeOwner.nodeId),
      scopeOwnerCatalogItem,
      scopeOwner.activityVersionId);
    return {
      ...canvas,
      mode: "compensation",
      compensation: {
        owner: scopeOwner,
        runtime: { ...(journal ? deriveCompensationRuntime(scopeOwner, associationActivities, incidents, { ...options, journal }) : derived), scopeLabel },
        openSlot: (ownerNodeId, ownerLabel, slot) => {
          const plan = planSlotNavigation(frames, scopeOwner, ownerNodeId, slot, slotCrumbLabel(ownerLabel, slot), activityCatalog);
          if (plan) onNavigateToScope(plan.frames);
        }
      }
    };
  }

  return {
    ...canvas,
    mode: support !== "unsupported" && scope?.slot.mode === "sequence" && scope.slot.cardinality === "many"
      ? "sequence" : "graph"
  };
}

function nodeRuntimeId(node: Node) {
  const data = node.data as Partial<WorkflowNodeData> & { boundActivity?: { nodeId?: string } };
  return data.runtimeNodeId ?? data.boundActivity?.nodeId ?? node.id;
}

function getInstanceScopeKey(frames: ScopeFrame[]) {
  if (frames.length === 0) return "root";
  return frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");
}

function collectWorkflowGraphNodeIds(definitionVersion: WorkflowDefinitionVersionDetails, activityCatalog: ActivityCatalogItem[]) {
  const ids = new Set<string>();
  const visit = (activity: ActivityNode | null | undefined) => {
    if (!activity) return;
    ids.add(activity.nodeId);
    for (const slot of getChildSlots(activity, activityCatalog)) {
      slot.activities.forEach(visit);
    }
  };
  visit(definitionVersion.state.rootActivity);
  return ids;
}

export function getIncidentHealthActionState(incidents?: IncidentStateSummary[] | null) {
  const available = Array.isArray(incidents);
  const activeIncidents = available ? incidents.filter(isActiveIncident) : [];
  const blockingCount = activeIncidents.filter(incident => incident.isBlocking).length;
  const health = !available ? "unavailable" : blockingCount > 0 ? "blocking" : activeIncidents.length > 0 ? "active" : "none";
  const label = !available
    ? "Incident health unavailable"
    : blockingCount > 0
      ? `Needs intervention · ${blockingCount} blocking · ${activeIncidents.length} active`
      : activeIncidents.length > 0
        ? `${activeIncidents.length} active non-blocking`
        : "No active incidents";

  return { health, label };
}

function IncidentHealthAction({ incidents, onClick }: { incidents?: IncidentStateSummary[] | null; onClick(): void }) {
  const { health, label } = getIncidentHealthActionState(incidents);

  return (
    <button type="button" className="wf-run-incident-action" data-health={health} aria-label={`Open run incidents: ${label}`} onClick={onClick}>
      <AlertCircle size={14} aria-hidden="true" /> {label}
    </button>
  );
}

function WorkflowInstanceInspector({
  activityContent,
  context,
  summary,
  details,
  state,
  error,
  selectedEvidenceId = null,
  associationActivities,
  associationLookupIncomplete = false,
  associationLookupSupported = false,
  associationLookupMessage = null,
  pendingIncidentActivityCount = 0,
  unavailableActivityFailures = [],
  onSelectEvidence,
  activeTab,
  onActiveTabChange,
  onShowAffectedActivity,
  onViewInput,
  onLoadMoreIncidentAssociations,
  focusInputKey,
  onSelectActivityExecution,
  renderIncidentActions,
  guidanceForIncident,
  onOpenDefinition,
  definitionAccess,
  graphNodeIds,
  rootNodeId,
  activityCatalog = [],
  executableGraph,
  definitionName,
  expressionEditors = [],
  collapsed = false,
  expanded = true,
  maximized = false,
  layoutMode = "desktop",
  responsiveOpen = false,
  onCloseResponsive,
  onToggleCollapsed,
  onToggleMaximized
}: {
  context: StudioEndpointContext;
  summary: WorkflowInstanceSummary | null;
  activityContent?: ReactNode;
  details: WorkflowInstanceDetails | null;
  associationActivities?: ActivityExecutionStateSummary[];
  associationLookupIncomplete?: boolean;
  associationLookupSupported?: boolean;
  associationLookupMessage?: string | null;
  pendingIncidentActivityCount?: number;
  unavailableActivityFailures?: UnavailableIncidentActivityFailure[];
  state: "idle" | "loading" | "ready" | "failed";
  error: string;
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
  activeTab: InstanceInspectorTab;
  onActiveTabChange(tab: InstanceInspectorTab): void;
  onShowAffectedActivity(incident: IncidentStateSummary): void;
  onViewInput(incident: IncidentStateSummary): void;
  onLoadMoreIncidentAssociations(): Promise<void>;
  focusInputKey: string | null;
  onSelectActivityExecution(activityExecutionId: string): void;
  renderIncidentActions?: OperatorActionsApi["renderIncidentActions"];
  guidanceForIncident?: OperatorActionsApi["guidanceForIncident"];
  onOpenDefinition?(definitionId: string): void;
  definitionAccess?: DefinitionAccessObservation;
  graphNodeIds?: Set<string>;
  rootNodeId?: string | null;
  activityCatalog?: ActivityCatalogItem[];
  executableGraph?: ExecutableActivityGraph | null;
  definitionName?: string | null;
  expressionEditors?: StudioExpressionEditorContribution[];
  collapsed?: boolean;
  expanded?: boolean;
  maximized?: boolean;
  layoutMode?: RunDetailLayoutMode;
  responsiveOpen?: boolean;
  onCloseResponsive?(): void;
  onToggleCollapsed?(): void;
  onToggleMaximized?(): void;
}) {
  const inspectorRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!responsiveOpen) return;
    const inspector = inspectorRef.current;
    const firstControl = inspector?.querySelector<HTMLElement>("button:not([disabled]), [href], [tabindex]:not([tabindex='-1'])");
    requestAnimationFrame(() => (firstControl ?? inspector)?.focus());
  }, [responsiveOpen]);

  if (!summary) {
    return <aside className="wf-instance-inspector"><div className="wf-empty">Select a workflow run to inspect its timeline.</div></aside>;
  }

  const incidents = Array.isArray(details?.incidents) ? details.incidents : [];
  const incidentEvidenceAvailable = Array.isArray(details?.incidents);
  const incidentCount = incidents.length;
  const activityAssociations = associationActivities ?? details?.activities ?? [];
  const selectedActivity = findSelectedActivityExecution(activityAssociations, selectedEvidenceId, incidents);
  const openActivityEvidence = (evidenceId: string) => {
    onSelectActivityExecution(evidenceId);
  };
  const tabs: WorkflowEditorPanelTab[] = [
    { id: "timeline", title: "Timeline", order: 0, icon: <ListTree size={14} />, render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: <ActivityIcon size={14} />, render: () => null },
    { id: "issues", title: incidentCount > 0 ? `Issues (${incidentCount})` : "Issues", order: 2, icon: <AlertCircle size={14} />, render: () => null },
    { id: "details", title: "Details", order: 3, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  return (
    <aside
      ref={inspectorRef}
      className="wf-instance-inspector"
      aria-label="Run details panel"
      role={layoutMode === "medium" ? "dialog" : undefined}
      aria-modal={layoutMode === "medium" ? true : undefined}
      tabIndex={layoutMode === "medium" ? -1 : undefined}
      onKeyDown={event => {
        if (layoutMode === "medium" && event.key === "Escape") {
          event.preventDefault();
          onCloseResponsive?.();
        }
        if (layoutMode === "medium" && event.key === "Tab") trapFocusWithin(event, inspectorRef.current);
      }}
    >
      <div className="wf-panel-title wf-instance-panel-title">
        <PanelTabList label="Run details tabs" tabs={tabs} activeTabId={activeTab} onSelect={tabId => onActiveTabChange(tabId as InstanceInspectorTab)} />
        <span className="wf-panel-actions">
          {layoutMode !== "desktop" ? (
            <button
              type="button"
              className="wf-panel-action-button"
              aria-label={layoutMode === "narrow" ? "Back to workflow run canvas" : "Close run details panel"}
              title={layoutMode === "narrow" ? "Back to canvas" : "Close"}
              onClick={onCloseResponsive}
            >
              <ChevronLeft size={14} />
            </button>
          ) : null}
          {layoutMode === "desktop" ? (
          <button
            type="button"
            className="wf-panel-action-button"
            aria-label={collapsed ? "Expand run details panel" : "Collapse run details panel"}
            title={collapsed ? "Expand" : "Collapse"}
            onClick={onToggleCollapsed}
          >
            {collapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
          </button>
          ) : null}
          {layoutMode === "desktop" && !collapsed ? (
            <button
              type="button"
              className="wf-panel-action-button"
              aria-label={maximized ? "Restore run details panel" : "Maximize run details panel"}
              title={maximized ? "Restore" : "Maximize"}
              onClick={onToggleMaximized}
            >
              {maximized ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
            </button>
          ) : null}
        </span>
      </div>
      {expanded ? (
        <>
          {state === "loading" ? <div className="wf-empty">Loading run details...</div> : null}
          {state === "failed" ? <WfErrorCard message={error} /> : null}
          {state === "ready" && details ? (
            <div className="wf-instance-tab-content">
              {activeTab === "timeline" ? (
                <>
                  {details.activityNextContinuationToken ? <p className="wf-instance-note" role="note">Runtime returned another activity page. This timeline shows the current bounded page; exact incident links can load an associated activity separately.</p> : null}
                  <WorkflowExecutionTimeline
                    activities={details.activities}
                    activityCatalog={activityCatalog}
                    executableGraph={executableGraph}
                    selectedEvidenceId={selectedEvidenceId}
                    onSelectEvidence={openActivityEvidence}
                  />
                </>
              ) : activeTab === "activity" ? (activityContent ??
                <WorkflowActivityExecutionDetails
                  context={context}
                  activity={selectedActivity}
                  activityCatalog={activityCatalog}
                  executableNodeFacts={findExecutableNodeFacts(executableGraph, selectedActivity)}
                  expressionEditors={expressionEditors}
                  focusInputKey={focusInputKey}
                  onSelectEvidence={openActivityEvidence}
                />
              ) : activeTab === "issues" ? (
                <>
                  <WorkflowIncidentList
                    incidents={incidentEvidenceAvailable ? incidents : undefined}
                    activities={activityAssociations}
                    executableGraph={executableGraph}
                    associationLookupIncomplete={associationLookupIncomplete}
                    associationLookupSupported={associationLookupSupported}
                    associationLookupMessage={associationLookupMessage}
                    pendingIncidentActivityCount={pendingIncidentActivityCount}
                    unavailableActivityFailures={unavailableActivityFailures}
                    activityCatalog={activityCatalog}
                    selectedEvidenceId={selectedEvidenceId}
                    onSelectEvidence={onSelectEvidence}
                    onShowAffectedActivity={onShowAffectedActivity}
                    onViewInput={onViewInput}
                    onLoadMoreIncidentAssociations={onLoadMoreIncidentAssociations}
                    renderIncidentActions={renderIncidentActions}
                    context={context}
                    summary={summary}
                    guidanceForIncident={guidanceForIncident}
                    onOpenDefinition={onOpenDefinition}
                    definitionAccess={definitionAccess}
                  />
                  <WorkflowUnmatchedEvidence details={details} graphNodeIds={graphNodeIds} rootNodeId={rootNodeId} activityCatalog={activityCatalog} executableGraph={executableGraph} />
                </>
              ) : (
                <>
                  <dl className="wf-instance-meta">
                    <dt>Status</dt>
                    <dd><WorkflowStatusBadge status={summary.status} subStatus={summary.subStatus} /></dd>
                    <dt>Run Kind</dt>
                    <dd>{formatRunKind(summary.runKind)}</dd>
                    <dt>Artifact</dt>
                    <dd>{summary.artifactId} <small>{summary.artifactVersion}</small></dd>
                    <dt>Definition</dt>
                    <dd>
                      <NamedIdentity
                        name={definitionName}
                        id={summary.definitionId}
                        secondary={summary.definitionVersionId}
                      />
                    </dd>
                    <dt>Created</dt>
                    <dd>{formatDate(summary.createdAt)}</dd>
                    <dt>Started</dt>
                    <dd>{formatDate(summary.startedAt)}</dd>
                    <dt>Completed</dt>
                    <dd>{formatDate(summary.completedAt)}</dd>
                    <dt>Correlation</dt>
                    <dd>{summary.correlationId || "None"}</dd>
                    <WorkflowInstanceInspectionFacts
                      persistenceMode={details.persistenceMode}
                      inspectionGranularity={details.inspectionGranularity}
                    />
                  </dl>
                  <WorkflowInstanceInspectionNote inspectionGranularity={details.inspectionGranularity} />
                </>
              )}
            </div>
          ) : null}
        </>
      ) : null}
    </aside>
  );
}

export interface IncidentActivityAssociation {
  nodeId: string | null;
  activityExecution: ActivityExecutionStateSummary | null;
}

export function resolveIncidentActivityAssociation(
  incident: IncidentStateSummary,
  activities: ActivityExecutionStateSummary[],
  executableGraph?: ExecutableActivityGraph | null
): IncidentActivityAssociation | null {
  const getNodeId = (activity: ActivityExecutionStateSummary) =>
    findExecutableGraphNodeId(executableGraph, activity) ?? (activity.authoredActivityId || activity.executableNodeId || null);
  const explicitExecutionId = readIncidentActivityExecutionId(incident);
  const exactExecution = explicitExecutionId
    ? activities.find(activity => activity.activityExecutionId === explicitExecutionId)
    : undefined;
  if (exactExecution) {
    return { nodeId: getNodeId(exactExecution), activityExecution: exactExecution };
  }

  const incidentExecutions = explicitExecutionId
    ? []
    : activities.filter(activity => activity.incidentIds?.includes(incident.incidentId) ?? false);
  if (incidentExecutions.length === 1) {
    const exact = incidentExecutions[0]!;
    return { nodeId: getNodeId(exact), activityExecution: exact };
  }

  const explicitNodeId = incident.executableNodeId?.trim() || incident.metadata?.["runtime.executableNodeId"]?.trim();
  if (explicitNodeId) {
    const pinnedNodeId = findExecutableGraphNodeId(executableGraph, { executableNodeId: explicitNodeId });
    if (pinnedNodeId) return { nodeId: pinnedNodeId, activityExecution: null };
    const nodeActivity = activities.find(activity => activity.executableNodeId === explicitNodeId);
    if (!nodeActivity) {
      const authoredMatches = activities.filter(activity => activity.authoredActivityId === explicitNodeId);
      if (new Set(authoredMatches.map(activity => activity.executableNodeId)).size > 1) {
        return { nodeId: null, activityExecution: null };
      }
      if (authoredMatches.length === 1) return { nodeId: getNodeId(authoredMatches[0]!), activityExecution: null };
    }
    const nodeId = nodeActivity ? getNodeId(nodeActivity) : explicitNodeId;
    return { nodeId, activityExecution: null };
  }

  const incidentNodeIds = [...new Set(incidentExecutions.map(getNodeId).filter(Boolean))];
  return incidentNodeIds.length === 1
    ? { nodeId: incidentNodeIds[0]!, activityExecution: null }
    : null;
}

function choosePreferredIncident(incidents: IncidentStateSummary[]) {
  return [...incidents].sort((left, right) =>
    Number(right.isBlocking) - Number(left.isBlocking) || Date.parse(right.createdAt) - Date.parse(left.createdAt))[0] ?? null;
}

function readIncidentInputKey(incident: IncidentStateSummary) {
  return incident.metadata?.["runtime.inputKey"]?.trim() || null;
}

function readIncidentActivityExecutionId(incident: IncidentStateSummary) {
  return incident.activityExecutionId?.trim() || incident.metadata?.["runtime.activityExecutionId"]?.trim() || null;
}

export function getIncidentRootCause(incident: IncidentStateSummary) {
  const rootCause = incident.metadata?.["runtime.faultInnerMessage"]?.trim();
  return rootCause || incident.message;
}

function findSelectedActivityExecution(
  activities: ActivityExecutionStateSummary[],
  selectedEvidenceId: string | null,
  incidents: IncidentStateSummary[] = []
) {
  if (!selectedEvidenceId) return null;

  const selectedIncident = incidents.find(incident => incident.incidentId === selectedEvidenceId);
  if (selectedIncident) return resolveIncidentActivityAssociation(selectedIncident, activities)?.activityExecution ?? null;

  const exactActivity = activities.find(activity => activity.activityExecutionId === selectedEvidenceId);
  if (exactActivity) return exactActivity;

  const nodeActivities = activities.filter(activity =>
    activity.executableNodeId === selectedEvidenceId ||
    activity.authoredActivityId === selectedEvidenceId);
  return nodeActivities.length > 0 ? latestActivityExecution(nodeActivities) : null;
}

export function focusSelectedCanvasActivity(selectedEvidenceId: string | null) {
  if (!selectedEvidenceId) return;
  const node = [...document.querySelectorAll<HTMLElement>(".wf-instance-canvas [data-id]")]
    .find(element => element.dataset.id === selectedEvidenceId || element.dataset.runtimeNodeId === selectedEvidenceId ||
      [...element.querySelectorAll<HTMLElement>("[data-runtime-node-id]")]
        .some(runtimeNode => runtimeNode.dataset.runtimeNodeId === selectedEvidenceId));
  node?.focus();
}

function trapFocusWithin(event: ReactKeyboardEvent<HTMLElement>, container: HTMLElement | null) {
  if (!container) return;
  const controls = [...container.querySelectorAll<HTMLElement>(
    "button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])"
  )].filter(element => !element.hidden && element.getAttribute("aria-hidden") !== "true");
  if (controls.length === 0) {
    event.preventDefault();
    container.focus();
    return;
  }

  const first = controls[0]!;
  const last = controls.at(-1)!;
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

const incidentStackTraceMetadataKeys = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];

export function WorkflowIncidentList({
  context,
  summary,
  incidents,
  activities = [],
  activityCatalog = [],
  executableGraph,
  associationLookupIncomplete = false,
  associationLookupSupported = false,
  associationLookupMessage = null,
  pendingIncidentActivityCount = 0,
  unavailableActivityFailures = [],
  selectedEvidenceId = null,
  onSelectEvidence,
  onShowAffectedActivity,
  onViewInput,
  onLoadMoreIncidentAssociations,
  renderIncidentActions,
  guidanceForIncident,
  onOpenDefinition,
  definitionAccess
}: {
  context?: StudioEndpointContext;
  summary?: WorkflowInstanceSummary | null;
  incidents?: IncidentStateSummary[] | null;
  activities?: ActivityExecutionStateSummary[];
  activityCatalog?: ActivityCatalogItem[];
  executableGraph?: ExecutableActivityGraph | null;
  associationLookupIncomplete?: boolean;
  associationLookupSupported?: boolean;
  associationLookupMessage?: string | null;
  pendingIncidentActivityCount?: number;
  unavailableActivityFailures?: UnavailableIncidentActivityFailure[];
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
  onShowAffectedActivity?(incident: IncidentStateSummary): void;
  onViewInput?(incident: IncidentStateSummary): void;
  onLoadMoreIncidentAssociations?(): Promise<void>;
  /** Spec 260: the operator actions of a waiting incident, shown in its details dialog. */
  renderIncidentActions?: OperatorActionsApi["renderIncidentActions"];
  guidanceForIncident?: OperatorActionsApi["guidanceForIncident"];
  onOpenDefinition?(definitionId: string): void;
  definitionAccess?: DefinitionAccessObservation;
}) {
  const [copyStatus, setCopyStatus] = useState("");
  const [loadingAssociations, setLoadingAssociations] = useState(false);
  const [incidentDetailsId, setIncidentDetailsId] = useState<string | null>(null);
  const incidentEvidenceAvailable = Array.isArray(incidents);
  const incidentRows = incidents ?? [];
  const incidentDetails = incidentRows.find(incident => incident.incidentId === incidentDetailsId) ?? null;
  const incidentGuidance = incidentDetails
    ? guidanceForIncident?.(incidentDetails) ?? {
        actionAvailability: "unknown" as const,
        canAct: false,
        pendingIncidentId: null,
        requesterCancelled: false,
        retryReviewEligibility: { status: "unknown", reason: "source-not-loaded" }
      }
    : undefined;
  const permissionDeniedCount = unavailableActivityFailures.filter(failure => failure.reason === "permission-denied").length;
  const identityMismatchCount = unavailableActivityFailures.filter(failure => failure.reason === "identity-mismatch").length;

  return (
    <section className="wf-instance-section">
      <h4>Incidents</h4>
      {!incidentEvidenceAvailable ? <p>Incident evidence is unavailable from this Runtime host.</p> : null}
      {associationLookupIncomplete ? (
        <div className="wf-run-association-status" role="status">
          <p className="wf-instance-note">{associationLookupSupported
            ? `Some affected activities are not shown yet (${pendingIncidentActivityCount} remaining).`
            : "This server cannot provide exact activity evidence."}</p>
          {associationLookupSupported && onLoadMoreIncidentAssociations ? (
            <button
              type="button"
              className="wf-run-incident-action"
              data-health="active"
              disabled={loadingAssociations}
              onClick={() => {
                setLoadingAssociations(true);
                void onLoadMoreIncidentAssociations().finally(() => setLoadingAssociations(false));
              }}
            >
              {loadingAssociations ? "Loading affected activities..." : `Load more affected activities (${pendingIncidentActivityCount})`}
            </button>
          ) : null}
        </div>
      ) : null}
      {unavailableActivityFailures.length > 0 ? (
        <div className="wf-run-association-status" role="status">
          {permissionDeniedCount > 0 ? (
            <p>You do not have permission to inspect {permissionDeniedCount} exact activity execution{permissionDeniedCount === 1 ? "" : "s"}. The incident{permissionDeniedCount === 1 ? " remains" : "s remain"} available here.</p>
          ) : null}
          {identityMismatchCount > 0 ? (
            <p>Runtime returned a different run or activity execution for {identityMismatchCount} association{identityMismatchCount === 1 ? "" : "s"}. Studio kept those incidents open and did not open the returned execution.</p>
          ) : null}
        </div>
      ) : null}
      {associationLookupMessage ? <p className="wf-run-association-status" role="status">{associationLookupMessage}</p> : null}
      {incidentEvidenceAvailable && incidentRows.length === 0 ? <p>No incidents recorded.</p> : null}
      {incidentRows.map(incident => (
        (() => {
          const association = resolveIncidentActivityAssociation(incident, activities, executableGraph);
          const inputKey = readIncidentInputKey(incident);
          const expressionLanguage = incident.metadata?.["runtime.expressionLanguage"];
          const activityFacts = findExecutableNodeFacts(executableGraph, association?.activityExecution) ??
            (association?.nodeId ? executableGraph?.factsByNodeId.get(association.nodeId) : undefined);
          const labelActivity = association?.activityExecution ?? activityFacts;
          const activityLabel = getIncidentActivityLabel(labelActivity, activityCatalog, activityFacts?.presentation);
          const inputLabel = getIncidentInputLabel(association?.activityExecution, activityCatalog, inputKey, activityFacts);
          const incidentHealthLabel = !isActiveIncident(incident)
            ? "Historical"
            : incident.isBlocking ? "Needs intervention" : "Non-blocking";
          return (
            <article
              className="wf-instance-incident"
              data-incident-id={incident.incidentId}
              data-severity={incident.severity.toLowerCase()}
              data-selected={incident.incidentId === selectedEvidenceId}
              key={incident.incidentId}
            >
              <button
                type="button"
                className="wf-instance-incident-summary"
                onClick={() => onSelectEvidence?.(incident.incidentId)}
              >
                <strong>{activityLabel ? `${activityLabel}${inputLabel ? ` · ${inputLabel}` : ""}` : inputLabel ? `Input · ${inputLabel}` : incident.failureType}</strong>
                <p className="wf-instance-incident-cause">{getIncidentRootCause(incident)}</p>
                <span className="wf-instance-incident-status" data-health={incident.isBlocking && isActiveIncident(incident) ? "blocking" : "neutral"}>{incidentHealthLabel} · {incident.status}</span>
                {expressionLanguage ? <small>Expression language · {expressionLanguage}</small> : null}
              </button>
              <div className="wf-instance-incident-actions">
                {association?.nodeId || hasIncidentActivityExecutionId(incident) ? (
                  <button type="button" onClick={() => onShowAffectedActivity?.(incident)}>Show affected activity</button>
                ) : <span className="wf-instance-note">Run-level issue · no activity association was recorded.</span>}
                {inputKey && (association?.activityExecution || hasIncidentActivityExecutionId(incident)) ? (
                  <button type="button" onClick={() => onViewInput?.(incident)}>View input evidence</button>
                ) : inputKey ? (
                  <span className="wf-instance-note">Input key is recorded, but its exact activity execution is unavailable.</span>
                ) : null}
                <button type="button" className="wf-incident-open-details" onClick={() => setIncidentDetailsId(incident.incidentId)}>Open incident details</button>
                <CopyValueButton
                  value={formatIncidentForClipboard(incident)}
                  ariaLabel={`Copy incident ${incident.failureType}`}
                  copiedLabel="incident"
                  textLabel="Copy incident details"
                  onCopied={label => setCopyStatus(`Copied ${label}`)}
                  onCopyFailed={label => setCopyStatus(`Could not copy ${label}.`)}
                />
              </div>
              <details className="wf-incident-technical-details">
                <summary>Technical details</summary>
                <IncidentTechnicalContent incident={incident} />
              </details>
            </article>
          );
        })()
      ))}
      {copyStatus ? <p className="wf-copy-status" role="status">{copyStatus}</p> : null}
      {incidentDetails ? (
        <IncidentDetailsDialog
          incident={incidentDetails}
          context={context}
          summary={summary}
          guidance={incidentGuidance}
          onOpenDefinition={onOpenDefinition}
          definitionAccess={definitionAccess}
          onClose={() => setIncidentDetailsId(null)}
          actions={renderIncidentActions?.(
            incidentDetails,
            incidentActivityLabel(incidentDetails, activities, activityCatalog, executableGraph),
            () => setIncidentDetailsId(null),
            () => document.querySelector<HTMLElement>(`.wf-instance-incident[data-incident-id="${CSS.escape(incidentDetails.incidentId)}"] .wf-incident-open-details`)?.focus())}
        />
      ) : null}
    </section>
  );
}

export function getIncidentActivityLabel(
  activity: Pick<ActivityExecutionStateSummary, "activityType" | "activityTypeVersion"> | null | undefined,
  catalog: ActivityCatalogItem[],
  presentation?: ExecutableGraphNodeFacts["presentation"]
) {
  if (!activity) return null;
  const catalogItem = catalog.find(item => item.activityTypeKey === activity.activityType && item.version === activity.activityTypeVersion);
  return resolveActivityLabel(presentation, catalogItem, activity.activityType);
}

function hasIncidentActivityExecutionId(incident: IncidentStateSummary) {
  return Boolean(readIncidentActivityExecutionId(incident));
}

export function getIncidentInputLabel(
  activity: Pick<ActivityExecutionStateSummary, "activityType" | "activityTypeVersion"> | null | undefined,
  catalog: ActivityCatalogItem[],
  inputKey: string | null,
  activityFacts?: Pick<ExecutableGraphNodeFacts, "inputBindings">
) {
  if (!inputKey) return null;
  const exactActivityVersion = activity
    ? catalog.find(item => item.activityTypeKey === activity.activityType && item.version === activity.activityTypeVersion)
    : undefined;
  const descriptor = exactActivityVersion?.inputs
    .find(input => {
      if (!input || typeof input !== "object") return false;
      const record = input as Record<string, unknown>;
      const referenceKey = typeof record.referenceKey === "string" ? record.referenceKey.trim() : "";
      return referenceKey ? referenceKey === inputKey : record.name === inputKey;
    }) as StudioActivityInputDescriptor | undefined;
  const displayName = descriptor?.displayName?.trim();
  const frozenInputName = activityFacts?.inputBindings.find(binding => (binding.inputKey?.trim() || binding.inputName) === inputKey)?.inputName.trim();
  const inputLabel = displayName && displayName !== inputKey
    ? `${displayName} input`
    : frozenInputName && frozenInputName !== inputKey
      ? `${frozenInputName} input`
      : `${inputKey} input`;
  return inputLabel;
}

function IncidentStackTrace({ incident }: { incident: IncidentStateSummary }) {
  const stackTrace = getIncidentStackTrace(incident);
  if (!stackTrace) return null;

  return (
    <details className="wf-incident-stacktrace">
      <summary>Stack trace</summary>
      <pre tabIndex={0}>{stackTrace}</pre>
    </details>
  );
}

function IncidentTechnicalContent({ incident }: { incident: IncidentStateSummary }) {
  const activityExecutionId = readIncidentActivityExecutionId(incident);
  const inputKey = readIncidentInputKey(incident);
  const inputFailureCode = incident.metadata?.["runtime.inputFailureCode"];
  const expressionLanguage = incident.metadata?.["runtime.expressionLanguage"];
  const inputPhase = incident.metadata?.["runtime.inputEvaluationPhase"];
  const innerType = incident.metadata?.["runtime.faultInnerType"];
  const hasMetadata = Boolean(incident.metadata && Object.keys(incident.metadata).length > 0);

  return (
    <div className="wf-incident-technical-content">
      <section className="wf-incident-message-section" aria-label="Incident message">
        <h5>Message</h5>
        <p className="wf-incident-message">{incident.message}</p>
      </section>
      <dl className="wf-incident-metadata">
        <dt>Failure type</dt><dd><code>{incident.failureType}</code></dd>
        <dt>Incident ID</dt><dd><code>{incident.incidentId}</code></dd>
        <dt>Workflow execution ID</dt><dd><code>{incident.workflowExecutionId}</code></dd>
        {activityExecutionId ? <><dt>Activity execution ID</dt><dd><code>{activityExecutionId}</code></dd></> : null}
        {incident.executableNodeId ? <><dt>Executable node ID</dt><dd><code>{incident.executableNodeId}</code></dd></> : null}
        <dt>Status</dt><dd>{incident.status}</dd>
        <dt>Severity</dt><dd>{incident.severity}</dd>
        <dt>Blocking</dt><dd>{incident.isBlocking ? "Yes" : "No"}</dd>
        {incident.resolutionOutcome ? <><dt>Resolution action</dt><dd>{resolutionOutcomeLabel(incident.resolutionOutcome)}</dd></> : null}
        <dt>Created</dt><dd>{formatDate(incident.createdAt)}</dd>
        {incident.resolvedAt ? <><dt>Resolved</dt><dd>{formatDate(incident.resolvedAt)}</dd></> : null}
        {innerType ? <><dt>Root cause type</dt><dd><code>{innerType}</code></dd></> : null}
        {inputKey ? <><dt>Input key</dt><dd><code>{inputKey}</code></dd></> : null}
        {inputFailureCode ? <><dt>Input failure code</dt><dd><code>{inputFailureCode}</code></dd></> : null}
        {inputPhase ? <><dt>Input evaluation phase</dt><dd>{inputPhase}</dd></> : null}
        {expressionLanguage ? <><dt>Expression language</dt><dd>{expressionLanguage}</dd></> : null}
      </dl>
      <IncidentStackTrace incident={incident} />
      {hasMetadata ? (
        <details className="wf-incident-raw-metadata">
          <summary>View complete runtime metadata</summary>
          <pre>{JSON.stringify(incident.metadata, null, 2)}</pre>
        </details>
      ) : null}
    </div>
  );
}

// The label the incident list shows for an incident's activity, for the operator dialog's target line.
function incidentActivityLabel(
  incident: IncidentStateSummary,
  activities: ActivityExecutionStateSummary[],
  activityCatalog: ActivityCatalogItem[],
  executableGraph: ExecutableActivityGraph | null | undefined
) {
  const association = resolveIncidentActivityAssociation(incident, activities, executableGraph);
  const activityFacts = findExecutableNodeFacts(executableGraph, association?.activityExecution) ??
    (association?.nodeId ? executableGraph?.factsByNodeId.get(association.nodeId) : undefined);
  return getIncidentActivityLabel(association?.activityExecution ?? activityFacts, activityCatalog, activityFacts?.presentation) ?? incident.failureType;
}

function IncidentDetailsDialog({ incident, context, summary, guidance, onOpenDefinition, definitionAccess, onClose, actions }: {
  incident: IncidentStateSummary;
  context?: StudioEndpointContext;
  summary?: WorkflowInstanceSummary | null;
  guidance?: ReturnType<OperatorActionsApi["guidanceForIncident"]>;
  onOpenDefinition?(definitionId: string): void;
  definitionAccess?: DefinitionAccessObservation;
  onClose(): void;
  actions?: ReactNode;
}) {
  const dialogRef = useRef<HTMLElement | null>(null);
  const titleId = useId();
  const [copyStatus, setCopyStatus] = useState("");
  useDialogFocus(dialogRef, onClose);

  return (
    <div className="wf-dialog-backdrop" role="presentation" onMouseDown={event => {
      if (event.target === event.currentTarget) onClose();
    }}>
      <section
        ref={dialogRef}
        className="wf-dialog wf-incident-details-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onKeyDown={event => {
          if (event.key !== "Escape") return;
          event.preventDefault();
          event.stopPropagation();
          onClose();
        }}
      >
        <header className="wf-dialog-heading wf-incident-details-heading">
          <div>
            <span>Workflow incident</span>
            <h3 id={titleId}>Incident details</h3>
            <p>{getIncidentRootCause(incident)}</p>
          </div>
          <button type="button" className="wf-incident-details-close" aria-label="Close incident details" onClick={onClose}>Close</button>
        </header>
        <div className="wf-incident-details-body">
          <div className="wf-incident-details-actions">
            <CopyValueButton
              value={formatIncidentForClipboard(incident)}
              ariaLabel={`Copy incident ${incident.failureType}`}
              copiedLabel="incident"
              textLabel="Copy incident details"
              onCopied={() => setCopyStatus("Copied incident")}
              onCopyFailed={() => setCopyStatus("Could not copy incident.")}
            />
            {copyStatus ? <p className="wf-copy-status" role="status">{copyStatus}</p> : null}
          </div>
          {actions}
          {guidance ? <IncidentNextStep context={context} summary={summary} incident={incident} guidance={guidance} onOpenDefinition={onOpenDefinition} definitionAccess={definitionAccess} /> : null}
          <IncidentTechnicalContent incident={incident} />
        </div>
      </section>
    </div>
  );
}

export function getIncidentStackTrace(incident: IncidentStateSummary) {
  const directStackTrace = firstNonBlank(incident.stackTrace, incident.exceptionStackTrace);
  if (directStackTrace) return directStackTrace;

  for (const key of incidentStackTraceMetadataKeys) {
    const value = incident.metadata?.[key];
    if (value && value.trim()) return value;
  }

  return null;
}

function firstNonBlank(...values: Array<string | null | undefined>) {
  return values.find(value => value?.trim()) ?? null;
}

function NamedIdentity({
  name,
  id,
  secondary
}: {
  name?: string | null;
  id: string;
  secondary?: string | null;
}) {
  const identity = resolveNamedIdentity(name, id, [secondary]);
  const secondaryText = identity.secondaryIds.join(" · ") || null;
  return (
    <span className="wf-identity-cell" title={formatNamedIdentityTitle(identity.label, identity.secondaryIds)}>
      <strong>{identity.label}</strong>
      {secondaryText ? <small>{secondaryText}</small> : null}
    </span>
  );
}

function WorkflowUnmatchedEvidence({ details, graphNodeIds, rootNodeId, activityCatalog, executableGraph }: {
  details: WorkflowInstanceDetails;
  graphNodeIds?: Set<string>;
  rootNodeId?: string | null;
  activityCatalog?: ActivityCatalogItem[];
  executableGraph?: ExecutableActivityGraph | null;
}) {
  if (!graphNodeIds) return null;

  const unmatchedActivities = details.activities.filter(activity => {
    const nodeId = activityNodeKey(activity);
    return nodeId && nodeId !== rootNodeId && !graphNodeIds.has(nodeId);
  });

  if (unmatchedActivities.length === 0) return null;

  return (
    <section className="wf-instance-section">
      <h4>Executions outside canvas</h4>
      <div className="wf-instance-unmatched-list">
        {unmatchedActivities.map(activity => (
          <div className="wf-instance-unmatched" key={`activity-${activity.activityExecutionId}`}>
            <strong>{getIncidentActivityLabel(activity, activityCatalog ?? [], findExecutableNodeFacts(executableGraph, activity)?.presentation)}</strong>
            <small title={activity.activityExecutionId}>{activity.activityExecutionId}</small>
          </div>
        ))}
      </div>
    </section>
  );
}

function formatIncidentForClipboard(incident: IncidentStateSummary) {
  const lines = [
    `Incident: ${incident.failureType}`,
    `Incident ID: ${incident.incidentId}`,
    `Workflow execution ID: ${incident.workflowExecutionId}`,
    incident.activityExecutionId ? `Activity execution ID: ${incident.activityExecutionId}` : "",
    incident.executableNodeId ? `Executable node ID: ${incident.executableNodeId}` : "",
    `Status: ${incident.status}`,
    `Severity: ${incident.severity}`,
    `Blocking: ${incident.isBlocking ? "Yes" : "No"}`,
    incident.resolutionOutcome ? `Resolution action: ${resolutionOutcomeLabel(incident.resolutionOutcome)}` : "",
    `Created: ${formatDate(incident.createdAt)}`,
    incident.resolvedAt ? `Resolved: ${formatDate(incident.resolvedAt)}` : "",
    "",
    incident.message
  ].filter(Boolean);

  const stackTrace = getIncidentStackTrace(incident);
  if (stackTrace) lines.push("", "Stack trace:", stackTrace);

  if (incident.metadata && Object.keys(incident.metadata).length > 0) {
    lines.push("", "Metadata:", JSON.stringify(incident.metadata, null, 2));
  }

  return lines.join("\n");
}
