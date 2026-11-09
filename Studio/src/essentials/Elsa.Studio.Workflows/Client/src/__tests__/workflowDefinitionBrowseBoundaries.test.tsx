import { describe, expect, it, onTestFinished, vi } from "vitest";
import {
  button,
  click,
  definition,
  pagedDefinitionCapabilities,
  renderWorkflowRoute,
  response,
  row,
  select,
  waitForCondition,
  waitForText
} from "./workflowDefinitionTestHelpers";

const browseTransitions = [
  { name: "folder change", key: "folder" },
  { name: "lifecycle change", key: "lifecycle" },
  { name: "explicit page change", key: "page" },
  { name: "page-size change", key: "pageSize" }
] as const;

describe("workflow definition browse boundaries", () => {
  it.each(browseTransitions)("clears old rows while a deferred $name request is pending", async ({ key }) => {
    const folder = { id: "folder-target", parentId: null, name: "Target folder", normalizedName: "target-folder", createdAt: "", lastModifiedAt: "" };
    const requests: URL[] = [];
    let resolveTransition: ((value: Response) => void) | undefined;
    onTestFinished(() => resolveTransition?.(response({ items: [], nextContinuationToken: null })));
    const isTransitionRequest = (url: URL) => {
      switch (key) {
        case "folder": return url.searchParams.get("folderId") === "folder-target";
        case "lifecycle": return url.searchParams.get("state") === "deleted";
        case "page": return url.searchParams.get("continuationToken") === "boundary-page-two";
        case "pageSize": return url.searchParams.get("pageSize") === "25";
      }
    };

    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/folders")) return response({ items: [folder], nextContinuationToken: null });
      if (url.pathname.endsWith("/folders/folder-target")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/definition-pages")) {
        requests.push(url);
        if (isTransitionRequest(url)) return new Promise<Response>(resolve => { resolveTransition = resolve; });
        return response({ items: [definition({ name: "Previous browse workflow" })], nextContinuationToken: "boundary-page-two" });
      }
      return response({ items: [] });
    });

    const { container } = await renderWorkflowRoute(
      "/workflows/definitions",
      fetchMock,
      pagedDefinitionCapabilities({ folders: key === "folder" })
    );
    await waitForText(container, "Previous browse workflow");

    if (key === "folder") {
      await waitForCondition(() => expect(container.querySelector("[data-folder-id='folder-target']")).not.toBeNull());
      await click(container.querySelector("[data-folder-id='folder-target']"));
    } else if (key === "lifecycle") {
      await click(button(container, "Deleted"));
    } else if (key === "page") {
      await click(button(container, "Next"));
    } else {
      await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "25");
    }

    await waitForCondition(() => expect(requests.some(isTransitionRequest)).toBe(true));
    expect(row(container, "Previous browse workflow")).toBeNull();

    resolveTransition?.(response({ items: [definition({ name: "Updated browse workflow" })], nextContinuationToken: null }));
    await waitForText(container, "Updated browse workflow");
  });
});
