import React, { useEffect, useRef, useState } from "react";
import { useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StudioAlert, StudioButton, StudioField, StudioPage, StudioPageHeader } from "@elsa-workflows/studio-ui";
import { enableSchedule, pause } from "./actions";
import { JobsClient, JobsFault, failureText, newOperation, statusOf } from "./jobsApi";
import { InvocationFields, invocationDraft, toInvocation, utc } from "./InvocationFields";
import { retainedValueJson } from "./jsonPayload";
import { JobsLink, JobsNavigation, MutationNotice, ParentLinks, ReadState, date, type Navigate } from "./Shared";
import { can, useJobsActorBoundary, useJobsMutation, useJobsNavigationGuard, useJobsRead } from "./useJobs";
import { permissions, type InputView, type Schedule } from "./types";

type Props = { api: ElsaStudioModuleApi; navigate: Navigate };
function Validation({ text, target }: { text: string; target: React.RefObject<HTMLDivElement | null> }) {
  return text ? <div ref={target} tabIndex={-1}><StudioAlert tone="danger">{text}</StudioAlert></div> : null;
}
export function SubmitPage({ api, navigate }: Props) {
  const read = useJobsRead(api, "submit", async () => true, permissions.submit);
  const [draft, setDraft] = useState(() => invocationDraft()); const [dirty, setDirty] = useState(false);
  const [validation, setValidation] = useState(""); const error = useRef<HTMLDivElement>(null); const [redirect, setRedirect] = useState("");
  const actorChanged = useJobsActorBoundary(api, read.identity, () => { setDraft(invocationDraft()); setDirty(false); setValidation(""); setRedirect(""); });
  const whenActive = useStudioWorkspaceWhenActive();
  const mutation = useJobsMutation(api, result => { setDirty(false); if (typeof result === "object" && result !== null && "jobId" in result && typeof result.jobId === "string") setRedirect(`/jobs/${encodeURIComponent(result.jobId)}`); }, () => void read.refresh());
  useJobsNavigationGuard(dirty && !mutation.unconfirmed);
  useEffect(() => { if (redirect && !dirty && !mutation.blocked) whenActive(() => navigate(redirect)); }, [redirect, dirty, mutation.blocked, navigate, whenActive]);
  useEffect(() => { if (validation) error.current?.focus(); }, [validation]);
  const discard = async () => { if (!dirty || await api.dialogs.confirm({ title: "Discard unsent job?", message: "Your unsent input will be discarded.", confirmLabel: "Discard input", cancelLabel: "Keep editing", tone: "danger" })) { setDirty(false); setRedirect("/jobs"); } };
  return <StudioPage className="jobs" aria-label="Submit standalone job"><StudioPageHeader title="Submit job" description="Invoke an explicitly registered handler independently of any workflow." />
    <StudioButton disabled={mutation.blocked} onClick={() => void discard()}>Cancel and return to Jobs</StudioButton><MutationNotice mutation={mutation} />
    <ReadState {...read} loading={read.loading || actorChanged} retry={() => void read.refresh()}><form className="jobs-form" onSubmit={event => {
      event.preventDefault(); try { const invocation = toInvocation(draft); setValidation(""); void mutation.run({ method: "POST", relation: "jobs", segments: [],
        permission: permissions.submit, title: "Job accepted", body: { idempotencyKey: crypto.randomUUID(), invocation } }); }
      catch (failure) { setValidation(failure instanceof Error ? failure.message : "Check the invocation fields."); }
    }}><Validation text={validation} target={error} /><fieldset disabled={mutation.blocked}><legend>Registered invocation</legend>
      <InvocationFields value={draft} change={value => { setDraft(value); setDirty(true); }} /><StudioButton type="submit" variant="primary">Submit independent job</StudioButton>
    </fieldset></form></ReadState>
  </StudioPage>;
}

