import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { FormsPage } from "./FormsPage";
import { actor, list, read, mutate, message, statusOf, type TaskActor, type TaskItem, type TaskPage } from "./taskApi";

export function TasksPage({ api, initialId }: { api: ElsaStudioModuleApi; initialId?: string }) {
  const [identity, setIdentity] = useState<TaskActor>();
  const [view, setView] = useState("mine"); const [page, setPage] = useState<TaskPage>();
  const [cursor, setCursor] = useState<string | null>(null); const [search, setSearch] = useState("");
  const [selection, setSelection] = useState(initialId); const [error, setError] = useState(""); const [loading, setLoading] = useState(true);
  const sequence = useRef(0);
  const refresh = useCallback(async () => {
    const current = ++sequence.current; setLoading(true);
    try { const who = await actor(api); if (current !== sequence.current) return; setIdentity(who);
      const next = who.canRead || who.canManage ? await list(api, view, cursor, search) : undefined;
      if (current === sequence.current) { setPage(next); setError(""); } }
    catch (failure) { if (current === sequence.current) setError(message(failure)); }
    finally { if (current === sequence.current) setLoading(false); }
  }, [api, view, cursor, search]);
  useEffect(() => { void refresh(); return () => { sequence.current++; }; }, [refresh]);
  useEffect(() => { setSelection(initialId); }, [initialId]);
  if (identity && !identity.canRead && !identity.canManage && (identity.canReadForms || identity.canManageForms)) return <FormsPage api={api} />;
  return <StudioPage className="user-tasks"><StudioPageHeader title="User Tasks" description="Your assigned work and available group tasks."
    actions={<StudioButton onClick={() => void refresh()}>Refresh</StudioButton>} />
    <StudioCollectionToolbar><label>Inbox <select value={view} onChange={event => { setView(event.target.value); setCursor(null); }}>
      <option value="mine">My tasks</option><option value="queue">Group queue</option><option value="history">History</option>{identity?.canManage ? <option value="all">All tasks</option> : null}</select></label>
      <label>Search <input type="search" value={search} onChange={event => { setSearch(event.target.value); setCursor(null); }} /></label>
      {identity?.canReadForms || identity?.canManageForms ? <a href={qualifyStudioBackendPath("/user-tasks/forms")}>Reusable forms</a> : null}
    </StudioCollectionToolbar>
    {error ? <div role="alert"><StudioResultState tone="danger" title="Tasks unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /></div> : null}
    <div className="user-task-workspace"><div aria-busy={loading}>
      {loading && !page ? <p role="status">Loading tasks…</p> : null}
      {page?.items.length === 0 ? <StudioResultState title="No tasks here" description="Choose another inbox or refresh when new work arrives." /> : null}
      {page && page.items.length > 0 ? <StudioTableContainer><table><thead><tr><th scope="col">Task</th><th scope="col">Status</th><th scope="col">Assignment</th></tr></thead>
        <tbody>{page.items.map(task => <tr key={task.id}><td><button className="user-task-link" onClick={() => setSelection(task.id)} aria-current={selection === task.id ? "true" : undefined}>{task.title}</button></td>
          <td>{task.status === "CompletionPending" ? "Response pending" : task.status}</td><td>{task.claimantId ?? task.assigneeId ?? task.groupId}</td></tr>)}</tbody></table></StudioTableContainer> : null}
      <div className="user-task-actions">{cursor ? <StudioButton onClick={() => setCursor(null)}>First page</StudioButton> : null}<StudioButton disabled={!page?.next || loading} onClick={() => setCursor(page?.next ?? null)}>Next page</StudioButton></div>
    </div>{selection && identity ? <TaskDetail key={selection} api={api} id={selection} identity={identity} changed={() => void refresh()} /> : <aside><p>Select a task to read its details.</p></aside>}</div>
  </StudioPage>;
}

export function TaskDetail({ api, id, identity, changed }: { api: ElsaStudioModuleApi; id: string; identity: TaskActor; changed(): void }) {
  const [task, setTask] = useState<TaskItem>(); const [data, setData] = useState<Record<string, unknown>>({});
  const [errors, setErrors] = useState<Record<string, string>>({}); const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false); const [missing, setMissing] = useState(false); const [stale, setStale] = useState(false);
  const [targetKind, setTargetKind] = useState("user"); const [target, setTarget] = useState("");
  const edited = useRef(false); const busyRef = useRef(false); const sequence = useRef(0);
  const attempt = useRef<{ action: string; data: Record<string, unknown>; requestId: string; revision: number } | null>(null);
  const refresh = useCallback(async () => {
    const current = ++sequence.current;
    try {
      const next = await read(api, id); if (current !== sequence.current) return;
      setTask(next); setMissing(false); setStale(false);
      if (!edited.current || next.decision) setData(next.decision?.response.data ?? next.prefill ?? {});
      if (next.status === "Completed" || next.status === "Cancelled") attempt.current = null;
    } catch (failure) { if (current !== sequence.current) return; setMissing(statusOf(failure) === 404); setNotice(message(failure)); setStale(true); }
  }, [api, id]);
  useEffect(() => { void refresh(); return () => { sequence.current++; }; }, [refresh]);
  useEffect(() => { if (task?.status !== "CompletionPending") return; const timer = setInterval(() => void refresh(), 2000); return () => clearInterval(timer); }, [task?.status, refresh]);
  const act = async (operation: string, action?: string) => {
    if (!task || busyRef.current) return;
    busyRef.current = true; setBusy(true); setNotice(""); setErrors({}); ++sequence.current;
    const retrying = operation === "complete" && attempt.current !== null;
    try {
      let request: Record<string, unknown> = { requestId: crypto.randomUUID() };
      if (operation === "complete") {
        const previous = attempt.current;
        if (previous && previous.action !== action) { setNotice("A response is unconfirmed. Retry that response or refresh its status first."); return; }
        const next = previous ?? { action: action!, data: structuredClone(data), requestId: crypto.randomUUID(), revision: task.revision };
        attempt.current = next; request = next;
      }
      if (operation === "reassign") request = { ...request, assigneeId: targetKind === "user" ? target : null, groupId: targetKind === "group" ? target : null };
      const result = await mutate(api, task, operation, request);
      setTask(result); if (result.decision) { setData(result.decision.response.data); edited.current = false; }
      if (result.status !== "CompletionPending") attempt.current = null;
      setNotice(result.status === "CompletionPending" ? "Your response is recorded. The workflow will continue when delivery completes." : "Task updated."); changed();
    } catch (failure) {
      const status = statusOf(failure); setNotice(status === 409 ? "This task changed. Refresh to review its current state; your entered values are preserved." : message(failure));
      if (status === 409 || status === 403 || status === 404) setStale(true);
      if (status >= 400 && status < 500 && !retrying) attempt.current = null;
      const payload = typeof failure === "object" && failure !== null && "payload" in failure ? failure.payload : null;
      if (payload && typeof payload === "object" && "errors" in payload && payload.errors && typeof payload.errors === "object") setErrors(payload.errors as Record<string, string>);
    } finally { busyRef.current = false; setBusy(false); }
  };
  if (missing) return <aside><StudioResultState title="Task not found" description="It may no longer be assigned to you or available to your group." /></aside>;
  if (!task) return <aside aria-busy={!notice}>{notice ? <p role="alert">{notice}</p> : <p>Loading task…</p>}<StudioButton onClick={() => void refresh()}>Retry</StudioButton></aside>;
  const terminal = task.status === "Completed" || task.status === "Cancelled";
  const pending = task.status === "CompletionPending";
  const own = task.assigneeId === identity.userId || task.claimantId === identity.userId;
  const canComplete = identity.canAct && own && !terminal && !pending && !stale;
  const renderers = task.form ? api.userTaskForms?.list().filter(item => item.provider === task.form?.provider) ?? [] : [];
  const Renderer = renderers.length === 1 ? renderers[0].component : null;
  return <aside aria-busy={busy} className="user-task-detail"><h2>{task.title}</h2><p>{task.description}</p><p role="status">{pending ? "Response pending" : task.status}</p>
    <StudioButton onClick={() => void refresh()} disabled={busy}>Refresh task</StudioButton>
    {notice ? <p role={stale || Object.keys(errors).length ? "alert" : "status"}>{notice}</p> : null}
    {task.form ? Renderer ? <Renderer form={task.form} value={data} onChange={next => { edited.current = true; setData(next); }} readOnly={!canComplete || busy || Boolean(attempt.current)} errors={errors} />
      : <StudioResultState tone="danger" title="Form integration unavailable" description="Install the matching form integration before completing this task." /> : null}
    <div className="user-task-actions">
      {!own && task.groupId && task.status === "Open" && identity.canAct ? <StudioButton disabled={busy || stale} onClick={() => void act("claim")}>Claim task</StudioButton> : null}
      {task.claimantId === identity.userId && task.status === "Claimed" && identity.canAct ? <StudioButton disabled={busy || stale || Boolean(attempt.current)} onClick={() => void act("release")}>Release task</StudioButton> : null}
      {own && !terminal && !pending ? task.actions.map(action => <StudioButton key={action} disabled={!canComplete || busy || Boolean(task.form && !Renderer) || Boolean(attempt.current && attempt.current.action !== action)} onClick={() => void act("complete", action)}>{attempt.current?.action === action ? `Retry ${action}` : action}</StudioButton>) : null}
    </div>
    {identity.canManage && !terminal && !pending ? <fieldset disabled={busy || stale || Boolean(attempt.current)}><legend>Reassign task</legend>
      <label>Assign to <select value={targetKind} onChange={event => setTargetKind(event.target.value)}><option value="user">User</option><option value="group">Group</option></select></label>
      <label>{targetKind === "user" ? "User ID" : "Group ID"}<input value={target} onChange={event => setTarget(event.target.value)} /></label>
      <StudioButton disabled={!target.trim()} onClick={() => void act("reassign")}>Reassign</StudioButton></fieldset> : null}
    <details><summary>History</summary><ol>{task.history.map((entry, index) => <li key={`${entry.at}-${index}`}>{entry.operation}{entry.action ? ` · ${entry.action}` : ""} · {entry.actorId} · <time dateTime={entry.at}>{new Date(entry.at).toLocaleString()}</time></li>)}</ol></details>
  </aside>;
}
