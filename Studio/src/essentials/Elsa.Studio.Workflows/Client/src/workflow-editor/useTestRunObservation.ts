import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../api/capabilities";
import { getWorkflowTestRun, getWorkflowTestRunWaits } from "../api/publishing";
import { getWorkflowInstance } from "../api/runtime";
import type { WorkflowInstanceDetails, WorkflowTestRunView, WorkflowTestRunWait } from "../workflowTypes";
import type { WorkflowTestRunState } from "./editorTypes";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

export type TestRunObservationStatus = "loading" | "connected" | "paused" | "disconnected" | "unavailable" | "expired";
interface Observation {
  key: string;
  status: TestRunObservationStatus;
  details: WorkflowInstanceDetails | null;
  waits: WorkflowTestRunWait[];
  waitsLoading: boolean;
  waitsError: string;
  error: string;
  revision: number;
}
class AttachmentIdentityError extends Error {}

function matchesAttachment(view: WorkflowTestRunView, refreshed: WorkflowTestRunView) {
  return refreshed.testRunId === view.testRunId && refreshed.definitionId === view.definitionId
    && refreshed.definitionVersionId === view.definitionVersionId
    && (!view.artifactId || refreshed.artifactId === view.artifactId)
    && (!view.workflowExecutionId || refreshed.workflowExecutionId === view.workflowExecutionId);
}

export function isTerminalExecution(status?: string) {
  return status === "Completed" || status === "Faulted" || status === "Cancelled";
}

