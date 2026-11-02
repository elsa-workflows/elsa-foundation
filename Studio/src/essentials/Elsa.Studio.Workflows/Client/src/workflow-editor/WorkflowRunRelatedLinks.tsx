import { useEffect, useRef, useState } from "react";
import { evaluateBackendRequirements, type StudioBackendSession, type StudioEndpointContext,
  type StudioWorkflowRunRelatedLink, type StudioWorkflowRunRelatedLinkContribution } from "@elsa-workflows/studio-sdk";
import { getActivityStateView, supportsActivityStateView } from "../api/runtime";
import type { ActivityExecutionStateSummary, ActivityStateView } from "../workflowTypes";
import { runtimeValueContextId } from "./RuntimeValueEvidence";
import { runSelectionParameter } from "./runSelectionUrl";

function identity(session: StudioBackendSession | undefined) {
  return JSON.stringify([session?.key, session?.profile.id, session?.profile.baseUrl, session?.identity.status,
    session?.identity.subject, session?.identity.tenantId]);
}

/** A public reference only: modules never receive private runtime state from this Slot. */
export function WorkflowRunRelatedLinks({ context, session, activity, contributions, refreshKey }: {
  context: StudioEndpointContext;
  session?: StudioBackendSession;
  activity: ActivityExecutionStateSummary;
  contributions: readonly StudioWorkflowRunRelatedLinkContribution[];
  refreshKey: number;
}) {
  const scope = identity(session);
  const key = JSON.stringify([scope, runtimeValueContextId(context), activity.workflowExecutionId, activity.activityExecutionId,
    activity.executableNodeId, activity.activityType, activity.status]);
  const live = useRef({ key, session, context }); live.current = { key, session, context };
  const requestedRevision = useRef(refreshKey); requestedRevision.current = refreshKey;
  const refreshReference = useRef<(() => void) | null>(null);
  const [state, setState] = useState<{ key: string; view?: ActivityStateView; unavailable?: boolean } | null>(null);
  const candidates = contributions.filter(item => item.activityTypes.includes(activity.activityType)
    && evaluateBackendRequirements(session, item.requiresBackend).state === "available");
  const eligible = !!session && !session.disposed && session.context === context
    && session.identity.status === "authenticated" && !!session.identity.subject && candidates.length > 0;

  useEffect(() => {
    if (!eligible) return;
    const controller = new AbortController();
    const current = () => !controller.signal.aborted && !session!.disposed && session!.context === context
      && identity(session) === scope && live.current.key === key && live.current.session === session && live.current.context === context;
    setState({ key });
    let running = false;
    let confirmed: number | null = null;
    const load = async () => {
      if (running || !current() || confirmed === requestedRevision.current) return;
      running = true;
      try {
        do {
          const revision = requestedRevision.current;
          try {
            if (!await supportsActivityStateView(context)) { if (current()) setState({ key, unavailable: true }); confirmed = revision; return; }
            if (!current()) return;
            const view = await getActivityStateView(context, activity.workflowExecutionId, activity.activityExecutionId, controller.signal);
            if (!current()) return;
            if (!view.available || view.workflowExecutionId !== activity.workflowExecutionId || view.activityExecutionId !== activity.activityExecutionId
              || view.executableNodeId !== activity.executableNodeId || !view.viewKind || !Number.isInteger(view.viewVersion) || view.viewVersion! < 1)
              setState({ key, unavailable: true });
            else setState({ key, view });
          } catch { if (current()) setState({ key, unavailable: true }); }
          confirmed = revision;
        } while (current() && confirmed !== requestedRevision.current);
      } finally { running = false; }
    };
    const start = () => { void load(); }; refreshReference.current = start; start();
    return () => { controller.abort(); if (refreshReference.current === start) refreshReference.current = null; };
  }, [context, session, scope, key, eligible, activity.workflowExecutionId, activity.activityExecutionId, activity.executableNodeId]);
  useEffect(() => { refreshReference.current?.(); }, [refreshKey]);

  if (!eligible) return null;
  const view = state?.key === key ? state.view : null;
  const returnTo = `/workflows/instances/${encodeURIComponent(activity.workflowExecutionId)}?${new URLSearchParams({
    backend: session!.profile.id, [runSelectionParameter]: activity.activityExecutionId, tab: "activity"
  })}`;
  const links = new Map<string, StudioWorkflowRunRelatedLink>();
  if (view) for (const contribution of candidates) {
    if (contribution.viewKind !== view.viewKind || contribution.viewVersion !== view.viewVersion) continue;
    try {
      const projected = contribution.getLinks({ workflowExecutionId: activity.workflowExecutionId, activityExecutionId: activity.activityExecutionId,
        executableNodeId: activity.executableNodeId, stateView: { kind: view.viewKind!, version: view.viewVersion!, data: view.view }, returnTo });
      for (const link of projected) {
        if (!link.id?.trim() || !link.label?.trim() || !link.path?.startsWith("/") || link.path.startsWith("//") || link.path.includes("\\")) continue;
        const target = new URL(link.path, window.location.origin);
        if (target.origin !== window.location.origin || target.username || target.password
          || target.searchParams.has("backend") && target.searchParams.get("backend") !== session!.profile.id) continue;
        target.searchParams.set("backend", session!.profile.id);
        const id = `${contribution.id}:${link.id}`;
        if (!links.has(id)) links.set(id, { ...link, id, path: target.pathname + target.search + target.hash });
      }
    } catch { /* One unavailable contributor must not remove native inspection or other related links. */ }
  }
  return <section className="wf-instance-section" aria-label="Related runs">
    <h4>Related runs</h4>
    {links.size ? <ul>{[...links.values()].map(link => <li key={link.id}><a href={link.path} onClick={event => {
      if (session!.disposed || identity(session) !== scope || live.current.key !== key || session!.context !== context) event.preventDefault();
    }}>{link.label}</a></li>)}</ul> : <p role="status">{state?.key === key && (state.unavailable || view)
      ? "Related run links are unavailable for this activity." : "Loading related run reference…"}</p>}
  </section>;
}
