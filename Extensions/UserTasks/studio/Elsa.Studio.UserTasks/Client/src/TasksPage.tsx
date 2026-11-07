import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Inbox, RefreshCw } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StatusChip, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { FormsPage } from "./FormsPage";
import { actor, list, read, mutate, message, statusOf, type TaskActor, type TaskItem, type TaskPage } from "./taskApi";

const responsePendingNotice = "Your response is recorded. The workflow will continue when delivery completes.";
function TaskStatus({ status }: { status: string }) {
  return <StatusChip tone={status === "Completed" ? "success" : "neutral"}>{status === "CompletionPending" ? "Response pending" : status}</StatusChip>;
}

export function TasksPage({ api, initialId }: { api: ElsaStudioModuleApi; initialId?: string }) {
  const [identity, setIdentity] = useState<TaskActor>();
  const [view, setView] = useState("mine"); const [page, setPage] = useState<TaskPage>();
  const [cursor, setCursor] = useState<string | null>(null); const [search, setSearch] = useState("");
  const [selection, setSelection] = useState(initialId); const [error, setError] = useState(""); const [loading, setLoading] = useState(true);
  const leaveGuard = useRef<() => Promise<boolean>>(async () => true); const selecting = useRef(false);
  const sequence = useRef(0); const root = useRef<HTMLDivElement>(null);
  const refresh = useCallback(async () => {
    const current = ++sequence.current; setLoading(true);
    try { const who = await actor(api); if (current !== sequence.current) return; setIdentity(who);
      const next = who.canRead || who.canManage ? await list(api, view, cursor, search) : undefined;
      if (current === sequence.current) { setPage(next); setError(""); } }
    catch (failure) { if (current === sequence.current) { setPage(undefined); setError(message(failure)); if ([401, 403].includes(statusOf(failure))) { setIdentity(undefined); setSelection(undefined); } } }
    finally { if (current === sequence.current) setLoading(false); }
  }, [api, view, cursor, search]);
  useEffect(() => { void refresh(); return () => { sequence.current++; }; }, [refresh]);
  useEffect(() => { setSelection(initialId); }, [initialId]);
  const changeScope = () => { setPage(undefined); setCursor(null); };
  const selectTask = async (id: string) => {
    if (id === selection || selecting.current) return;
    selecting.current = true;
    try { if (await leaveGuard.current()) setSelection(id); } finally { selecting.current = false; }
  };
  const closeDetail = () => {
    const selected = selection; setSelection(undefined);
    requestAnimationFrame(() => {
      const link = [...(root.current?.querySelectorAll<HTMLAnchorElement>("[data-task-id]") ?? [])].find(item => item.dataset.taskId === selected);
      (link ?? root.current?.querySelector<HTMLInputElement>('input[type="search"]'))?.focus();
    });
  };
  if (identity && !identity.canRead && !identity.canManage && (identity.canReadForms || identity.canManageForms)) return <FormsPage api={api} />;
  const denied = identity && !identity.canRead && !identity.canManage;
  const filtered = Boolean(search.trim());
  return <StudioPage className="user-tasks" aria-label="User Tasks management">
    <StudioPageHeader title="User Tasks" description="Your assigned work and available group tasks."
      actions={<><StudioButton onClick={() => void refresh()} disabled={loading}><RefreshCw size={16} aria-hidden="true" />Refresh</StudioButton>
        {identity?.canReadForms || identity?.canManageForms ? <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks/forms")}>Reusable forms</a> : null}</>} />
    <div ref={root} className="user-task-content">
    {!denied && <StudioCollectionToolbar>
      <StudioSearchInput className="user-task-search" aria-label="Search tasks" placeholder="Search tasks" value={search} onChange={event => { changeScope(); setSearch(event.target.value); }} />
      <label className="user-task-inbox">Inbox<select value={view} onChange={event => { changeScope(); setView(event.target.value); }}>
        <option value="mine">My tasks</option><option value="queue">Group queue</option><option value="history">History</option>{identity?.canManage ? <option value="all">All tasks</option> : null}</select></label>
      {filtered && <StudioButton variant="ghost" onClick={() => { changeScope(); setSearch(""); }}>Clear search</StudioButton>}
    </StudioCollectionToolbar>}
    <div className={`user-task-workspace${selection && !denied ? " user-task-workspace--selected" : ""}`}>
      <div className="user-task-results" aria-busy={loading}>
        <StudioTableContainer role="region" aria-label="Task inbox" tabIndex={0}>
          {loading && !page ? <SkeletonRows rows={3} label="Loading tasks" /> : error ?
            <StudioResultState tone="danger" title="Tasks unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : denied ?
            <StudioResultState title="Task access unavailable" description="You do not have permission to view tasks." /> : page?.items.length === 0 ?
            <StudioResultState icon={<Inbox size={32} />} title={filtered ? "No matching tasks" : cursor ? "No more tasks" : view === "queue" ? "No group tasks available" : view === "history" ? "No task history yet" : "No tasks here"}
              description={filtered ? "Try a different search or clear it to see your inbox." : "Choose another inbox or refresh when new work arrives."}
              actions={filtered ? <StudioButton onClick={() => { changeScope(); setSearch(""); }}>Clear search</StudioButton> : cursor ? <StudioButton onClick={() => setCursor(null)}>First page</StudioButton> : undefined} /> : page ?
            <table className="studio-table" aria-label="User Tasks"><thead><tr><th scope="col">Task</th><th scope="col">Status</th><th scope="col">Assignment</th></tr></thead>
              <tbody>{page.items.map(task => <tr key={task.id} data-selected={selection === task.id}>
                <td><a data-task-id={task.id} className="user-task-link" href={qualifyStudioBackendPath(`/user-tasks/${encodeURIComponent(task.id)}`)} onClick={event => { if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return; event.preventDefault(); void selectTask(task.id); }} aria-current={selection === task.id ? "true" : undefined}>{task.title}</a></td>
                <td><TaskStatus status={task.status} /></td><td>{task.claimantId ?? task.assigneeId ?? task.groupId ?? "Unassigned"}</td>
              </tr>)}</tbody></table> : null}
        </StudioTableContainer>
        {!loading && !error && !denied && page && <StudioCollectionFooter>
          <span>{page.items.length} {page.items.length === 1 ? "task" : "tasks"} shown</span>
          {(cursor || page.next) && <div role="group" aria-label="Task pages">
            {cursor && <StudioButton onClick={() => { setPage(undefined); setCursor(null); }}>First page</StudioButton>}
            <StudioButton disabled={!page.next} onClick={() => { setPage(undefined); setCursor(page.next ?? null); }}>Next page</StudioButton>
          </div>}
        </StudioCollectionFooter>}
      </div>
      {selection && identity && !denied ? <TaskDetail key={selection} api={api} id={selection} identity={identity} changed={() => void refresh()} onClose={closeDetail} navigationGuard={leaveGuard} /> : null}
    </div></div>
  </StudioPage>;
}

export function TaskDetail({ api, id, identity, changed, onClose, navigationGuard }: { api: ElsaStudioModuleApi; id: string; identity: TaskActor; changed(): void; onClose?(): void; navigationGuard?: { current: () => Promise<boolean> } }) {
  const [task, setTask] = useState<TaskItem>(); const [data, setData] = useState<Record<string, unknown>>({});
  const [validationNotice, setValidationNotice] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({}); const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false); const [missing, setMissing] = useState(false); const [stale, setStale] = useState(false);
  const [targetKind, setTargetKind] = useState("user"); const [target, setTarget] = useState("");
  const owner = useMemo(() => ({ api, id }), [api, id]);
  const currentOwner = useRef(owner); currentOwner.current = owner;
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, [task?.id]);
  const edited = useRef(false); const busyRef = useRef(false); const sequence = useRef(0);
  const attempt = useRef<{ action: string; data: Record<string, unknown>; requestId: string; revision: number } | null>(null);
  const refresh = useCallback(async () => {
    const current = ++sequence.current;
    try {
      const next = await read(api, id); if (current !== sequence.current) return; setValidationNotice(false);
      setTask(next); setMissing(false); setStale(false);
      setNotice(next.status === "CompletionPending" ? responsePendingNotice : "");
      if (!edited.current || next.decision) setData(next.decision?.response.data ?? next.prefill ?? {});
      // A newer unaccepted revision proves the previous conditional write cannot still win.
      if (next.status === "Completed" || next.status === "Cancelled" ||
          (!next.decision && attempt.current && next.revision > attempt.current.revision)) attempt.current = null;
    } catch (failure) { if (current !== sequence.current) return; setValidationNotice(false); setMissing(statusOf(failure) === 404); setNotice(message(failure)); setStale(true); }
  }, [api, id]);
  useEffect(() => { void refresh(); return () => { sequence.current++; }; }, [refresh]);
  useEffect(() => { if (task?.status !== "CompletionPending") return; const timer = setInterval(() => void refresh(), 2000); return () => clearInterval(timer); }, [task?.status, refresh]);
  const act = async (operation: string, action?: string) => {
    if (!task || busyRef.current) return;
    busyRef.current = true; setBusy(true); setNotice(""); setValidationNotice(false); setErrors({}); const operationSequence = ++sequence.current;
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
      if (currentOwner.current !== owner) return;
      if (sequence.current !== operationSequence) {
        // A retained-tab refresh can precede the accepted write. Reconcile from the server
        // without replacing a newer read with this response or reviving its confirmation.
        await refresh();
        if (currentOwner.current === owner) changed();
        return;
      }
      setTask(result); if (result.decision) { setData(result.decision.response.data); edited.current = false; }
      if (result.status !== "CompletionPending") attempt.current = null;
      if (result.status === "CompletionPending") setNotice(responsePendingNotice);
      else api.notifications?.notify({ level: "success", title: "Task updated." });
      changed();
    } catch (failure) {
      if (currentOwner.current !== owner) return;
      const status = statusOf(failure); setNotice(status === 409 ? "This task changed. Refresh to review its current state; your entered values are preserved." : message(failure));
      if (status === 409 || status === 403 || status === 404) setStale(true);
      if (status >= 400 && status < 500 && !retrying) attempt.current = null;
      const payload = typeof failure === "object" && failure !== null && "payload" in failure ? failure.payload : null;
      if (payload && typeof payload === "object" && "errors" in payload && payload.errors && typeof payload.errors === "object") { setErrors(payload.errors as Record<string, string>); setValidationNotice(true); }
    } finally { busyRef.current = false; setBusy(false); }
  };
  const canLeave = useCallback(async () => {
    if (busyRef.current || attempt.current) return false;
    if (edited.current && !task?.decision) return api.dialogs.confirm({ title: "Discard task response?", message: "Your unsent response will be discarded when you leave this task.", confirmLabel: "Discard response", cancelLabel: "Keep editing", tone: "danger" });
    return true;
  }, [api, task?.decision]);
  useEffect(() => {
    if (!navigationGuard) return;
    navigationGuard.current = canLeave;
    return () => { navigationGuard.current = async () => true; };
  }, [navigationGuard, canLeave]);
  const back = onClose ? <StudioButton variant="ghost" disabled={busy || Boolean(attempt.current)} onClick={() => { void canLeave().then(allowed => { if (allowed) onClose(); }); }}><ArrowLeft size={16} aria-hidden="true" />Back to task list</StudioButton> : null;
  if (missing) return <aside className="user-task-detail">{back}<StudioResultState title="Task not found" description="It may no longer be assigned to you or available to your group." /></aside>;
  if (!task) return <aside className="user-task-detail" aria-busy={!notice}>{back}{notice ? <p role="alert">{notice}</p> : <p>Loading task…</p>}<StudioButton onClick={() => void refresh()}>Retry</StudioButton></aside>;
  const terminal = task.status === "Completed" || task.status === "Cancelled";
  const pending = task.status === "CompletionPending";
  const own = task.assigneeId === identity.userId || task.claimantId === identity.userId;
  const canComplete = identity.canAct && own && !terminal && !pending && !stale;
  const renderers = task.form ? api.userTaskForms?.list().filter(item => item.provider === task.form?.provider) ?? [] : [];
  const Renderer = renderers.length === 1 ? renderers[0].component : null;
  return <aside aria-label={task.title} aria-busy={busy} className="user-task-detail">
    <header className="user-task-detail-header"><div><h2 ref={heading} tabIndex={-1}>{task.title}</h2><TaskStatus status={task.status} /></div>
      {back}
    </header>
    {task.description && <p className="user-task-description">{task.description}</p>}
    <StudioButton onClick={() => void refresh()} disabled={busy}><RefreshCw size={16} aria-hidden="true" />Refresh task</StudioButton>
    {notice ? <p role={stale || Object.keys(errors).length ? "alert" : "status"}>{notice}</p> : null}
    {task.form ? Renderer ? <Renderer form={task.form} value={data} onChange={next => { edited.current = true; setData(next); if (validationNotice) { setNotice(""); setValidationNotice(false); } setErrors(previous => Object.fromEntries(Object.entries(previous).filter(([key]) => Object.is(data[key], next[key])))); }} readOnly={!canComplete || busy || Boolean(attempt.current)} errors={errors} />
      : <StudioResultState tone="danger" title="Form integration unavailable" description="Install the matching form integration before completing this task." /> : null}
    <div className="user-task-actions">
      {!own && task.groupId && task.status === "Open" && identity.canAct ? <StudioButton disabled={busy || stale} onClick={() => void act("claim")}>Claim task</StudioButton> : null}
      {task.claimantId === identity.userId && task.status === "Claimed" && identity.canAct ? <StudioButton disabled={busy || stale || Boolean(attempt.current)} onClick={() => void act("release")}>Release task</StudioButton> : null}
      {own && !terminal && !pending ? task.actions.map((action, index) => <StudioButton variant={index === 0 ? "primary" : "default"} key={action} disabled={!canComplete || busy || Boolean(task.form && !Renderer) || Boolean(attempt.current && attempt.current.action !== action)} onClick={() => void act("complete", action)}>{attempt.current?.action === action ? `Retry ${action}` : action}</StudioButton>) : null}
    </div>
    {identity.canManage && !terminal && !pending ? <fieldset disabled={busy || stale || Boolean(attempt.current)}><legend>Reassign task</legend>
      <label>Assign to <select value={targetKind} onChange={event => setTargetKind(event.target.value)}><option value="user">User</option><option value="group">Group</option></select></label>
      <label>{targetKind === "user" ? "User ID" : "Group ID"}<input value={target} onChange={event => setTarget(event.target.value)} /></label>
      <StudioButton disabled={!target.trim()} onClick={() => void act("reassign")}>Reassign</StudioButton></fieldset> : null}
    <details><summary>History</summary><ol>{task.history.map((entry, index) => <li key={`${entry.at}-${index}`}>{entry.operation}{entry.action ? ` · ${entry.action}` : ""} · {entry.actorId} · <time dateTime={entry.at}>{new Date(entry.at).toLocaleString()}</time></li>)}</ol></details>
  </aside>;
}
