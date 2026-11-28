import { useEffect, useRef, useState } from "react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { readWorkflowParkRecovery, requestWorkflowParkRecovery, resolveWorkflowParkRecovery, type WorkflowParkRecovery } from "../api/parkRecovery";

interface RecoveryState {
  context: StudioEndpointContext;
  path: string;
  park: WorkflowParkRecovery | null;
}

interface Feedback {
  text: string;
  tone: "info" | "error";
}

interface RunParkRecoveryProps {
  context: StudioEndpointContext;
  workflowExecutionId: string;
  inspect: boolean;
  refreshVersion: number;
  refreshRun: () => Promise<unknown>;
}

const pollInterval = 500;
const confirmationBudget = 15_000;

export function RunParkRecovery({ context, workflowExecutionId, inspect, refreshVersion, refreshRun }: RunParkRecoveryProps) {
  const [state, setState] = useState<RecoveryState | null>(null);
  const [pending, setPending] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const action = useRef<AbortController | null>(null);
  const recoverButton = useRef<HTMLButtonElement | null>(null);
  const recoveryStatus = useRef<HTMLSpanElement | null>(null);

  useEffect(() => {
    setFeedback(null);
    setPending(false);
    return () => {
      action.current?.abort();
      action.current = null;
    };
  }, [context, workflowExecutionId]);

  useEffect(() => {
    if (!action.current) setFeedback(null);
  }, [refreshVersion]);

  useEffect(() => {
    if (!inspect || action.current) return;
    const read = new AbortController();
    setState(null);
    void (async () => {
      try {
        const path = await resolveWorkflowParkRecovery(context, workflowExecutionId);
        if (!path || read.signal.aborted) return;
        const park = await readWorkflowParkRecovery(context, path, read.signal);
        if (!read.signal.aborted) setState({ context, path, park });
      } catch {
        if (!read.signal.aborted) setState(null);
      }
    })();
    return () => read.abort();
  }, [context, workflowExecutionId, inspect, refreshVersion]);

  const current = inspect && state?.context === context ? state : null;
  const selected = current?.park?.status === "Available" ? current.park : null;
  const parked = current?.park && current.park.status !== "NotParked";
  if (!parked && !feedback && !pending) return null;

  function focusStatusBeforeRemovingAction() {
    if (document.activeElement === recoverButton.current) recoveryStatus.current?.focus();
  }

  async function recover(path: string, selection: Extract<WorkflowParkRecovery, { status: "Available" }>) {
    if (action.current) return;
    const attempt = new AbortController();
    action.current = attempt;
    const confirmation = AbortSignal.timeout(confirmationBudget);
    const signal = AbortSignal.any([attempt.signal, confirmation]);
    setPending(true);
    setFeedback(null);
    try {
      try {
        await requestWorkflowParkRecovery(context, path, selection.token, signal);
      } catch (error) {
        if (attempt.signal.aborted) return;
        if (error instanceof StudioHttpError && [401, 403, 404, 409].includes(error.status)) {
          const stale = error.status === 409;
          setFeedback({ tone: "error", text: stale
            ? "This run changed. Check its current state before recovering it."
            : "Recovery is unavailable for this run." });
          const park = stale ? await readWorkflowParkRecovery(context, path, signal).catch(() => null) : null;
          if (attempt.signal.aborted) return;
          if (park?.status !== "Available") focusStatusBeforeRemovingAction();
          setState({ context, path, park });
        } else if (error instanceof StudioHttpError && error.retryAfterSeconds) {
          const retry = error.retryAfterSeconds;
          setFeedback({ tone: "error", text: `Recovery is busy. Try again in ${retry} ${retry === 1 ? "second" : "seconds"}.` });
        } else {
          focusStatusBeforeRemovingAction();
          setState(null);
          setFeedback({ tone: "error", text: "Recovery could not be confirmed. Refresh the run to check its status." });
        }
        return;
      }

      if (attempt.signal.aborted) return;
      setState({ context, path, park: { status: "Unavailable", parkedAt: selection.parkedAt } });
      setFeedback({ tone: "info", text: "Recovery requested. Waiting for the run to resume." });
      while (!signal.aborted) {
        const park = await readWorkflowParkRecovery(context, path, signal).catch(() => undefined);
        if (attempt.signal.aborted) return;
        if (park === null) {
          focusStatusBeforeRemovingAction();
          setState(null);
          setFeedback({ tone: "error", text: "Recovery status is unavailable." });
          return;
        }
        if (park?.status === "NotParked") {
          focusStatusBeforeRemovingAction();
          setState({ context, path, park });
          setFeedback({ tone: "info", text: "This run is no longer parked." });
          void refreshRun();
          return;
        }
        if (park && park.parkedAt !== selection.parkedAt) {
          if (park.status !== "Available") focusStatusBeforeRemovingAction();
          setState({ context, path, park });
          setFeedback({ tone: "error", text: "The run parked again. Review the current run before recovering it." });
          void refreshRun();
          return;
        }
        await new Promise<void>(resolve => setTimeout(resolve, pollInterval));
      }
      if (!attempt.signal.aborted) {
        focusStatusBeforeRemovingAction();
        setFeedback({ tone: "error", text: "Recovery could not be confirmed. Refresh the run to check its status." });
      }
    } finally {
      if (action.current === attempt) {
        action.current = null;
        setPending(false);
      }
    }
  }

  return <div className="wf-operator-status" data-tone={feedback?.tone ?? "info"}>
    <span ref={recoveryStatus} role="status" tabIndex={-1}>{feedback?.text ?? (selected
      ? "This run is parked. Recover it to accept new work."
      : "This run is parked and recovery is unavailable.")}</span>
    {selected || pending ? <StudioButton ref={recoverButton} type="button" size="sm" aria-disabled={pending}
      onClick={() => { if (current && selected && !pending) void recover(current.path, selected); }}>Recover</StudioButton> : null}
  </div>;
}
