import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, Loader, OctagonX, RotateCw, UserCheck } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  getAlterationJob,
  getAlterationPlan,
  pageAlterationJobs,
  submitAlterationPlan,
  supportsOperatorAlterations
} from "../api/alterations";
import { getActivityStateView, supportsActivityStateView } from "../api/runtime";
import { isCompensationScope } from "../compensationScope";
import { isActiveIncident } from "../workflowRuntimeOverlays";
import {
  compensationDecisions,
  deriveCompensationRuntime,
  interventionDecisionLabel,
  interventionRequesterOf,
  isWaitingForIntervention,
  isRetryDue,
  offeredDecisions,
  readCompensationJournal,
  type CompensationJournalSource
} from "../compensationRuntime";
import {
  abandonActionModel,
  clearPendingOperatorAction,
  followOperatorPlan,
  incidentActionModel,
  mintIdempotencyKey,
  operatorActionPlan,
  operatorApplyingTimeoutMessage,
  operatorErrorMessage,
  operatorPollingTimeoutMessage,
  operatorRefusalMessage,
  readPendingOperatorAction,
  readWorkflowInstancePermissions,
  resolveActionModel,
  retryActionModel,
  storePendingOperatorAction,
  waitUntilApplied,
  type OperatorActionApi,
  type OperatorActionModel,
  type PendingOperatorAction
} from "../operatorActions";
import { findActivityNode } from "../workflowAdapter";
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import { OperatorActionDialog, type OperatorActionForm } from "./OperatorActionDialog";

// Spec 260 (T422, T423): the run view's operator actions. One action per run at a time: a confirmed action is submitted
// with the key minted when its dialog opened, kept in session storage, and followed until the run shows it.

/** The journals the run view has read, by scope occurrence. */
export interface CompensationJournals {
  get(scopeActivityExecutionId: string | undefined): CompensationJournalSource | undefined;
  /** Reads a scope occurrence's journal once; a completed scope keeps none, so it is never read. */
  request(scopeActivityExecutionId: string, activityStatus?: string): void;
  /** Reads it again, for a refresh while an action is being applied. */
  reload(scopeActivityExecutionId: string): Promise<CompensationJournalSource>;
  /** Forgets every journal read, so the shown scope's is read again (the run view's Refresh). */
  reset(): void;
  /** Changes on each reset, so a view that asks for its scope's journal asks again. */
  generation: number;
}

export function useCompensationJournals(context: StudioEndpointContext, workflowExecutionId: string): CompensationJournals {
  const [journals, setJournals] = useState<ReadonlyMap<string, CompensationJournalSource>>(() => new Map());
  // Bumped by a reset, so the shown scope asks for its journal again.
  const [generation, setGeneration] = useState(0);
  const requested = useRef(new Set<string>());
  const runRef = useRef(workflowExecutionId);

  const reset = useCallback(() => {
    requested.current = new Set();
    setJournals(new Map());
    setGeneration(current => current + 1);
  }, []);

  useEffect(() => {
    runRef.current = workflowExecutionId;
    reset();
  }, [reset, workflowExecutionId]);

  const reload = useCallback(async (scopeActivityExecutionId: string): Promise<CompensationJournalSource> => {
    const run = runRef.current;
    let result: CompensationJournalSource;
    try {
      result = await supportsActivityStateView(context)
        ? await getActivityStateView(context, run, scopeActivityExecutionId)
        : "unavailable";
    } catch {
      result = "unavailable";
    }
    if (runRef.current === run) setJournals(current => new Map(current).set(scopeActivityExecutionId, result));
    return result;
  }, [context]);

  const request = useCallback((scopeActivityExecutionId: string, activityStatus?: string) => {
    if (activityStatus === "Completed" || requested.current.has(scopeActivityExecutionId)) return;
    requested.current.add(scopeActivityExecutionId);
    setJournals(current => current.has(scopeActivityExecutionId) ? current : new Map(current).set(scopeActivityExecutionId, "loading"));
    void reload(scopeActivityExecutionId);
  }, [reload]);

  const get = useCallback((id: string | undefined) => (id ? journals.get(id) : undefined), [journals]);
  return { get, request, reload, reset, generation };
}

