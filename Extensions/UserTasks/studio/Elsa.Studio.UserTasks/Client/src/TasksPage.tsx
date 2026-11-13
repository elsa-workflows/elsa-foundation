import { qualifyStudioBackendPath, isStudioWorkspaceTabClick, openStudioWorkspaceTab, useStudioRouteLocation } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { Inbox, RefreshCw } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { FormsPage } from "./FormsPage";
import { actor, list, message, statusOf, type TaskActor, type TaskPage } from "./taskApi";
import { TaskStatus } from "./TaskStatus";
import { taskAssignment } from "./taskPeople";
import { readTaskBrowseLocation, replaceTaskBrowseLocation, taskDetailPath } from "./taskNavigation";

export function TasksPage({ api, navigate }: { api: ElsaStudioModuleApi; navigate?(path: string): void }) {
  const location = useStudioRouteLocation();
  const { view, search, cursor, focusTask } = readTaskBrowseLocation(location.search);
  const [identity, setIdentity] = useState<TaskActor>(); const [page, setPage] = useState<TaskPage>();
  const [error, setError] = useState(""); const [loading, setLoading] = useState(true);
  const [, renderLocation] = useState(0); const focusHandled = useRef<string | null>(null);
  const sequence = useRef(0); const root = useRef<HTMLDivElement>(null);
  useEffect(() => { const update = () => renderLocation(value => value + 1); window.addEventListener("popstate", update); return () => window.removeEventListener("popstate", update); }, []);
  const browse = { view, search, cursor };
  const changeBrowse = (next: typeof browse) => { setPage(undefined); replaceTaskBrowseLocation(next); renderLocation(value => value + 1); };
  const refresh = useCallback(async () => {
    const current = ++sequence.current; setLoading(true);
    try { const who = await actor(api); if (current !== sequence.current) return; setIdentity(who);
      const next = who.canRead || who.canManage ? await list(api, view, cursor, search) : undefined;
      if (current === sequence.current) { setPage(next); setError(""); } }
    catch (failure) { if (current === sequence.current) { setPage(undefined); setError(message(failure)); if ([401, 403].includes(statusOf(failure))) { setIdentity(undefined); } } }
    finally { if (current === sequence.current) setLoading(false); }
  }, [api, view, cursor, search]);
  useEffect(() => { void refresh(); return () => { sequence.current++; }; }, [refresh]);
  useEffect(() => {
    if (!focusTask || loading || !page || error || focusHandled.current === focusTask) return;
    const frame = requestAnimationFrame(() => {
      focusHandled.current = focusTask;
      if (document.activeElement !== document.body) return;
      const link = [...(root.current?.querySelectorAll<HTMLAnchorElement>("[data-task-id]") ?? [])].find(item => item.dataset.taskId === focusTask);
      (link ?? root.current?.querySelector<HTMLInputElement>('input[type="search"]'))?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [focusTask, loading, page, error]);
  if (identity && !identity.canRead && !identity.canManage && (identity.canReadForms || identity.canManageForms)) return <FormsPage api={api} />;
  const denied = identity && !identity.canRead && !identity.canManage;
  const filtered = Boolean(search.trim());
  // The empty state offers its own Clear search; the toolbar repeats it only beside results.
  const clearInToolbar = filtered && !(page && page.items.length === 0);
  return <StudioPage className="user-tasks" aria-label="User Tasks management">
    <StudioPageHeader title="User Tasks" description="Your assigned work and available group tasks."
      actions={<><StudioButton onClick={() => void refresh()} disabled={loading}><RefreshCw size={16} aria-hidden="true" />Refresh</StudioButton>
        {identity?.canReadForms || identity?.canManageForms ? <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks/forms")}>Reusable forms</a> : null}</>} />
    <div ref={root} className="user-task-content">
    {!denied && <StudioCollectionToolbar>
      <StudioSearchInput className="user-task-search" aria-label="Search tasks" placeholder="Search tasks" value={search} onChange={event => { changeBrowse({ ...browse, search: event.target.value, cursor: null }); }} />
      <label className="user-task-inbox">Inbox<select value={view} onChange={event => { changeBrowse({ ...browse, view: event.target.value, cursor: null }); }}>
        <option value="mine">My tasks</option><option value="queue">Group queue</option><option value="history">History</option>{identity?.canManage ? <option value="all">All tasks</option> : null}</select></label>
      {clearInToolbar && <StudioButton variant="ghost" onClick={() => { changeBrowse({ ...browse, search: "", cursor: null }); }}>Clear search</StudioButton>}
    </StudioCollectionToolbar>}
      <div className="user-task-results" aria-busy={loading}>
        <StudioTableContainer role="region" aria-label="Task inbox" tabIndex={0}>
          {loading && !page ? <SkeletonRows rows={3} label="Loading tasks" /> : error ?
            <StudioResultState tone="danger" title="Tasks unavailable" description={error} actions={<><StudioButton onClick={() => void refresh()}>Retry</StudioButton>{cursor ? <StudioButton onClick={() => changeBrowse({ ...browse, cursor: null })}>First page</StudioButton> : null}</>} /> : denied ?
            <StudioResultState title="Task access unavailable" description="You do not have permission to view tasks." /> : page?.items.length === 0 ?
            <StudioResultState icon={<Inbox size={32} />} title={filtered ? "No matching tasks" : cursor ? "No more tasks" : view === "queue" ? "No group tasks available" : view === "history" ? "No task history yet" : "No tasks here"}
              description={filtered ? "Try a different search or clear it to see your inbox." : "Choose another inbox or refresh when new work arrives."}
              actions={filtered ? <StudioButton onClick={() => { changeBrowse({ ...browse, search: "", cursor: null }); }}>Clear search</StudioButton> : cursor ? <StudioButton onClick={() => changeBrowse({ ...browse, cursor: null })}>First page</StudioButton> : undefined} /> : page ?
            <table className="studio-table user-task-table" aria-label="User Tasks"><thead><tr><th scope="col">Task</th><th scope="col">Status</th><th scope="col">Assignment</th></tr></thead>
              <tbody>{page.items.map(task => <tr key={task.id}>
                <td data-label="Task"><a data-task-id={task.id} className="user-task-link" href={taskDetailPath(task.id, browse)} onClick={event => {
                  const path = taskDetailPath(task.id, browse);
                  if (isStudioWorkspaceTabClick(event) && openStudioWorkspaceTab(path)) { event.preventDefault(); return; }
                  if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                  replaceTaskBrowseLocation(browse, task.id);
                  if (navigate) { event.preventDefault(); navigate(path); }
                }}>{task.title}</a></td>
                <td data-label="Status"><TaskStatus status={task.status} /></td><td data-label="Assignment">{taskAssignment(identity, task) || "Unassigned"}</td>
              </tr>)}</tbody></table> : null}
        </StudioTableContainer>
        {!loading && !error && !denied && page && <StudioCollectionFooter>
          <span>{page.items.length} {page.items.length === 1 ? "task" : "tasks"} shown</span>
          {(cursor || page.next) && <div role="group" aria-label="Task pages">
            {cursor && <StudioButton onClick={() => { changeBrowse({ ...browse, cursor: null }); }}>First page</StudioButton>}
            <StudioButton disabled={!page.next} onClick={() => { changeBrowse({ ...browse, cursor: page.next ?? null }); }}>Next page</StudioButton>
          </div>}
        </StudioCollectionFooter>}
      </div>
    </div>
  </StudioPage>;
}
