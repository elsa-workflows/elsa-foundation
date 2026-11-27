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

  useEffect(() => {
    setFeedback(null);
    setPending(false);
    return () => {
      action.current?.abort();
      action.current = null;
    };
  }, [context, workflowExecutionId]);

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

  async function recover(path: string, selection: Extract<WorkflowParkRecovery, { status: "Available" }>) {
    if (action.current) return;
    const attempt = new AbortController();
    action.current = attempt;
    const confirmation = AbortSignal.timeout(confirmationBudget);
    const signal = AbortSignal.any([attempt.signal, confirmation]);
    setPending(true);
    setFeedback(null);
    try {
      await requestWorkflowParkRecovery(context, path, selection.token, signal);
      if (attempt.signal.aborted) return;
      setFeedback({ tone: "info", text: "Recovery requested. Waiting for the run to resume." });
      while (!signal.aborted) {
        const park = await readWorkflowParkRecovery(context, path, signal);
        if (attempt.signal.aborted) return;
        setState({ context, path, park });
        if (!park) {
          setFeedback({ tone: "error", text: "Recovery status is unavailable." });
          return;
        }
        if (park.status === "NotParked") {
          setFeedback({ tone: "info", text: "The run is ready to continue." });
          void refreshRun();
          return;
        }
        if (park.parkedAt !== selection.parkedAt) {
          setFeedback({ tone: "error", text: "The run parked again. Review the current run before recovering it." });
          void refreshRun();
          return;
        }
        await new Promise<void>(resolve => setTimeout(resolve, pollInterval));
      }
      if (!attempt.signal.aborted) setFeedback({ tone: "error", text: "Recovery could not be confirmed. Refresh the run to check its status." });
    } catch (error) {
      if (attempt.signal.aborted) return;
      if (error instanceof StudioHttpError && [401, 403, 404, 409].includes(error.status)) {
        setFeedback({ tone: "error", text: error.status === 409
          ? "This run changed. Check its current state before recovering it."
          : "Recovery is unavailable for this run." });
        setState({ context, path, park: null });
        const park = await readWorkflowParkRecovery(context, path, signal).catch(() => null);
        if (!attempt.signal.aborted) setState({ context, path, park });
      } else {
        const retry = error instanceof StudioHttpError ? error.retryAfterSeconds : null;
        setFeedback({ tone: "error", text: retry ? `Recovery is busy. Try again in ${retry} ${retry === 1 ? "second" : "seconds"}.`
          : "Recovery could not be confirmed. Refresh the run to check its status." });
      }
    } finally {
      if (action.current === attempt) {
        action.current = null;
        setPending(false);
      }
    }
  }

  return <div className="wf-operator-status" data-tone={feedback?.tone ?? "info"}>
    <span role="status">{feedback?.text ?? (selected ? "This run is parked. Recover it to accept new work." : "This run is parked and recovery is unavailable.")}</span>
    {current && selected ? <StudioButton type="button" size="sm" disabled={pending}
      onClick={() => void recover(current.path, selected)}>Recover</StudioButton> : null}
  </div>;
}
