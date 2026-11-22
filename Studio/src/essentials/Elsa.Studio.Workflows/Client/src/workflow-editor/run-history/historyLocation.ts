import type { RunHistorySelection } from "../../runtimeHistoryTypes";

export interface RunHistoryFilters {
  activityExecutionId: string | null;
  includeDescendants: boolean;
  category: string;
  name: string;
  search: string;
  detail: "highlights" | "all";
}
export interface RunHistoryLocation { view: "timeline" | "diagram"; selection: RunHistorySelection; filters: RunHistoryFilters }
export const defaultHistoryFilters: RunHistoryFilters = { activityExecutionId: null, includeDescendants: false, category: "", name: "", search: "", detail: "highlights" };

export function readRunHistoryLocation(search: string): RunHistoryLocation {
  const params = new URLSearchParams(search);
  const event = params.get("historyEvent")?.trim(), bookmark = params.get("bookmark")?.trim();
  const activity = params.get("historyActivity")?.trim() || null;
  return { view: params.get("view") === "diagram" ? "diagram" : "timeline",
    selection: event ? { kind: "event", id: event } : bookmark ? { kind: "bookmark", id: bookmark } : null,
    filters: { activityExecutionId: activity, includeDescendants: !!activity && params.get("historyDescendants") === "1",
      category: params.get("historyCategory") ?? "", name: params.get("historyKind") ?? "", search: params.get("historySearch") ?? "",
      detail: params.get("historyDetail") === "all" ? "all" : "highlights" } };
}

export function withRunHistoryLocation(href: string, value: RunHistoryLocation) {
  const url = new URL(href);
  const set = (key: string, content: string | null) => { if (content) url.searchParams.set(key, content); else url.searchParams.delete(key); };
  set("view", value.view === "diagram" ? "diagram" : null);
  set("historyEvent", value.selection?.kind === "event" ? value.selection.id : null);
  set("bookmark", value.selection?.kind === "bookmark" ? value.selection.id : null);
  set("historyActivity", value.filters.activityExecutionId);
  set("historyDescendants", value.filters.activityExecutionId && value.filters.includeDescendants ? "1" : null);
  set("historyCategory", value.filters.category); set("historyKind", value.filters.name); set("historySearch", value.filters.search);
  set("historyDetail", value.filters.detail === "all" ? "all" : null);
  return url.toString();
}

export function writeRunHistoryLocation(value: RunHistoryLocation,
  location: Pick<Location, "href"> = window.location, history: Pick<History, "replaceState" | "state"> = window.history) {
  const href = withRunHistoryLocation(location.href, value);
  if (href !== location.href) history.replaceState(history.state, "", href);
}
