import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { formatStudioDate } from "@elsa-workflows/studio-sdk";
import { useEffect, useRef, useState } from "react";
import { useInfiniteQuery, useQuery, useQueryClient } from "@tanstack/react-query";
import { studioRowInteraction, StudioRefreshControl, StudioResultState, StudioButton, StudioCollectionEmptyState, StudioPageHeader, StudioTimestamp } from "@elsa-workflows/studio-ui";
import { evaluateBackendRequirements, qualifyStudioBackendPath, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { createBpmnRuntimeClient, type BpmnPage, type BpmnExecution, type BpmnPublication } from "../api/bpmnRuntime";
import { WorkflowListIdentifiers } from "../workflow-editor/WorkflowListIdentifiers";
import { BpmnRunDetails, bpmnRunState } from "./BpmnRunDetails";
import { BpmnCommandNotice } from "./BpmnCommandNotice";
import { useBpmnCommand } from "./useBpmnCommand";
import "./bpmnWorkflows.css";

type Client = ReturnType<typeof createBpmnRuntimeClient>;
type View = "processes" | "runs" | "run";
type Props = { session: StudioBackendSession; view: View; navigate(path: string): void };
const runPath = (id: string) => `/workflows/bpmn/runs/${encodeURIComponent(id)}`;
function readLocation() { return { path: window.location.pathname, search: window.location.search }; }

export function BpmnWorkflowsPage({ session, view, navigate }: Props) {

  const [location, setLocation] = useState(readLocation);
  useEffect(() => { const sync = () => setLocation(readLocation()); window.addEventListener("popstate", sync); return () => window.removeEventListener("popstate", sync); }, []);
  const client = createBpmnRuntimeClient(session);
  const available = evaluateBackendRequirements(session, [{ id: view === "run" ? "elsa.api.bpmn-inspection" : "elsa.api.bpmn-catalog", contractVersion: "1" }]).state === "available";
  const open = (path: string) => navigate(qualifyStudioBackendPath(path));
  let executionId = "";
  try { executionId = view === "run" ? decodeURIComponent(location.path.slice("/workflows/bpmn/runs/".length)) : ""; } catch { /* Refuse malformed route identities. */ }
  const artifactId = new URLSearchParams(location.search).get("artifact") ?? undefined;
  return <section className="studio-page wf-page wf-bpmn-page">
    <StudioPageHeader headingLevel={2} title={view === "processes" ? "BPMN processes" : view === "runs" ? "BPMN runs" : "BPMN run"}
      description={view === "processes" ? "Start a published process and return to its saved work." : "Inspect and resume persisted BPMN work."} />
    <nav aria-label="BPMN views" className="wf-bpmn-views">
      <a href={qualifyStudioBackendPath("/workflows/bpmn")} aria-current={view === "processes" ? "page" : undefined} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey && event.button === 0) { event.preventDefault(); open("/workflows/bpmn"); } }}>Processes</a>
      <a href={qualifyStudioBackendPath("/workflows/bpmn/runs")} aria-current={view !== "processes" ? "page" : undefined} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey && event.button === 0) { event.preventDefault(); open("/workflows/bpmn/runs"); } }}>Runs</a>
    </nav>
    {session.disposed ? <p role="alert">This backend connection ended. Reconnect to load current BPMN state.</p>
      : !available ? <StudioResultState title="BPMN unavailable" description="Choose a backend with BPMN support to open processes and runs." />
        : view === "run" ? executionId ? <BpmnRunDetails key={`${client.sessionKey}:${client.scope}:${executionId}`} client={client} session={session} executionId={executionId} /> : <p role="alert">This BPMN run address is invalid.</p>
          : <BpmnCatalog key={`${client.sessionKey}:${client.scope}:${view}:${artifactId ?? ""}`} client={client} session={session} view={view} artifactId={artifactId} open={open} />}
  </section>;
}

