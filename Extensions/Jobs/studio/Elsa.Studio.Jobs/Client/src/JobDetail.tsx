import React, { useEffect, useMemo, useRef, useState } from "react";
import { useStudioRouteLocation, useStudioWorkspaceTitle, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StatusChip, StudioAlert, StudioButton, StudioPage, StudioPageHeader, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { cancel, pause, release, retry } from "./actions";
import { JobsClient, JobsFault, failureText, gateQuery } from "./jobsApi";
import { payloadDocument } from "./jsonPayload";
import { JobsLink, JobsNavigation, MutationNotice, PageFooter, ParentLinks, ReadState, date, gateLabel, type Navigate } from "./Shared";
import { can, useJobsActorBoundary, useJobsMutation, useJobsRead } from "./useJobs";
import { permissions, type Attempt, type Gate, type Hold, type InputView, type Job, type JobHolds, type Page, type Receipt, type ResultView } from "./types";

export function attemptLabel(attempt: Attempt) {
  if (attempt.state === "permitted" && !attempt.physicalStartedAt) return "Starting";
  if (attempt.state === "running" && attempt.physicalStartedAt && !attempt.physicalExitedAt) return "Running";
  return attempt.state === "running" ? "Entry unconfirmed" : attempt.state.replace(/([A-Z])/g, " $1");
}
export function consumptionText(job: Job) {
  switch (job.consumptionState) {
    case "pending-result": return "No terminal result yet. The parent continuation remains unresolved.";
    case "owed": return "The result is saved. Delivery to its model owner remains owed; workflow holds may prevent a later continuation permission.";
    case "settled": return "Delivery is settled by accepted consumption or positive terminal-parent proof. This does not independently prove that the model consumed the result.";
    case "not-applicable": return "No awaited parent continuation applies to this independent or detached job.";
    default: return "Continuation status is unknown or unavailable. A saved result is not evidence of model consumption.";
  }
}

export function JobDetail({ api, id, navigate }: { api: ElsaStudioModuleApi; id: string; navigate: Navigate }) {
  const location = useStudioRouteLocation(); const requestedBack = location.searchParams.get("returnTo");
  const back = requestedBack && (requestedBack === "/jobs" || requestedBack.startsWith("/jobs?")) ? requestedBack : "/jobs";
  const read = useJobsRead(api, "job:" + id, client => client.read<Job>("jobs", [id]));
  const [newJobId, setNewJobId] = useState<string>();
  const [evidenceRefresh, setEvidenceRefresh] = useState(0);
  const refresh = () => { setEvidenceRefresh(value => value + 1); void read.refresh(); };
  const actorChanged = useJobsActorBoundary(api, read.identity, () => setNewJobId(undefined));
  const mutation = useJobsMutation(api, result => {
    if (typeof result === "object" && result !== null && "jobId" in result && typeof result.jobId === "string" && result.jobId !== id) setNewJobId(result.jobId);
    refresh();
  }, refresh);
  const heading = useRef<HTMLDivElement>(null); useEffect(() => { const title = heading.current?.querySelector("h1"); title?.setAttribute("tabindex", "-1"); title?.focus(); }, [id]);
  const job = read.value; useStudioWorkspaceTitle(job?.jobId, job?.outcome ?? "Job");
  const gates = useMemo<Gate[]>(() => {
    if (!job) return [];
    const result: Gate[] = [];
    if (job.queue) result.push({ kind: "queue", name: job.queue });
    if (job.handler) result.push({ kind: "jobType", name: job.handler.jobType });
    if (job.association) result.push({ kind: "workflowInstance", name: job.association.executionId, modelKind: job.association.modelKind },
      { kind: "workflowDefinition", name: job.association.definitionId, modelKind: job.association.modelKind });
    return result;
  }, [job]);
  return <StudioPage className="jobs" aria-label="Job detail">
    <div ref={heading}><StudioPageHeader title="Job detail" description="Retained work, physical evidence and delivery responsibility."
      actions={<StudioButton onClick={refresh} disabled={read.loading || mutation.busy}>Refresh</StudioButton>} /></div>
    <JobsNavigation navigate={navigate} /><JobsLink path={back} navigate={navigate}>Back to Jobs</JobsLink>
    <MutationNotice mutation={mutation} />
    {job && newJobId && !actorChanged && <StudioAlert><JobsLink path={`/jobs/${encodeURIComponent(newJobId)}`} navigate={navigate}>Open new linked job {newJobId}</JobsLink>. The original job and result remain retained.</StudioAlert>}
    <ReadState {...read} loading={read.loading || actorChanged} retry={() => void read.refresh()}>{job && <>
      <section className="jobs-stack" aria-label="Job summary">
        <CopyableIdentifier value={job.jobId} label="Job ID" />
        <div className="jobs-actions"><StatusChip tone={job.outcome === "succeeded" ? "success" : job.outcome === "failed" || job.outcome === "timedOut" ? "danger" : "neutral"}>{job.diagnostic === "jobs.record.unavailable" ? "Execution metadata unavailable" : job.outcome ?? (job.activePermission ? "Start permission granted" : "Waiting")}</StatusChip>
          {job.resultId && <a href="#job-result">Inspect saved result</a>}</div>
        <dl className="jobs-facts"><div><dt>Handler</dt><dd>{job.handler ? `${job.handler.jobType} · ${job.handler.version}` : "Unavailable"}</dd></div>
          <div><dt>Queue</dt><dd>{job.queue ?? "Unavailable"}</dd></div><div><dt>Revision</dt><dd>{job.revision}</dd></div>
          <div><dt>Accepted</dt><dd>{date(job.acceptedAt)}</dd></div><div><dt>Eligible</dt><dd>{date(job.eligibleAt)}</dd></div>
          <div><dt>Completed</dt><dd>{date(job.completedAt)}</dd></div></dl>
        {job.state === "accepted" && <p>Waiting until eligible and all applicable holds/capacity permit another start. Eligibility can reflect initial delay or retry backoff.</p>}
        {job.activePermission && <p>Attempt {job.activePermission.number} has durable start permission until {date(job.activePermission.expiresAt)} and a deadline of {date(job.activePermission.deadline)}. See reported Starting/Running evidence below; permission alone does not prove handler entry or exit.</p>}
        {job.progressFraction !== null && job.progressFraction !== undefined && <label>Reported progress <progress value={job.progressFraction} max={1} /> {Math.round(job.progressFraction * 100)}%</label>}
        {job.cancellationRequested && <StudioAlert tone="warning">Cancellation was requested. Granted code may still run; physical exit is a separate observation.</StudioAlert>}
        {job.diagnostic && <StudioAlert tone="warning">{job.diagnostic === "jobs.record.unavailable" ? "Retained execution content is unavailable. Metadata is preserved; new recovery actions may be refused." : "This job ended without a successful result. Review attempts and permitted result details."}</StudioAlert>}
        {job.retriedFromJobId && <JobsLink path={`/jobs/${encodeURIComponent(job.retriedFromJobId)}`} navigate={navigate}>Open original job {job.retriedFromJobId}</JobsLink>}
        <div className="jobs-actions">
          {job.state !== "terminal" && can(read.identity, permissions.cancel) && <StudioButton disabled={mutation.blocked} onClick={() => void cancel(api, mutation, job)}>Request cancellation…</StudioButton>}
          {job.state === "terminal" && can(read.identity, permissions.retry) && <StudioButton disabled={mutation.blocked || job.inputPayloadState === "expired" || Boolean(job.diagnostic === "jobs.record.unavailable")} onClick={() => void retry(api, mutation, job)}>Retry as new run…</StudioButton>}
        </div>
        {job.inputPayloadState === "expired" && <p>The accepted input payload has expired. Its identity remains retained; Retry as new run requires the original input and is unavailable.</p>}
      </section>
      <section aria-label="Parent and result delivery" className="jobs-stack"><h2>Parent and result delivery</h2><p>{consumptionText(job)}</p>
        <ParentLinks association={job.association} navigate={navigate} /></section>
      <section className="jobs-stack" aria-label="Pause scopes"><h2>Pause scopes</h2>
        <p>Pause blocks later durable permissions. Already granted work may finish. Workflow holds also prevent later result-driven continuation permissions.</p>
        {can(read.identity, permissions.pause) ? <div className="jobs-actions">{gates.map(gate => <StudioButton key={JSON.stringify(gate)} disabled={mutation.blocked} onClick={() => void pause(api, mutation, gate)}>Pause {gateLabel(gate)}…</StudioButton>)}</div> : <p>Pause actions are unavailable with your current permissions.</p>}
      </section>
      <JobHoldPanel key={`holds:${evidenceRefresh}`} api={api} jobId={id} revision={job.revision} mutation={mutation} />
      <AttemptPanel key={`attempts:${evidenceRefresh}`} api={api} jobId={id} revision={job.revision} activeAttemptId={job.activePermission?.attemptId} />
      <ReceiptPanel key={`receipts:${evidenceRefresh}`} api={api} jobId={id} revision={job.revision} />
      <section id="job-result" aria-label="Saved result" className="jobs-stack"><h2>Saved result</h2>
        {job.resultId ? <CopyableIdentifier value={job.resultId} label="Selected result ID" /> : <p>{job.diagnostic === "jobs.record.unavailable" ? "Selected result metadata is unavailable." : "No terminal result has been selected."}</p>}
        <p>{consumptionText(job)}</p><PayloadPanel key={`result:${evidenceRefresh}:${job.resultPayloadState}`} api={api} jobId={id} kind="result" allowed={can(read.identity, permissions.results)} /></section>
      <section aria-label="Accepted input" className="jobs-stack"><h2>Accepted input</h2><PayloadPanel key={`input:${evidenceRefresh}:${job.inputPayloadState}`} api={api} jobId={id} kind="input" allowed={can(read.identity, permissions.inputs)} /></section>
    </>}</ReadState>
  </StudioPage>;
}

function AttemptPanel({ api, jobId, revision, activeAttemptId }: { api: ElsaStudioModuleApi; jobId: string; revision: number; activeAttemptId?: string | null }) {
  const [after, setAfter] = useState<string>();
  const read = useJobsRead(api, JSON.stringify([jobId, revision, "attempts", after]), client => client.read<Page<Attempt>>("jobs", [jobId, "attempts"], { limit: 25, after }));
  return <section className="jobs-stack" aria-label="Attempt timeline"><h2>Attempt timeline</h2>
    <p>Starting means permission granted without reported entry. Running requires reported entry. Expiry, timeout, cancellation and ownership loss do not prove physical exit.</p>
    <ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}><StudioTableContainer role="region" aria-label="Attempt timeline table" tabIndex={0}>
      <table className="studio-table"><thead><tr><th scope="col">Attempt</th><th scope="col">State</th><th scope="col">Permission / owner</th><th scope="col">Physical entry / exit</th></tr></thead>
        <tbody>{read.value?.items.map(attempt => <tr key={attempt.permission.attemptId}><td>{attempt.permission.number}{attempt.permission.attemptId === activeAttemptId ? " · current permission" : ""}<br />{attempt.permission.attemptId}</td>
          <td><StatusChip>{attemptLabel(attempt)}</StatusChip></td><td>{date(attempt.permission.grantedAt)}<br />{attempt.permission.owner}<br />Expires {date(attempt.permission.expiresAt)}<br />Deadline {date(attempt.permission.deadline)}</td>
          <td>{date(attempt.physicalStartedAt)} / {attempt.physicalExitedAt ? date(attempt.physicalExitedAt) : "Exit unobserved"}</td></tr>)}</tbody></table>
    </StudioTableContainer></ReadState>
    {read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={setAfter} />}
  </section>;
}
function ReceiptPanel({ api, jobId, revision }: { api: ElsaStudioModuleApi; jobId: string; revision: number }) {
  const [after, setAfter] = useState<string>();
  const read = useJobsRead(api, JSON.stringify([jobId, revision, "receipts", after]), client => client.read<Page<Receipt>>("jobs", [jobId, "physical-receipts"], { limit: 25, after }));
  return <section className="jobs-stack" aria-label="Physical receipts"><h2>Physical receipts</h2><p>Physical reports can arrive late. They do not replace the selected result.</p>
    <ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}><ul className="jobs-records">{read.value?.items.map(receipt => <li key={receipt.receiptId}>{receipt.attemptId} · {receipt.outcome} · {date(receipt.reportedAt)} · {receipt.owner}{receipt.payloadState === "expired" ? " · Payload expired; report identity retained" : ""}</li>)}</ul></ReadState>
    {read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={setAfter} />}
  </section>;
}
function JobHoldPanel({ api, jobId, revision, mutation }: { api: ElsaStudioModuleApi; jobId: string; revision: number; mutation: ReturnType<typeof useJobsMutation> }) {
  const read = useJobsRead(api, JSON.stringify([jobId, revision, "holds"]), client => client.read<JobHolds>("jobs", [jobId, "holds"], { limit: 25 }));
  return <section className="jobs-stack" aria-label="All matching hold reasons"><h2>Matching holds</h2>
    <p>All matching scopes compose. Releasing one hold does not establish eligibility. Continue every gate page to inspect all reasons.</p>
    <ReadState {...read} retry={() => void read.refresh()}>{read.value?.gates.map(entry => <GateHoldPage key={JSON.stringify([entry.gate, revision])} api={api} gate={entry.gate} mutation={mutation} />)}</ReadState>
  </section>;
}
export function GateHoldPage({ api, gate, mutation }: { api: ElsaStudioModuleApi; gate: Gate; mutation: ReturnType<typeof useJobsMutation> }) {
  const [after, setAfter] = useState<string>();
  const read = useJobsRead(api, JSON.stringify([gate, after]), client => client.read<Page<Hold>>("holds", [], { ...gateQuery(gate), limit: 25, after }));
  return <section className="jobs-stack"><h3>{gateLabel(gate)}</h3>
    <ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}><ul className="jobs-records">{read.value?.items.map(hold => <li key={hold.holdId}>
      <strong>{hold.reason}</strong><CopyableIdentifier value={hold.holdId} label="Hold ID" /><p>{hold.actor} · revision {hold.revision} · {date(hold.requestedAt)} · {hold.releasedAt ? `Released by ${hold.releasedBy ?? "operator"} at ${date(hold.releasedAt)}` : "Active hold"}</p>
      {!hold.releasedAt && can(read.identity, permissions.pause) && <StudioButton disabled={mutation.blocked} onClick={() => void release(api, mutation, hold)}>Release this hold…</StudioButton>}
    </li>)}</ul></ReadState>
    {read.value?.next && <StudioAlert tone="warning">More hold reasons remain on the next page.</StudioAlert>}
    {read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={setAfter} />}
  </section>;
}
function PayloadPanel({ api, jobId, kind, allowed }: { api: ElsaStudioModuleApi; jobId: string; kind: "input" | "result"; allowed: boolean }) {
  const [open, setOpen] = useState(false);
  if (!allowed) return <p>This payload requires a separate current permission.</p>;
  return <div className="jobs-stack"><StudioButton onClick={() => setOpen(!open)}>{open ? "Hide" : "Load permitted"} {kind}</StudioButton>
    {open && <LoadedPayload api={api} jobId={jobId} kind={kind} close={() => setOpen(false)} />}</div>;
}
function LoadedPayload({ api, jobId, kind, close }: { api: ElsaStudioModuleApi; jobId: string; kind: "input" | "result"; close(): void }) {
  const permission = kind === "input" ? permissions.inputs : permissions.results;
  const read = useJobsRead(api, jobId + ":" + kind, async client => {
    const value = await client.read<InputView | ResultView>("jobs", [jobId, kind]); payloadDocument(value, kind); return value;
  }, permission);
  const exportPayload = async () => {
    try {
      const client = new JobsClient(api); const identity = await client.access();
      if (!can(identity, permission) || identity.tenantId !== read.identity?.tenantId || identity.subjectId !== read.identity?.subjectId) throw new JobsFault(403);
      const payload = await client.read<InputView | ResultView>("jobs", [jobId, kind]);
      const url = URL.createObjectURL(new Blob([payloadDocument(payload, kind)], { type: "application/json" }));
      try { const link = document.createElement("a"); link.href = url; link.download = `job-${jobId.replace(/[^a-zA-Z0-9_-]/g, "_")}-${kind}.json`; link.click(); }
      finally { URL.revokeObjectURL(url); }
    } catch (error) { close(); api.notifications?.notify({ level: "error", title: "Export unavailable", message: failureText(error) }); }
  };
  return <ReadState {...read} retry={() => void read.refresh()}>{read.value && <>
    <p>{read.value.state === "expired" ? `The ${kind} payload expired on ${date(read.value.expiredAt)}. Its identity and original digest remain retained.`
      : read.value.state === "pending" ? "No terminal result is available yet." : "Current authorized payload. Values are rendered as text."}</p>
    <pre className="jobs-json" tabIndex={0} role="region" aria-label={`Permitted ${kind} JSON`}>{payloadDocument(read.value, kind)}</pre>
    <StudioButton onClick={() => void exportPayload()}>{read.value.state === "expired" ? `Export retained ${kind} metadata` : `Export permitted ${kind} JSON`}</StudioButton>
  </>}</ReadState>;
}
