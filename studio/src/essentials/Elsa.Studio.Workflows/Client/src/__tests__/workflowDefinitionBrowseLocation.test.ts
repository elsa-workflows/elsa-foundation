import { describe, expect, it } from "vitest";
import {
  parseWorkflowDefinitionBrowseLocation,
  updateWorkflowDefinitionBrowseUrl,
  clearUnavailableWorkflowBrowseConstraints,
  unavailableWorkflowBrowseConstraints
} from "../workflow-editor/workflowDefinitionBrowseLocation";
import { updateWorkflowDefinitionIdInUrl } from "../workflow-editor/workflowDefinitionUrl";

describe("workflow definition browse location", () => {
  it("retains a supported direct folder when only extended query options are unavailable", () => {
    const location = parseWorkflowDefinitionBrowseLocation("?folderId=finance&includeDescendants=true&sort=modified&publicationState=published&columns=location&search=invoice");
    const recovered = clearUnavailableWorkflowBrowseConstraints(location, { folders: true, tags: false, favorites: false, views: false, paging: true, queryOptions: false });
    expect(recovered.folderSelection).toEqual({ id: "finance" });
    expect(recovered.includeDescendants).toBe(false);
    expect(recovered.sort).toBe("name");
    expect(recovered.publicationState).toBeUndefined();
    expect(recovered.search).toBe("invoice");
    expect(recovered.columns).toEqual(["location"]);
  });

  it("clears only unsupported predicates on a partially capable backend and retires their saved-view association", () => {
    const source = new URL("https://studio.example/workflows/definitions?backend=authoring&search=invoice&state=deleted&folderId=finance&favoritesOnly=true&view=mine&columns=tags,location&sort=modified&publicationState=published&other=keep#result");
    source.searchParams.set("tagFilters", JSON.stringify([{ tagId: "critical", operator: "exists" }]));
    const location = parseWorkflowDefinitionBrowseLocation(source.search);
    const support = { folders: true, tags: false, favorites: true, views: true, paging: true, queryOptions: true };
    expect(unavailableWorkflowBrowseConstraints(location, support)).toEqual({ folders: false, descendants: false, tags: true, favorites: false, view: false, sort: false, publication: false });
    const recovered = clearUnavailableWorkflowBrowseConstraints(location, support);
    expect(recovered).toMatchObject({ folderSelection: { id: "finance" }, favoritesOnly: true, search: "invoice", listState: "deleted", columns: ["tags", "location"], sort: "modified", publicationState: "published" });
    expect(recovered.tagFilters).toEqual([]);
    expect(recovered.viewId).toBeUndefined();
    const url = updateWorkflowDefinitionBrowseUrl(source, recovered);
    expect(url.searchParams.get("backend")).toBe("authoring");
    expect(url.searchParams.get("other")).toBe("keep");
    expect(url.hash).toBe("#result");
    expect(url.searchParams.has("tagFilters")).toBe(false);
  });

  it("retains a completely supported saved view unchanged", () => {
    const location = parseWorkflowDefinitionBrowseLocation("?view=mine&favoritesOnly=true");
    expect(clearUnavailableWorkflowBrowseConstraints(location, { folders: true, tags: true, favorites: true, views: true, paging: true, queryOptions: true })).toBe(location);
  });

  it("round-trips an opaque folder ID, lifecycle, and search without using path identity", () => {
    const location = {
      folderSelection: { id: " folder/with ?# and ünicode " } as const,
      listState: "all" as const,
      search: "invoice approvals"
    };

    const url = updateWorkflowDefinitionBrowseUrl(
      new URL("https://studio.example/workflows/definitions?definition=definition-1#details"),
      location
    );

    expect(`${url.pathname}${url.search}${url.hash}`).toBe(
      "/workflows/definitions?definition=definition-1&folderId=+folder%2Fwith+%3F%23+and+%C3%BCnicode+&state=all&search=invoice+approvals#details"
    );
    expect(parseWorkflowDefinitionBrowseLocation(url.search)).toEqual(location);
    expect(url.search).not.toContain("folderPath");
  });

  it("canonicalizes conflicting hand-edited folder selectors to All workflows", () => {
    const parsed = parseWorkflowDefinitionBrowseLocation(
      "?folderId=opaque-folder&unfiled=true&state=deleted&search=retired"
    );

    expect(parsed).toEqual({
      folderSelection: "all",
      listState: "deleted",
      search: "retired"
    });

    const canonical = updateWorkflowDefinitionBrowseUrl(
      new URL("https://studio.example/workflows/definitions?folderId=opaque-folder&unfiled=true"),
      parsed
    );
    expect(canonical.searchParams.has("folderId")).toBe(false);
    expect(canonical.searchParams.has("unfiled")).toBe(false);
  });

  it("uses clean defaults and encodes Unfiled independently from opaque folder IDs", () => {
    expect(parseWorkflowDefinitionBrowseLocation("?state=unknown&unfiled=false")).toEqual({
      folderSelection: "all",
      listState: "active",
      search: ""
    });

    const unfiled = updateWorkflowDefinitionBrowseUrl(
      new URL("https://studio.example/workflows/definitions?folderId=old&state=deleted&search=old"),
      { folderSelection: "unfiled", listState: "active", search: "" }
    );
    expect(unfiled.search).toBe("?unfiled=true");
  });

  it("adds and removes only the definition selector when entering and leaving the designer", () => {
    const source = new URL(
      "https://studio.example/workflows/definitions?folderId=folder%2Fopaque&state=all&search=hello#result"
    );

    const opened = updateWorkflowDefinitionIdInUrl(source, "definition/opaque");
    expect(`${opened.pathname}${opened.search}${opened.hash}`).toBe(
      "/workflows/definitions?folderId=folder%2Fopaque&state=all&search=hello&definition=definition%2Fopaque#result"
    );

    const closed = updateWorkflowDefinitionIdInUrl(opened, null);
    expect(`${closed.pathname}${closed.search}${closed.hash}`).toBe(
      "/workflows/definitions?folderId=folder%2Fopaque&state=all&search=hello#result"
    );
  });
});
