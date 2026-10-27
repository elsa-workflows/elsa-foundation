import { useMemo, useState, type FormEvent } from "react";
import type {
  StudioEndpointContext,
  StudioTestStimulusEditorContribution,
  StudioTestStimulusEditorContext,
  StudioTestRunOptionDescriptor,
  StudioTestRunWaitDescriptor
} from "@elsa-workflows/studio-sdk";
import type {
  WorkflowTestRunView,
  WorkflowTestRunWait,
  WorkflowTestRunWaitEventView,
} from "../workflowTypes";
import { deliverWorkflowTestRunWaitEvent, getWorkflowTestRun, stopWorkflowTestRun } from "../api/publishing";
import { isTerminalExecution, useTestRunObservation, type TestRunObservation } from "./useTestRunObservation";
import type { WorkflowTestRunState, WorkflowTestRunWaitDraftState } from "./editorTypes";

export function WorkflowTestRunControls({
  context,
  testRun,
  stimulusEditors,
  onRunViewUpdated,
  onWaitDraftUpdated,
  observation
}: {
  context: StudioEndpointContext;
  testRun: WorkflowTestRunState;
  stimulusEditors: StudioTestStimulusEditorContribution[];
  onRunViewUpdated(view: WorkflowTestRunView): void;
  onWaitDraftUpdated?(waitId: string, draftState: WorkflowTestRunWaitDraftState): void;
  observation?: TestRunObservation;
}) {
  const localObservation = useTestRunObservation({ context, testRun, enabled: !observation, onRunViewUpdated });
  return <TestRunCommands key={(observation ?? localObservation).identity} context={context} testRun={testRun} stimulusEditors={stimulusEditors}
    onWaitDraftUpdated={onWaitDraftUpdated} observation={observation ?? localObservation} />;
}

function TestRunCommands({ context, testRun, stimulusEditors, onWaitDraftUpdated, observation }: {
  context: StudioEndpointContext;
  testRun: WorkflowTestRunState;
  stimulusEditors: StudioTestStimulusEditorContribution[];
  onWaitDraftUpdated?(waitId: string, draftState: WorkflowTestRunWaitDraftState): void;
  observation: TestRunObservation;
}) {
  const { waits, waitsLoading: loading, waitsError, error: executionError, refreshWaits } = observation;
  const execution = observation.details?.instance;
  const [stopping, setStopping] = useState(false);
  const [stopError, setStopError] = useState("");
  const [deliveryFeedback, setDeliveryFeedback] = useState<{ waitId: string; view: WorkflowTestRunWaitEventView } | null>(null);
  const canInteract = observation.status === "connected";

  const stop = async () => {
    if (!canInteract || stopping) return;
    setStopping(true);
    setStopError("");
    try {
      await stopWorkflowTestRun(context, testRun.view.testRunId);
    } catch (error) {
      setStopError(error instanceof Error ? error.message : "The Test Run could not be stopped.");
    }
    try {
      const refreshed = await getWorkflowTestRun(context, testRun.view.testRunId);
      observation.acceptView(refreshed);
      if (observation.isCurrent()) observation.refresh();
    } catch (error) {
      setStopError(current => current || (error instanceof Error ? error.message : "The Test Run status could not be refreshed."));
    } finally {
      setStopping(false);
    }
  };

  const canStop = !["Rejected", "DispatchRejected", "Stopped", "Expired"].includes(testRun.view.status) && !isTerminalExecution(execution?.status);
  const executionState = waits.length > 0 ? "Waiting" : execution ? runtimeStateLabel(execution.status) : null;
  return (
    <section className="wf-test-run-controls" aria-label="Test Run controls">
      {canStop ? <button type="button" onClick={() => { void stop(); }} disabled={stopping || !canInteract}>{stopping ? "Stopping…" : "Stop Test Run"}</button> : null}
      {stopError ? <p className="wf-test-drive-error" role="alert">{stopError}</p> : null}
      {execution && executionState ? <p className="wf-test-run-execution-state" role="status"><strong>Execution</strong> {executionState}{execution.subStatus ? ` · ${execution.subStatus}` : ""}</p> : null}
      {observation.status === "paused" ? <p role="status">Execution observation paused while this view is inactive.</p> : null}
      {["disconnected", "unavailable", "expired"].includes(observation.status) ? <button type="button" onClick={observation.refresh}>Reconnect execution</button> : null}
      {executionError ? <p className="wf-test-drive-error" role="status">{executionError}</p> : null}
      <div className="wf-test-run-waits">
        <header><h4>Waiting triggers</h4><button type="button" onClick={() => { observation.refresh(); }} disabled={loading || stopping}>Refresh</button></header>
        {waitsError ? <p className="wf-test-drive-error" role="alert">{waitsError}</p> : null}
        {loading ? <p role="status">Loading waiting triggers…</p> : null}
        {canInteract && !loading && waits.length === 0 ? <p>No supported waits are active on this Test Run.</p> : null}
        {waits.map(wait => <WaitCard
          key={wait.waitId}
          context={context}
          testRunId={testRun.view.testRunId}
          wait={wait}
          savedState={testRun.waitDrafts?.[wait.waitId]}
          stimulusEditors={stimulusEditors}
          disabled={stopping || !canInteract}
          onDraftStateChange={state => onWaitDraftUpdated?.(wait.waitId, state)}
          onDelivered={(view, runView) => {
            if (!observation.isCurrent()) return;
            if (runView && !observation.acceptView(runView)) return;
            setDeliveryFeedback({ waitId: wait.waitId, view });
            observation.refresh();
          }}
          refreshWaits={refreshWaits}
        />)}
        {deliveryFeedback ? (
          <p role={deliveryFeedback.view.status === "Rejected" ? "alert" : "status"}>
            {deliveryStatusLabel(deliveryFeedback.view.status)}{deliveryFeedback.view.reason ? ` · ${deliveryFeedback.view.reason}` : ""}
          </p>
        ) : null}
      </div>
    </section>
  );
}

