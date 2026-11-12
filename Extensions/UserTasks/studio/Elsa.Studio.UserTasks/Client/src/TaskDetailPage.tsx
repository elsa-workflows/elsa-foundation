import { notifyStudioNavigationGuardReleased, studioNavigationRequestedEvent, useStudioRouteLocation, useStudioWorkspaceTitle, type ElsaStudioModuleApi, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, RefreshCw } from "lucide-react";
import { StudioAlert, StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import { actor, read, mutate, message, statusOf, type TaskActor, type TaskItem } from "./taskApi";
import { TaskStatus } from "./TaskStatus";
import { useTaskPeople } from "./taskPeople";
import { readTaskBrowseLocation, taskCollectionPath } from "./taskNavigation";

const responsePendingNotice = "Your response is recorded. The workflow will continue when delivery completes. You can leave this page.";

export function TaskDetailPage({ api, id, navigate }: { api: ElsaStudioModuleApi; id: string; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  const backPath = taskCollectionPath(readTaskBrowseLocation(location.search), id);
  const scope = api.backendSession ?? api.backend;
  const [authorization, setAuthorization] = useState<{ scope: typeof scope; identity?: TaskActor; error?: string }>();
  const identity = authorization?.scope === scope ? authorization.identity : undefined;
  const error = authorization?.scope === scope ? authorization.error ?? "" : "";
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let current = true;
    // Activity reconnects effects on tab return. Keep the authorized child and its response while revalidating
    // this immutable connection; a new connection or actor must not inherit another scope's local response.
    void actor(api).then(who => { if (current) setAuthorization({ scope, identity: who }); }, failure => {
      if (current) setAuthorization(previous => ({ scope, identity: ![401, 403].includes(statusOf(failure)) && previous?.scope === scope ? previous.identity : undefined, error: message(failure) }));
    });
    return () => { current = false; };
  }, [api, scope, revision]);
  if (identity && (identity.canRead || identity.canManage)) return <TaskDetail key={JSON.stringify([id, identity.userId])} api={api} id={id} identity={identity} onClose={() => navigate(backPath)}
    accessNotice={error ? <StudioAlert tone="danger">Could not refresh task access: {error}<StudioButton onClick={() => setRevision(value => value + 1)}>Retry access</StudioButton></StudioAlert> : undefined} />;
  return <StudioPage className="user-tasks" aria-label="User task">
    <StudioPageHeader title="User task" actions={<StudioButton variant="ghost" onClick={() => navigate(backPath)}><ArrowLeft size={16} aria-hidden="true" />Back to task list</StudioButton>} />
    {identity ? <StudioResultState title="Task access unavailable" description="You do not have permission to view this task." /> : error ? <StudioResultState tone="danger" title="Task unavailable" description={error} actions={<StudioButton onClick={() => setRevision(value => value + 1)}>Retry</StudioButton>} /> : <p role="status">Loading task…</p>}
  </StudioPage>;
}