function BpmnCatalog({ client, session, view, artifactId, open }: { client: Client; session: StudioBackendSession; view: "processes" | "runs"; artifactId?: string; open(path: string): void }) {
  useStudioTimeZone();
  const queryClient = useQueryClient();
  const queryKey = [...client.queryKey, client.scope, view, artifactId ?? null];
  const query = useInfiniteQuery({ queryKey, initialPageParam: null as string | null,
    queryFn: async ({ pageParam, signal }): Promise<BpmnPage<BpmnPublication | BpmnExecution>> => view === "processes" ? client.publications({ continuationToken: pageParam, artifactId }, signal) : client.executions({ continuationToken: pageParam, artifactId }, signal),
    getNextPageParam: page => page.nextContinuationToken ?? undefined, retry: false, refetchOnWindowFocus: false });
  const runtimeAvailable = evaluateBackendRequirements(session, [{ id: "elsa.api.bpmn-runtime", contractVersion: "1", relations: ["bpmn-admission", "bpmn-start"] }]).state === "available";
  const admission = useQuery({ queryKey: [...client.queryKey, client.scope, "admission"], queryFn: ({ signal }) => client.admission(signal),
    enabled: view === "processes" && runtimeAvailable, retry: false, refetchOnWindowFocus: false });
  const paging = useRef(false);
  const items = [...new Map(query.data?.pages.flatMap(page => page.items).map(item => ["sourceReferenceId" in item ? item.sourceReferenceId : item.executionId, item]) ?? []).values()];
  const loadMore = () => {
    if (paging.current) return; paging.current = true;
    void query.fetchNextPage().catch(() => undefined).finally(() => { paging.current = false; });
  };
  return <div className="wf-bpmn-catalog">
    <div className="wf-bpmn-actions"><StudioRefreshControl refresh={{ id: "bpmn-" + view, label: "Refresh BPMN " + view, refreshing: query.isFetching, onRefresh: () => queryClient.resetQueries({ queryKey, exact: true }) }} />
      {artifactId ? <StudioButton type="button" variant="default" onClick={() => open("/workflows/bpmn/runs")}>Show all runs</StudioButton> : null}</div>
    {view === "processes" && admission.data?.admitNewExecutions === false ? <p role="status">New starts are disabled. Existing runs can still be inspected and resumed.</p> : null}
    {view === "processes" && admission.isError ? <p role="alert">Execution availability could not be loaded: {admission.error.message}</p> : null}
    {query.isError ? <p role="alert">{query.error.message}</p> : query.isPending ? <p role="status">Loading {view}…</p> : <>
      {!items.length ? <StudioCollectionEmptyState title={`No BPMN ${view} yet`}
        description={`No visible ${view} on the loaded pages.${query.hasNextPage ? " Load more to continue." : ""}`}
        actions={query.hasNextPage ? <StudioButton type="button" disabled={query.isFetching} onClick={loadMore}>{query.isFetchingNextPage ? "Loading…" : "Load more"}</StudioButton> : undefined} /> : <div className="wf-bpmn-table-scroll"><table>
        <caption className="wf-toolbar-sr-only">Loaded BPMN {view}</caption>
        <thead><tr>{view === "processes" ? <><th scope="col">Process</th><th scope="col">Published version</th><th scope="col">Actions</th></>
          : <><th scope="col">Run started</th><th scope="col">State</th><th scope="col">Updated</th><th scope="col">Actions</th></>}</tr></thead>
        <tbody>{items.map(item => "sourceReferenceId" in item ? <ProcessRow key={item.sourceReferenceId} client={client} session={session} publication={item}
          canStart={runtimeAvailable && admission.data?.admitNewExecutions === true && !admission.isFetching} open={open} />
          : <tr key={item.executionId} {...studioRowInteraction<HTMLTableRowElement>(() => open(runPath(item.executionId)))}><td><StudioTimestamp value={item.createdAt} fallback="—" />
            <WorkflowListIdentifiers name={`BPMN run ${formatStudioDate(item.createdAt)}`} kind="BPMN run" identifiers={[{ label: "BPMN run", value: item.executionId }, { label: "Executable", value: item.executable.artifactId }]} /></td>
            <td>{bpmnRunState(item)}</td><td><StudioTimestamp value={item.updatedAt} fallback="—" /></td>
            <td><StudioButton type="button" onClick={() => open(runPath(item.executionId))}>Open run</StudioButton></td></tr>)}</tbody>
      </table></div>}
      {query.hasNextPage && items.length ? <StudioButton type="button" disabled={query.isFetching} onClick={loadMore}>{query.isFetchingNextPage ? "Loading…" : "Load more"}</StudioButton> : null}
    </>}
  </div>;
}

function ProcessRow({ client, session, publication, canStart, open }: { client: Client; session: StudioBackendSession; publication: BpmnPublication; canStart: boolean; open(path: string): void }) {

  const command = useBpmnCommand(client, { kind: "start", sourceReferenceId: publication.sourceReferenceId }, () => undefined);
  const name = publication.displayName?.trim() || "BPMN process";
  return <tr><td><strong>{name}</strong><WorkflowListIdentifiers name={name} kind="BPMN process" identifiers={[
    { label: "Source reference", value: publication.sourceReferenceId }, { label: "Authored version", value: publication.sourceId }, { label: "Executable", value: publication.executable.artifactId }]} /></td>
    <td>{publication.sourceVersion ?? "Unversioned"}</td><td>
      <div className="wf-bpmn-actions">
        <StudioButton type="button" variant="primary" disabled={!canStart || command.busy || command.blocked || session.identity.status !== "authenticated" || !!command.record && !command.confirmed}
          onClick={() => void command.begin({ kind: "start", sourceReferenceId: publication.sourceReferenceId }, !!command.record && command.confirmed)}>{command.record && command.confirmed ? "Start another run" : "Start process"}</StudioButton>
        {command.record?.outcome && !command.record.outcome.pending && command.record.outcome.result?.status === "Accepted"
          ? <StudioButton type="button" onClick={() => open(runPath(command.record!.outcome!.executionId))}>Open run</StudioButton> : null}
        <StudioButton type="button" variant="default" onClick={() => open(`/workflows/bpmn/runs?artifact=${encodeURIComponent(publication.executable.artifactId)}`)}>View runs</StudioButton>
      </div><BpmnCommandNotice command={command} label="start" />
    </td></tr>;
}