function WaitCard({
  context,
  testRunId,
  wait,
  savedState,
  stimulusEditors,
  disabled,
  onDraftStateChange,
  onDelivered,
  refreshWaits
}: {
  context: StudioEndpointContext;
  testRunId: string;
  wait: WorkflowTestRunWait;
  savedState?: WorkflowTestRunWaitDraftState;
  stimulusEditors: StudioTestStimulusEditorContribution[];
  disabled: boolean;
  onDraftStateChange(state: WorkflowTestRunWaitDraftState): void;
  onDelivered(view: WorkflowTestRunWaitEventView, runView: WorkflowTestRunView | null): void;
  refreshWaits(testRunId: string): Promise<void>;
}) {
  const [modeId, setModeId] = useState(() => wait.modes.some(mode => mode.modeId === savedState?.modeId) ? savedState!.modeId : defaultMode(wait.modes)?.modeId ?? "");
  const [draft, setDraft] = useState(() => savedState?.draft ?? "{}");
  const [validationError, setValidationError] = useState("");
  const [deliveryError, setDeliveryError] = useState("");
  const [deliveryPending, setDeliveryPending] = useState(false);
  const [attempt, setAttempt] = useState<{ signature: string; deliveryId: string } | null>(() => savedState?.attempt ?? null);
  const mode = wait.modes.find(candidate => candidate.modeId === modeId) ?? null;
  const option = useMemo(() => adaptWaitToOption(wait), [wait]);
  const editor = useMemo(() => {
    if (!mode) return undefined;
    for (const candidate of [...stimulusEditors].sort((left, right) => (left.order ?? 500) - (right.order ?? 500))) {
      try {
        if (candidate.supports({ option, mode })) return candidate;
      } catch (error) {
        console.error(`[workflow.test-stimulus.editors] ${candidate.id} failed while matching wait ${wait.driverId ?? "unknown"}/${mode.modeId}.`, error);
      }
    }
    return undefined;
  }, [mode, option, stimulusEditors, wait.driverId]);
  const editorContext: StudioTestStimulusEditorContext | null = mode ? { option, mode, draft } : null;
  const needsEditor = !editor && !mode?.configurationSchema;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setValidationError("");
    setDeliveryError("");
    if (disabled || deliveryPending || !wait.isAvailable || !mode || needsEditor) return;
    let data: unknown;
    try {
      const error = editorContext ? editor?.validate(editorContext) : undefined;
      if (error) { setValidationError(error); return; }
      data = editorContext && editor ? editor.serialize(editorContext) : JSON.parse(draft || "{}");
    } catch (error) {
      setValidationError(error instanceof Error ? error.message : "Enter valid JSON for the wait data.");
      return;
    }

    const signature = JSON.stringify({ waitId: wait.waitId, modeId: mode.modeId, data });
    const nextAttempt = attempt?.signature === signature ? attempt : { signature, deliveryId: createDeliveryId() };
    const deliveryId = nextAttempt.deliveryId;
    setAttempt(nextAttempt);
    onDraftStateChange({ modeId: mode.modeId, draft, attempt: nextAttempt });
    setDeliveryPending(true);
    try {
      const result = await deliverWorkflowTestRunWaitEvent(context, testRunId, {
        waitId: wait.waitId,
        deliveryId,
        modeId: mode.modeId,
        ...(data !== undefined ? { data } : {})
      });
      const [waitsResult, runResult] = await Promise.allSettled([
        refreshWaits(testRunId),
        getWorkflowTestRun(context, testRunId)
      ]);
      const runView = runResult.status === "fulfilled" ? runResult.value : null;
      if (waitsResult.status === "rejected" || runResult.status === "rejected") {
        const error = waitsResult.status === "rejected" ? waitsResult.reason : (runResult as PromiseRejectedResult).reason;
        setDeliveryError(error instanceof Error ? error.message : "The event was sent, but Test Run status could not be refreshed.");
      }
      onDelivered(result, runView);
    } catch {
      // Keep the same delivery ID and the captured data draft for a safe retry after an uncertain response.
      setDeliveryError("Delivery outcome is uncertain. Retry this same wait and data to reuse the same delivery ID.");
      const refreshedView = await getWorkflowTestRun(context, testRunId).catch(() => null);
      onDelivered({ status: "Uncertain" }, refreshedView);
    } finally {
      setDeliveryPending(false);
    }
  };

  const canDeliver = wait.isAvailable && Boolean(mode) && !needsEditor && !disabled;
  const controlProps = { id: `test-run-wait-${safeDomId(wait.waitId)}`, "aria-label": `${mode?.label ?? "Wait"} data`, "aria-invalid": Boolean(validationError) };
  return (
    <article className="wf-test-run-wait" data-available={wait.isAvailable}>
      <header><div><strong>{wait.label || "Waiting trigger"}</strong><small>Node {wait.nodeId}</small></div>{wait.driverId ? <span>{wait.driverId}</span> : null}</header>
      {!wait.isAvailable ? <p className="wf-test-drive-error" role="status">{wait.reason || "No installed module can deliver to this wait."}</p> : null}
      {wait.isAvailable && wait.modes.length === 0 ? <p className="wf-test-drive-error" role="status">No delivery modes are available for this wait.</p> : null}
      {wait.isAvailable && wait.modes.length ? (
        <form onSubmit={event => { void submit(event); }}>
          {wait.modes.length > 1 ? <label className="wf-form-field"><span>Mode</span><select value={modeId} disabled={disabled || deliveryPending} onChange={event => {
            const nextModeId = event.currentTarget.value;
            setModeId(nextModeId);
            setAttempt(null);
            onDraftStateChange({ modeId: nextModeId, draft });
          }}>{wait.modes.map(item => <option key={item.modeId} value={item.modeId}>{item.label}</option>)}</select></label> : null}
          {mode?.description ? <p className="wf-test-drive-description">{mode.description}</p> : null}
          {editor && editorContext ? <editor.component
            option={option}
            mode={mode!}
            draft={draft}
            disabled={disabled || deliveryPending}
            controlProps={controlProps}
            onChange={value => { setDraft(value); setValidationError(""); onDraftStateChange({ modeId, draft: value, ...(attempt ? { attempt } : {}) }); }}
          /> : mode?.configurationSchema ? <label className="wf-form-field" htmlFor={controlProps.id}>
            <span>Wait data</span><textarea {...controlProps} rows={5} value={draft} disabled={disabled || deliveryPending} onChange={event => { const value = event.currentTarget.value; setDraft(value); setValidationError(""); onDraftStateChange({ modeId, draft: value, ...(attempt ? { attempt } : {}) }); }} />
            <small>Enter JSON matching the driver’s declared schema.</small>
          </label> : <p className="wf-test-drive-error" role="status">No installed module editor is available, and this wait does not declare a JSON Schema.</p>}
          {validationError ? <p className="wf-test-drive-error" role="alert">{validationError}</p> : null}
          {deliveryError ? <p className="wf-test-drive-error" role="alert">{deliveryError}</p> : null}
          <button type="submit" disabled={!canDeliver || deliveryPending}>{deliveryPending ? "Delivering…" : editor?.actionLabel ?? "Deliver event"}</button>
        </form>
      ) : null}
    </article>
  );
}

