import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import { signIn, expectAccessible } from "./support/journey";

// The caller owns both hosts and creates an open task with e2e-tests/user-tasks/Test-UserTasks.ps1 -LeaveOpen.
// This uses the normal Studio module, real authentication and real backend APIs; no route interception.
const receiptPath = process.env.ELSA_USER_TASK_RECEIPT;
test.skip(!process.env.ELSA_STUDIO_URL || !receiptPath, "Set ELSA_STUDIO_URL and ELSA_USER_TASK_RECEIPT for owned User Task hosts.");
test.setTimeout(120_000);
type Receipt = { taskId: string; title: string; executionId: string; definitionId: string; baseUrl: string; artifactId: string; sourceReferenceId: string };

for (const width of [1440, 390]) for (const theme of ["light", "dark"]) test(`real Studio inbox validates and completes the selected action once (${width}px ${theme})`, async ({ page }, testInfo) => {
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, theme);
  const receipt = JSON.parse(readFileSync(receiptPath!, "utf8")) as Receipt;
  await page.setViewportSize({ width, height: 1000 }); await signIn(page);
  let taskId = receipt.taskId; let executionId = receipt.executionId;
  if (width === 390 || theme === "dark") {
    const start = await page.request.post(`${receipt.baseUrl}/runtime/workflows/executables/${receipt.artifactId}/execute`, { data: { sourceReferenceId: receipt.sourceReferenceId } });
    expect(start.ok()).toBe(true); executionId = (await start.json()).workflowExecutionId;
    await expect.poll(async () => {
      const response = await page.request.get(`${receipt.baseUrl}/runtime/user-tasks?view=mine&limit=100`);
      const task = (await response.json()).items.find((item: { workflowExecutionId: string; id: string }) => item.workflowExecutionId === executionId);
      if (task) taskId = task.id; return Boolean(task);
    }, { timeout: 30_000 }).toBe(true);
  }
  await page.goto("/user-tasks?backend=legacy");
  const row = page.locator(`a[href*="/user-tasks/${taskId}"]`); await expect(row).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);
  const search = page.getByRole("searchbox", { name: "Search tasks", exact: true });
  await search.fill("no matching task for this acceptance run");
  await expect(page.getByRole("heading", { name: "No matching tasks", exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("no-matches.png") });
  await page.getByRole("button", { name: "Clear search", exact: true }).last().click();
  await expect(row).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("task-list.png") });
  await row.focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: receipt.title, exact: true })).toBeVisible();
  await expect.poll(() => page.getByRole("heading", { name: receipt.title, exact: true })
    .evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  const account = page.getByRole("textbox", { name: /^Account/ }); await expect(account).toHaveValue("A-107"); await expect(account).toBeDisabled();
  await page.getByRole("button", { name: "Approve", exact: true }).press("Enter");
  await expect(page.getByText("This field is required.", { exact: true }).first()).toBeVisible();
  await page.getByLabel(/^Amount/).fill("42"); await page.getByLabel(/^Confirmed/).selectOption("false"); await page.getByLabel(/^Note/).fill("Studio case preserved");
  await expectAccessible(page, ".user-tasks");
  const detail = page.getByRole("complementary", { name: receipt.title, exact: true });
  await detail.getByRole("heading").scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("task-detail-top.png") });
  await detail.getByRole("button", { name: "Approve", exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("task-response.png") });
  const sent = page.waitForRequest(request => request.method() === "POST" && request.url().endsWith(`/runtime/user-tasks/${taskId}/complete`));
  await page.getByRole("button", { name: "Approve", exact: true }).press("Enter");
  const request = (await sent).postDataJSON(); expect(request.data).toEqual({ AccountID: "A-107", AmountUSD: 42, Confirmed: false, Note: "Studio case preserved" });
  await expect.poll(async () => (await (await page.request.get(`${receipt.baseUrl}/runtime/user-tasks/${taskId}`)).json()).status, { timeout: 30_000 }).toBe("Completed");
  const duplicate = await page.request.post(`${receipt.baseUrl}/runtime/user-tasks/${taskId}/complete`, { data: request }); expect(duplicate.ok()).toBe(true);
  const task = await duplicate.json(); expect(task.history.filter((entry: { operation: string }) => entry.operation === "accepted")).toHaveLength(1);
  expect(task.history.filter((entry: { operation: string }) => entry.operation === "completed")).toHaveLength(1);
  await expect.poll(async () => (await (await page.request.get(`${receipt.baseUrl}/runtime/workflows/instances/${executionId}`)).json()).instance.status, { timeout: 30_000 }).toBe("Completed");
  const run = await (await page.request.get(`${receipt.baseUrl}/runtime/workflows/instances/${executionId}`)).json();
  expect(run.instance.status).toBe("Completed");
  expect(run.activities.filter((item: { executableNodeId: string; status: string }) => item.executableNodeId.startsWith("branch-") && item.status === "Completed").map((item: { executableNodeId: string }) => item.executableNodeId)).toEqual(["branch-0"]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.getByRole("button", { name: "Refresh task", exact: true }).press("Enter");
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
  await expect(page.getByText("Your response is recorded. The workflow will continue when delivery completes.", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  await expect(row).toHaveCount(0);
  await page.getByRole("complementary", { name: receipt.title, exact: true }).getByText("History", { exact: true }).click();
  await page.getByRole("complementary", { name: receipt.title, exact: true }).locator("details li").last().scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("task-history.png") });
  await page.getByRole("complementary", { name: receipt.title, exact: true }).getByRole("heading").scrollIntoViewIfNeeded();
  const screenshot = testInfo.outputPath(`user-task-${width}-${theme}.png`);
  await page.screenshot({ path: screenshot });
  await testInfo.attach(`user-task-${width}`, { path: screenshot, contentType: "image/png" });
});

