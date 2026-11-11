import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";

export interface TaskBrowseLocation { view: string; search: string; cursor: string | null; focusTask?: string | null }
export function readTaskBrowseLocation(search: string): TaskBrowseLocation {
  const query = new URLSearchParams(search);
  const view = query.get("view") ?? "mine";
  return { view: ["mine", "queue", "history", "all"].includes(view) ? view : "mine", search: query.get("search") ?? "", cursor: query.get("after") || null, focusTask: query.get("focusTask") };
}
function browseQuery(browse: TaskBrowseLocation) {
  const query = new URLSearchParams();
  if (browse.view !== "mine") query.set("view", browse.view);
  if (browse.search) query.set("search", browse.search);
  if (browse.cursor) query.set("after", browse.cursor);
  return query;
}
export function taskCollectionPath(browse: TaskBrowseLocation, focusTask?: string) {
  const query = browseQuery(browse);
  if (focusTask) query.set("focusTask", focusTask);
  return qualifyStudioBackendPath("/user-tasks" + (query.size ? `?${query}` : ""));
}
export function taskDetailPath(id: string, browse: TaskBrowseLocation) {
  const query = browseQuery(browse);
  return qualifyStudioBackendPath(`/user-tasks/${encodeURIComponent(id)}` + (query.size ? `?${query}` : ""));
}
/** Keep filters in their history entry so browser Back and an explicit return restore the same collection. */
export function replaceTaskBrowseLocation(browse: TaskBrowseLocation, focusTask?: string) {
  window.history.replaceState(window.history.state, "", taskCollectionPath(browse, focusTask));
}
