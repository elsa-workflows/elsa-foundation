import { useCallback, useEffect, useRef } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowTestRun, getWorkflowTestRunWaits } from "../api/publishing";
import type { WorkflowInstanceDetails, WorkflowTestRunView, WorkflowTestRunWait } from "../workflowTypes";
import type { WorkflowTestRunState } from "./editorTypes";
import { ExecutionObservationIdentityError, isTerminalExecution, readObservedExecution, useExecutionObservation } from "./useExecutionObservation";
export { isTerminalExecution } from "./useExecutionObservation";
export type { ExecutionObservationStatus as TestRunObservationStatus } from "./useExecutionObservation";

interface TestRunEvidence { details: WorkflowInstanceDetails | null; waits: WorkflowTestRunWait[]; waitsError: string; waitsLoading: boolean }
function matchesAttachment(view: WorkflowTestRunView, refreshed: WorkflowTestRunView) {
  return refreshed.testRunId === view.testRunId && refreshed.definitionId === view.definitionId
    && refreshed.definitionVersionId === view.definitionVersionId
    && (!view.artifactId || refreshed.artifactId === view.artifactId)
    && (!view.workflowExecutionId || refreshed.workflowExecutionId === view.workflowExecutionId);
}

/** Test Run metadata and commands adapt the shared runtime observer without a second polling loop. */
export function useTestRunObservation({ context, testRun, enabled = true, onRunViewUpdated }: {
  context: StudioEndpointContext;
  testRun: WorkflowTestRunState | null;
  enabled?: boolean;
  onRunViewUpdated?(view: WorkflowTestRunView): void;
}) {
  const view = testRun?.view;
  const notify = useRef(onRunViewUpdated); notify.current = onRunViewUpdated;
  const manualWaits = useRef<AbortController | null>(null);
  const observation = useExecutionObservation<TestRunEvidence>({ context,
    identity: JSON.stringify(["test-run", view?.testRunId, view?.artifactId, view?.workflowExecutionId]),
    enabled: enabled && !!view && (!testRun?.endpointContext || testRun.endpointContext === context),
    read: async scope => {
      manualWaits.current?.abort();
      if (view?.expiresAt && Date.parse(view.expiresAt) <= Date.now()) return { value: null, expired: true, terminal: true };
      const refreshed = await getWorkflowTestRun(context, view!.testRunId, scope.signal);
      if (!scope.isCurrent()) return { value: null, terminal: true };
      if (!view || !matchesAttachment(view, refreshed)) throw new ExecutionObservationIdentityError("The Test Run identity did not match this attachment.");
      if (scope.isCurrent()) notify.current?.(refreshed);
      if (refreshed.status === "Expired" || refreshed.expiresAt && Date.parse(refreshed.expiresAt) <= Date.now())
        return { value: null, expired: true, terminal: true };
      const waitController = new AbortController();
      const abortWaits = () => waitController.abort();
      scope.signal.addEventListener("abort", abortWaits, { once: true });
      const waitsTask = getWorkflowTestRunWaits(context, view.testRunId, waitController.signal).then(
        result => ({ waits: result.waits ?? [], waitsError: "" }),
        error => ({ waits: [], waitsError: error instanceof Error ? error.message : "Waiting triggers could not be loaded." }));
      try {
        const details = refreshed.workflowExecutionId ? await readObservedExecution(context, refreshed.workflowExecutionId, refreshed.artifactId, scope.signal) : null;
        // Runtime confirmation is useful even when waiting-trigger details are still loading.
        const terminal = details ? isTerminalExecution(details.instance.status) : ["Stopped", "Rejected", "DispatchRejected"].includes(refreshed.status);
        scope.publish(previous => ({ details, waits: terminal ? [] : previous?.waits ?? [], waitsError: previous?.waitsError ?? "", waitsLoading: !terminal && (!previous || previous.waitsLoading) }));
        if (terminal) return { value: { details, waits: [], waitsError: "", waitsLoading: false }, terminal };
        const waits = await waitsTask;
        return { value: { details, ...waits, waitsLoading: false }, terminal };
      } finally {
        scope.signal.removeEventListener("abort", abortWaits); waitController.abort();
      }
    }
  });
  const isCurrent = observation.isCurrent;
  const revision = observation.revision;
  const updateValue = observation.updateValue;
  const readIdentity = JSON.stringify([observation.identity, revision]);
  const latestRead = useRef(readIdentity); latestRead.current = readIdentity;
  useEffect(() => () => { manualWaits.current?.abort(); }, [observation.identity]);
  const refreshWaits = useCallback(async (testRunId: string) => {
    if (!isCurrent() || testRunId !== view?.testRunId) return;
    manualWaits.current?.abort();
    const controller = new AbortController(); manualWaits.current = controller;
    try {
      const result = await getWorkflowTestRunWaits(context, testRunId, controller.signal);
      if (isCurrent() && latestRead.current === readIdentity && manualWaits.current === controller && !controller.signal.aborted)
        updateValue(value => ({ ...value, waits: isTerminalExecution(value.details?.instance.status) ? [] : result.waits ?? [], waitsError: "", waitsLoading: false }));
    } catch (error) {
      if (isCurrent() && latestRead.current === readIdentity && manualWaits.current === controller && !controller.signal.aborted) throw error;
    }
  }, [context, isCurrent, readIdentity, updateValue, view?.testRunId]);
  return {
    ...observation,
    details: observation.value?.details ?? null,
    waits: observation.value?.waits ?? [],
    waitsLoading: observation.value?.waitsLoading ?? observation.status === "loading",
    waitsError: observation.value?.waitsError ?? "",
    refreshWaits,
    acceptView: (refreshed: WorkflowTestRunView) => {
      if (!isCurrent() || !view || !matchesAttachment(view, refreshed)) return false;
      notify.current?.(refreshed); return true;
    }
  };
}

export type TestRunObservation = ReturnType<typeof useTestRunObservation>;
