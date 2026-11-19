import type { StudioBackendSession, StudioWorkflowRunRelatedLinkContext, StudioWorkflowRunRelatedLink } from "@elsa-workflows/studio-sdk";

export const runEtlActivityType = "Elsa.Etl.Workflows.Runtime.Activities.RunEtl";

export function etlBackendScope(session: StudioBackendSession | undefined) {
  return JSON.stringify([session?.key, session?.profile.id, session?.profile.baseUrl, session?.identity.status,
    session?.identity.subject, session?.identity.tenantId]);
}

function object(value: unknown, keys: string[]): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value) && Object.keys(value).length === keys.length
    && keys.every(key => Object.hasOwn(value, key));
}
function nonblank(value: unknown): value is string { return typeof value === "string" && !!value.trim(); }

/** Only the ETL-owned public reference shape is understood here, never private activity state. */
export function etlNativeRunLinks(context: StudioWorkflowRunRelatedLinkContext, backendId?: string): StudioWorkflowRunRelatedLink[] {
  const value = context.stateView.data;
  if (!backendId || context.stateView.kind !== "etl.run-reference" || context.stateView.version !== 1
    || !object(value, ["runId", "resultAddress"]) || !nonblank(value.runId)
    || !object(value.resultAddress, ["workflowExecutionId", "activityExecutionId", "nodeId", "registrationId", "resumeTargetId"])
    || !Object.values(value.resultAddress).every(nonblank)) return [];
  const address = value.resultAddress;
  if (address.workflowExecutionId !== context.workflowExecutionId || address.activityExecutionId !== context.activityExecutionId
    || address.nodeId !== context.executableNodeId) return [];
  return [{ id: "etl-run", label: "Open ETL run", path: buildEtlRunPath(value.runId, backendId, context.returnTo) }];
}

export function buildEtlRunPath(runId: string, backendId: string, returnTo: string | null = null) {
  const query = new URLSearchParams({ backend: backendId, runId });
  if (returnTo) query.set("returnTo", returnTo);
  return `/etl/runs?${query}`;
}

export function readEtlRunLocation(href: string, backendId?: string): { runId: string | null; returnTo: string | null; error: string | null } {
  const empty = { runId: null, returnTo: null, error: null };
  const url = new URL(href);
  if (url.pathname !== "/etl/runs" || !url.searchParams.has("runId")) return empty;
  if (!backendId || url.searchParams.getAll("backend").length !== 1 || url.searchParams.get("backend") !== backendId)
    return { ...empty, error: "This run link belongs to another or unavailable backend. Select its backend to inspect the run." };
  const runId = url.searchParams.get("runId");
  if (url.searchParams.getAll("runId").length !== 1 || !nonblank(runId))
    return { ...empty, error: "The ETL run link does not identify one run." };
  const raw = url.searchParams.getAll("returnTo").length === 1 ? url.searchParams.get("returnTo") : null;
  let returnTo: string | null = null;
  if (raw?.startsWith("/") && !raw.startsWith("//") && !raw.includes("\\")) {
    const back = new URL(raw, url.origin);
    if (back.origin === url.origin && /^\/workflows\/instances\/[^/]+$/.test(back.pathname)
      && back.searchParams.getAll("backend").length === 1 && back.searchParams.get("backend") === backendId)
      returnTo = back.pathname + back.search + back.hash;
  }
  return { runId, returnTo, error: null };
}
