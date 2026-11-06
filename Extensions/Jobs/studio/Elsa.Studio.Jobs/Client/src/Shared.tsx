import React from "react";
import { isStudioWorkspaceTabClick, openStudioWorkspaceTab, qualifyStudioBackendPath, requestStudioNavigation, useStudioRouteLocation } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, SkeletonRows, StudioAlert, StudioButton, StudioCollectionFooter, StudioResultState } from "@elsa-workflows/studio-ui";
import { failureText, statusOf } from "./jobsApi";
import type { useJobsMutation } from "./useJobs";
import type { Association, Gate } from "./types";

export type Navigate = (path: string) => void;
export function JobsLink({ path, navigate, children, ...props }: { path: string; navigate: Navigate; children: React.ReactNode } & Omit<React.AnchorHTMLAttributes<HTMLAnchorElement>, "href">) {
  return <a {...props} href={qualifyStudioBackendPath(path)} onClick={event => {
    props.onClick?.(event); if (event.defaultPrevented) return;
    if (isStudioWorkspaceTabClick(event) && openStudioWorkspaceTab(qualifyStudioBackendPath(path))) { event.preventDefault(); return; }
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault(); if (requestStudioNavigation(window.location.pathname, qualifyStudioBackendPath(path))) navigate(path);
  }}>{children}</a>;
}
export function JobsNavigation({ navigate }: { navigate: Navigate }) {
  const location = useStudioRouteLocation();
  return <nav className="jobs-nav" aria-label="Jobs operations">{[["/jobs", "Jobs"], ["/jobs/queues", "Queues"], ["/jobs/schedules", "Schedules"], ["/jobs/holds", "Holds"], ["/jobs/capacity", "Capacity"], ["/jobs/operations", "Operation audit"]].map(([path, title]) =>
    <JobsLink key={path} path={path} navigate={navigate} aria-current={location.pathname.endsWith(path) ? "page" : undefined}>{title}</JobsLink>)}</nav>;
}
export function ReadState({ loading, error, empty, children, retry }: { loading: boolean; error?: unknown; empty?: boolean; children: React.ReactNode; retry(): void }) {
  return loading ? <SkeletonRows rows={3} label="Loading Jobs state" /> : error ? <StudioResultState
    tone={[401, 403, 404].includes(statusOf(error)) ? undefined : "danger"} title={[401, 403].includes(statusOf(error)) ? "Jobs access denied" : statusOf(error) === 404 ? "Resource not available" : "Jobs state unavailable"}
    description={failureText(error)} actions={<StudioButton onClick={retry}>Refresh access and state</StudioButton>} /> : empty ?
    <StudioResultState title="No matching items on this page" description="Change the filters or use the next page when a continuation is available. Items outside your current access are omitted." /> : <>{children}</>;
}
export function PageFooter({ count, next, after, change }: { count: number; next?: string | null; after?: string | null; change(cursor?: string): void }) {
  return <StudioCollectionFooter><span>{count} items shown</span><div className="jobs-actions" role="group" aria-label="Pages">
    {after && <StudioButton onClick={() => change()}>First page</StudioButton>}
    <StudioButton disabled={!next} onClick={() => { if (next) change(next); }}>Next page</StudioButton>
  </div></StudioCollectionFooter>;
}
export function MutationNotice({ mutation }: { mutation: ReturnType<typeof useJobsMutation> }) {
  const body = mutation.pending?.body;
  const operationId = typeof body === "object" && body !== null && "operationId" in body ? String(body.operationId) :
    typeof body === "object" && body !== null && "idempotencyKey" in body ? String(body.idempotencyKey) : "";
  if (!mutation.problem) return null;
  return <StudioAlert tone="warning"><p>{failureText(mutation.problem, true)}</p>{mutation.unconfirmed && <>
    <p>This outcome is unconfirmed. Check current server state or replay the exact original request. A new operation may duplicate work.</p>
    {operationId && <CopyableIdentifier value={operationId} label="Original operation ID" />}
    <div className="jobs-actions"><StudioButton disabled={mutation.busy} onClick={() => void mutation.retry()}>Retry original operation</StudioButton>
      <StudioButton disabled={mutation.busy} onClick={() => void mutation.forget()}>Forget local confirmation…</StudioButton></div>
  </>}</StudioAlert>;
}
export function gateLabel(gate: Gate) {
  const kinds = { queue: "Queue", jobType: "Job type (all versions)", workflowInstance: "Workflow instance", workflowDefinition: "Workflow definition (all versions)" };
  return `${kinds[gate.kind]}: ${gate.name}${gate.modelKind ? ` · ${gate.modelKind}` : ""}`;
}
export function ParentLinks({ association, navigate }: { association?: Association | null; navigate: Navigate }) {
  if (!association) return <p>The API did not supply a parent association.</p>;
  const native = association.modelKind === "elsa.native";
  const bpmn = association.modelKind === "elsa.bpmn";
  const instance = native ? `/workflows/instances/${encodeURIComponent(association.executionId)}` : bpmn ? `/workflows/bpmn/runs/${encodeURIComponent(association.executionId)}` : null;
  return <div className="jobs-stack"><p>{association.mode} · {association.modelKind}</p>
    {instance ? <JobsLink path={instance} navigate={navigate}>Open parent run {association.executionId}</JobsLink> : <CopyableIdentifier value={association.executionId} label="Parent execution" />}
    {native ? <JobsLink path={`/workflows/definitions?definition=${encodeURIComponent(association.definitionId)}`} navigate={navigate}>Open definition {association.definitionId}</JobsLink> : <CopyableIdentifier value={association.definitionId} label="Parent definition" />}
    <p>Parent version {association.definitionVersion}. Parent access and availability are checked by its owning module.</p>
  </div>;
}
export function date(value?: string | null) { return value ? new Date(value).toLocaleString() : "—"; }
export function collectionReturn(search: string) { const params = new URLSearchParams(search); return `/jobs${params.size ? "?" + params.toString() : ""}`; }
