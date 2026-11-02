import { describe, expect, it, vi } from "vitest";
import { setDialogs } from "../workflow-editor/dialogs";
import { button, click, definition, fill, input, renderWorkflowRoute, response, waitForCondition, waitForText } from "./workflowDefinitionTestHelpers";

function deletionFixture(failSecond = false) {
  const deleted = new Set<string>();
  const requests: string[] = [];
  let shouldFail = failSecond;
  const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(request));
    if (init?.method === "DELETE") {
      requests.push(url.pathname);
      const id = url.pathname.split("/").at(-1)!;
      if (id === "second" && shouldFail) return response({ title: "Permission denied" }, 403);
      deleted.add(id);
      return new Response(null, { status: 204 });
    }
    return response({ items: [definition({ id: "first", name: "First workflow" }), definition({ id: "second", name: "Second workflow" })].filter(item => !deleted.has(item.id)), nextContinuationToken: null });
  });
  return { fetchMock, requests, allowRetry: () => { shouldFail = false; } };
}

async function selectAll(container: HTMLElement) {
  await click(input(container, "Select visible workflow definitions"));
}

function dialogs(confirm: () => Promise<boolean>) {
  setDialogs({ confirm, prompt: async () => null, alert: async () => undefined });
}

describe("workflow definition bulk deletion", () => {
  it("deletes only explicit selections after one confirmation using recoverable endpoints", async () => {
    const fixture = deletionFixture();
    const { container } = await renderWorkflowRoute("/workflows/definitions", fixture.fetchMock);
    await waitForText(container, "First workflow");
    expect(button(container, "Delete selected")).toBeNull();
    await selectAll(container);
    const confirm = vi.fn(async () => true);
    dialogs(confirm);
    await click(button(container, "Delete selected"));
    await waitForText(container, "2 workflow definitions deleted.");
    expect(confirm).toHaveBeenCalledOnce();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ tone: "danger", message: expect.stringContaining("restore") }));
    expect(fixture.requests).toEqual(["/design/workflows/definitions/first", "/design/workflows/definitions/second"]);
    await waitForCondition(() => expect(container.querySelectorAll(".wf-grid-row")).toHaveLength(0));
  });

  it("sends no writes when confirmation is cancelled", async () => {
    const fixture = deletionFixture();
    const { container } = await renderWorkflowRoute("/workflows/definitions", fixture.fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    dialogs(async () => false);
    await click(button(container, "Delete selected"));
    expect(fixture.requests).toEqual([]);
    expect(container.textContent).toContain("2 selected");
    await waitForCondition(() => expect(button(container, "Delete selected")?.disabled).toBe(false));
  });

  it("keeps failed identities selected and retries only failures", async () => {
    const fixture = deletionFixture(true);
    const { container } = await renderWorkflowRoute("/workflows/definitions", fixture.fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    await click(button(container, "Delete selected"));
    await waitForText(container, "1 couldn't be deleted");
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("second:");
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Permission denied");
    await waitForText(container, "1 selected");
    fixture.allowRetry();
    await click(button(container, "Delete selected"));
    await waitForText(container, "1 workflow definition deleted.");
    expect(fixture.requests).toEqual(["/design/workflows/definitions/first", "/design/workflows/definitions/second", "/design/workflows/definitions/second"]);
  });

  it("locks selection and prevents another confirmation while one is pending", async () => {
    const fixture = deletionFixture();
    const { container } = await renderWorkflowRoute("/workflows/definitions", fixture.fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    let finish!: (value: boolean) => void;
    const confirm = vi.fn(() => new Promise<boolean>(resolve => { finish = resolve; }));
    dialogs(confirm);
    await click(button(container, "Delete selected"));
    expect(button(container, "Deleting…")?.disabled).toBe(true);
    expect(input(container, "Select visible workflow definitions")?.disabled).toBe(true);
    await click(button(container, "Deleting…"));
    expect(confirm).toHaveBeenCalledOnce();
    finish(true);
    await waitForText(container, "2 workflow definitions deleted.");
  });

  it("abandons a confirmation after switching backends", async () => {
    const fixture = deletionFixture();
    const { container, updateEndpoint } = await renderWorkflowRoute("/workflows/definitions", fixture.fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    let finish!: (value: boolean) => void;
    dialogs(() => new Promise<boolean>(resolve => { finish = resolve; }));
    await click(button(container, "Delete selected"));
    updateEndpoint("https://other.example/");
    await click(button(container, "Refresh"));
    await waitForCondition(() => expect(fixture.fetchMock.mock.calls.some(([url]) => String(url).startsWith("https://other.example/"))).toBe(true));
    finish(true);
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(fixture.requests).toEqual([]);
  });

  it("stops queued deletions and preserves a new search after an in-flight request finishes", async () => {
    const fixture = deletionFixture();
    let finish!: () => void;
    const pending = new Promise<void>(resolve => { finish = resolve; });
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "DELETE" && String(request).endsWith("/first")) await pending;
      return fixture.fetchMock(request, init);
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    await click(button(container, "Delete selected"));
    await fill(input(container, "Search all workflows"), "new search");
    await waitForCondition(() => expect(fetchMock.mock.calls.some(([request]) => new URL(String(request)).searchParams.get("searchTerm") === "new search")).toBe(true));
    finish();
    await waitForCondition(() => expect(fixture.requests.length).toBeGreaterThan(0));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(fixture.requests).toEqual(["/design/workflows/definitions/first"]);
    await waitForCondition(() => expect(container.querySelectorAll(".wf-grid-row[aria-label]").length).toBeGreaterThan(0));
    expect(input(container, "Search all workflows")?.value).toBe("new search");
  });

  for (const state of ["deleted", "all"]) it(`does not offer bulk deletion in the ${state} view`, async () => {
    const fixture = deletionFixture();
    const { container } = await renderWorkflowRoute(`/workflows/definitions?state=${state}`, fixture.fetchMock);
    await waitForText(container, "First workflow");
    await selectAll(container);
    expect(container.textContent).toContain("2 selected");
    expect(button(container, "Delete selected")).toBeNull();
  });
});
