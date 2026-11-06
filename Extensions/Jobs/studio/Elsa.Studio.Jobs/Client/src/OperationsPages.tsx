import React, { useEffect, useState } from "react";
import { useStudioRouteLocation, useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StatusChip, StudioAlert, StudioButton, StudioCollectionToolbar, StudioField, StudioPage, StudioPageHeader, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { enableSchedule, pause, release } from "./actions";
import { newOperation } from "./jobsApi";
import { JobsLink, JobsNavigation, MutationNotice, PageFooter, ReadState, date, gateLabel, type Navigate } from "./Shared";
import { can, useJobsActorBoundary, useJobsMutation, useJobsNavigationGuard, useJobsRead } from "./useJobs";
import { permissions, type Capacity, type Gate, type GateKind, type Hold, type Page, type Queue, type Schedule } from "./types";

type Props = { api: ElsaStudioModuleApi; navigate: Navigate };
function pagePath(base: string, after?: string, filters: Record<string, string> = {}) {
  const params = new URLSearchParams(); for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
  if (after) params.set("after", after); return base + (params.size ? "?" + params.toString() : "");
}
export function QueuesPage({ api, navigate }: Props) {
  const after = useStudioRouteLocation().searchParams.get("after");
  const read = useJobsRead(api, "queues:" + after, client => client.read<Page<Queue>>("queues", [], { limit: 25, after }));
  const mutation = useJobsMutation(api, () => void read.refresh(), () => void read.refresh());
  return <StudioPage className="jobs" aria-label="Job queues"><StudioPageHeader title="Queues" description="Current authorized queue counts. Active permissions do not bound stale physical processes."
    actions={<StudioButton onClick={() => void read.refresh()} disabled={read.loading || mutation.busy}>Refresh</StudioButton>} /><JobsNavigation navigate={navigate} />
    <MutationNotice mutation={mutation} /><ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}>
      <StudioTableContainer role="region" aria-label="Queues table" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Queue</th><th scope="col">Retained accepted jobs</th><th scope="col">Live permissions</th><th scope="col">Held work</th><th scope="col">Actions</th></tr></thead>
        <tbody>{read.value?.items.map(queue => <tr key={queue.name}><td><JobsLink path={`/jobs?queue=${encodeURIComponent(queue.name)}`} navigate={navigate}>{queue.name}</JobsLink></td>
          <td>{queue.acceptedCount}</td><td>{queue.activeLeaseCount}</td><td>{queue.heldCount}</td><td>{can(read.identity, permissions.pause) && <StudioButton disabled={mutation.blocked} onClick={() => void pause(api, mutation, { kind: "queue", name: queue.name })}>Pause queue…</StudioButton>}
            <JobsLink path={`/jobs/holds?kind=queue&name=${encodeURIComponent(queue.name)}`} navigate={navigate}>Inspect holds</JobsLink></td></tr>)}</tbody></table></StudioTableContainer>
    </ReadState>{read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={cursor => navigate(pagePath("/jobs/queues", cursor))} />}
  </StudioPage>;
}
export function SchedulesPage({ api, navigate }: Props) {
  const after = useStudioRouteLocation().searchParams.get("after");
  const read = useJobsRead(api, "schedules:" + after, client => client.read<Page<Schedule>>("schedules", [], { limit: 25, after }));
  const mutation = useJobsMutation(api, () => void read.refresh(), () => void read.refresh());
  return <StudioPage className="jobs" aria-label="Job schedules"><StudioPageHeader title="Schedules" description="Recurring UTC work. Accepted historical jobs remain durable when a schedule changes."
    actions={<>{can(read.identity, permissions.schedules) && <JobsLink className="studio-button" data-variant="primary" path="/jobs/schedules/create" navigate={navigate}>Create schedule</JobsLink>}
      <StudioButton onClick={() => void read.refresh()} disabled={read.loading || mutation.busy}>Refresh</StudioButton></>} /><JobsNavigation navigate={navigate} />
    <MutationNotice mutation={mutation} /><ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}>
      <StudioTableContainer role="region" aria-label="Schedules table" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Schedule</th><th scope="col">State</th><th scope="col">Next owed occurrence</th><th scope="col">Catch-up policy</th><th scope="col">Actions</th></tr></thead>
        <tbody>{read.value?.items.map(schedule => <tr key={schedule.scheduleId}><td><JobsLink path={`/jobs/schedules/${encodeURIComponent(schedule.scheduleId)}`} navigate={navigate}>{schedule.scheduleId}</JobsLink><br />{schedule.queue} · {schedule.handler?.jobType ?? "Definition unavailable"}</td>
          <td><StatusChip tone={schedule.diagnostic ? "warning" : "neutral"}>{schedule.diagnostic ? "Unavailable" : schedule.enabled ? "Enabled" : "Disabled"}</StatusChip></td>
          <td>{date(schedule.nextOccurrence)}</td><td>{schedule.missedPolicy === "coalesceOne" ? "One catch-up run" : schedule.missedPolicy === "replayEveryOccurrence" ? "Replay every occurrence" : "Unavailable"}</td>
          <td>{can(read.identity, permissions.schedules) && <StudioButton disabled={mutation.blocked} onClick={() => void enableSchedule(api, mutation, schedule)}>{schedule.enabled ? "Disable" : "Enable"}…</StudioButton>}</td></tr>)}</tbody></table></StudioTableContainer>
    </ReadState>{read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={cursor => navigate(pagePath("/jobs/schedules", cursor))} />}
  </StudioPage>;
}

