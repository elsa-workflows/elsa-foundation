import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
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
import {
  compensationDecisions,
  deriveCompensationRuntime,
  interventionDecisionLabel,
  interventionRequesterOf,
  isWaitingForIntervention,
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
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowInstanceDetails } from "../workflowTypes";
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

export interface OperatorActionsApi {
  canAct: boolean;
  pending: { incidentId: string; label: string } | null;
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
  const [linksAvailable, setLinksAvailable] = useState(false);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [followed, setFollowed] = useState<FollowedAction | null>(null);
  const [message, setMessage] = useState<{ text: string; tone: "info" | "error" } | null>(null);
  // The plan being followed and the controller that stops following it; one at a time.
  const following = useRef<{ planId: string; controller: AbortController } | null>(null);
  const refreshRunRef = useRef(refreshRun);
  refreshRunRef.current = refreshRun;
  const journalsRef = useRef(journals);
  journalsRef.current = journals;

  const permissions = readWorkflowInstancePermissions(details);
  const canAct = permissions.canManage && linksAvailable;

  useEffect(() => {
    let active = true;
    supportsOperatorAlterations(context).then(supported => {
      if (active) setLinksAvailable(supported);
    }, () => {
      if (active) setLinksAvailable(false);
    });
    return () => {
      active = false;
    };
  }, [context]);

  // A different run, or leaving the page, stops following; the stored plan is resumed when the run is opened again.
  useEffect(() => {
    setFollowed(null);
    setMessage(null);
    return () => {
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
    if (!linksAvailable) return;
    const stored = readPendingOperatorAction(workflowExecutionId);
    if (stored) void follow(stored);
  }, [follow, linksAvailable, workflowExecutionId]);

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

  const modelForIncident = (incident: IncidentStateSummary, activityLabel: string, action: "retry" | { decision: string }) => {
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
        journal: journals.get(requester)
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

  const renderIncidentActions = (incident: IncidentStateSummary, activityLabel: string, closeIncident: () => void, focusFallback: () => void): ReactNode => {
    if (!canAct || followed || !isWaitingForIntervention(incident)) return null;
    const requester = interventionRequesterOf(incident);
    // A cancelled scope never starts a handler, and decides nothing (spec 260 R9).
    if (requester && activities.find(activity => activity.activityExecutionId === requester)?.status === "Cancelled") return null;
    const decisions = offeredDecisions(incident);
    const launch = async (action: "retry" | { decision: string }) => {
      closeIncident();
      if (requester && !journals.get(requester)) await journals.reload(requester).catch(() => undefined);
      open(modelForIncident(incident, activityLabel, action), focusFallback);
    };
    return (
      <div className="wf-incident-operator-actions" role="group" aria-label="Operator actions">
        <button type="button" onClick={() => void launch("retry")}>
          <RotateCw size={13} aria-hidden="true" /> Retry
        </button>
        {decisions.map(decision => (
          <button key={decision} type="button" data-tone={decision === compensationDecisions.abandon ? "danger" : undefined} onClick={() => void launch({ decision })}>
            {decision === compensationDecisions.abandon ? <OctagonX size={13} aria-hidden="true" /> : <UserCheck size={13} aria-hidden="true" />}
            {" "}{interventionDecisionLabel(decision)}
          </button>
        ))}
      </div>
    );
  };

  return {
    canAct,
    pending: followed ? { incidentId: followed.incidentId, label: followed.label } : null,
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

