import { getNotifications } from "../workflow-editor/notifications";
import { describe, expect, it, vi } from "vitest";
import { button, click, definition, pagedDefinitionCapabilities, renderWorkflowRoute, response, waitForCondition, waitForText } from "./workflowDefinitionTestHelpers";

const folder = { id: "demo", parentId: null, name: "Demo", revision: "folder-1", normalizedName: "DEMO", createdAt: "", lastModifiedAt: "" };
function capabilities() {
  const document = pagedDefinitionCapabilities({ folders: true, queryOptions: true });
  document.capabilities[0].links.push(
    { rel: "workflow-folder-deletion-preview", href: "custom/folders/{folderId}/preview", templated: true },
    { rel: "workflow-folder-delete", href: "custom/folders/{folderId}/remove", templated: true });
  return document;
}
function preview(revision = "preview-1", workflowCount = 2) {
  return { folder, parent: null, workflowCount, deletedWorkflowCount: 1, childFolderCount: 2, revision };
}

describe("workflow folder deletion", () => {
  it("restores focus to All workflows when a deep-linked surviving parent is not rendered", async () => {
    const root = { ...folder, id: "root", name: "Root" };
    const parent = { ...folder, id: "parent", name: "Parent", parentId: root.id };
    const selected = { ...folder, parentId: parent.id };
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/preview")) return response({ ...preview(), folder: selected, parent });
      if (url.pathname.endsWith("/remove")) return new Response(null, { status: 204 });
      if (url.pathname.endsWith("/folders/demo")) return response({ folder: selected, ancestors: [root, parent] });
      if (url.pathname.endsWith("/folders/parent")) return response({ folder: parent, ancestors: [root] });
      if (url.pathname.endsWith("/folders")) return response({ items: [root] });
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=demo", fetchMock, capabilities());
    await waitForCondition(() => expect(button(container, "Delete")).not.toBeNull());
    await click(button(container, "Delete"));
    await waitForText(container, "2 active workflows");
    await click(button(container, "Delete folder"));
    await vi.waitFor(() => expect(getNotifications().notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", title: "Deleted folder Demo" })));
    expect(container.querySelector('[data-folder-id="parent"]')).toBeNull();
    await waitForCondition(() => expect(document.activeElement).toBe(container.querySelector('[data-tree-key="all"]')));
  });

  it("preserves loaded promoted and unrelated expanded branches", async () => {
    const child = { ...folder, id: "child", name: "Child", parentId: folder.id };
    const grandchild = { ...folder, id: "grandchild", name: "Grandchild", parentId: child.id };
    const finance = { ...folder, id: "finance", name: "Finance" };
    const invoices = { ...folder, id: "invoices", name: "Invoices", parentId: finance.id };
    let removed = false;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/preview")) return response(preview());
      if (url.pathname.endsWith("/remove")) { removed = true; return new Response(null, { status: 204 }); }
      if (url.pathname.endsWith("/folders/demo")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/folders")) {
        const parentId = url.searchParams.get("parentId");
        return response({ items: parentId === "demo" ? [child] : parentId === "child" ? [grandchild] : parentId === "finance" ? [invoices] : removed ? [{ ...child, parentId: null }, finance] : [folder, finance] });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=demo", fetchMock, capabilities());
    await waitForCondition(() => expect(button(container, "Expand Demo")).not.toBeNull());
    await click(button(container, "Expand Demo"));
    await waitForText(container, "Child");
    await click(button(container, "Expand Child"));
    await waitForText(container, "Grandchild");
    await click(button(container, "Expand Finance"));
    await waitForText(container, "Invoices");
    await click(button(container, "Delete"));
    await waitForText(container, "2 active workflows");
    await click(button(container, "Delete folder"));
    await vi.waitFor(() => expect(getNotifications().notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", title: "Deleted folder Demo" })));
    expect(container.textContent).toContain("Grandchild");
    expect(container.textContent).toContain("Invoices");
    expect(button(container, "Collapse Child")).not.toBeNull();
    expect(button(container, "Collapse Finance")).not.toBeNull();
  });

  it.each([false, true])("previews all contents independently of browse filters and submits deleteWorkflows=%s", async deleteWorkflows => {
    const commits: unknown[] = [];
    const requests: URL[] = [];
    let removed = false;
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input)); requests.push(url);
      if (url.pathname.endsWith("/custom/folders/demo/preview")) return response(preview());
      if (url.pathname.endsWith("/custom/folders/demo/remove")) {
        commits.push(JSON.parse(String(init?.body))); removed = true;
        return new Response(null, { status: 204 });
      }
      if (url.pathname.endsWith("/folders/demo")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/folders")) return response({ items: removed ? [] : [folder] });
      if (url.pathname.endsWith("definition-pages")) return response({ items: [definition({ name: "Only visible result" })], totalCount: 1 });
      throw new Error(`Unexpected request ${url}`);
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=demo&search=visible&pageSize=1", fetchMock, capabilities());
    await waitForText(container, "Only visible result");
    await waitForCondition(() => expect(button(container, "Delete")).not.toBeNull());
    await click(button(container, "Delete"));
    await waitForText(container, "This folder contains 2 active workflows");
    expect(requests.find(url => url.pathname.endsWith("/preview"))?.search).toBe("");
    expect(container.textContent).toContain("They will be moved to Unfiled");
    expect(container.textContent).toContain("1 already deleted workflow");
    expect(container.textContent).toContain("2 subfolders will move to the top level");
    const checkbox = container.querySelector<HTMLInputElement>('.wf-folder-delete-choice input');
    expect(checkbox?.checked).toBe(false);
    if (deleteWorkflows) {
      await click(checkbox);
      expect(container.textContent).toContain("can be restored from the Deleted view");
    }
    await click(button(container, deleteWorkflows ? "Delete folder and 2 workflows" : "Delete folder"));
    await waitForCondition(() => expect(commits).toHaveLength(1));
    expect(commits[0]).toEqual({ previewRevision: "preview-1", deleteWorkflows, operationKey: expect.any(String) });
    await vi.waitFor(() => expect(getNotifications().notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", title: "Deleted folder Demo" })));
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    expect(new URLSearchParams(window.location.search).get("unfiled")).toBe("true");
  });

  it("requires a fresh preview and a new unchecked choice after a stale deletion", async () => {
    let previewCalls = 0; let commits = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/preview")) return response(preview(`preview-${++previewCalls}`, previewCalls === 1 ? 2 : 3));
      if (url.pathname.endsWith("/remove")) { commits++; return response({ title: "Folder contents changed" }, 409); }
      if (url.pathname.endsWith("/folders/demo")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/folders")) return response({ items: [folder] });
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=demo", fetchMock, capabilities());
    await waitForCondition(() => expect(button(container, "Delete")).not.toBeNull());
    await click(button(container, "Delete"));
    await waitForText(container, "2 active workflows");
    await click(container.querySelector('.wf-folder-delete-choice input'));
    await click(button(container, "Delete folder and 2 workflows"));
    await waitForText(container, "Reload the preview before confirming again");
    expect(commits).toBe(1);
    expect(button(container, "Delete folder")?.disabled).toBe(true);
    expect(container.querySelector('.wf-folder-delete-choice input')).toBeNull();
    await click(button(container, "Reload preview"));
    await waitForText(container, "3 active workflows");
    expect(container.querySelector<HTMLInputElement>('.wf-folder-delete-choice input')?.checked).toBe(false);
    expect(button(container, "Delete folder")?.disabled).toBe(false);
    await click(button(container, "Cancel"));
    expect(commits).toBe(1);
    await waitForCondition(() => expect(document.activeElement).toBe(button(container, "Delete")));
  });

  it("keeps deletion unavailable when preview is denied and allows cancellation", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/preview")) return response({ title: "Not permitted" }, 403);
      if (url.pathname.endsWith("/remove")) throw new Error("Deletion must remain unavailable");
      if (url.pathname.endsWith("/folders/demo")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/folders")) return response({ items: [folder] });
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=demo", fetchMock, capabilities());
    await waitForCondition(() => expect(button(container, "Delete")).not.toBeNull());
    await click(button(container, "Delete"));
    await waitForText(container, "Couldn't inspect this folder");
    expect(button(container, "Delete folder")?.disabled).toBe(true);
    await click(button(container, "Cancel"));
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });
});