interface DialogState {
  model: OperatorActionModel;
  key: string;
  busy: boolean;
  error: string | null;
  trigger: HTMLElement | null;
  focusFallback?: () => void;
}

interface FollowedAction extends PendingOperatorAction {
  /** The label the row shows: "Retry submitted", then "Applying…". */
  label: string;
}

export interface UseOperatorActionsInput {
  context: StudioEndpointContext;
  workflowExecutionId: string;
  details: WorkflowInstanceDetails | null;
  root: ActivityNode | null | undefined;
  activities: ActivityExecutionStateSummary[];
  journals: CompensationJournals;
  labelOf(nodeId: string | undefined): string | undefined;
  /** Re-reads the run (details and incidents) without resetting the view, and returns what it read. */
  refreshRun(): Promise<WorkflowInstanceDetails | null>;
}

/** Read-only discovery state for the canonical operator action routes. */
export type OperatorActionAvailability = "checking" | "available" | "unavailable" | "unknown";

export type RetryReviewEligibility = "eligible" | "ineligible" | "unknown";

export type RetryReviewReason = "historical-incident" | "ended-run" | "source-not-faulted" | "parent-not-running" | "source-not-loaded" | "parent-not-loaded" | "blocking-descendant-incident" | "descendant-ancestry-not-loaded";

export interface IncidentRetryReviewEligibility {
  status: RetryReviewEligibility;
  reason?: RetryReviewReason;
  observedStatus?: string;
}

const endedWorkflowRunStatuses = new Set(["completed", "faulted", "cancelled", "canceled"]);

/** Local Retry review eligibility mirrors the existing server preflight; the server remains authoritative. */
export function getIncidentRetryReviewEligibility(
  incident: IncidentStateSummary,
  run: Pick<WorkflowInstanceSummary, "status"> | null | undefined,
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[] = [],
  workflowExecutionId = incident.workflowExecutionId
): IncidentRetryReviewEligibility {
  if (!isActiveIncident(incident)) return { status: "ineligible", reason: "historical-incident" };
  const runStatus = run?.status?.trim().toLowerCase();
  if (runStatus && endedWorkflowRunStatuses.has(runStatus)) return { status: "ineligible", reason: "ended-run" };

  const sourceId = incident.activityExecutionId?.trim() || incident.metadata?.["runtime.activityExecutionId"]?.trim();
  if (!sourceId) return { status: "unknown", reason: "source-not-loaded" };
  const source = activities.find(activity => activity.activityExecutionId === sourceId);
  if (!source || source.workflowExecutionId !== workflowExecutionId) return { status: "unknown", reason: "source-not-loaded" };
  const sourceStatus = source.status?.trim();
  if (!sourceStatus) return { status: "unknown", reason: "source-not-loaded" };
  if (sourceStatus.toLowerCase() !== "faulted") {
    return { status: "ineligible", reason: "source-not-faulted", observedStatus: sourceStatus };
  }

  const parentId = source.parentActivityExecutionId?.trim() || source.provenance?.parentActivityExecutionId?.trim();
  if (parentId) {
    const parent = activities.find(activity => activity.activityExecutionId === parentId);
    if (!parent || parent.workflowExecutionId !== workflowExecutionId) return { status: "unknown", reason: "parent-not-loaded" };
    const parentStatus = parent.status?.trim();
    if (!parentStatus) return { status: "unknown", reason: "parent-not-loaded" };
    if (parentStatus.toLowerCase() !== "running") {
      return { status: "ineligible", reason: "parent-not-running", observedStatus: parentStatus };
    }
  }

  const descendantBlocker = getBlockingDescendantStatus(incident, sourceId, workflowExecutionId, activities, incidents);
  if (descendantBlocker === "confirmed") {
    return { status: "ineligible", reason: "blocking-descendant-incident" };
  }
  if (descendantBlocker === "unknown") {
    return { status: "unknown", reason: "descendant-ancestry-not-loaded" };
  }

  return { status: "eligible" };
}

