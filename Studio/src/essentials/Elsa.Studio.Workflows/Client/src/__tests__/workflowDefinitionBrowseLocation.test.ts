import { describe, expect, it } from "vitest";
import {
  parseWorkflowDefinitionBrowseLocation,
  updateWorkflowDefinitionBrowseUrl
} from "../workflow-editor/workflowDefinitionBrowseLocation";
import { updateWorkflowDefinitionIdInUrl } from "../workflow-editor/workflowDefinitionUrl";

describe("workflow definition browse location", () => {
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