export function ScheduleDetail({ api, id, navigate }: Props & { id: string }) {
  const read = useJobsRead(api, "schedule:" + id, client => client.read<Schedule>("schedules", [id]));
  const mutation = useJobsMutation(api, () => void read.refresh(), () => void read.refresh()); const schedule = read.value;
  const scopes = schedule ? [schedule.queue ? { kind: "queue" as const, name: schedule.queue } : null,
    schedule.handler ? { kind: "jobType" as const, name: schedule.handler.jobType } : null,
    schedule.association ? { kind: "workflowInstance" as const, name: schedule.association.executionId, modelKind: schedule.association.modelKind } : null,
    schedule.association ? { kind: "workflowDefinition" as const, name: schedule.association.definitionId, modelKind: schedule.association.modelKind } : null].filter(value => value !== null) : [];
  return <StudioPage className="jobs" aria-label="Job schedule detail"><StudioPageHeader title="Schedule" description="UTC recurrence and retained schedule responsibility."
    actions={<StudioButton onClick={() => void read.refresh()} disabled={read.loading || mutation.busy}>Refresh</StudioButton>} /><JobsNavigation navigate={navigate} />
    <JobsLink path="/jobs/schedules" navigate={navigate}>Back to schedules</JobsLink><MutationNotice mutation={mutation} />
    <ReadState {...read} retry={() => void read.refresh()}>{schedule && <div className="jobs-stack"><CopyableIdentifier value={schedule.scheduleId} label="Schedule ID" />
      <dl className="jobs-facts"><div><dt>State</dt><dd>{schedule.enabled ? "Enabled" : "Disabled"}</dd></div><div><dt>Revision</dt><dd>{schedule.revision}</dd></div>
        <div><dt>Next owed occurrence</dt><dd>{date(schedule.nextOccurrence)}</dd></div><div><dt>Recurrence (UTC)</dt><dd>{schedule.recurrence ? `${schedule.recurrence.kind}: ${schedule.recurrence.expression}` : "Unavailable"}</dd></div>
        <div><dt>Catch-up policy</dt><dd>{schedule.missedPolicy === "coalesceOne" ? "One catch-up run, then resume schedule" : schedule.missedPolicy === "replayEveryOccurrence" ? "Replay every occurrence in bounded batches" : "Unavailable"}</dd></div></dl>
      {schedule.diagnostic && <StudioAlert tone="warning">The schedule definition or recurrence arithmetic is unavailable. Its original cursor and accepted jobs remain retained.</StudioAlert>}
      <p>Changing or disabling this schedule retains accepted history and owed occurrences. A due cursor can be held by any matching scope. Counts and occurrence history are not supplied by this API.</p>
      <ParentLinks association={schedule.association} navigate={navigate} />
      <div className="jobs-actions">{can(read.identity, permissions.schedules) && <><StudioButton disabled={mutation.blocked} onClick={() => void enableSchedule(api, mutation, schedule)}>{schedule.enabled ? "Disable" : "Enable"} schedule…</StudioButton>
        {can(read.identity, permissions.inputs) && !schedule.diagnostic && <JobsLink path={`/jobs/schedules/${encodeURIComponent(id)}/edit`} navigate={navigate}>Edit schedule</JobsLink>}</>}
        {can(read.identity, permissions.pause) && scopes.map(gate => <StudioButton key={JSON.stringify(gate)} disabled={mutation.blocked} onClick={() => void pause(api, mutation, gate)}>Pause {gate.kind}…</StudioButton>)}
      </div>{schedule.queue && <JobsLink path={`/jobs?queue=${encodeURIComponent(schedule.queue)}&jobType=${encodeURIComponent(schedule.handler?.jobType ?? "")}`} navigate={navigate}>Inspect accepted jobs with this queue and type</JobsLink>}
      <div className="jobs-actions">{scopes.map(gate => <JobsLink key={JSON.stringify(gate)} path={`/jobs/holds?${new URLSearchParams({ kind: gate.kind, name: gate.name, ...(gate.modelKind ? { modelKind: gate.modelKind } : {}) })}`} navigate={navigate}>Inspect {gate.kind} holds</JobsLink>)}</div>
    </div>}</ReadState>
  </StudioPage>;
}