/** One observer for the editor canvas and wait controls. Reads never dispatch another execution. */
export function useTestRunObservation({ context, testRun, enabled = true, onRunViewUpdated }: {
  context: StudioEndpointContext;
  testRun: WorkflowTestRunState | null;
  enabled?: boolean;
  onRunViewUpdated?(view: WorkflowTestRunView): void;
}) {
  const [revision, setRevision] = useState(0);
  const [session, setSession] = useState({ authorized: true, revision: 0 });
  const [visible, setVisible] = useState(() => !document.hidden);
  const [online, setOnline] = useState(() => navigator.onLine);
  const [state, setState] = useState<Observation | null>(null);
  const notify = useRef(onRunViewUpdated); notify.current = onRunViewUpdated;
  const view = testRun?.view;
  const key = JSON.stringify([runtimeValueContextId(context), view?.testRunId, view?.artifactId, view?.workflowExecutionId, session.revision]);
  const allowed = enabled && session.authorized && (!testRun?.endpointContext || testRun.endpointContext === context);
  const currentKey = useRef(key); currentKey.current = key;
  const active = useRef(false); active.current = allowed;
  const request = useRef<AbortController | null>(null);
  const refresh = useCallback(() => setRevision(value => value + 1), []);
  const isCurrent = useCallback(() => active.current && currentKey.current === key, [key]);
  const acceptView = (refreshed: WorkflowTestRunView) => {
    if (!isCurrent() || !view || !matchesAttachment(view, refreshed)) return false;
    notify.current?.(refreshed);
    return true;
  };

  useEffect(() => {
    const invalidate = (authorized: boolean) => {
      active.current = false;
      request.current?.abort(); request.current = null;
      setState(null);
      setSession(value => ({ authorized, revision: value.revision + 1 }));
    };
    const end = () => invalidate(false);
    const start = () => invalidate(true);
    const visibility = () => setVisible(!document.hidden);
    const connect = () => setOnline(true);
    const disconnect = () => { request.current?.abort(); setOnline(false); };
    window.addEventListener(authSessionEndedEvent, end);
    window.addEventListener(authSessionStartedEvent, start);
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("online", connect);
    window.addEventListener("offline", disconnect);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end);
      window.removeEventListener(authSessionStartedEvent, start);
      document.removeEventListener("visibilitychange", visibility);
      window.removeEventListener("online", connect);
      window.removeEventListener("offline", disconnect);
    };
  }, []);

  const refreshWaits = useCallback(async (testRunId: string) => {
    if (!active.current || testRunId !== view?.testRunId) return;
    const expectedKey = key;
    const expectedRequest = request.current;
    const result = await getWorkflowTestRunWaits(context, testRunId);
    if (active.current && currentKey.current === expectedKey && request.current === expectedRequest && !expectedRequest?.signal.aborted) {
      setState(value => value?.key === expectedKey ? { ...value, waits: result.waits ?? [], waitsLoading: false, waitsError: "" } : value);
    }
  }, [context, key, view?.testRunId]);

  useEffect(() => {
    if (!allowed || !view) return;
    if (!visible || !online) return;
    const controller = new AbortController(); request.current = controller;
    const isCurrent = () => active.current && currentKey.current === key && request.current === controller && !controller.signal.aborted;
    let timer: number | undefined;
    let failures = 0;
    const update = (recipe: (value: Observation) => Observation) => {
      if (!isCurrent()) return;
      setState(value => recipe(value?.key === key ? value : {
        key, status: "loading", details: null, waits: [], waitsLoading: true, waitsError: "", error: "", revision: 0
      }));
    };
    const poll = async () => {
      let terminal = false;
      try {
        const refreshed = await getWorkflowTestRun(context, view.testRunId);
        if (!isCurrent()) return;
        if (!matchesAttachment(view, refreshed)) {
          throw new AttachmentIdentityError("The Test Run identity did not match this attachment.");
        }
        notify.current?.(refreshed);
        if (refreshed.status === "Expired" || refreshed.expiresAt && Date.parse(refreshed.expiresAt) <= Date.now()) {
          update(value => ({ ...value, status: "expired", details: null, waits: [], waitsLoading: false, error: "This Test Run has expired." }));
          return;
        }
        terminal = ["Stopped", "Rejected", "DispatchRejected"].includes(refreshed.status);
        // Prepared triggers can gain their execution identity through this existing read operation.
        const executionId = refreshed.workflowExecutionId;
        const waitsTask = refreshWaits(view.testRunId).catch(error => {
          update(value => ({ ...value, waits: [], waitsLoading: false, waitsError: error instanceof Error ? error.message : "Waiting triggers could not be loaded." }));
        });
        if (executionId) {
          const details = await getWorkflowInstance(context, executionId);
          if (!isCurrent()) return;
          if (details.instance.workflowExecutionId !== executionId || refreshed.artifactId && details.instance.artifactId !== refreshed.artifactId) {
            throw new AttachmentIdentityError("Runtime evidence identity did not match this Test Run.");
          }
          terminal ||= isTerminalExecution(details.instance.status);
          update(value => ({ ...value, status: "connected", details, error: "", revision: value.revision + 1 }));
        } else update(value => ({ ...value, status: "connected", details: null, error: "", revision: value.revision + 1 }));
        await waitsTask;
        if (!isCurrent()) return;
        failures = 0;
      } catch (error) {
        if (!isCurrent()) return;
        const unavailable = error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError || error instanceof AttachmentIdentityError;
        update(value => ({ ...value, status: unavailable ? "unavailable" : "disconnected", details: null,
          waits: [], waitsLoading: false, error: error instanceof AttachmentIdentityError ? error.message : unavailable ? "Live runtime state is unavailable on this host." : error instanceof Error ? error.message : "Execution observation disconnected." }));
        if (unavailable) return;
        failures++;
      }
      if (isCurrent() && !terminal) timer = window.setTimeout(() => { void poll(); }, failures ? Math.min(30000, 2000 * 2 ** failures) : 2000);
    };
    update(value => ({ ...value, status: value.details ? "connected" : "loading" }));
    void poll();
    return () => { controller.abort(); if (timer !== undefined) window.clearTimeout(timer); };
    // Read identity primitives so view-result updates do not restart the polling loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allowed, context, key, online, refreshWaits, revision, visible]);

  const current = allowed && state?.key === key ? state : null;
  const status: TestRunObservationStatus = !allowed ? "unavailable" : !online ? "disconnected" : !visible ? "paused" : current?.status ?? "loading";
  return {
    identity: key,
    status,
    details: status === "connected" || status === "paused" ? current?.details ?? null : null,
    waits: status === "connected" ? current?.waits ?? [] : [],
    waitsLoading: current?.waitsLoading ?? true,
    waitsError: current?.waitsError ?? "",
    error: !session.authorized ? "Sign in again to observe this Test Run." : !online ? "Execution observation disconnected. Reconnect to refresh." : current?.error ?? "",
    revision: current?.revision ?? 0,
    refresh,
    refreshWaits,
    acceptView,
    isCurrent
  };
}

export type TestRunObservation = ReturnType<typeof useTestRunObservation>;
