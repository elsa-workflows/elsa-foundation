import { expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

// Only called by the owned-host suite. Seed via real authoring/publication/runtime APIs;
// the task is never inserted directly into a store and no response is intercepted.
export async function userTasksJourney(page: Page, info: TestInfo, capture: Capture) {
  const base = process.env.UX_BACKEND_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned backend URL is required");
  const request: APIRequestContext = page.request;
  async function get(path: string) {
    const response = await request.get(`${base}${path}`); expect(response.ok()).toBe(true); return response.json();
  }
  async function post(path: string, data: unknown) {
    const response = await request.post(`${base}${path}`, { data }); expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const operate = page.locator(".sidebar").getByRole("navigation", { name: "Operate", exact: true });
  const operateToggle = operate.getByRole("button", { name: "Operate", exact: true });
  if (await operateToggle.getAttribute("aria-expanded") === "false") await operateToggle.click();
  await operate.getByRole("link", { name: "User Tasks", exact: true }).click();
  await expect(page).toHaveURL(/\/user-tasks(?:\?|$)/);
  expect(new URL(page.url()).searchParams.get("backend")).toBe("legacy");
  await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "User Tasks management", exact: true }).getByRole("complementary")).toHaveCount(0);
  await capture(page, info, "tasks-empty");
  const who = await get("/runtime/user-task-identity");
  const suffix = `${info.project.name}-${Date.now()}`;
  const title = `Review purchase ${suffix}`;
  const form = { id: `purchase-${suffix}`, version: "1", name: "Purchase response", provider: "elsa.basic-forms/1", fields: [
    { key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false }
  ] };
  await post("/runtime/user-task-forms", form);
  const key = "Elsa.Activities.Primitives.Activities.CreateUserTask";
  const catalog = await get(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
  const activity = catalog.items.find((item: { definition: { activityTypeKey: string } }) => item.definition.activityTypeKey === key);
  expect(activity).toBeTruthy();
  const values = { Title: title, AssigneeId: who.userId, Actions: ["Approve", "Reject"], FormId: form.id, FormVersion: "1" };
  const definition = await post("/design/workflows/definitions/submit", { name: `Task UX ${suffix}`, state: { variables: [], inputs: [], outputs: [], rootActivity: {
    nodeId: "review", activityVersionId: activity.definition.headVersionId, inputs: Object.entries(values).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })), outputs: []
  } } });
  try {
    const published = await post(`/publishing/workflows/${definition.version.id}/publish`, {});
    const run = await post(`/runtime/workflows/executables/${published.artifactId}/execute`, { sourceReferenceId: published.sourceReferenceId });
    let id = "";
    await expect.poll(async () => {
      const tasks = await get("/runtime/user-tasks?view=mine&limit=100");
      id = tasks.items.find((item: { workflowExecutionId: string; id: string }) => item.workflowExecutionId === run.workflowExecutionId)?.id ?? "";
      return id;
    }, { timeout: 30_000 }).not.toBe("");
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    const row = page.getByRole("link", { name: title, exact: true });
    await expect(row).toBeVisible();
    // #4068: the assignee reads as a person, and no column is clipped or hidden off the side at 360 px.
    const taskRow = page.getByRole("row").filter({ has: row });
    await expect(taskRow.getByRole("cell").last()).toHaveText("You");
    const fitsWidth = (region: string) => page.getByRole("region", { name: region, exact: true }).evaluate(element => element.scrollWidth <= element.clientWidth);
    expect(await fitsWidth("Task inbox")).toBe(true);
    await capture(page, info, "tasks-list");
    await page.getByRole("searchbox", { name: "Search tasks", exact: true }).fill("Review purchase");
    // #4068: the search shell draws the only focus ring; the inner input adds none.
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCSS("outline-style", "none");
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCSS("box-shadow", "none");
    await expect(row).toBeVisible();
    await row.press("Enter");
    await expect(page).toHaveURL(new RegExp(`/user-tasks/${id}(?:\\?|$)`));
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCount(0);
    await expect(page.getByRole("region", { name: "Task inbox", exact: true })).toHaveCount(0);
    const detail = page.getByRole("region", { name: title, exact: true });
    const note = detail.getByRole("textbox", { name: /^Note/ });
    await note.fill("Reviewed purchase request");
    expect(new URL(page.url()).searchParams.get("backend")).toBe("legacy");
    const retainedNote = await note.elementHandle();
    await page.getByRole("button", { name: "New tab", exact: true }).click();
    const accessRefresh = page.waitForResponse(response => new URL(response.url()).pathname.endsWith("/runtime/user-task-identity"));
    await page.getByRole("tablist", { name: "Open screens", exact: true }).getByRole("tab", { name: new RegExp(title) }).click();
    await (await accessRefresh).finished();
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    expect(await retainedNote!.evaluate(element => element.isConnected)).toBe(true);
    await expect(note).toHaveValue("Reviewed purchase request");
    await capture(page, info, "task-tab-return");
    await detail.getByRole("button", { name: "Back to task list", exact: true }).click();
    await page.getByRole("dialog", { name: "Discard task response?", exact: true }).getByRole("button", { name: "Keep editing", exact: true }).click();
    await expect(note).toHaveValue("Reviewed purchase request");
    await detail.getByRole("heading").scrollIntoViewIfNeeded(); await capture(page, info, "task-detail");
    await detail.getByRole("button", { name: "Approve", exact: true }).scrollIntoViewIfNeeded(); await capture(page, info, "task-actions");
    await detail.getByRole("button", { name: "Approve", exact: true }).press("Enter");
    await expect.poll(async () => (await get(`/runtime/user-tasks/${id}`)).status, { timeout: 30_000 }).toBe("Completed");
    await expect.poll(async () => (await get(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const completed = await get(`/runtime/user-tasks/${id}`);
    expect(completed.history.filter((entry: { operation: string }) => entry.operation === "accepted")).toHaveLength(1);
    expect(completed.history.filter((entry: { operation: string }) => entry.operation === "completed")).toHaveLength(1);
    expect(completed.decision.response.data).toEqual({ Note: "Reviewed purchase request" });
    await detail.getByRole("button", { name: "Refresh task", exact: true }).click();
    await expect(detail.getByText("Completed", { exact: true })).toBeVisible();
    await detail.getByText("History", { exact: true }).click();
    // #4068: history names the person, never the raw user ID.
    await expect(detail.locator("details li").last()).toContainText("completed · Approve · You");
    await expect(detail.locator("details")).not.toContainText(who.userId);
    await detail.locator("details li").last().scrollIntoViewIfNeeded(); await capture(page, info, "task-history");
    // #4068: a completed response reads as a record, with no required marker, resize handle or empty action row.
    const record = detail.getByRole("textbox", { name: "Note", exact: true });
    await expect(record).toBeDisabled(); await expect(record).toHaveCSS("resize", "none");
    await expect(detail.locator(".user-task-actions")).toHaveCount(0);
    await detail.getByRole("heading").scrollIntoViewIfNeeded(); await capture(page, info, "completed");
    await expect(detail.getByRole("button", { name: "Back to task list", exact: true })).toBeEnabled();
    await detail.getByRole("button", { name: "Back to task list", exact: true }).click();
    await expect(page.getByRole("region", { name: title, exact: true })).toHaveCount(0);
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveValue("Review purchase");
    await expect(row).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "No matching tasks", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Clear search", exact: true })).toHaveCount(1);
    expect(new URL(page.url()).searchParams.get("backend")).toBe("legacy");
    await capture(page, info, "tasks-return");
    await page.goto(`/user-tasks/absent-${suffix}?backend=legacy`);
    await expect(page.getByRole("heading", { name: "Task not found", exact: true })).toBeVisible();
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCount(0);
    // #4068: the not-found body offers its own way back, beside the header link.
    await expect(page.getByRole("button", { name: "Back to task list", exact: true })).toHaveCount(2);
    await capture(page, info, "task-not-found");
    await page.getByRole("button", { name: "Back to task list", exact: true }).last().click();
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toBeVisible();
    await page.goto("/user-tasks/forms");
    await expect(page.getByRole("table", { name: "Reusable task forms", exact: true }).getByText(form.id, { exact: true })).toBeVisible();
    const nextVersion = page.getByRole("button", { name: "Create next version", exact: true }).first();
    expect((await nextVersion.boundingBox())!.width).toBeGreaterThan(120);
    expect((await nextVersion.boundingBox())!.height).toBeLessThan(48);
    // #4068: every column fits at 360 px, each action sits beside its form's name, and only a form's latest version offers a next one.
    expect(await fitsWidth("Reusable form versions")).toBe(true);
    const formRows = await page.getByRole("table", { name: "Reusable task forms", exact: true }).locator("tbody tr").evaluateAll(rows => rows.map(row => ({
      id: row.querySelector("td[data-label='Identifier']")?.textContent ?? "",
      version: Number(row.querySelector("td[data-label='Version']")?.textContent ?? "0"),
      offersNext: [...row.querySelectorAll("button")].some(button => button.textContent === "Create next version")
    })));
    const superseded = formRows.filter(item => item.version < Math.max(...formRows.filter(other => other.id === item.id).map(other => other.version)));
    expect(superseded.filter(item => item.offersNext)).toEqual([]);
    const actionRow = page.getByRole("row").filter({ has: nextVersion }).first();
    await nextVersion.scrollIntoViewIfNeeded();
    const [nameBox, actionBox] = [await actionRow.getByRole("button").first().boundingBox(), await nextVersion.boundingBox()];
    expect(nameBox!.x).toBeGreaterThanOrEqual(0); expect(actionBox!.x + actionBox!.width).toBeLessThanOrEqual(page.viewportSize()!.width);
    await capture(page, info, "task-forms");
    await nextVersion.scrollIntoViewIfNeeded(); await capture(page, info, "task-form-versions");
    await page.getByRole("button", { name: "Create form version", exact: true }).first().press("Enter");
    await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeFocused();
    await page.getByRole("button", { name: "Add field", exact: true }).click();
    // #4068: the automatically assigned version is shown as a value, not as an editable-looking box.
    await expect(page.getByRole("textbox", { name: /^Version/ })).toHaveCount(0);
    await page.getByRole("textbox", { name: "Form ID", exact: true }).scrollIntoViewIfNeeded(); await capture(page, info, "task-form-editor");
    await page.getByRole("button", { name: "Cancel", exact: true }).scrollIntoViewIfNeeded(); await capture(page, info, "task-form-actions");
    await page.getByRole("button", { name: "Cancel", exact: true }).press("Enter");
    await expect(page.getByRole("button", { name: "Create form version", exact: true }).first()).toBeFocused();
  } finally {
    // This definition belongs to this test's disposable host; keep the reference
    // collection empty for the next project without affecting any developer store.
    const deleted = await request.delete(`${base}/design/workflows/definitions/${definition.definition.id}`);
    expect(deleted.ok()).toBe(true);
  }
}
