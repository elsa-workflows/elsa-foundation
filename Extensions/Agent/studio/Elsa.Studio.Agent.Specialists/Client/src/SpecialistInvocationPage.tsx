import React, { useCallback, useEffect, useRef, useState } from "react";
import { Bot, RefreshCw } from "lucide-react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import { InvocationEvidenceDetails, applicationText } from "./InvocationEvidence";
import { pollDelay } from "./SpecialistPlayground";
import { getInvocationEvidence, type InvocationEvidenceView } from "./playgroundApi";

export type InvocationReference = { workflowExecutionId: string; activityExecutionId: string };
type Load = { kind: "loading" } | { kind: "missing" } | { kind: "error"; message: string } | { kind: "ready"; view: InvocationEvidenceView; failures: number };

const refreshIntervalMs = 2000;
const maximumRefreshFailures = 5;
// The native statuses after which the runtime does no more work for this occurrence.
const settledStatuses = ["Completed", "Faulted", "Cancelled", "Superseded", "Recovered"];

/** The occurrence a link names: /agents/invocations/{workflowExecutionId}/{activityExecutionId}. */
export function readInvocationReference(pathname: string): InvocationReference | null {
  const match = /^\/agents\/invocations\/([^/]+)\/([^/]+)\/?$/.exec(pathname);
  return match ? { workflowExecutionId: decodeURIComponent(match[1]), activityExecutionId: decodeURIComponent(match[2]) } : null;
}

export function invocationPath(reference: InvocationReference) {
  return `/agents/invocations/${encodeURIComponent(reference.workflowExecutionId)}/${encodeURIComponent(reference.activityExecutionId)}`;
}

/** The native run page with this occurrence selected, where its incidents, inputs and parent activities are inspected. */
export function runPath(reference: InvocationReference) {
  return `/workflows/instances/${encodeURIComponent(reference.workflowExecutionId)}?${new URLSearchParams({ selected: reference.activityExecutionId, tab: "activity" })}`;
}

/**
 * One agent invocation inside a workflow run, as the caller may see it: the exact published or candidate version, its
 * model, the turn's requests, usage and application, an unknown outcome stated as such, and the result only with the
 * value permission. It refreshes while the occurrence has not settled.
 */
export function SpecialistInvocationPage({ context, reference: given, navigate, refreshInterval = refreshIntervalMs }:
  { context: StudioEndpointContext; reference?: InvocationReference | null; navigate?(path: string): void; refreshInterval?: number }) {
  const reference = given ?? readInvocationReference(window.location.pathname);
  const key = reference ? invocationPath(reference) : "";
  return <InvocationScreen key={key} context={context} reference={reference} navigate={navigate} refreshInterval={refreshInterval} />;
}

function InvocationScreen({ context, reference, navigate, refreshInterval }:
  { context: StudioEndpointContext; reference: InvocationReference | null; navigate?(path: string): void; refreshInterval: number }) {
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [tick, setTick] = useState(0);
  const loads = useRef(0);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (!reference) { setLoad({ kind: "missing" }); return; }
    // Only the latest read may update the page, so a slow earlier read never replaces newer evidence.
    const sequence = ++loads.current;
    try {
      const view = await getInvocationEvidence(context, reference.workflowExecutionId, reference.activityExecutionId, signal);
      if (sequence !== loads.current) return;
      setLoad({ kind: "ready", view, failures: 0 });
    } catch (error) {
      if (sequence !== loads.current || signal?.aborted) return;
      const status = error instanceof StudioHttpError ? error.status : (error as { status?: number }).status;
      // A failed refresh keeps the last evidence on screen; a missing occurrence replaces it.
      setLoad(current => status === 404 ? { kind: "missing" }
        : current.kind === "ready" ? { ...current, failures: current.failures + 1 }
        : { kind: "error", message: error instanceof Error ? error.message : "The agent invocation could not be loaded." });
    }
  }, [context, reference?.workflowExecutionId, reference?.activityExecutionId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => { void refresh(); }, [refresh]);

  const settled = load.kind === "ready" && settledStatuses.includes(load.view.invocation.activityStatus);
  const failures = load.kind === "ready" ? load.failures : 0;
  useEffect(() => {
    if (load.kind !== "ready" || settled || failures >= maximumRefreshFailures) return;
    const abort = new AbortController();
    const timer = setTimeout(() => { void refresh(abort.signal).then(() => setTick(current => current + 1)); }, pollDelay(refreshInterval, failures, true));
    return () => { abort.abort(); clearTimeout(timer); };
  }, [load.kind, settled, failures, tick, refresh, refreshInterval]);

  if (load.kind === "loading") return <StudioPage aria-busy="true"><p>Loading agent invocation…</p></StudioPage>;
  if (load.kind === "missing")
    return <StudioPage data-testid="invocation-missing"><StudioResultState title="Agent invocation not found"
      description="No agent invocation you may inspect matches this link. It may belong to another tenant, or need the activity execution inspection permission." /></StudioPage>;
  if (load.kind === "error")
    return <StudioPage><StudioResultState tone="danger" title="Agent invocation unavailable" description={load.message}
      actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /></StudioPage>;

  const { invocation, valuesPermitted } = load.view;
  const turn = invocation.turn;
  return (
    <StudioPage className="agent-invocation" data-status={invocation.activityStatus}>
      <StudioPageHeader
        icon={<Bot aria-hidden="true" size={18} />}
        eyebrow="Agent invocation"
        title={<>{invocation.specialist.version} <StatusChip tone={tone(invocation.activityStatus)}>{invocation.activityStatus}</StatusChip></>}
        description={<span role="status" data-testid="invocation-summary">{summary(invocation.activityStatus, turn ? applicationText(turn.application) : null)}</span>}
        actions={<>
          {navigate && reference ? <StudioButton onClick={() => navigate(runPath(reference))}>Open the run</StudioButton> : null}
          <StudioButton aria-label="Refresh invocation" onClick={() => void refresh()}><RefreshCw aria-hidden="true" size={14} /> Refresh</StudioButton>
        </>} />
      {failures > 0 ? <p className="agent-playground__hint" role="status" data-testid="invocation-stale">
        The latest evidence could not be read; showing the last loaded state.{failures >= maximumRefreshFailures ? " It is no longer refreshed automatically." : ""}</p> : null}
      <section className="agent-playground__run" aria-label="Invocation evidence">
        <InvocationEvidenceDetails invocation={invocation} valuesPermitted={valuesPermitted} terminal={settled} versionLabel="Version"
          outcomeUnknownAdvice="Inspect the run before deciding on recovery or another run." />
      </section>
    </StudioPage>
  );
}

function summary(activityStatus: string, application: string | null) {
  const native = nativeText(activityStatus);
  // A missing turn is missing evidence: the turn may never have been staged, or may not be readable here.
  return application ? `${native} ${application}.` : `${native} No model turn evidence is available.`;
}

function nativeText(activityStatus: string) {
  switch (activityStatus) {
    case "Completed": return "The agent completed.";
    case "Faulted": return "The agent faulted.";
    case "Cancelled": return "The agent was cancelled.";
    case "Superseded": return "This invocation was superseded by another one.";
    case "Recovered": return "The agent's fault was recovered.";
    default: return "The agent is still running.";
  }
}

function tone(status: string) {
  switch (status) {
    case "Completed": return "success" as const;
    case "Faulted": return "danger" as const;
    case "Cancelled": case "Superseded": case "Recovered": return "warning" as const;
    default: return "accent" as const;
  }
}
