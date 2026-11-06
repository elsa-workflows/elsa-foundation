import React, { useEffect, useRef, useState } from "react";
import { useStudioRouteLocation, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioButton, StudioCollectionToolbar, StudioField, StudioPage, StudioPageHeader, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { JobsLink, JobsNavigation, PageFooter, ReadState, collectionReturn, date, type Navigate } from "./Shared";
import { can, useJobsRead } from "./useJobs";
import { permissions, type Job, type Page } from "./types";

export function JobsPage({ api, navigate }: { api: ElsaStudioModuleApi; navigate: Navigate }) {
  const location = useStudioRouteLocation(); const search = location.search; const params = new URLSearchParams(search);
  const after = params.get("after"); const queue = params.get("queue") ?? ""; const jobType = params.get("jobType") ?? ""; const state = params.get("state") ?? "";
  const [filters, setFilters] = useState({ queue, jobType, state });
  const container = useRef<HTMLDivElement>(null); const focused = useRef(false); const focusJob = params.get("focus");
  useEffect(() => setFilters({ queue, jobType, state }), [queue, jobType, state]);
  const read = useJobsRead(api, "jobs:" + search, client => client.read<Page<Job>>("jobs", [], { limit: 25, after, queue, jobType, state }));
  const apply = (cursor?: string, next = filters) => {
    const query = new URLSearchParams(); for (const [key, value] of Object.entries(next)) if (value) query.set(key, value);
    if (cursor) query.set("after", cursor); navigate(collectionReturn(query.toString()));
  };
  const page = read.value;
  useEffect(() => {
    if (!page || !focusJob || focused.current) return;
    const link = [...(container.current?.querySelectorAll<HTMLAnchorElement>("[data-job-id]") ?? [])].find(item => item.dataset.jobId === focusJob);
    if (link) { link.focus(); focused.current = true; }
  }, [page, focusJob]);
  const detailPath = (jobId: string) => { const context = new URLSearchParams(search); context.set("focus", jobId); return `/jobs/${encodeURIComponent(jobId)}?returnTo=${encodeURIComponent(collectionReturn(context.toString()))}`; };
  return <StudioPage className="jobs" aria-label="Jobs">
    <StudioPageHeader title="Jobs" description="Inspect accepted work, retained results and recovery."
      actions={<>{can(read.identity, permissions.submit) && <JobsLink className="studio-button" data-variant="primary" path="/jobs/submit" navigate={navigate}>Submit job</JobsLink>}
        <StudioButton onClick={() => void read.refresh()} disabled={read.loading}>Refresh</StudioButton></>} />
    <JobsNavigation navigate={navigate} />
    <form onSubmit={event => { event.preventDefault(); apply(); }}><StudioCollectionToolbar>
      <StudioField label="Queue"><input value={filters.queue} maxLength={256} onChange={event => setFilters({ ...filters, queue: event.target.value })} /></StudioField>
      <StudioField label="Job type"><input value={filters.jobType} maxLength={256} onChange={event => setFilters({ ...filters, jobType: event.target.value })} /></StudioField>
      <StudioField label="Status"><select value={filters.state} onChange={event => setFilters({ ...filters, state: event.target.value })}>
        <option value="">All statuses</option><option value="Accepted">Waiting</option><option value="AttemptActive">Start permission active</option><option value="Terminal">Terminal</option>
      </select></StudioField><StudioButton type="submit" disabled={read.loading}>Apply filters</StudioButton>
      {(queue || jobType || state) && <StudioButton onClick={() => { setFilters({ queue: "", jobType: "", state: "" }); apply(undefined, { queue: "", jobType: "", state: "" }); }}>Clear filters</StudioButton>}
    </StudioCollectionToolbar></form>
    <div ref={container}><ReadState {...read} empty={page?.items.length === 0} retry={() => void read.refresh()}>
      <StudioTableContainer role="region" aria-label="Jobs table" tabIndex={0}><table className="studio-table"><thead><tr>
        <th scope="col">Job</th><th scope="col">Queue / type</th><th scope="col">State</th><th scope="col">Eligible / completed</th><th scope="col">Result delivery</th>
      </tr></thead><tbody>{page?.items.map(job => <tr key={job.jobId}>
        <td><JobsLink data-job-id={job.jobId} path={detailPath(job.jobId)} navigate={navigate}>{job.jobId}</JobsLink></td>
        <td>{job.queue ?? "Unavailable"}<br />{job.handler ? `${job.handler.jobType} · ${job.handler.version}` : "Handler metadata unavailable"}</td>
        <td><StatusChip tone={job.outcome === "failed" || job.outcome === "timedOut" ? "danger" : job.outcome === "succeeded" ? "success" : "neutral"}>{job.diagnostic === "jobs.record.unavailable" ? "Execution metadata unavailable" : job.state === "attemptActive" ? "Start permission active" : job.outcome ?? "Waiting"}</StatusChip>{job.cancellationRequested && <p>Cancellation requested; physical exit may be unobserved.</p>}</td>
        <td>{date(job.completedAt ?? job.eligibleAt)}</td><td>{job.resultId ? "Saved result" : job.diagnostic === "jobs.record.unavailable" ? "Result metadata unavailable" : "No terminal result"}<br />{job.consumptionState}</td>
      </tr>)}</tbody></table></StudioTableContainer>
    </ReadState></div>
    {!read.loading && !read.error && page && <PageFooter count={page.items.length} next={page.next} after={after} change={cursor => apply(cursor, { queue, jobType, state })} />}
  </StudioPage>;
}
