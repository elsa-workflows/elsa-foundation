import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import { signIn, expectAccessible } from "./support/journey";

// The caller owns both hosts and creates an open task with e2e-tests/user-tasks/Test-UserTasks.ps1 -LeaveOpen.
// This uses the normal Studio module, real authentication and real backend APIs; no route interception.
const receiptPath = process.env.ELSA_USER_TASK_RECEIPT;
test.skip(!process.env.ELSA_STUDIO_URL || !receiptPath, "Set ELSA_STUDIO_URL and ELSA_USER_TASK_RECEIPT for owned User Task hosts.");
test.setTimeout(120_000);
type Receipt = { taskId: string; title: string; executionId: string; definitionId: string; baseUrl: string; artifactId: string; sourceReferenceId: string };

for (const width of [1440, 390]) test(`real Studio inbox validates and completes the selected action once (${width}px)`, async ({ page }, testInfo) => {
  const receipt = JSON.parse(readFileSync(receiptPath!, "utf8")) as Receipt;
  await page.setViewportSize({ width, height: 1000 }); await signIn(page);
  let taskId = receipt.taskId; let executionId = receipt.executionId;
  if (width === 390) {
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
  await row.focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: receipt.title, exact: true })).toBeVisible();
  const account = page.getByLabel(/^Account/); await expect(account).toHaveValue("A-107"); await expect(account).toBeDisabled();
  await page.getByRole("button", { name: "Approve", exact: true }).press("Enter");
  await expect(page.getByText("This field is required.", { exact: true }).first()).toBeVisible();
  await page.getByLabel(/^Amount/).fill("42"); await page.getByLabel(/^Confirmed/).selectOption("false"); await page.getByLabel(/^Note/).fill("Studio case preserved");
  await expectAccessible(page, ".user-tasks");
  const sent = page.waitForRequest(request => request.method() === "POST" && request.url().endsWith(`/runtime/user-tasks/${taskId}/complete`));
  await page.getByRole("button", { name: "Approve", exact: true }).press("Enter");
  const request = (await sent).postDataJSON(); expect(request.data).toEqual({ AccountID: "A-107", AmountUSD: 42, Confirmed: false, Note: "Studio case preserved" });
  await expect.poll(async () => (await (await page.request.get(`${receipt.baseUrl}/runtime/user-tasks/${taskId}`)).json()).status, { timeout: 30_000 }).toBe("Completed");
  const duplicate = await page.request.post(`${receipt.baseUrl}/runtime/user-tasks/${taskId}/complete`, { data: request }); expect(duplicate.ok()).toBe(true);
  const task = await duplicate.json(); expect(task.history.filter((entry: { operation: string }) => entry.operation === "accepted")).toHaveLength(1);
  expect(task.history.filter((entry: { operation: string }) => entry.operation === "completed")).toHaveLength(1);
  const run = await (await page.request.get(`${receipt.baseUrl}/runtime/workflows/instances/${executionId}`)).json();
  expect(run.instance.status).toBe("Completed");
  expect(run.activities.filter((item: { executableNodeId: string; status: string }) => item.executableNodeId.startsWith("branch-") && item.status === "Completed").map((item: { executableNodeId: string }) => item.executableNodeId)).toEqual(["branch-0"]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.getByRole("button", { name: "Refresh task", exact: true }).press("Enter");
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
  await testInfo.attach(`user-task-${width}`, { body: await page.screenshot(), contentType: "image/png" });
});