test("authors reusable forms, assignment and connected action paths in the normal Studio designer", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1600, height: 1100 }); await signIn(page);
  const base = (JSON.parse(readFileSync(receiptPath!, "utf8")) as Receipt).baseUrl;
  const who = await (await page.request.get(`${base}/runtime/user-task-identity`)).json();
  const suffix = String(Date.now()); const formId = `studio-form-${suffix}`; const title = `Studio authored approval ${suffix}`;
  await page.goto("/user-tasks/forms?backend=legacy");
  await expect(page.getByRole("table", { name: "Reusable task forms", exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("forms-list.png") });
  await page.getByRole("button", { name: "Create form version", exact: true }).first().click();
  await page.getByLabel("Form ID", { exact: true }).fill(formId); await page.getByLabel("Name", { exact: true }).fill(title);
  await page.getByRole("button", { name: "Add field", exact: true }).click();
  await page.getByLabel("Key", { exact: true }).fill("Reason"); await page.getByLabel("Label", { exact: true }).fill("Reason");
  await page.getByLabel("Required", { exact: true }).check();
  await page.screenshot({ path: testInfo.outputPath("form-editor.png") });
  await page.setViewportSize({ width: 390, height: 1000 });
  await page.screenshot({ path: testInfo.outputPath("form-editor-narrow.png") });
  await page.getByRole("button", { name: "Cancel", exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("form-actions-narrow.png") });
  await page.setViewportSize({ width: 1600, height: 1100 });
  await page.getByRole("button", { name: "Publish form version", exact: true }).click();
  await expect(page.getByText("The immutable form version is available to new tasks.", { exact: true })).toBeVisible();
  await page.goto("/workflows/definitions?backend=legacy");
  await page.getByRole("button", { name: /^(Create|Create workflow)$/ }).first().click();
  const dialog = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  await dialog.getByLabel("Display name").fill(title); await dialog.getByRole("radio", { name: "Flowchart", exact: true }).check();
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const palette = page.getByRole("searchbox", { name: "Search activity palette" });
  await palette.fill("Create User Task"); await page.getByRole("treeitem", { name: /^Create User Task,/ }).click();
  const taskNode = page.locator(".react-flow__node").filter({ hasText: "Create User Task" });
  const taskNodeId = await taskNode.getAttribute("data-id"); expect(taskNodeId).toBeTruthy();
  await expect(page.getByLabel("Action 1", { exact: true })).toHaveValue("Approve");
  await expect(taskNode.locator('.source[data-handleid="Approve"]')).toBeVisible();
  await page.getByRole("textbox", { name: "Title", exact: true }).fill(title);
  await page.getByLabel("Task assignee", { exact: true }).selectOption(who.userId);
  await page.getByLabel("Task form", { exact: true }).selectOption(formId);
  await page.getByRole("textbox", { name: "Form Version", exact: true }).fill("1");
  await page.getByRole("button", { name: "Add action", exact: true }).click();
  await page.getByLabel("Action 3", { exact: true }).fill("Request More Info"); await page.getByLabel("Action 3", { exact: true }).press("Tab");
  const actions = ["Approve", "Reject", "Request More Info"]; const branchIds: string[] = [];
  for (const action of actions) {
    await palette.fill("Write Line"); await page.getByRole("treeitem", { name: /^Write Line,/ }).click();
    const node = page.locator(".react-flow__node").filter({ hasText: "Write Line" }).last(); branchIds.push((await node.getAttribute("data-id"))!);
    await page.getByRole("textbox", { name: "Text", exact: true }).fill(`${action} branch`); await page.getByRole("textbox", { name: "Text", exact: true }).press("Tab");
  }
  await page.getByRole("button", { name: "Auto-layout", exact: true }).click(); await page.getByRole("button", { name: "Fit View", exact: true }).click();
  for (const [index, action] of actions.entries()) {
    await taskNode.locator(`.source[data-handleid="${action}"]`).dragTo(page.locator(`.react-flow__node[data-id="${branchIds[index]}"] .target`));
  }
  await expect(page.locator(".react-flow__edge")).toHaveCount(3);
  const saved = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/design/workflows/drafts/"));
  await page.getByRole("button", { name: "Save", exact: true }).click(); expect((await saved).ok()).toBe(true); await page.reload();
  await expect(taskNode.locator('.source[data-handleid="Request More Info"]')).toBeVisible(); await expect(page.locator(".react-flow__edge")).toHaveCount(3);
  const publication = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/publish"));
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Publish workflow", exact: true }); await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
  await review.getByRole("button", { name: "Publish", exact: true }).click(); const published = await (await publication).json();
  await page.getByRole("dialog", { name: "Published", exact: true }).getByRole("button", { name: "Close", exact: true }).click();
  const start = await page.request.post(`${base}/runtime/workflows/executables/${published.artifactId}/execute`, { data: { sourceReferenceId: published.sourceReferenceId } }); expect(start.ok()).toBe(true);
  const executionId = (await start.json()).workflowExecutionId; let id = "";
  await expect.poll(async () => { const response = await page.request.get(`${base}/runtime/user-tasks?view=mine`); const task = (await response.json()).items.find((item: { title: string }) => item.title === title); id = task?.id ?? ""; return id; }, { timeout: 30_000 }).not.toBe("");
  await page.goto(`/user-tasks/${id}?backend=legacy`); await page.getByLabel(/^Reason/).fill("Authored in Studio"); await page.getByRole("button", { name: "Approve", exact: true }).click();
  await expect.poll(async () => (await (await page.request.get(`${base}/runtime/user-tasks/${id}`)).json()).status, { timeout: 30_000 }).toBe("Completed");
  await expect.poll(async () => (await (await page.request.get(`${base}/runtime/workflows/instances/${executionId}`)).json()).instance.status, { timeout: 30_000 }).toBe("Completed");
  const run = await (await page.request.get(`${base}/runtime/workflows/instances/${executionId}`)).json(); expect(run.instance.status).toBe("Completed");
  expect(run.activities.filter((item: { executableNodeId: string; status: string }) => branchIds.includes(item.executableNodeId) && item.status === "Completed").map((item: { executableNodeId: string }) => item.executableNodeId)).toEqual([branchIds[0]]);
  const screenshot = testInfo.outputPath("studio-authored-user-task.png");
  await page.screenshot({ path: screenshot });
  await testInfo.attach("studio-authored-user-task", { path: screenshot, contentType: "image/png" });
});
