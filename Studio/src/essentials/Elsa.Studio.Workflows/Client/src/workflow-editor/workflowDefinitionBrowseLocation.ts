import type { DefinitionListState } from "../workflowTypes";
import type { WorkflowFolderSelection } from "./WorkflowFolderNavigation";
import type { WorkflowLibraryQuery, WorkflowTagFilter } from "../api/workflowOrganization";

export interface WorkflowDefinitionBrowseLocation {
  folderSelection: WorkflowFolderSelection;
  listState: DefinitionListState;
  search: string;
  tagFilters?: WorkflowTagFilter[];
  includeDescendants?: boolean;
  favoritesOnly?: boolean;
  sort?: WorkflowLibraryQuery["sort"];
  viewId?: string;
  columns?: string[];
  invalidTagFilters?: string;
  publicationState?: string;
}

export function workflowLibraryFilterKey(location: WorkflowDefinitionBrowseLocation) {
  return JSON.stringify({ tags: location.tagFilters ?? [], invalid: location.invalidTagFilters ?? null,
    descendants: location.includeDescendants === true, favorites: location.favoritesOnly === true, sort: location.sort ?? "name", publication: location.publicationState ?? null });
}

function parseTagFilters(value: string | null): Pick<WorkflowDefinitionBrowseLocation, "tagFilters" | "invalidTagFilters"> {
  if (!value) return {};
  try {
    const filters: unknown = JSON.parse(value);
    if (!Array.isArray(filters) || filters.length > 16 || filters.some(filter => !filter || typeof filter !== "object"
      || typeof filter.tagId !== "string" || !filter.tagId
      || !["exists", "missing", "any", "all", "none"].includes(filter.operator)
      || (filter.valueIds !== undefined && (!Array.isArray(filter.valueIds) || filter.valueIds.length > 32 || filter.valueIds.some((id: unknown) => typeof id !== "string"))))) {
      return { invalidTagFilters: value };
    }
    return filters.length ? { tagFilters: filters as WorkflowTagFilter[] } : {};
  } catch { return { invalidTagFilters: value }; }
}

export function workflowBrowseQuery(location: WorkflowDefinitionBrowseLocation): WorkflowLibraryQuery {
  return { searchTerm: location.search, state: location.listState,
    folderId: typeof location.folderSelection === "object" ? location.folderSelection.id : null,
    unfiled: location.folderSelection === "unfiled", includeDescendants: location.includeDescendants ?? false,
    favoritesOnly: location.favoritesOnly ?? false, tagFilters: location.tagFilters ?? [], sort: location.sort ?? "name", publicationState: location.publicationState };
}

export function workflowBrowseLocationFromQuery(query: WorkflowLibraryQuery, viewId?: string, columns?: string[]): WorkflowDefinitionBrowseLocation {
  return { folderSelection: query.folderId ? { id: query.folderId } : query.unfiled ? "unfiled" : "all",
    listState: query.state ?? "active", search: query.searchTerm ?? "", tagFilters: query.tagFilters,
    includeDescendants: query.includeDescendants, favoritesOnly: query.favoritesOnly, sort: query.sort,
    viewId, columns, publicationState: query.publicationState ?? undefined };
}

export function workflowFolderSelectionKey(selection: WorkflowFolderSelection) {
  return typeof selection === "object" ? `folder:${selection.id}` : selection;
}

export function parseWorkflowDefinitionBrowseLocation(search: string): WorkflowDefinitionBrowseLocation {
  const parameters = new URLSearchParams(search);
  const folderId = parameters.get("folderId") ?? "";
  const unfiled = parameters.get("unfiled") === "true";
  const requestedState = parameters.get("state");

  return {
    folderSelection: folderId && unfiled
      ? "all"
      : folderId
        ? { id: folderId }
        : unfiled
          ? "unfiled"
          : "all",
    listState: requestedState === "deleted" || requestedState === "all" ? requestedState : "active",
    search: parameters.get("search") ?? "",
    ...parseTagFilters(parameters.get("tagFilters")),
    ...(parameters.get("includeDescendants") === "true" ? { includeDescendants: true } : {}),
    ...(parameters.get("favoritesOnly") === "true" ? { favoritesOnly: true } : {}),
    ...(["name-desc", "modified"].includes(parameters.get("sort") ?? "") ? { sort: parameters.get("sort") as WorkflowLibraryQuery["sort"] } : {}),
    ...(parameters.get("view") ? { viewId: parameters.get("view")! } : {}),
    ...(parameters.get("publicationState") ? { publicationState: parameters.get("publicationState")! } : {}),
    ...(parameters.get("columns") ? { columns: parameters.get("columns")!.split(",").filter(column => ["tags", "location"].includes(column)) } : {})
  };
}

export function updateWorkflowDefinitionBrowseUrl(
  source: URL,
  location: WorkflowDefinitionBrowseLocation
) {
  const url = new URL(source.toString());
  url.searchParams.delete("folderId");
  url.searchParams.delete("unfiled");
  url.searchParams.delete("state");
  url.searchParams.delete("search");
  for (const key of ["tagFilters", "includeDescendants", "favoritesOnly", "sort", "view", "columns", "publicationState"]) url.searchParams.delete(key);

  if (typeof location.folderSelection === "object") {
    url.searchParams.set("folderId", location.folderSelection.id);
  } else if (location.folderSelection === "unfiled") {
    url.searchParams.set("unfiled", "true");
  }

  if (location.listState !== "active") url.searchParams.set("state", location.listState);
  if (location.search) url.searchParams.set("search", location.search);
  if (location.invalidTagFilters) url.searchParams.set("tagFilters", location.invalidTagFilters);
  else if (location.tagFilters?.length) url.searchParams.set("tagFilters", JSON.stringify(location.tagFilters));
  if (location.includeDescendants) url.searchParams.set("includeDescendants", "true");
  if (location.favoritesOnly) url.searchParams.set("favoritesOnly", "true");
  if (location.sort && location.sort !== "name") url.searchParams.set("sort", location.sort);
  if (location.viewId) url.searchParams.set("view", location.viewId);
  if (location.publicationState) url.searchParams.set("publicationState", location.publicationState);
  if (location.columns?.length) url.searchParams.set("columns", location.columns.filter(column => ["tags", "location"].includes(column)).join(","));

  return url;
}
