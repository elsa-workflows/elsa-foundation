import type { DefinitionListState } from "../workflowTypes";
import type { WorkflowFolderSelection } from "./WorkflowFolderNavigation";

export interface WorkflowDefinitionBrowseLocation {
  folderSelection: WorkflowFolderSelection;
  listState: DefinitionListState;
  search: string;
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
    search: parameters.get("search") ?? ""
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

  if (typeof location.folderSelection === "object") {
    url.searchParams.set("folderId", location.folderSelection.id);
  } else if (location.folderSelection === "unfiled") {
    url.searchParams.set("unfiled", "true");
  }

  if (location.listState !== "active") url.searchParams.set("state", location.listState);
  if (location.search) url.searchParams.set("search", location.search);

  return url;
}