export function TaskDetail({ api, id, identity, onClose, accessNotice }: { api: ElsaStudioModuleApi; id: string; identity: TaskActor; onClose?(): void; accessNotice?: React.ReactNode }) {
  const [task, setTask] = useState<TaskItem>(); const [data, setData] = useState<Record<string, unknown>>({});
  const [validationNotice, setValidationNotice] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({}); const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false); const [missing, setMissing] = useState(false); const [stale, setStale] = useState(false);
  const [targetKind, setTargetKind] = useState("user"); const [target, setTarget] = useState("");
  const owner = useMemo(() => ({ api, id }), [api, id]);
  const currentOwner = useRef(owner); currentOwner.current = owner;
  const content = useRef<HTMLDivElement>(null); const focused = useRef<string | null>(null);
  useStudioWorkspaceTitle(task?.title, "User task");
  const person = useTaskPeople(api, identity);
  useEffect(() => {
    if (!task?.id || focused.current === task.id) return;
    const heading = content.current?.querySelector("h1");
    if (heading) { heading.tabIndex = -1; heading.focus(); focused.current = task.id; }
  }, [task?.id]);
  const lifetime = useRef(0);
  useEffect(() => () => { lifetime.current++; }, []);
  const edited = useRef(false); const busyRef = useRef(false); const sequence = useRef(0);
  const attempt = useRef<{ action: string; data: Record<string, unknown>; requestId: string; revision: number } | null>(null);
  const refresh = useCallback(async () => {
    const current = ++sequence.current;
    try {
      const next = await read(api, id); if (current !== sequence.current) return; setValidationNotice(false);
      setTask(next); setMissing(false); setStale(false);
      setNotice(next.status === "CompletionPending" ? responsePendingNotice : "");
      if (next.decision) edited.current = false;
      if (!edited.current || next.decision) setData(next.decision?.response.data ?? next.prefill ?? {});
      // A newer unaccepted revision proves the previous conditional write cannot still win.
      if (next.decision || next.status === "Completed" || next.status === "Cancelled" ||
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
      if (sequence.current !== operationSequence) { await refresh(); return; }
      setTask(result); if (result.decision) { setData(result.decision.response.data); edited.current = false; }
      // A successful write confirms this request. Runtime delivery may remain pending independently.
      attempt.current = null;
      if (operation === "complete") edited.current = false;
      if (result.status === "CompletionPending") setNotice(responsePendingNotice);
      else api.notifications?.notify({ level: "success", title: "Task updated." });
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
    const origin = lifetime.current;
    if (busyRef.current || attempt.current) return false;
    if (edited.current && !task?.decision) {
      const discard = await api.dialogs.confirm({ title: "Discard task response?", message: "Your unsent response will be discarded when you leave this task.", confirmLabel: "Discard response", cancelLabel: "Keep editing", tone: "danger" });
      if (!discard || busyRef.current || attempt.current || lifetime.current !== origin) return false;
      edited.current = false;
      notifyStudioNavigationGuardReleased();
    }
    return lifetime.current === origin;
  }, [api, task?.decision]);
  const guarded = busy || Boolean(attempt.current) || (edited.current && !task?.decision);
  useEffect(() => {
    const preventLeaving = (event: Event) => {
      if (!busyRef.current && !attempt.current && (!edited.current || task?.decision)) return;
      event.preventDefault();
      if (event.type === studioNavigationRequestedEvent && (event as CustomEvent<StudioNavigationRequest>).detail.intent !== "probe") {
        setNotice(busyRef.current || attempt.current ? "Wait for the task update, or refresh to confirm its result, before leaving." : "You have an unsent response. Finish it or use Back to task list to discard it.");
      }
    };
    window.addEventListener(studioNavigationRequestedEvent, preventLeaving);
    window.addEventListener("beforeunload", preventLeaving);
    return () => { window.removeEventListener(studioNavigationRequestedEvent, preventLeaving); window.removeEventListener("beforeunload", preventLeaving); };
  }, [task?.decision]);
  useEffect(() => { if (!guarded) notifyStudioNavigationGuardReleased(); }, [guarded]);
  const frame = (children: React.ReactNode) => <StudioPage className="user-tasks" aria-label={task?.title ?? "Task details"} aria-busy={busy}><div ref={content} className="user-task-detail">{children}</div></StudioPage>;
  const leave = () => { void canLeave().then(allowed => { if (allowed) onClose?.(); }); };
  const back = onClose ? <StudioButton variant="ghost" disabled={busy || Boolean(attempt.current)} onClick={() => leave()}><ArrowLeft size={16} aria-hidden="true" />Back to task list</StudioButton> : null;
  if (missing) return frame(<><StudioPageHeader title="User task" actions={back} /><StudioResultState title="Task not found" description="It may no longer be assigned to you or available to your group."
    actions={onClose ? <StudioButton variant="primary" onClick={leave}>Back to task list</StudioButton> : undefined} /></>);
  if (!task) return frame(<><StudioPageHeader title="User task" actions={back} />{notice ? <p role="alert">{notice}</p> : <p role="status">Loading task…</p>}<StudioButton onClick={() => void refresh()}>Retry</StudioButton></>);
  const terminal = task.status === "Completed" || task.status === "Cancelled";
  const pending = task.status === "CompletionPending";
  const own = task.assigneeId === identity.userId || task.claimantId === identity.userId;
  const canComplete = identity.canAct && own && !terminal && !pending && !stale;
  const renderers = task.form ? api.userTaskForms?.list().filter(item => item.provider === task.form?.provider) ?? [] : [];
  const Renderer = renderers.length === 1 ? renderers[0].component : null;
  const claimable = !own && Boolean(task.groupId) && task.status === "Open" && identity.canAct;
  const releasable = task.claimantId === identity.userId && task.status === "Claimed" && identity.canAct;
  const respondable = own && !terminal && !pending && task.actions.length > 0;
  return frame(<>
    <StudioPageHeader title={task.title} description={task.description ? <span className="user-task-description">{task.description}</span> : undefined} actions={<>{back}<StudioButton onClick={() => void refresh()} disabled={busy}><RefreshCw size={16} aria-hidden="true" />Refresh task</StudioButton></>}><TaskStatus status={task.status} /></StudioPageHeader>
    {accessNotice}
    {notice ? <p role={stale || Object.keys(errors).length ? "alert" : "status"}>{notice}</p> : null}
    {task.form ? Renderer ? <div className="user-task-response" data-final={terminal || undefined}><Renderer form={task.form} value={data} onChange={next => { edited.current = true; setData(next); if (validationNotice) { setNotice(""); setValidationNotice(false); } setErrors(previous => Object.fromEntries(Object.entries(previous).filter(([key]) => Object.is(data[key], next[key])))); }} readOnly={!canComplete || busy || Boolean(attempt.current)} errors={errors} /></div>
      : <StudioResultState tone="danger" title="Form integration unavailable" description="Install the matching form integration before completing this task." /> : null}
    {(claimable || releasable || respondable) && <div className="user-task-actions">
      {claimable ? <StudioButton disabled={busy || stale} onClick={() => void act("claim")}>Claim task</StudioButton> : null}
      {releasable ? <StudioButton disabled={busy || stale || Boolean(attempt.current)} onClick={() => void act("release")}>Release task</StudioButton> : null}
      {respondable ? task.actions.map((action, index) => <StudioButton variant={index === 0 ? "primary" : "default"} key={action} disabled={!canComplete || busy || Boolean(task.form && !Renderer) || Boolean(attempt.current && attempt.current.action !== action)} onClick={() => void act("complete", action)}>{attempt.current?.action === action ? `Retry ${action}` : action}</StudioButton>) : null}
    </div>}
    {identity.canManage && !terminal && !pending ? <fieldset disabled={busy || stale || Boolean(attempt.current)}><legend>Reassign task</legend>
      <label>Assign to <select value={targetKind} onChange={event => setTargetKind(event.target.value)}><option value="user">User</option><option value="group">Group</option></select></label>
      <label>{targetKind === "user" ? "User ID" : "Group ID"}<input value={target} onChange={event => setTarget(event.target.value)} /></label>
      <StudioButton disabled={!target.trim()} onClick={() => void act("reassign")}>Reassign</StudioButton></fieldset> : null}
    <details><summary>History</summary><ol>{task.history.map((entry, index) => <li key={`${entry.at}-${index}`}>{entry.operation}{entry.action ? ` · ${entry.action}` : ""} · {person(entry.actorId)} · <time dateTime={entry.at}>{new Date(entry.at).toLocaleString()}</time></li>)}</ol></details>
  </>);
}