export function GateFields({ value, change, all = false }: { value: { kind: string; name: string; modelKind: string }; change(value: { kind: string; name: string; modelKind: string }): void; all?: boolean }) {
  const workflow = value.kind === "workflowInstance" || value.kind === "workflowDefinition";
  return <><StudioField label="Scope"><select value={value.kind} onChange={event => change({ ...value, kind: event.target.value, modelKind: "" })}>
    {all && <option value="">All scopes</option>}<option value="queue">Queue</option><option value="jobType">Job type (all versions)</option>
    <option value="workflowInstance">Workflow instance</option><option value="workflowDefinition">Workflow definition (all versions)</option>
  </select></StudioField><StudioField label="Exact scope name"><input value={value.name} maxLength={256} required={Boolean(value.kind)} disabled={!value.kind} onChange={event => change({ ...value, name: event.target.value })} /></StudioField>
    {workflow && <StudioField label="Model kind" description="For example elsa.native or elsa.bpmn; use the owning model's exact identity."><input value={value.modelKind} maxLength={256} required onChange={event => change({ ...value, modelKind: event.target.value })} /></StudioField>}</>;
}
function asGate(value: { kind: string; name: string; modelKind: string }): Gate | undefined {
  if (!["queue", "jobType", "workflowInstance", "workflowDefinition"].includes(value.kind) || !value.name.trim()) return;
  if ((value.kind === "workflowInstance" || value.kind === "workflowDefinition") && !value.modelKind.trim()) return;
  return { kind: value.kind as GateKind, name: value.name, modelKind: value.modelKind || null };
}
export function HoldsPage({ api, navigate }: Props) {
  const location = useStudioRouteLocation(); const search = location.search; const params = new URLSearchParams(search);
  const applied = { kind: params.get("kind") ?? "", name: params.get("name") ?? "", modelKind: params.get("modelKind") ?? "" }; const after = params.get("after");
  const [filter, setFilter] = useState(applied); useEffect(() => setFilter({ kind: applied.kind, name: applied.name, modelKind: applied.modelKind }), [applied.kind, applied.name, applied.modelKind]);
  const gate = asGate(applied);
  const read = useJobsRead(api, "holds:" + search, client => client.read<Page<Hold>>("holds", [], { limit: 25, after, ...applied }));
  const mutation = useJobsMutation(api, () => void read.refresh(), () => void read.refresh());
  return <StudioPage className="jobs" aria-label="Job holds"><StudioPageHeader title="Holds" description="Independent reasons across queue, type, workflow instance and logical definition. Releasing one reason preserves every other hold."
    actions={<StudioButton onClick={() => void read.refresh()} disabled={read.loading || mutation.busy}>Refresh</StudioButton>} /><JobsNavigation navigate={navigate} />
    <form onSubmit={event => { event.preventDefault(); navigate(pagePath("/jobs/holds", undefined, filter.kind ? filter : { kind: "", name: "", modelKind: "" })); }}><StudioCollectionToolbar>
      <GateFields value={filter} change={setFilter} all /><StudioButton type="submit" disabled={read.loading}>Apply scope</StudioButton>
      {gate && can(read.identity, permissions.pause) && <StudioButton disabled={mutation.blocked} onClick={() => void pause(api, mutation, gate)}>Pause selected scope…</StudioButton>}
    </StudioCollectionToolbar></form><MutationNotice mutation={mutation} />
    <ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}><StudioTableContainer role="region" aria-label="Holds table" tabIndex={0}>
      <table className="studio-table"><thead><tr><th scope="col">Scope</th><th scope="col">Reason</th><th scope="col">Actor / requested</th><th scope="col">State / action</th></tr></thead>
        <tbody>{read.value?.items.map(hold => <tr key={JSON.stringify([hold.gate, hold.holdId])}><td>{gateLabel(hold.gate)}</td><td>{hold.reason}<CopyableIdentifier value={hold.holdId} label="Hold ID" /></td><td>{hold.actor}<br />{date(hold.requestedAt)}<br />Revision {hold.revision}</td>
          <td>{hold.releasedAt ? `Released by ${hold.releasedBy ?? "operator"} · ${date(hold.releasedAt)}` : <><StatusChip tone="warning">Active</StatusChip>{can(read.identity, permissions.pause) && <StudioButton disabled={mutation.blocked} onClick={() => void release(api, mutation, hold)}>Release this hold…</StudioButton>}</>}</td></tr>)}</tbody></table>
    </StudioTableContainer></ReadState>{read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={cursor => navigate(pagePath("/jobs/holds", cursor, applied))} />}
  </StudioPage>;
}
export function CapacityPage({ api, navigate }: Props) {
  const after = useStudioRouteLocation().searchParams.get("after");
  const read = useJobsRead(api, "capacity:" + after, client => client.read<Page<Capacity>>("capacity", [], { limit: 25, after }));
  const mutation = useJobsMutation(api, () => void read.refresh(), () => void read.refresh());
  const edit = async (capacity: Capacity) => {
    const value = await api.dialogs.prompt({ title: "Change live permission limit", message: `${gateLabel(capacity.gate)} at revision ${capacity.revision}. Enter a nonnegative whole number or “unlimited”. This bounds live authorized permissions, not stale physical processes.`, defaultValue: capacity.limit === null || capacity.limit === undefined ? "unlimited" : String(capacity.limit), confirmLabel: "Continue" });
    if (value === null) return;
    const limit = value.trim() === "unlimited" ? null : Number(value);
    if (value.trim() !== "unlimited" && (!value.trim() || !Number.isInteger(limit) || limit! < 0 || limit! > 2147483647)) { await api.dialogs.alert({ title: "Invalid limit", message: "Use a nonnegative whole number or unlimited." }); return; }
    const reason = await api.dialogs.prompt({ title: "Reason for capacity change", message: "Enter a nonblank reason of at most 256 characters.", confirmLabel: "Change limit" });
    if (reason === null) return;
    if (!reason.trim() || reason.length > 256) { await api.dialogs.alert({ title: "Invalid reason", message: "A reason of 1–256 characters is required." }); return; }
    await mutation.run({ method: "PUT", relation: "capacity", segments: [], body: { ...newOperation(reason), gate: capacity.gate, limit, expectedRevision: capacity.revision }, permission: permissions.capacity, title: "Capacity limit updated" });
  };
  return <StudioPage className="jobs" aria-label="Job capacity"><StudioPageHeader title="Capacity" description="Deployment-wide limits on live authorized attempts. Stale physical processes can overlap these limits."
    actions={<>{can(read.identity, permissions.capacity) && <JobsLink className="studio-button" path="/jobs/capacity/create" navigate={navigate}>Create policy</JobsLink>}
      <StudioButton onClick={() => void read.refresh()} disabled={read.loading || mutation.busy}>Refresh</StudioButton></>} /><JobsNavigation navigate={navigate} />
    <MutationNotice mutation={mutation} /><ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}><StudioTableContainer role="region" aria-label="Capacity table" tabIndex={0}>
      <table className="studio-table"><thead><tr><th scope="col">Scope</th><th scope="col">Live permission limit</th><th scope="col">Revision</th><th scope="col">Action</th></tr></thead>
        <tbody>{read.value?.items.map(capacity => <tr key={JSON.stringify(capacity.gate)}><td>{gateLabel(capacity.gate)}</td><td>{capacity.limit ?? "Unlimited"}</td><td>{capacity.revision}</td>
          <td>{can(read.identity, permissions.capacity) && <StudioButton disabled={mutation.blocked} onClick={() => void edit(capacity)}>Change limit…</StudioButton>}</td></tr>)}</tbody></table>
    </StudioTableContainer></ReadState>{read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after} change={cursor => navigate(pagePath("/jobs/capacity", cursor))} />}
  </StudioPage>;
}
export function CreateCapacityPage({ api, navigate }: Props) {
  const [gate, setGate] = useState({ kind: "queue", name: "", modelKind: "" }); const [limit, setLimit] = useState("1"); const [reason, setReason] = useState("");
  const [dirty, setDirty] = useState(false); const [validation, setValidation] = useState(""); const [leaving, setLeaving] = useState(false); const whenActive = useStudioWorkspaceWhenActive();
  const read = useJobsRead(api, "create-capacity", async () => true, permissions.capacity);
  const actorChanged = useJobsActorBoundary(api, read.identity, () => { setDirty(false); setGate({ kind: "queue", name: "", modelKind: "" }); setLimit("1"); setReason(""); setValidation(""); setLeaving(false); });
  const mutation = useJobsMutation(api, () => { setDirty(false); setLeaving(true); }, () => void read.refresh());
  useJobsNavigationGuard(dirty && !mutation.unconfirmed);
  useEffect(() => { if (leaving && !dirty && !mutation.blocked) whenActive(() => navigate("/jobs/capacity")); }, [leaving, dirty, mutation.blocked, navigate, whenActive]);
  const update = (action: () => void) => { action(); setDirty(true); };
  const cancelCreate = async () => { if (!dirty || await api.dialogs.confirm({ title: "Discard capacity policy?", message: "Unsent policy fields will be discarded.", confirmLabel: "Discard policy", cancelLabel: "Keep editing", tone: "danger" })) { setDirty(false); setLeaving(true); } };
  return <StudioPage className="jobs" aria-label="Create capacity policy"><StudioPageHeader title="Create capacity policy" description="Create a revision-zero policy. Change an existing policy from its current collection row." />
    <StudioButton disabled={mutation.blocked} onClick={() => void cancelCreate()}>Cancel and return to capacity</StudioButton><MutationNotice mutation={mutation} />
    <ReadState {...read} loading={read.loading || actorChanged} retry={() => void read.refresh()}><form className="jobs-form" onSubmit={event => {
      event.preventDefault(); const selected = asGate(gate);
      if (!selected || !reason.trim() || reason.length > 256 || !limit.trim() || !Number.isInteger(Number(limit)) || Number(limit) < 0 || Number(limit) > 2147483647) { setValidation("Enter a complete exact scope, nonnegative whole-number limit and nonblank bounded reason."); return; }
      setValidation("");
      void mutation.run({ method: "PUT", relation: "capacity", segments: [], permission: permissions.capacity, title: "Capacity policy created",
        body: { ...newOperation(reason), gate: selected, limit: Number(limit), expectedRevision: 0 } });
    }}>{validation && <StudioAlert tone="danger">{validation}</StudioAlert>}<fieldset disabled={mutation.blocked}><legend>New policy</legend><GateFields value={gate} change={value => update(() => setGate(value))} />
      <StudioField label="Live permission limit" description="Zero prevents new grants; it does not stop already granted code."><input type="number" min={0} max={2147483647} step={1} required value={limit} onChange={event => update(() => setLimit(event.target.value))} /></StudioField>
      <StudioField label="Reason"><input required maxLength={256} value={reason} onChange={event => update(() => setReason(event.target.value))} /></StudioField>
      <StudioButton type="submit" variant="primary">Create policy</StudioButton></fieldset></form></ReadState>
  </StudioPage>;
}