function getBlockingDescendantStatus(
  selectedIncident: IncidentStateSummary,
  sourceId: string,
  workflowExecutionId: string,
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[]
): "confirmed" | "unknown" | "none" {
  if (selectedIncident.workflowExecutionId !== workflowExecutionId) return "none";
  const activitiesById = new Map(activities.map(activity => [activity.activityExecutionId, activity] as const));
  const source = activitiesById.get(sourceId);
  if (!source || source.workflowExecutionId !== workflowExecutionId) return "none";

  let incompleteKnownAncestry = false;
  for (const blocker of incidents) {
    if (blocker.incidentId === selectedIncident.incidentId
      || blocker.workflowExecutionId !== workflowExecutionId
      || !blocker.isBlocking
      || !isActiveIncident(blocker)) continue;

    const blockerActivityId = blocker.activityExecutionId?.trim() || blocker.metadata?.["runtime.activityExecutionId"]?.trim();
    // An incident with no exact activity cannot be tied to an ancestry. Once it names an activity, missing or
    // cross-run rows leave that known blocker's relationship unresolved.
    if (!blockerActivityId || blockerActivityId === sourceId) continue;
    let current = activitiesById.get(blockerActivityId);
    if (!current || current.workflowExecutionId !== workflowExecutionId) {
      incompleteKnownAncestry = true;
      continue;
    }

    const visited = new Set<string>();
    while (current) {
      if (visited.has(current.activityExecutionId)) {
        incompleteKnownAncestry = true;
        break;
      }
      visited.add(current.activityExecutionId);
      const parentId = current.parentActivityExecutionId?.trim() || current.provenance?.parentActivityExecutionId?.trim();
      if (!parentId) break;
      if (parentId === sourceId) return "confirmed";
      const parent = activitiesById.get(parentId);
      if (!parent) {
        incompleteKnownAncestry = true;
        break;
      }
      if (parent.workflowExecutionId !== workflowExecutionId) {
        incompleteKnownAncestry = true;
        break;
      }
      current = parent;
    }
  }

  return incompleteKnownAncestry ? "unknown" : "none";
}

export interface IncidentNextStepGuidance {
  actionAvailability: OperatorActionAvailability;
  canAct: boolean;
  pendingIncidentId: string | null;
  requesterCancelled: boolean;
  retryReviewEligibility: IncidentRetryReviewEligibility;
  retryNotBefore?: string;
  retryDue?: boolean;
}

export interface OperatorActionsApi {
  canAct: boolean;
  actionAvailability: OperatorActionAvailability;
  pending: { incidentId: string; label: string } | null;
  guidanceForIncident(incident: IncidentStateSummary): IncidentNextStepGuidance;
  open(model: OperatorActionModel, focusFallback?: () => void): void;
  /** The Retry and decision buttons of the incident dialog; `close` closes that dialog first. */
  renderIncidentActions(incident: IncidentStateSummary, activityLabel: string, close: () => void, focusFallback: () => void): ReactNode;
  /** The dialog, when one is open, and the status line that announces progress and refusals. */
  dialog: ReactNode;
  status: ReactNode;
}

const resultMessages: Record<OperatorActionModel["kind"], string> = {
  retry: "Retry applied.",
  resolve: "Resolve applied.",
  abandon: "Recovery abandoned.",
  decide: "Decision applied."
};

const sleep = (milliseconds: number) => new Promise<void>(resolve => setTimeout(resolve, milliseconds));