export function ScheduleEditor({ api, id, navigate }: Props & { id?: string }) {
  const read = useJobsRead<{ schedule?: Schedule; input?: InputView }>(api, "schedule-editor:" + (id ?? "new"), async client => {
    if (!id) return {};
    const access = await client.access(); if (!can(access, permissions.inputs) || !can(access, permissions.read)) throw new JobsFault(403);
    const [schedule, input] = await Promise.all([client.read<Schedule>("schedules", [id]), client.read<InputView>("schedules", [id, "input"])]);
    if (!schedule.handler || !schedule.inputSchema || !schedule.resultSchema || !schedule.queue || !schedule.policy || !schedule.recurrence || !schedule.firstOccurrence || !schedule.missedPolicy || !input.input) throw new JobsFault(503);
    retainedValueJson(input.valueJson);
    return { schedule, input };
  }, permissions.schedules);
  const [scheduleId, setScheduleId] = useState(id ?? ""); const [draft, setDraft] = useState(() => invocationDraft());
  const [kind, setKind] = useState<"interval" | "cron">("interval"); const [expression, setExpression] = useState("PT1H");
  const [first, setFirst] = useState(() => new Date(Math.ceil(Date.now() / 60000) * 60000 + 3600000).toISOString());
  const [missed, setMissed] = useState<"coalesceOne" | "replayEveryOccurrence">("coalesceOne");
  const [reason, setReason] = useState(""); const [dirty, setDirty] = useState(false); const [validation, setValidation] = useState("");
  const [expectedRevision, setExpectedRevision] = useState(0); const [needsReview, setNeedsReview] = useState(false); const [review, setReview] = useState<Schedule>(); const [reviewError, setReviewError] = useState<unknown>();
  const [redirect, setRedirect] = useState(""); const error = useRef<HTMLDivElement>(null); const saveButton = useRef<HTMLButtonElement>(null); const whenActive = useStudioWorkspaceWhenActive();
  const actorChanged = useJobsActorBoundary(api, read.identity, () => { setDirty(false); setDraft(invocationDraft()); setReason(""); setValidation(""); setRedirect(""); setReview(undefined); setReviewError(undefined); setNeedsReview(false); });
  const mutation = useJobsMutation(api, () => { setDirty(false); setRedirect("/jobs/schedules"); }, () => void read.refresh());
  useJobsNavigationGuard(dirty && !mutation.unconfirmed);
  useEffect(() => {
    const schedule = read.value?.schedule; const input = read.value?.input?.input; if (!schedule || !input || dirty) return;
    setDraft(invocationDraft({ handler: schedule.handler!, input, resultSchema: schedule.resultSchema!, queue: schedule.queue!, policy: schedule.policy!, notBefore: schedule.notBefore }, read.value!.input!.valueJson!));
    setKind(schedule.recurrence!.kind); setExpression(schedule.recurrence!.expression); setFirst(schedule.firstOccurrence!); setMissed(schedule.missedPolicy!);
    setExpectedRevision(schedule.revision);
  }, [read.value, dirty]);
  useEffect(() => { if (statusOf(mutation.problem) === 409) { setNeedsReview(true); setReview(undefined); } }, [mutation.problem]);
  useEffect(() => { if (redirect && !dirty && !mutation.blocked) whenActive(() => navigate(redirect)); }, [redirect, dirty, mutation.blocked, navigate, whenActive]);
  useEffect(() => { if (validation) error.current?.focus(); }, [validation]);
  const cancelEdit = async () => { if (!dirty || await api.dialogs.confirm({ title: "Discard schedule edits?", message: "Unsent edits will be discarded. Accepted jobs and the saved schedule are unchanged.", confirmLabel: "Discard edits", cancelLabel: "Keep editing", tone: "danger" })) { setDirty(false); setRedirect("/jobs/schedules"); } };
  const openExisting = async () => { if (await api.dialogs.confirm({ title: "Discard new schedule fields?", message: "Open the existing schedule after discarding these unsent fields.", confirmLabel: "Open existing schedule", cancelLabel: "Keep editing", tone: "danger" })) { setDirty(false); setRedirect(`/jobs/schedules/${encodeURIComponent(scheduleId)}`); } };
  const save = async () => {
    if (needsReview) return;
    try {
      if (!scheduleId.trim() || scheduleId.length > 256 || !expression.trim() || expression.length > 1024 || !reason.trim() || reason.length > 256) throw new Error("A bounded schedule ID, recurrence and reason are required.");
      const invocation = toInvocation(draft); const firstOccurrence = utc(first);
      if (!await api.dialogs.confirm({ title: id ? "Save schedule revision?" : "Create schedule?", message: `${missed === "coalesceOne" ? "Create one catch-up run after missed time, then return to schedule." : "Replay every missed occurrence in bounded batches; this can create a large durable backlog."} Accepted jobs and owed occurrences remain retained.`, confirmLabel: id ? "Save schedule" : "Create schedule" })) return;
      setValidation(""); await mutation.run({ method: "PUT", relation: "schedules", segments: [scheduleId], permission: permissions.schedules, title: "Schedule saved",
        body: { ...newOperation(reason), invocation, recurrence: { kind, expression }, firstOccurrence, missedPolicy: missed, expectedRevision } });
    } catch (failure) { setValidation(failure instanceof Error ? failure.message : "Check the schedule fields."); }
  };
  const update = (action: () => void) => { action(); setDirty(true); };
  const reviewCurrent = async () => {
    try {
      const client = new JobsClient(api); const identity = await client.access();
      if (!can(identity, permissions.read) || !can(identity, permissions.schedules)) throw new JobsFault(403);
      setReview(await client.read<Schedule>("schedules", [scheduleId])); setReviewError(undefined);
    } catch (failure) { setReview(undefined); setReviewError(failure); }
  };
  return <StudioPage className="jobs" aria-label={id ? "Edit job schedule" : "Create job schedule"}><StudioPageHeader title={id ? "Edit schedule" : "Create schedule"}
    description="Pin a registered invocation and a UTC recurrence. Accepted jobs retain their original content." />
    <StudioButton disabled={mutation.blocked} onClick={() => void cancelEdit()}>Cancel and return to schedules</StudioButton><MutationNotice mutation={mutation} />
    {needsReview && <StudioAlert tone="warning"><p>Your entered values are preserved. {id ? "Review the current schedule before adopting its revision for a new save." : "This identity may already exist. Change the new schedule ID or open its existing detail."}</p>
      {id ? <StudioButton disabled={mutation.blocked} onClick={() => void reviewCurrent()}>Review current schedule</StudioButton> : <StudioButton onClick={() => void openExisting()}>Open existing schedule…</StudioButton>}
      {review && <div><p>Current revision {review.revision} · {review.enabled ? "Enabled" : "Disabled"} · {review.recurrence?.kind}: {review.recurrence?.expression} · {review.missedPolicy} · first {date(review.firstOccurrence)}</p>
        <StudioButton onClick={() => { setExpectedRevision(review.revision); setNeedsReview(false); requestAnimationFrame(() => saveButton.current?.focus()); }}>Use reviewed revision {review.revision}</StudioButton></div>}
      {reviewError ? <p>{failureText(reviewError)}</p> : null}
    </StudioAlert>}
    <ReadState {...read} loading={read.loading || actorChanged} retry={() => void read.refresh()}><form className="jobs-form" onSubmit={event => { event.preventDefault(); void save(); }}><Validation text={validation} target={error} />
      <fieldset disabled={mutation.blocked}><legend>Schedule</legend><StudioField label="Schedule ID"><input required maxLength={256} disabled={Boolean(id)} value={scheduleId} onChange={event => update(() => { setScheduleId(event.target.value); setNeedsReview(false); })} /></StudioField>
        {id && <p>Editing revision {expectedRevision}. A conflict preserves entered values and requires current-state review before another save.</p>}
        <InvocationFields value={draft} change={value => update(() => setDraft(value))} />
        <div className="jobs-form-grid"><StudioField label="UTC recurrence"><select value={kind} onChange={event => update(() => setKind(event.target.value as "interval" | "cron"))}><option value="interval">Interval</option><option value="cron">Cron (UTC)</option></select></StudioField>
          <StudioField label="Recurrence expression" description={kind === "interval" ? "ISO 8601 duration, for example PT1H." : "UTC cron expression interpreted by the installed calculator."}><input required maxLength={1024} value={expression} onChange={event => update(() => setExpression(event.target.value))} /></StudioField>
          <StudioField label="First occurrence (UTC)" description="ISO UTC time ending in Z, including seconds."><input required value={first} onChange={event => update(() => setFirst(event.target.value))} /></StudioField>
          <StudioField label="Missed occurrences" description="Accepted jobs are always retained. This policy applies to unmaterialized occurrences."><select value={missed} onChange={event => update(() => setMissed(event.target.value as typeof missed))}>
            <option value="coalesceOne">One catch-up run, then resume schedule</option><option value="replayEveryOccurrence">Replay every occurrence in bounded batches</option>
          </select></StudioField></div>
        <StudioField label="Reason"><input required maxLength={256} value={reason} onChange={event => update(() => setReason(event.target.value))} /></StudioField>
        <StudioButton ref={saveButton} type="submit" variant="primary" disabled={needsReview}>{id ? "Review and save schedule" : "Review and create schedule"}</StudioButton>
      </fieldset></form></ReadState>
  </StudioPage>;
}