function adaptWaitToOption(wait: StudioTestRunWaitDescriptor): StudioTestRunOptionDescriptor {
  return {
    optionId: wait.waitId,
    driverId: wait.driverId,
    nodeId: wait.nodeId,
    label: wait.label,
    isAvailable: wait.isAvailable,
    reason: wait.reason,
    modes: wait.modes
  };
}

function defaultMode<T extends { isDefault?: boolean }>(modes: T[]) {
  return modes.find(mode => mode.isDefault) ?? modes[0];
}

function deliveryStatusLabel(status: string) {
  switch (status) {
    case "Delivered": return "Event delivered";
    case "AlreadyDelivered": return "Event was already delivered";
    case "Pending": return "Delivery is pending; retrying will reuse the same delivery ID";
    case "NoLongerWaiting": return "This trigger is no longer waiting";
    case "Rejected": return "Event delivery was rejected";
    case "Uncertain": return "Delivery outcome is uncertain; retrying with unchanged data reuses the same delivery ID";
    default: return `Delivery status: ${status}`;
  }
}

function runtimeStateLabel(status: string) {
  switch (status) {
    case "Pending": return "Pending";
    case "Running": return "Running";
    case "Suspended": return "Waiting";
    case "Completed": return "Completed";
    case "Faulted": return "Faulted";
    case "Cancelled": return "Cancelled";
    default: return status;
  }
}

function createDeliveryId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function safeDomId(value: string) {
  return value.replace(/[^A-Za-z0-9_-]/g, "-");
}