export function useOperatorActions({
  context,
  workflowExecutionId,
  details,
  root,
  activities,
  journals,
  labelOf,
  refreshRun
}: UseOperatorActionsInput): OperatorActionsApi {
  const [availabilityObservation, setAvailabilityObservation] = useState<{ context: StudioEndpointContext; status: OperatorActionAvailability }>({ context, status: "checking" });
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [followed, setFollowed] = useState<FollowedAction | null>(null);
  const [message, setMessage] = useState<{ text: string; tone: "info" | "error" } | null>(null);
  const [pendingIncidentActionReads, setPendingIncidentActionReads] = useState<ReadonlySet<string>>(() => new Set());
  const pendingIncidentActionReadKeys = useRef(new Map<string, symbol>());
  // The plan being followed and the controller that stops following it; one at a time.
  const following = useRef<{ planId: string; controller: AbortController } | null>(null);
  const refreshRunRef = useRef(refreshRun);
  refreshRunRef.current = refreshRun;
  const journalsRef = useRef(journals);
  journalsRef.current = journals;

  const permissions = readWorkflowInstancePermissions(details);
  const actionAvailability = availabilityObservation.context === context ? availabilityObservation.status : "checking";
  const canAct = permissions.canManage && actionAvailability === "available";

  useEffect(() => {
    let active = true;
    setAvailabilityObservation({ context, status: "checking" });
    supportsOperatorAlterations(context).then(supported => {
      if (active) setAvailabilityObservation({ context, status: supported ? "available" : "unavailable" });
    }, () => {
      if (active) setAvailabilityObservation({ context, status: "unknown" });
    });
    return () => {
      active = false;
    };
  }, [context]);

  // The run this hook shows now; null once it unmounts. A submission that resolves after a switch only stores its plan.
  const currentRun = useRef<string | null>(workflowExecutionId);
  const incidentActionRunGeneration = useRef(0);

  useLayoutEffect(() => {
    const pendingReadsForRun = pendingIncidentActionReadKeys.current;
    incidentActionRunGeneration.current += 1;
    pendingReadsForRun.clear();
    setPendingIncidentActionReads(new Set());
    return () => {
      incidentActionRunGeneration.current += 1;
      pendingReadsForRun.clear();
    };
  }, [workflowExecutionId]);

  // A different run, or leaving the page, stops following; the stored plan is resumed when the run is opened again.
  useEffect(() => {
    currentRun.current = workflowExecutionId;
    setFollowed(null);
    setMessage(null);
    return () => {
      currentRun.current = null;
      following.current?.controller.abort();
      following.current = null;
    };
  }, [workflowExecutionId]);

  const api: OperatorActionApi = {
    submit: (request, key) => submitAlterationPlan(context, request, key),
    getPlan: planId => getAlterationPlan(context, planId),
    pageJobs: planId => pageAlterationJobs(context, planId),
    getJob: (planId, jobId) => getAlterationJob(context, planId, jobId)
  };
  const apiRef = useRef(api);
  apiRef.current = api;

  // Whether the run shows the action: the incident no longer waits, and for a decision the scope applied it (its
  // journal no longer waits on that incident) or is no longer running.
  const isApplied = useCallback(async (pending: PendingOperatorAction) => {
    const latest = await refreshRunRef.current();
    if (!latest) return false;
    const incident = latest.incidents?.find(candidate => candidate.incidentId === pending.incidentId);
    if (incident && isWaitingForIntervention(incident)) return false;
    if (pending.kind !== "retry" && pending.scopeActivityExecutionId) {
      const view = await journalsRef.current.reload(pending.scopeActivityExecutionId);
      const journal = typeof view === "object" ? readCompensationJournal(view) : null;
      if (journal && typeof view === "object" && view.activityStatus === "Running"
        && journal.entries.some(entry => entry.waiting?.incidentId === pending.incidentId)) return false;
    } else if (pending.scopeActivityExecutionId) {
      void journalsRef.current.reload(pending.scopeActivityExecutionId);
    }
    return true;
  }, []);

  const follow = useCallback(async (pending: PendingOperatorAction) => {
    if (following.current?.planId === pending.planId) return;
    following.current?.controller.abort();
    const controller = new AbortController();
    following.current = { planId: pending.planId, controller };
    const options = { sleep, now: () => Date.now(), signal: controller.signal };
    setFollowed({ ...pending, label: pending.submittedLabel });
    try {
      const result = await followOperatorPlan(apiRef.current, pending.planId, {
        ...options,
        onStillQueued: () => setMessage({ text: operatorPollingTimeoutMessage, tone: "info" })
      });
      if (result.kind === "aborted") return;
      if (result.kind === "refused") {
        clearPendingOperatorAction(workflowExecutionId);
        setFollowed(null);
        setMessage({ text: operatorRefusalMessage(result.code), tone: "error" });
        await refreshRunRef.current();
        if (pending.scopeActivityExecutionId) void journalsRef.current.reload(pending.scopeActivityExecutionId);
        return;
      }
      setFollowed({ ...pending, label: "Applying…" });
      setMessage({ text: "Applying…", tone: "info" });
      const applied = await waitUntilApplied(() => isApplied(pending), options);
      if (applied === "aborted") return;
      clearPendingOperatorAction(workflowExecutionId);
      setFollowed(null);
      setMessage(applied
        ? { text: resultMessages[pending.kind], tone: "info" }
        : { text: operatorApplyingTimeoutMessage, tone: "info" });
    } catch (error) {
      if (controller.signal.aborted) return;
      const failure = operatorErrorMessage(error);
      setFollowed(null);
      setMessage({ text: failure.message, tone: "error" });
      if (failure.refreshPermissions || failure.refreshRun) {
        clearPendingOperatorAction(workflowExecutionId);
        await refreshRunRef.current();
      }
    } finally {
      if (following.current?.controller === controller) following.current = null;
    }
  }, [isApplied, workflowExecutionId]);

  // A reload resumes the plan this run was following; it is never submitted again (FR-423).
  useEffect(() => {
    if (actionAvailability !== "available") return;
    const stored = readPendingOperatorAction(workflowExecutionId);
    if (stored) void follow(stored);
  }, [actionAvailability, follow, workflowExecutionId]);

  const open = useCallback((model: OperatorActionModel, focusFallback?: () => void) => {
    setMessage(null);
    setDialog({
      model,
      key: mintIdempotencyKey(),
      busy: false,
      error: null,
      trigger: document.activeElement instanceof HTMLElement ? document.activeElement : null,
      ...(focusFallback ? { focusFallback } : {})
    });
  }, []);

  const returnFocus = (state: DialogState) => {
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (state.trigger?.isConnected && !state.trigger.closest("[aria-hidden='true'], [inert]")) state.trigger.focus();
      else state.focusFallback?.();
    }));
  };

  const close = (state: DialogState) => {
    setDialog(null);
    returnFocus(state);
  };

  const confirm = async (state: DialogState, form: OperatorActionForm) => {
    setDialog({ ...state, busy: true, error: null });
    try {
      const submission = await apiRef.current.submit(operatorActionPlan(state.model, form), state.key);
      const pending: PendingOperatorAction = {
        planId: submission.planId,
        kind: state.model.kind,
        incidentId: state.model.incidentId,
        key: state.key,
        submittedLabel: state.model.submittedLabel,
        ...(state.model.scopeActivityExecutionId ? { scopeActivityExecutionId: state.model.scopeActivityExecutionId } : {})
      };
      storePendingOperatorAction(workflowExecutionId, pending);
      // The page moved on while the submission was in flight: the stored plan is followed when this run opens again.
      if (currentRun.current !== workflowExecutionId) return;
      setMessage({ text: state.model.submittedLabel, tone: "info" });
      close(state);
      void follow(pending);
    } catch (error) {
      const failure = operatorErrorMessage(error);
      if (failure.refreshPermissions || failure.refreshRun || (error as { status?: number })?.status === 401) {
        close(state);
        setMessage({ text: failure.message, tone: "error" });
        await refreshRunRef.current();
        return;
      }
      // Kept open: confirming again resends with the same key, so a lost response never runs twice.
      setDialog({ ...state, busy: false, error: failure.message });
    }
  };

  // `journal` is the requester's journal when the caller has just read it: `journals` is this render's snapshot, so a
  // journal read after the click is not in it yet.
  const modelForIncident = (incident: IncidentStateSummary, activityLabel: string, action: "retry" | { decision: string }, journal?: CompensationJournalSource) => {
    const requester = interventionRequesterOf(incident);
    const requesterActivity = requester ? activities.find(activity => activity.activityExecutionId === requester) : undefined;
    const scopeNode = requesterActivity && root
      ? findActivityNode(root, requesterActivity.authoredActivityId) ?? findActivityNode(root, requesterActivity.executableNodeId)
      : undefined;
    if (requester && scopeNode && isCompensationScope(scopeNode)) {
      const derived = deriveCompensationRuntime(scopeNode, activities, details?.incidents ?? [], {
        complete: !details?.activityNextContinuationToken,
        selectedActivityExecutionId: requester,
        root,
        journal: journal ?? journals.get(requester)
      });
      const runtime = { ...derived, scopeLabel: labelOf(scopeNode.nodeId) ?? derived.scopeLabel };
      const row = [...runtime.rows, ...(runtime.received ?? [])].find(candidate => candidate.actions?.incidentId === incident.incidentId);
      if (row) {
        const scopeContext = { workflowExecutionId, runtime, labelOf };
        if (action === "retry") return retryActionModel(row, scopeContext);
        if (action.decision === compensationDecisions.abandon) return abandonActionModel(scopeContext);
        if (action.decision === compensationDecisions.resolveEffectAbsent || action.decision === compensationDecisions.resolveEffectAccepted) {
          return resolveActionModel(row, scopeContext, action.decision);
        }
      }
    }
    return incidentActionModel(
      { incidentId: incident.incidentId, workflowExecutionId: incident.workflowExecutionId, activityExecutionId: incident.activityExecutionId ?? incident.metadata?.["runtime.activityExecutionId"] },
      activityLabel,
      action,
      requester);
  };

  const scheduledRetryForIncident = (incident: IncidentStateSummary, journalOverride?: CompensationJournalSource) => {
    const requester = interventionRequesterOf(incident);
    const requesterActivity = requester ? activities.find(activity => activity.activityExecutionId === requester) : undefined;
    const scopeNode = requesterActivity && root
      ? findActivityNode(root, requesterActivity.authoredActivityId) ?? findActivityNode(root, requesterActivity.executableNodeId)
      : undefined;
    const source = requester ? journalOverride ?? journals.get(requester) : undefined;
    if (incident.workflowExecutionId !== workflowExecutionId
      || !requester
      || !requesterActivity
      || requesterActivity.workflowExecutionId !== workflowExecutionId
      || !scopeNode
      || !isCompensationScope(scopeNode)
      || typeof source !== "object"
      || source.available !== true
      || source.workflowExecutionId !== workflowExecutionId
      || source.activityExecutionId !== requester) return undefined;

    const journal = readCompensationJournal(source);
    if (!journal) return undefined;
    const runtime = deriveCompensationRuntime(scopeNode, activities, details?.incidents ?? [], {
      complete: !details?.activityNextContinuationToken,
      selectedActivityExecutionId: requester,
      root,
      journal: source
    });
    const activityExecutionId = incident.activityExecutionId?.trim() || incident.metadata?.["runtime.activityExecutionId"]?.trim();
    const row = [...runtime.rows, ...(runtime.received ?? [])].find(candidate => {
      if (candidate.recovery.state !== "RetryScheduled" || !candidate.recovery.retryNotBefore || !candidate.entryId) return false;
      const entry = journal.entries.find(item => item.id === candidate.entryId);
      return entry?.attempts.some(attempt => attempt.incidentId === incident.incidentId
        && (!activityExecutionId || attempt.activityExecutionId === activityExecutionId)) ?? false;
    });

    return row?.recovery.retryNotBefore
      ? { retryNotBefore: row.recovery.retryNotBefore, retryDue: isRetryDue(row.recovery) }
      : undefined;
  };

  const guidanceForIncident = (incident: IncidentStateSummary): IncidentNextStepGuidance => {
    const retryReviewEligibility = getIncidentRetryReviewEligibility(incident, details?.instance, activities, details?.incidents ?? [], workflowExecutionId);
    const scheduledRetry = scheduledRetryForIncident(incident);
    const requester = interventionRequesterOf(incident);
    const requesterActivity = requester ? activities.find(activity => activity.activityExecutionId === requester) : undefined;
    if (scheduledRetry) {
      return {
        actionAvailability,
        canAct,
        pendingIncidentId: followed?.incidentId ?? null,
        requesterCancelled: requesterActivity?.status === "Cancelled",
        retryReviewEligibility,
        ...scheduledRetry
      };
    }

    return {
      actionAvailability,
      canAct,
      pendingIncidentId: followed?.incidentId ?? null,
      requesterCancelled: requesterActivity?.status === "Cancelled",
      retryReviewEligibility
    };
  };

  const renderIncidentActions = (incident: IncidentStateSummary, activityLabel: string, closeIncident: () => void, focusFallback: () => void): ReactNode => {
    if (!canAct || followed || !isWaitingForIntervention(incident) || scheduledRetryForIncident(incident)) return null;
    const retryEligibility = getIncidentRetryReviewEligibility(incident, details?.instance, activities, details?.incidents ?? [], workflowExecutionId);
    if (retryEligibility.reason === "historical-incident" || retryEligibility.reason === "ended-run") return null;
    const requester = interventionRequesterOf(incident);
    // A cancelled scope never starts a handler, and decides nothing (spec 260 R9).
    if (requester && activities.find(activity => activity.activityExecutionId === requester)?.status === "Cancelled") return null;
    const decisions = offeredDecisions(incident);
    const showRetry = retryEligibility.status === "eligible";
    if (!showRetry && decisions.length === 0) return null;
    const actionReadKey = `${workflowExecutionId}\u0000${incident.incidentId}`;
    const actionReadPending = pendingIncidentActionReads.has(actionReadKey);
    const launch = async (action: "retry" | { decision: string }) => {
      if (pendingIncidentActionReadKeys.current.has(actionReadKey)) return;
      const ownerDialog = document.activeElement instanceof HTMLElement
        ? document.activeElement.closest<HTMLElement>("[role=dialog]")
        : null;
      const readToken = Symbol();
      const runGeneration = incidentActionRunGeneration.current;
      pendingIncidentActionReadKeys.current.set(actionReadKey, readToken);
      // Keep focus in the incident dialog before the pending state disables its action buttons.
      ownerDialog?.focus();
      setPendingIncidentActionReads(current => new Set(current).add(actionReadKey));
      try {
        // Read the journal when the run view has none yet (or is still reading it), and use what it read: the
        // journals state only reaches this closure on the next render. The read can also discover that an automatic retry
        // is already scheduled, in which case keep the incident open and let the updated journal guidance speak for it.
        const cached = requester ? journals.get(requester) : undefined;
        const journal = requester && (!cached || cached === "loading")
          ? await journals.reload(requester).catch(() => undefined)
          : cached;
        if (currentRun.current !== workflowExecutionId || incidentActionRunGeneration.current !== runGeneration) return;
        // Escape or another close can end the incident dialog while the journal is loading. Do not reopen a
        // confirmation after the operator has left this incident.
        if (ownerDialog && !ownerDialog.isConnected) return;
        if (scheduledRetryForIncident(incident, journal)) {
          if (ownerDialog?.isConnected) ownerDialog.focus();
          return;
        }
        closeIncident();
        open(modelForIncident(incident, activityLabel, action, journal), focusFallback);
      } finally {
        if (pendingIncidentActionReadKeys.current.get(actionReadKey) === readToken) {
          pendingIncidentActionReadKeys.current.delete(actionReadKey);
          setPendingIncidentActionReads(current => {
            if (!current.has(actionReadKey)) return current;
            const next = new Set(current);
            next.delete(actionReadKey);
            return next;
          });
        }
      }
    };
    return (
      <div className="wf-incident-operator-actions" role="group" aria-label="Operator actions" aria-busy={actionReadPending}>
        {showRetry ? (
          <button type="button" disabled={actionReadPending} onClick={() => void launch("retry")}>
            <RotateCw size={13} aria-hidden="true" /> Retry
          </button>
        ) : null}
        {decisions.map(decision => (
          <button key={decision} type="button" disabled={actionReadPending} data-tone={decision === compensationDecisions.abandon ? "danger" : undefined} onClick={() => void launch({ decision })}>
            {decision === compensationDecisions.abandon ? <OctagonX size={13} aria-hidden="true" /> : <UserCheck size={13} aria-hidden="true" />}
            {" "}{interventionDecisionLabel(decision)}
          </button>
        ))}
      </div>
    );
  };

  return {
    canAct,
    actionAvailability,
    pending: followed ? { incidentId: followed.incidentId, label: followed.label } : null,
    guidanceForIncident,
    open,
    renderIncidentActions,
    dialog: dialog ? (
      <OperatorActionDialog
        key={dialog.key}
        model={dialog.model}
        busy={dialog.busy}
        error={dialog.error}
        onCancel={() => close(dialog)}
        onConfirm={form => void confirm(dialog, form)}
        restoreFocus={dialog.focusFallback}
      />
    ) : null,
    status: (
      <div className="wf-operator-status" role="status" data-tone={message?.tone ?? (followed ? "info" : undefined)}>
        {message ? (
          <>
            {message.tone === "error" ? <AlertTriangle size={14} aria-hidden="true" /> : followed ? <Loader size={14} aria-hidden="true" /> : null}
            <span>{message.text}</span>
          </>
        ) : null}
      </div>
    )
  };
}

